using System;
using System.ComponentModel;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents the physical and hazard properties of a gas substance used in dispersion modeling.
    /// </summary>
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class GasProperties
    {
        [Category("Identity")]
        [Description("Display name of the gas substance.")]
        public string Name { get; set; }

        [Category("Physical")]
        [Description("Molecular weight in kg/mol. Air ≈ 0.029, methane ≈ 0.016, H₂S ≈ 0.034.")]
        public double MolarMass { get; set; }

        [Category("Physical")]
        [Description("Specific heat at constant pressure, J/(kg·K). 0 = estimated from the name / molar mass (used by the unified integral dispersion model for the mixture temperature).")]
        public double SpecificHeatJPerKgK { get; set; }

        /// <summary>Specific heat to use: the explicit value, or an estimate from the gas name / molar mass.</summary>
        [System.ComponentModel.Browsable(false)]
        [System.Xml.Serialization.XmlIgnore]
        public double EffectiveSpecificHeatJPerKgK
        {
            get
            {
                if (SpecificHeatJPerKgK > 0) return SpecificHeatJPerKgK;
                string n = (Name ?? "").ToLowerInvariant();
                if (n.Contains("h2s") || n.Contains("sulfeto") || n.Contains("sulfide")) return 1000;
                if (n.Contains("hydrogen") || n.Contains("hidrog") || n == "h2") return 14300;
                if (n.Contains("lng") || n.Contains("gnl") || n.Contains("methane") || n.Contains("metano") || n.Contains("ch4")) return 2200;
                if (n.Contains("ammonia") || n.Contains("amônia") || n.Contains("amonia") || n.Contains("nh3")) return 2100;
                if (n.Contains("co2") || n.Contains("carbon dioxide") || n.Contains("dióxido de carbono")) return 850;
                if (n.Contains("cl2") || n.Contains("chlor") || n.Contains("cloro")) return 480;
                if (n.Contains("so2") || n.Contains("sulfur dioxide") || n.Contains("dióxido de enxofre")) return 620;
                if (n.Contains("propane") || n.Contains("propano") || n.Contains("lpg") || n.Contains("glp")) return 1670;
                if (n.Contains("butane") || n.Contains("butano")) return 1700;
                if (n.Contains("ethane") || n.Contains("etano")) return 1750;
                // ideal-gas estimate: Cp ≈ (7/2 R)/M for light diatomic-like gases, lower per kg for heavy molecules
                if (MolarMass > 0) return Math.Max(500.0, Math.Min(2500.0, 3.5 * 8.314 / MolarMass * (MolarMass < 0.03 ? 1.0 : 1.5)));
                return 1000;
            }
        }

        [Category("Flammability")]
        [Description("Lower Flammability Limit (kg/m³). Concentration above this in air can ignite.")]
        public double LFL { get; set; }

        [Category("Flammability")]
        [Description("Upper Flammability Limit (kg/m³). Above this, mixture is too rich to ignite.")]
        public double UFL { get; set; }

        [Category("Toxicity")]
        [Description("Immediately Dangerous to Life and Health concentration (kg/m³).")]
        public double IDLH { get; set; }

        [Category("Toxicity")]
        [Description("Emergency Response Planning Guideline Level 1 — mild transient effects (kg/m³).")]
        public double ERPG1 { get; set; }

        [Category("Toxicity")]
        [Description("ERPG Level 2 — irreversible or other serious health effects threshold (kg/m³).")]
        public double ERPG2 { get; set; }

        [Category("Toxicity")]
        [Description("ERPG Level 3 — life-threatening health effects threshold (kg/m³).")]
        public double ERPG3 { get; set; }

        [Category("Decay")]
        [Description("Chemical half-life for first-order decay (s). Zero disables decay.")]
        public double HalfLifeS { get; set; }

        [Category("Decay")]
        [Description("Dry deposition velocity (m/s). Zero disables deposition.")]
        public double DryDepositionVelocityMPerS { get; set; }

        [Category("Physical")]
        [Description("Cryogenic release (e.g. LNG vapour at ~111 K). When true, OpenFOAM case generators emit cold-jet boundary conditions matching Vu (2019) §5.3: T = Tbp at the source patch, flowRateInletVelocity, gasInlet patch via topoSet + createPatch. Mirrors the same flag on GasLibraryItem.")]
        public bool IsCryogenic { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GasProperties"/> class with default values (air-like gas).
        /// </summary>
        public GasProperties()
        {
            Name = "Unknown";
            MolarMass = 0.029;
            HalfLifeS = 0;
            DryDepositionVelocityMPerS = 0;
        }

        /// <summary>
        /// Creates a <see cref="GasProperties"/> instance pre-populated with methane (CH4) properties.
        /// </summary>
        /// <returns>A new <see cref="GasProperties"/> for methane.</returns>
        public static GasProperties CreateMethane()
        {
            return new GasProperties
            {
                Name = "Methane",
                MolarMass = 0.01604,
                LFL = 0.033,    // 5% v/v at NTP
                UFL = 0.099,    // 15% v/v at NTP
                IDLH = 0.0,
                ERPG1 = 0.0,
                ERPG2 = 0.0,
                ERPG3 = 0.0
            };
        }

        /// <summary>
        /// Creates a <see cref="GasProperties"/> instance pre-populated with hydrogen sulfide (H2S) properties.
        /// </summary>
        /// <returns>A new <see cref="GasProperties"/> for hydrogen sulfide.</returns>
        public static GasProperties CreateH2S()
        {
            return new GasProperties
            {
                Name = "Hydrogen Sulfide",
                MolarMass = 0.03408,
                LFL = 0.043,
                IDLH = 0.0696,
                ERPG1 = 0.0014,
                ERPG2 = 0.0418,
                ERPG3 = 0.0696
            };
        }

        /// <summary>
        /// Creates a <see cref="GasProperties"/> instance pre-populated with ammonia (NH3) properties.
        /// </summary>
        /// <returns>A new <see cref="GasProperties"/> for ammonia.</returns>
        public static GasProperties CreateAmmonia()
        {
            return new GasProperties
            {
                Name = "Ammonia",
                MolarMass = 0.01703,
                LFL = 0.150,
                IDLH = 0.2108,
                ERPG1 = 0.0175,
                ERPG2 = 0.1230,
                ERPG3 = 0.5270
            };
        }
    }
}
