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

            // Combustion: atoms per molecule are mole-weighted; the heat of combustion and
            // the soot and CO yields are per kg of the whole mixture, so they are weighted
            // by each component's mass (x_i·M_i), inerts counting as mass that releases and
            // yields nothing. The burning velocity is mole-weighted over the components
            // that have one — a first estimate, not a mixing rule for flame speeds.
            // The specific heat is mass-weighted, each component's from its name or molar mass.
            double carbon = 0, hydrogen = 0, mass = 0, heat = 0, soot = 0, co = 0, sl = 0, slFrac = 0, cp = 0;
            foreach (var c in Mixture.Components)
            {
                double x = c.MoleFraction, m = c.MoleFraction * c.MolarMass;
                cp += m * new GasProperties { Name = c.Name, MolarMass = c.MolarMass }.EffectiveSpecificHeatJPerKgK;
                carbon += x * FuelCombustionData.Resolve(c.CarbonAtoms, c.Name, d => d.CarbonAtoms);
                hydrogen += x * FuelCombustionData.Resolve(c.HydrogenAtoms, c.Name, d => d.HydrogenAtoms);
                mass += m;
                heat += m * FuelCombustionData.Resolve(c.HeatOfCombustionJPerKg, c.Name, d => d.HeatOfCombustionJPerKg);
                soot += m * FuelCombustionData.Resolve(c.SootYield, c.Name, d => d.SootYield);
                co += m * FuelCombustionData.Resolve(c.CoYield, c.Name, d => d.CoYield);
                double s = FuelCombustionData.Resolve(c.LaminarBurningVelocityMPerS, c.Name, d => d.LaminarBurningVelocityMPerS);
                if (s > 0) { sl += x * s; slFrac += x; }
            }
            if (totalFrac > 0) { carbon /= totalFrac; hydrogen /= totalFrac; }
            if (mass > 0) { heat /= mass; soot /= mass; co /= mass; cp /= mass; }

            return new GasProperties
            {
                Name = Name,
                MolarMass = mw,
                SpecificHeatJPerKgK = cp,
                LFL = lflMix,
                UFL = uflMix,
                IDLH = idlh,
                HeatOfCombustionJPerKg = heat,
                CarbonAtoms = carbon,
                HydrogenAtoms = hydrogen,
                SootYield = soot,
                CoYield = co,
                LaminarBurningVelocityMPerS = slFrac > 0 ? sl / slFrac : 0
            };
        }
    }
}
