using System;
using System.ComponentModel;
using System.Xml.Serialization;

namespace DisperSim3D.Models
{
    public enum GasLibraryItemKind
    {
        Pure,
        Mixture
    }

    /// <summary>
    /// An entry in the project Gas Library. Wraps either a pure substance (<see cref="GasProperties"/>)
    /// or a multi-component <see cref="GasMixture"/>. Sources reference items by <see cref="Id"/>.
    /// </summary>
    public class GasLibraryItem
    {
        [Category("Identity")]
        [Description("Unique identifier (read-only).")]
        public string Id { get; set; }

        [Category("Identity")]
        [Description("Display name of the gas or mixture.")]
        public string Name { get; set; }

        [Category("Identity")]
        [Description("Whether this is a pure substance or a multi-component mixture.")]
        public GasLibraryItemKind Kind { get; set; }

        [Category("Composition")]
        [Description("Properties of the pure substance (used when Kind = Pure).")]
        public GasProperties PureGas { get; set; }

        [Category("Composition")]
        [Description("Multi-component mixture definition (used when Kind = Mixture).")]
        public GasMixture Mixture { get; set; }

        [Category("Behavior")]
        [Description("Mark this gas as cryogenic (e.g. LNG vapour at ~111 K). Triggers CFD presets: Sc_t=0.15 and FixedTemperature ground BC, per Vu 2019 §5.4.")]
        public bool IsCryogenic { get; set; }

        [XmlIgnore]
        [Browsable(false)]
        public bool IsMixture => Kind == GasLibraryItemKind.Mixture;

        public GasLibraryItem()
        {
            Id = Guid.NewGuid().ToString();
            Name = "Gas";
            Kind = GasLibraryItemKind.Pure;
            PureGas = new GasProperties();
        }

        public static GasLibraryItem FromGasProperties(GasProperties gas)
        {
            return new GasLibraryItem
            {
                Id = Guid.NewGuid().ToString(),
                Name = gas?.Name ?? "Unnamed",
                Kind = GasLibraryItemKind.Pure,
                PureGas = gas ?? new GasProperties()
            };
        }

        public static GasLibraryItem FromMixture(string name, GasMixture mixture)
        {
            return new GasLibraryItem
            {
                Id = Guid.NewGuid().ToString(),
                Name = string.IsNullOrEmpty(name) ? "Mixture" : name,
                Kind = GasLibraryItemKind.Mixture,
                Mixture = mixture ?? new GasMixture()
            };
        }

        /// <summary>
        /// Returns a representative <see cref="GasProperties"/> for the item.
        /// For mixtures, returns a synthetic GasProperties with mole-fraction-weighted molar mass.
        /// </summary>
        public GasProperties AsGasProperties()
        {
            if (Kind == GasLibraryItemKind.Pure && PureGas != null)
                return PureGas;
            if (Mixture == null || Mixture.Components.Count == 0)
                return new GasProperties { Name = Name };

            // Use Le Chatelier's rule for LFL/UFL of mixtures of flammables — it's
            // the standard mole-fraction-of-fuels weighted reciprocal:
            //   1/LFL_mix = Σ y_i / LFL_i   (over components with LFL > 0)
            // For components without flammability data we skip them in the sum, which
            // approximates them as inerts.
            double mw = 0, idlh = 0;
            double totalFrac = 0;
            double recipLfl = 0, recipUfl = 0;
            double flammableFrac = 0;
            foreach (var c in Mixture.Components)
            {
                mw += c.MolarMass * c.MoleFraction;
                idlh += c.IDLH * c.MoleFraction;
                totalFrac += c.MoleFraction;
                if (c.LFL > 0) { recipLfl += c.MoleFraction / c.LFL; flammableFrac += c.MoleFraction; }
                if (c.UFL > 0) recipUfl += c.MoleFraction / c.UFL;
            }
            if (totalFrac > 0)
            {
                mw /= totalFrac;
                idlh /= totalFrac;
            }
            double lflMix = (flammableFrac > 0 && recipLfl > 0) ? flammableFrac / recipLfl : 0;
            double uflMix = (flammableFrac > 0 && recipUfl > 0) ? flammableFrac / recipUfl : 0;
            return new GasProperties
            {
                Name = Name,
                MolarMass = mw,
                LFL = lflMix,
                UFL = uflMix,
                IDLH = idlh
            };
        }
    }
}
