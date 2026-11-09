using System;
using System.ComponentModel;
using DisperSim3D.Geometry;
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
        ContourYZ = 3,
        /// <summary>The simulation's per-triangle heat load ({time}_surface.bin) coloured on
        /// the imported geometry. Reads SurfaceIncidentRadiationKwM2 or SurfaceTotalHeatFluxKwM2.</summary>
        Surface = 4
    }

    /// <summary>
    /// Curated property the View samples from the simulation's OpenFOAM result. Each value
    /// resolves to one (or, for WindSpeed, three) actual field name(s) the reader reads.
    /// </summary>
    public enum ViewFieldProperty
    {
        /// <summary>The release species mass fraction (CH4 / SF6 / s — auto from source gas). Synonym of MassFraction.</summary>
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
        TurbulentViscosity = 6,
        /// <summary>Mass fraction of released species (kg/kg, [0..1]). Same as Concentration.</summary>
        MassFraction = 7,
        /// <summary>Mole fraction (Y_i × M_mix / M_species) — useful for ppm/ppb conversion.</summary>
        MoleFraction = 8,
        /// <summary>Mass concentration in kg/m³ (Y_i × ρ_mixture).</summary>
        ConcentrationKgM3 = 9,
        /// <summary>Volumetric concentration in parts per million (mole_fraction × 1e6).</summary>
        ConcentrationPpm = 10,
        /// <summary>Volumetric concentration in parts per billion (mole_fraction × 1e9).</summary>
        ConcentrationPpb = 11,
        /// <summary>Percentage of Lower Flammability Limit (kg/m³ / LFL_kg/m³ × 100).</summary>
        PercentLFL = 12,
        /// <summary>Percentage of Upper Flammability Limit (kg/m³ / UFL_kg/m³ × 100).</summary>
        PercentUFL = 13,
        /// <summary>Thermal radiation flux (kW/m²) — the simulation's own {time}_Q.bin field when it
        /// wrote one, else computed analytically from every FireSource.</summary>
        ThermalRadiationKwM2 = 14,
        /// <summary>Time (s) at which the flash-fire flame front reaches each cell,
        /// from the IgnitionEvent attached to the view's simulation.</summary>
        FlashFireArrivalS = 15,
        /// <summary>1 inside the flash-fire hazard envelope, 0 outside. Take the
        /// isosurface at 0.5 to draw the envelope.</summary>
        FlashFireEnvelope = 16,
        /// <summary>Thermal dose (W/m²)^(4/3)·s from the radiation field and the
        /// scenario's exposure time.</summary>
        ThermalDose = 17,
        /// <summary>Probability of fatality (0–1) from the thermal dose, via the
        /// Eisenberg probit.</summary>
        FatalityProbability = 18,
        /// <summary>Soot mass concentration (kg/m³), the simulation's {time}_Soot.bin.</summary>
        SootDensityKgM3 = 19,
        /// <summary>Carbon monoxide volume fraction, the simulation's {time}_CO.bin.</summary>
        COVolumeFraction = 20,
        /// <summary>Carbon dioxide volume fraction, the simulation's {time}_CO2.bin.</summary>
        CO2VolumeFraction = 21,
        /// <summary>Oxygen volume fraction, the simulation's {time}_O2.bin. Its peak over
        /// time is the lowest value each cell reached.</summary>
        O2VolumeFraction = 22,
        /// <summary>Visibility through smoke (m), the simulation's {time}_Vis.bin. Its peak
        /// over time is the lowest value each cell reached.</summary>
        VisibilityM = 23,
        /// <summary>Incident radiative heat flux on the plant surfaces (kW/m²), from the
        /// simulation's {time}_surface.bin; drawn by a Surface view.</summary>
        SurfaceIncidentRadiationKwM2 = 24,
        /// <summary>Total (radiative + convective) heat flux into the plant surfaces
        /// (kW/m²), from the simulation's {time}_surface.bin; drawn by a Surface view.</summary>
        SurfaceTotalHeatFluxKwM2 = 25,
        /// <summary>Overpressure of an explosion (Pa), the simulation's {time}_P.bin.</summary>
        OverpressurePa = 26,
        /// <summary>Peak overpressure an explosion reached in each cell (Pa), the simulation's {time}_Pmax.bin.</summary>
        PeakOverpressurePa = 27,
        /// <summary>Positive-phase impulse of an explosion (Pa·s), the simulation's {time}_Imp.bin.</summary>
        ImpulsePaS = 28,
        /// <summary>Time the explosion's flame reached each cell (s), the simulation's {time}_Arr.bin.
        /// Its peak over time is the earliest arrival.</summary>
        FlameArrivalS = 29,
        /// <summary>Peak overpressure on the plant surfaces (Pa), from the simulation's
        /// {time}_blast.bin; drawn by a Surface view.</summary>
        SurfacePeakOverpressurePa = 30,
        /// <summary>Positive-phase impulse on the plant surfaces (Pa·s), from the simulation's
        /// {time}_blast.bin; drawn by a Surface view.</summary>
        SurfaceImpulsePaS = 31
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
        [Editor("DisperSim3D.Controls.ColorPickerPropertyEditor, DisperSim3D.UI.Wpf", "HandyControl.Controls.PropertyEditorBase, HandyControl")]
        public Color IsoColor { get; set; }

        [Category("Isosurface")]
        [Description("Render as a realistic turbulent gas cloud instead of a solid isosurface.")]
        public bool UseCloudAppearance { get; set; }

        [Category("Isosurface")]
        [Description("Tint colour for cloud-style rendering. Ignored when UseCloudAppearance is off.")]
        [Editor("DisperSim3D.Controls.ColorPickerPropertyEditor, DisperSim3D.UI.Wpf", "HandyControl.Controls.PropertyEditorBase, HandyControl")]
        public Color CloudColor { get; set; }

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

        [Category("Contour")]
        [Description("Leave the plane transparent where the value is below MinValue or above MaxValue, so only that range shows. Needs MinValue < MaxValue.")]
        public bool ClipToRange { get; set; }

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
            UseCloudAppearance = false;
            CloudColor = Color.FromRgb(200, 200, 210);
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

        /// <summary>True when the view colours the plant surfaces rather than drawing a field in space.</summary>
        [XmlIgnore]
        [Browsable(false)]
        public bool IsSurface => Kind == ViewKind.Surface || FieldTransform.IsSurface(FieldProperty);
    }
}
