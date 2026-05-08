namespace DisperSim3D.Models
{
    /// <summary>
    /// Pasquill-Gifford atmospheric stability classes used for Gaussian dispersion modeling.
    /// </summary>
    public enum PasquillStabilityClass
    {
        /// <summary>
        /// Very unstable conditions (strong insolation, light winds).
        /// </summary>
        A,

        /// <summary>
        /// Moderately unstable conditions.
        /// </summary>
        B,

        /// <summary>
        /// Slightly unstable conditions.
        /// </summary>
        C,

        /// <summary>
        /// Neutral conditions (overcast or windy).
        /// </summary>
        D,

        /// <summary>
        /// Slightly stable conditions (light winds, nighttime).
        /// </summary>
        E,

        /// <summary>
        /// Stable conditions (very light winds, clear nighttime sky).
        /// </summary>
        F
    }

    /// <summary>
    /// Types of concentration thresholds used for dispersion hazard zone visualization.
    /// </summary>
    public enum DispersionThresholdType
    {
        /// <summary>
        /// Lower Flammability Limit.
        /// </summary>
        LFL,

        /// <summary>
        /// Immediately Dangerous to Life or Health concentration.
        /// </summary>
        IDLH,

        /// <summary>
        /// Emergency Response Planning Guideline Level 1 (mild, transient health effects).
        /// </summary>
        ERPG1,

        /// <summary>
        /// Emergency Response Planning Guideline Level 2 (irreversible or serious health effects).
        /// </summary>
        ERPG2,

        /// <summary>
        /// Emergency Response Planning Guideline Level 3 (life-threatening health effects).
        /// </summary>
        ERPG3,

        /// <summary>
        /// User-defined custom concentration threshold.
        /// </summary>
        Custom
    }

    /// <summary>
    /// States of the dispersion simulation engine.
    /// </summary>
    public enum DispersionSimulationState
    {
        /// <summary>
        /// Simulation is stopped and idle.
        /// </summary>
        Stopped,

        /// <summary>
        /// Simulation is actively running.
        /// </summary>
        Running,

        /// <summary>
        /// Simulation is paused and can be resumed.
        /// </summary>
        Paused,

        /// <summary>
        /// Simulation is solving the CFD (Computational Fluid Dynamics) phase.
        /// </summary>
        SolvingCfd
    }
}
