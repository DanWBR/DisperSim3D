using System;
using System.Collections.Generic;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a gas dispersion simulation scenario, including meteorological conditions,
    /// release sources, solver settings, and domain configuration.
    /// </summary>
    public class DispersionScenario
    {
        /// <summary>
        /// Gets or sets the unique identifier for this scenario.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the display name of this scenario.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the meteorological conditions for this scenario.
        /// </summary>
        public MeteorologicalConditions Meteo { get; set; }

        /// <summary>
        /// Gets or sets the list of gas release sources in this scenario.
        /// </summary>
        public List<ReleaseSource3D> Sources { get; set; }

        /// <summary>
        /// Gets or sets the list of concentration thresholds used for visualization and alerting.
        /// </summary>
        public List<DispersionThreshold> Thresholds { get; set; }

        /// <summary>
        /// Gets or sets the total simulation duration in seconds.
        /// </summary>
        public double SimulationDurationS { get; set; }

        /// <summary>
        /// Gets or sets the simulation time step in seconds.
        /// </summary>
        public double TimeStepS { get; set; }

        /// <summary>
        /// Gets or sets the simulation domain size in meters.
        /// </summary>
        public double DomainSizeM { get; set; }

        /// <summary>
        /// Gets or sets the number of grid cells along each axis of the simulation domain.
        /// </summary>
        public int GridResolution { get; set; }

        /// <summary>
        /// Gets or sets the list of contour plane configurations for result visualization.
        /// </summary>
        public List<ContourPlaneConfig> ContourPlanes { get; set; }

        /// <summary>
        /// Gets or sets the seed points used for streamline visualization of the wind field.
        /// </summary>
        public List<System.Windows.Media.Media3D.Point3D> StreamlineSeedPoints { get; set; }

        /// <summary>
        /// Gets or sets the transient wind profile for time-varying wind conditions.
        /// </summary>
        public TransientWindProfile TransientWind { get; set; }

        /// <summary>
        /// Gets or sets the gas mixture definition for multi-component releases.
        /// </summary>
        public GasMixture GasMixture { get; set; }

        /// <summary>
        /// Gets or sets the CFD solver type used for this scenario.
        /// </summary>
        public CfdSolverType SolverType { get; set; }

        /// <summary>
        /// Gets or sets the CFD solver configuration parameters.
        /// </summary>
        public CfdConfiguration CfdConfig { get; set; }

        /// <summary>
        /// Gets or sets the ID of the associated <see cref="WindFieldScenario"/>.
        /// Required for all dispersion runs.
        /// </summary>
        public string WindFieldScenarioId { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DispersionScenario"/> class with default values.
        /// </summary>
        public DispersionScenario()
        {
            Id = Guid.NewGuid().ToString();
            Name = "Dispersion Scenario";
            Meteo = new MeteorologicalConditions();
            Sources = new List<ReleaseSource3D>();
            Thresholds = new List<DispersionThreshold>();
            ContourPlanes = new List<ContourPlaneConfig>();
            StreamlineSeedPoints = new List<System.Windows.Media.Media3D.Point3D>();
            TransientWind = new TransientWindProfile();
            GasMixture = new GasMixture();
            SolverType = CfdSolverType.GaussianPuff;
            SimulationDurationS = 300.0;
            TimeStepS = 0.5;
            DomainSizeM = 200.0;
            GridResolution = 80;
        }
    }
}
