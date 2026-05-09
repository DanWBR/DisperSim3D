using System;
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
        public string Id { get; set; }
        public string Name { get; set; }
        public GasLibraryItemKind Kind { get; set; }
        public GasProperties PureGas { get; set; }
        public GasMixture Mixture { get; set; }

        [XmlIgnore]
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

            double mw = 0, lfl = 0, idlh = 0;
            double totalFrac = 0;
            foreach (var c in Mixture.Components)
            {
                mw += c.MolarMass * c.MoleFraction;
                lfl += c.LFL * c.MoleFraction;
                idlh += c.IDLH * c.MoleFraction;
                totalFrac += c.MoleFraction;
            }
            if (totalFrac > 0)
            {
                mw /= totalFrac;
                lfl /= totalFrac;
                idlh /= totalFrac;
            }
            return new GasProperties
            {
                Name = Name,
                MolarMass = mw,
                LFL = lfl,
                IDLH = idlh
            };
        }
    }
}
