using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a fire source (jet fire or pool fire) in the 3D scene for thermal radiation analysis.
    /// </summary>
    public class FireSource
    {
        /// <summary>
        /// Gets or sets the unique identifier for this fire source.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Gets or sets the display name of this fire source.
        /// </summary>
        public string Name { get; set; } = "JetFire1";

        /// <summary>
        /// Gets or sets the 3D position of the fire source in the scene, in meters.
        /// </summary>
        public Point3D Position { get; set; }

        /// <summary>
        /// Gets or sets the direction vector of the jet fire flame. Default is along the positive X axis.
        /// </summary>
        public Vector3D Direction { get; set; } = new Vector3D(1, 0, 0);

        /// <summary>
        /// Gets or sets the mass flow rate of fuel in kilograms per second. Default is 1.0 kg/s.
        /// </summary>
        public double MassFlowRateKgS { get; set; } = 1.0;

        /// <summary>
        /// Gets or sets the orifice diameter in meters for jet fire calculations. Default is 0.02 m.
        /// </summary>
        public double OrificeDiameterM { get; set; } = 0.02;

        /// <summary>
        /// Gets or sets the heat of combustion of the fuel in joules per kilogram. Default is 50 MJ/kg.
        /// </summary>
        public double HeatOfCombustionJKg { get; set; } = 50e6;

        /// <summary>
        /// Gets or sets the fraction of total heat released as thermal radiation. Default is 0.2 (20%).
        /// </summary>
        public double RadiativeFraction { get; set; } = 0.2;

        /// <summary>
        /// Gets or sets a value indicating whether this source is a pool fire. If <c>false</c>, it is treated as a jet fire.
        /// </summary>
        public bool IsPoolFire { get; set; }

        /// <summary>
        /// Gets or sets the pool diameter in meters, used when <see cref="IsPoolFire"/> is <c>true</c>. Default is 5.0 m.
        /// </summary>
        public double PoolDiameterM { get; set; } = 5.0;

        /// <summary>
        /// Gets or sets the pool surface burn rate in kilograms per square meter per second. Default is 0.05 kg/m^2/s.
        /// </summary>
        public double PoolBurnRateKgM2S { get; set; } = 0.05;
    }

    /// <summary>
    /// Represents a fire scenario containing one or more <see cref="FireSource"/> instances
    /// and the radiation contour levels for thermal hazard zone visualization.
    /// </summary>
    public class FireScenario
    {
        /// <summary>
        /// Gets or sets the unique identifier for this fire scenario.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Gets or sets the display name of this fire scenario.
        /// </summary>
        public string Name { get; set; } = "Fire Scenario";

        /// <summary>
        /// Gets or sets the list of fire sources included in this scenario.
        /// </summary>
        public List<FireSource> Sources { get; set; } = new List<FireSource>();

        /// <summary>
        /// Gets or sets the thermal radiation contour levels in watts per square meter (W/m^2).
        /// Default levels are 4,000 (pain threshold), 12,500 (piloted ignition of wood), and 37,500 (structural damage).
        /// </summary>
        public List<double> RadiationContourLevels { get; set; } = new List<double> { 4000, 12500, 37500 };
    }
}
