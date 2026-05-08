using System;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a recorded CFD simulation run, storing its metadata, grid dimensions, and result status.
    /// </summary>
    public class CfdSimulationEntry
    {
        /// <summary>Gets or sets the unique short identifier for this simulation entry.</summary>
        public string Id { get; set; }

        /// <summary>Gets or sets the user-defined name for this simulation.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the name of the scenario that produced this simulation.</summary>
        public string ScenarioName { get; set; }

        /// <summary>Gets or sets the filesystem path to the OpenFOAM case directory.</summary>
        public string CasePath { get; set; }

        /// <summary>Gets or sets the date and time when this simulation was created.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Gets or sets the total simulation duration in seconds.</summary>
        public double DurationS { get; set; }

        /// <summary>Gets or sets the number of time steps in the simulation.</summary>
        public int TimeStepCount { get; set; }

        /// <summary>Gets or sets the number of grid cells in the X direction.</summary>
        public int GridNx { get; set; }

        /// <summary>Gets or sets the number of grid cells in the Y direction.</summary>
        public int GridNy { get; set; }

        /// <summary>Gets or sets the number of grid cells in the Z direction.</summary>
        public int GridNz { get; set; }

        /// <summary>Gets or sets the physical domain size in meters.</summary>
        public double DomainSizeM { get; set; }

        /// <summary>Gets or sets a value indicating whether result data is available for this simulation.</summary>
        public bool HasResults { get; set; }

        /// <summary>Gets or sets the solver type used for this simulation (e.g., "OpenFOAM").</summary>
        public string SolverType { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CfdSimulationEntry"/> class with a generated ID and current timestamp.
        /// </summary>
        public CfdSimulationEntry()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            CreatedAt = DateTime.Now;
            SolverType = "OpenFOAM";
        }

        /// <summary>Gets a brief text summary of the simulation parameters (step count, duration, and grid dimensions).</summary>
        public string Summary =>
            string.Format("{0} steps, {1:F0}s, {2}x{3}x{4}",
                TimeStepCount, DurationS, GridNx, GridNy, GridNz);
    }
}
