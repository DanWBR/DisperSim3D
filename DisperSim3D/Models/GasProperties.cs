using System.ComponentModel;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents the physical and hazard properties of a gas substance used in dispersion modeling.
    /// </summary>
    public class GasProperties
    {
        [Description("Display name of the gas substance.")]
        public string Name { get; set; }

        [Description("Molecular weight in kg/mol. Air ≈ 0.029, methane ≈ 0.016, H₂S ≈ 0.034.")]
        public double MolarMass { get; set; }

        [Description("Lower Flammability Limit (kg/m³). Concentration above this in air can ignite.")]
        public double LFL { get; set; }

        [Description("Upper Flammability Limit (kg/m³). Above this, mixture is too rich to ignite.")]
        public double UFL { get; set; }

        [Description("Immediately Dangerous to Life and Health concentration (kg/m³).")]
        public double IDLH { get; set; }

        [Description("Emergency Response Planning Guideline Level 1 — mild transient effects (kg/m³).")]
        public double ERPG1 { get; set; }

        [Description("ERPG Level 2 — irreversible or other serious health effects threshold (kg/m³).")]
        public double ERPG2 { get; set; }

        [Description("ERPG Level 3 — life-threatening health effects threshold (kg/m³).")]
        public double ERPG3 { get; set; }

        [Description("Chemical half-life for first-order decay (s). Zero disables decay.")]
        public double HalfLifeS { get; set; }

        [Description("Dry deposition velocity (m/s). Zero disables deposition.")]
        public double DryDepositionVelocityMPerS { get; set; }

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
