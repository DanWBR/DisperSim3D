using System;
using System.Collections.Generic;
using System.ComponentModel;
using DisperSim3D.Geometry;

namespace DisperSim3D.Models
{
    /// <summary>How the thermal radiation field around a fire source is computed.</summary>
    public enum RadiationModel
    {
        /// <summary>All radiated power leaves a single point at the source position:
        /// <c>I = χ·Q/(4πr²)</c>. Ignores flame shape, tilt and atmospheric attenuation,
        /// and diverges close in — but it is one square root per cell, so it stays the
        /// fast preview mode.</summary>
        PointSource = 0,
        /// <summary>Tilted cylinder radiating at a surface emissive power, with a
        /// numerically integrated view factor and atmospheric transmissivity. See
        /// <see cref="DisperSim3D.Core.SolidFlameModel"/>.</summary>
        SolidFlame = 1
    }

    /// <summary>Orientation assumed for the receiving surface when evaluating the
    /// solid-flame view factor.</summary>
    public enum ReceiverMode
    {
        /// <summary>The orientation that sees the most flame. The conservative choice,
        /// and the right one for hazard-zone contours.</summary>
        MaxOriented = 0,
        /// <summary>An upward-facing horizontal surface (a roof, the ground).</summary>
        Horizontal = 1,
        /// <summary>The best-oriented vertical surface (a wall, a standing person).</summary>
        Vertical = 2
    }

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
        /// Gets or sets a value indicating whether this fire source is visible in the 3D viewport.
        /// </summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// Gets or sets the 3D position of the fire source in the scene, in meters.
        /// </summary>
        [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
        [Editor("DisperSim3D.Controls.Point3DPropertyEditor, DisperSim3D.UI.Wpf",
            "HandyControl.Controls.PropertyEditorBase, HandyControl")]
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
        /// Gets or sets the orifice diameter in meters for jet fire calculations. Default is 0.025 m.
        /// </summary>
        public double OrificeDiameterM { get; set; } = 0.025;

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

        /// <summary>
        /// Gets or sets which radiation model computes this source's flux field.
        /// Defaults to <see cref="Models.RadiationModel.SolidFlame"/>.
        /// </summary>
        public RadiationModel RadiationModel { get; set; } = RadiationModel.SolidFlame;

        /// <summary>
        /// Gets or sets the flame diameter in meters, overriding the correlation.
        /// Zero (the default) means derive it: the pool diameter for a pool fire,
        /// Chamberlain's frustum width for a jet.
        /// </summary>
        public double FlameDiameterM { get; set; }

        /// <summary>
        /// Gets or sets the surface emissive power in kW/m², overriding the value
        /// derived from the energy balance. Zero (the default) means derive it — set
        /// this when a measured or specified SEP is available.
        /// </summary>
        public double SepKwM2 { get; set; }

        /// <summary>
        /// Gets or sets the fuel molar mass in kg/mol, used to get the gas density at
        /// the orifice and from there the exit velocity that sets the wind tilt.
        /// Default is 0.016 (methane).
        /// </summary>
        public double FuelMolarMassKgMol { get; set; } = 0.016;
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

        /// <summary>
        /// Gets or sets the receiver orientation used when evaluating the solid-flame
        /// view factor. This is a property of the target, not of any one fire, so it
        /// applies to the whole radiation field.
        /// </summary>
        public ReceiverMode ReceiverMode { get; set; } = ReceiverMode.MaxOriented;
    }
}
