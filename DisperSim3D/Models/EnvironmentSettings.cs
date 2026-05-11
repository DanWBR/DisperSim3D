using System.ComponentModel;

namespace DisperSim3D.Models
{
    /// <summary>Procedural ground material applied to the editor ground plane.</summary>
    public enum GroundMaterial
    {
        /// <summary>Original procedural green tile pattern (legacy default).</summary>
        Grid = 0,
        /// <summary>Mottled green grass.</summary>
        Grass = 1,
        /// <summary>Light grey concrete with subtle joints.</summary>
        Concrete = 2,
        /// <summary>Sandy beige with grain noise.</summary>
        Sand = 3,
        /// <summary>Dark asphalt.</summary>
        Asphalt = 4
    }

    /// <summary>
    /// Per-project visual environment toggles — sun, sky dome, and ground texture.
    /// Stored under <see cref="Scene3D"/> and serialised in .dsproj so reopening
    /// preserves the chosen look. Lives separate from <see cref="ProjectSettings"/>
    /// so the General Settings node stays focused on simulation defaults.
    /// </summary>
    public class EnvironmentSettings
    {
        [Category("Lighting")]
        [Description("Use a directional Sun + sky-ambient pair instead of HelixToolkit's flat DefaultLights. Turn off to recover the legacy look.")]
        public bool UseSunLighting { get; set; } = true;

        [Category("Lighting")]
        [Description("Sun azimuth in degrees (0 = North, clockwise). Controls horizontal direction of shadows.")]
        public double SunAzimuthDeg { get; set; } = 135;

        [Category("Lighting")]
        [Description("Sun elevation above horizon in degrees (0 = sunrise/sunset, 90 = noon overhead).")]
        public double SunElevationDeg { get; set; } = 55;

        [Category("Lighting")]
        [Description("Sun brightness multiplier (0–2). Higher = stronger highlights.")]
        public double SunIntensity { get; set; } = 1.0;

        [Category("Lighting")]
        [Description("Sky-ambient fill light intensity (0–1). Provides cool-tinted fill in shadowed areas.")]
        public double AmbientIntensity { get; set; } = 0.45;

        [Category("Sky")]
        [Description("Render a sky dome around the scene with a vertical gradient (zenith → horizon).")]
        public bool SkydomeEnabled { get; set; } = true;

        [Category("Sky")]
        [Description("Zenith colour (top of the sky dome).")]
        public System.Windows.Media.Color SkyZenithColor { get; set; }
            = System.Windows.Media.Color.FromRgb(80, 130, 200);

        [Category("Sky")]
        [Description("Horizon colour (bottom of the sky dome — blends into the ground).")]
        public System.Windows.Media.Color SkyHorizonColor { get; set; }
            = System.Windows.Media.Color.FromRgb(220, 225, 230);

        [Category("Ground")]
        [Description("Procedural ground material drawn on the editor's ground plane.")]
        public GroundMaterial Ground { get; set; } = GroundMaterial.Grass;

        [Category("Ground")]
        [Description("Whether to overlay the metric grid (5 m minor / 25 m major) on top of the ground texture.")]
        public bool ShowGridOverlay { get; set; } = true;
    }
}
