namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents the physical and hazard properties of a gas substance used in dispersion modeling.
    /// </summary>
    public class GasProperties
    {
        /// <summary>
        /// Gets or sets the name of the gas substance.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the molar mass of the gas in kilograms per mole (kg/mol).
        /// </summary>
        public double MolarMass { get; set; }

        /// <summary>
        /// Gets or sets the Lower Flammability Limit as a volume fraction.
        /// </summary>
        public double LFL { get; set; }

        /// <summary>
        /// Gets or sets the Immediately Dangerous to Life or Health concentration as a volume fraction.
        /// </summary>
        public double IDLH { get; set; }

        /// <summary>
        /// Gets or sets the Emergency Response Planning Guideline Level 1 concentration as a volume fraction.
        /// </summary>
        public double ERPG1 { get; set; }

        /// <summary>
        /// Gets or sets the Emergency Response Planning Guideline Level 2 concentration as a volume fraction.
        /// </summary>
        public double ERPG2 { get; set; }

        /// <summary>
        /// Gets or sets the Emergency Response Planning Guideline Level 3 concentration as a volume fraction.
        /// </summary>
        public double ERPG3 { get; set; }

        /// <summary>
        /// Gets or sets the chemical half-life in seconds for first-order decay modeling. Zero means no decay.
        /// </summary>
        public double HalfLifeS { get; set; }

        /// <summary>
        /// Gets or sets the dry deposition velocity in meters per second. Zero means no deposition.
        /// </summary>
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
                LFL = 0.033,
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
