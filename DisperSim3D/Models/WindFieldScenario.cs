using System;
using System.ComponentModel;
using System.Xml.Serialization;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Status of a wind field simulation scenario.
    /// </summary>
    public enum WindFieldStatus
    {
        NotRun,
        Running,
        Ready,
        Failed
    }

    /// <summary>How the wind field is rendered in the 3D viewport.</summary>
    public enum WindFieldDisplayMode
    {
        /// <summary>Discrete arrows on a regular grid (legacy mode).</summary>
        Arrows = 0,
        /// <summary>Continuous streamlines coloured by local wind speed (blue → red).</summary>
        Streamlines = 1
    }

    /// <summary>
    /// A scenario that pre-computes a 3D steady-state wind field via CFD (simpleFoam),
    /// to be referenced by one or more <see cref="DispersionScenario"/> instances.
    /// </summary>
    public class WindFieldScenario
    {
        /// <summary>Gets or sets the unique identifier for this wind field scenario.</summary>
        public string Id { get; set; }

        /// <summary>Gets or sets the display name.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the meteorological inlet conditions.</summary>
        public MeteorologicalConditions Meteo { get; set; }

        /// <summary>Gets or sets the half-extent of the simulation box in meters.</summary>
        public double DomainSizeM { get; set; }

        /// <summary>Gets or sets the maximum height of the domain in meters.</summary>
        public double DomainHeightM { get; set; }

        /// <summary>Gets or sets the number of cells per axis.</summary>
        public int GridResolution { get; set; }

        /// <summary>Gets or sets the CFD configuration.</summary>
        public CfdConfiguration CfdConfig { get; set; }

        /// <summary>Gets or sets the persisted OpenFOAM case path on disk.</summary>
        public string CasePath { get; set; }

        /// <summary>Gets or sets the simulation status.</summary>
        public WindFieldStatus Status { get; set; }

        /// <summary>Gets or sets the last error or status message.</summary>
        public string StatusMessage { get; set; }

        /// <summary>
        /// Cached wind field; not serialized — reloaded from <see cref="CasePath"/> when needed.
        /// </summary>
        [XmlIgnore]
        public WindField3D WindField { get; set; }

        // ─── Visualization (editable in property grid) ───
        [Category("Visualization")]
        [Description("Number of arrows per horizontal axis (X and Y).")]
        public int ArrowsPerAxis { get; set; }

        [Category("Visualization")]
        [Description("Number of vertical layers of arrows.")]
        public int ArrowVerticalLayers { get; set; }

        [Category("Visualization")]
        [Description("Maximum opacity of the arrows (0–1).")]
        public double ArrowOpacity { get; set; }

        [Category("Visualization")]
        [Description("ARGB hex color of the arrows (e.g. FF000000 = solid black).")]
        public string ArrowColorHex { get; set; }

        [Category("Visualization")]
        [Description("Arrow length scale factor relative to grid cell (0.1–1.0).")]
        public double ArrowLengthFactor { get; set; }

        [Category("Visualization")]
        [Description("Arrow shaft thickness factor relative to length (0.005–0.1).")]
        public double ArrowThicknessFactor { get; set; }

        [Category("Visualization")]
        [Description("Whether arrows pulse/animate over time.")]
        public bool ArrowAnimated { get; set; }

        [Category("Visualization")]
        [Description("How the wind field is drawn: discrete Arrows (legacy) or continuous Streamlines coloured blue→red by speed.")]
        public WindFieldDisplayMode DisplayMode { get; set; }

        [Category("Visualization")]
        [Description("Number of streamlines to seed across the domain (only used when DisplayMode = Streamlines).")]
        public int StreamlineCount { get; set; }

        [Category("Visualization")]
        [Description("Vertical layers of streamline seeds (only when DisplayMode = Streamlines). 1 = ground level only.")]
        public int StreamlineVerticalLayers { get; set; }

        [Category("Visualization")]
        [Description("Streamline tube thickness as a fraction of the smaller horizontal cell size (default 0.04).")]
        public double StreamlineThicknessFactor { get; set; }

        [Category("Visualization")]
        [Description("Whether streamlines animate the brightness pulse flowing along the line.")]
        public bool StreamlineAnimated { get; set; }

        [Category("Visualization")]
        [Description("Half-extent (m) of the visualised wind region. The CFD domain may be much larger (km), but the streamlines/arrows are clipped to this AABB around the origin so they stay near the scene of interest. 0 = use the full CFD domain.")]
        public double DisplayExtentM { get; set; }

        [Category("Bundling")]
        [Description("How the OpenFOAM case is packed into a .dsproj bundle. ResultsOnly = small, FullCase = re-runnable after extraction.")]
        public BundleEmbedMode EmbedMode { get; set; } = BundleEmbedMode.ResultsOnly;

        public WindFieldScenario()
        {
            Id = Guid.NewGuid().ToString();
            Name = "Wind Field";
            Meteo = new MeteorologicalConditions();
            DomainSizeM = 200.0;
            DomainHeightM = 100.0;
            GridResolution = 40;
            CfdConfig = new CfdConfiguration();
            Status = WindFieldStatus.NotRun;

            ArrowsPerAxis = 24;
            ArrowVerticalLayers = 1;
            ArrowOpacity = 0.5;
            ArrowColorHex = "FF000000";
            ArrowLengthFactor = 0.30;
            ArrowThicknessFactor = 0.025;
            ArrowAnimated = true;

            DisplayMode = WindFieldDisplayMode.Streamlines;
            StreamlineCount = 256;
            StreamlineVerticalLayers = 1;
            StreamlineThicknessFactor = 0.025;
            StreamlineAnimated = true;
            DisplayExtentM = 50.0;
        }
    }
}
