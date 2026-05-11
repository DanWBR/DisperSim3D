using System;
using System.ComponentModel;
using System.Windows.Media;
using System.Xml.Serialization;
using DisperSim3D.Core;

namespace DisperSim3D.Models
{
    /// <summary>What the View renders.</summary>
    public enum ViewKind
    {
        /// <summary>3D isosurface at the IsoValue threshold (Marching Cubes).</summary>
        Isosurface = 0,
        /// <summary>2D contour plane parallel to the XY plane at PlanePosition (z).</summary>
        ContourXY = 1,
        /// <summary>2D contour plane parallel to the XZ plane at PlanePosition (y).</summary>
        ContourXZ = 2,
        /// <summary>2D contour plane parallel to the YZ plane at PlanePosition (x).</summary>
        ContourYZ = 3
    }

    /// <summary>
    /// Curated property the View samples from the simulation's OpenFOAM result. Each value
    /// resolves to one (or, for WindSpeed, three) actual field name(s) the reader reads.
    /// </summary>
    public enum ViewFieldProperty
    {
        /// <summary>The release species mass fraction (CH4 / SF6 / s — auto from source gas).</summary>
        Concentration = 0,
        /// <summary>Temperature field T (K).</summary>
        Temperature = 1,
        /// <summary>|U| — magnitude of the velocity field. Requires mag(U) function object.</summary>
        WindSpeed = 2,
        /// <summary>p_rgh (buoyant cases) or p (incompressible).</summary>
        Pressure = 3,
        /// <summary>Turbulent kinetic energy k.</summary>
        TurbulentK = 4,
        /// <summary>Turbulent dissipation rate epsilon.</summary>
        TurbulentEpsilon = 5,
        /// <summary>Turbulent eddy viscosity nut.</summary>
        TurbulentViscosity = 6
    }

    /// <summary>How the View collapses transient timesteps to a single field.</summary>
    public enum ViewTimeMode
    {
        /// <summary>Per-cell maximum across every written timestep.</summary>
        PeakOverTime = 0,
        /// <summary>Last-written timestep only.</summary>
        FinalSnapshot = 1,
        /// <summary>The timestep closest to <see cref="View.SpecificTimeS"/>.</summary>
        SpecificTime = 2
    }

    /// <summary>
    /// A first-class result visualisation pinned to a Simulation. Each View is either a
    /// 3D isosurface or a 2D contour plane; together they replace the legacy inline
    /// thresholds + ContourPlanes from DispersionScenario.
    /// </summary>
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class View
    {
        [Category("Identity")]
        [Description("Unique identifier (read-only).")]
        public string Id { get; set; }

        [Category("Identity")]
        [Description("Display name shown in the project tree.")]
        public string Name { get; set; }

        [Category("Identity")]
        [Description("3D isosurface (Marching Cubes) or 2D contour plane (XY / XZ / YZ).")]
        public ViewKind Kind { get; set; }

        [Category("Source")]
        [Description("ID of the Simulation this view samples (must be Completed).")]
        public string SimulationId { get; set; }

        [Category("Source")]
        [Description("Which scalar property to sample. Concentration auto-resolves to the source gas's species (CH4/SF6/s).")]
        public ViewFieldProperty FieldProperty { get; set; }

        [Category("Source")]
        [Description("How transient timesteps are collapsed: PeakOverTime (cell max), FinalSnapshot (last only), or SpecificTime.")]
        public ViewTimeMode TimeMode { get; set; }

        [Category("Source")]
        [Description("Time in seconds to sample when TimeMode = SpecificTime. Closest written timestep wins.")]
        public double SpecificTimeS { get; set; }

        [Category("Visualization")]
        [Description("Whether the view is drawn in the 3D viewport.")]
        public bool IsVisible { get; set; }

        [Category("Visualization")]
        [Description("Surface opacity, 0 (transparent) to 1 (opaque).")]
        public double Opacity { get; set; }

        // ── Isosurface-specific ──
        [Category("Isosurface")]
        [Description("Threshold value at which the isosurface is extracted (units = same as the chosen field).")]
        public double IsoValue { get; set; }

        [Category("Isosurface")]
        [Description("Surface colour for the isosurface.")]
        [Editor(typeof(DisperSim3D.Controls.ColorPickerPropertyEditor), typeof(HandyControl.Controls.PropertyEditorBase))]
        public Color IsoColor { get; set; }

        // ── Contour-plane-specific ──
        [Category("Contour")]
        [Description("Position of the slicing plane along its normal axis (z for XY, y for XZ, x for YZ).")]
        public double PlanePosition { get; set; }

        [Category("Contour")]
        [Description("Colour map for the 2D contour: Jet, Viridis, Inferno, Coolwarm.")]
        public ColorMapName ColorMap { get; set; }

        [Category("Contour")]
        [Description("Lower bound of the colour scale. Set MinValue=MaxValue=0 for auto-range from the data.")]
        public double MinValue { get; set; }

        [Category("Contour")]
        [Description("Upper bound of the colour scale. Set MinValue=MaxValue=0 for auto-range from the data.")]
        public double MaxValue { get; set; }

        [Category("Contour")]
        [Description("Pixels per axis for the contour bitmap (resolution of the slice texture).")]
        public int SampleResolution { get; set; }

        public View()
        {
            Id = Guid.NewGuid().ToString();
            Name = "View";
            Kind = ViewKind.Isosurface;
            FieldProperty = ViewFieldProperty.Concentration;
            TimeMode = ViewTimeMode.PeakOverTime;
            SpecificTimeS = 0;
            IsVisible = true;
            Opacity = 0.5;
            IsoValue = 0.05;
            IsoColor = Colors.Cyan;
            PlanePosition = 1.0;
            ColorMap = ColorMapName.Jet;
            MinValue = 0;
            MaxValue = 0;
            SampleResolution = 80;
        }

        [XmlIgnore]
        [Browsable(false)]
        public bool IsContourPlane =>
            Kind == ViewKind.ContourXY || Kind == ViewKind.ContourXZ || Kind == ViewKind.ContourYZ;
    }
}
