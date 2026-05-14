using System;
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

        [Category("Lighting")]
        [Description("When enabled, sun azimuth and elevation are computed from time of day, latitude, and day of year instead of the manual angles above.")]
        public bool UseSolarClock { get; set; } = false;

        [Category("Lighting")]
        [Description("Site latitude in degrees (−90 to 90). Affects how high the sun climbs across the sky.")]
        public double Latitude { get; set; } = 40.0;

        [Category("Lighting")]
        [Description("Day of year (1–365). Controls seasonal sun height: 172 ≈ summer solstice, 355 ≈ winter solstice (Northern Hemisphere).")]
        public int DayOfYear { get; set; } = 172;

        [Category("Lighting")]
        [Description("Local solar time in hours (0–24). 6 ≈ sunrise, 12 = solar noon, 18 ≈ sunset.")]
        public double TimeOfDayHours { get; set; } = 10.0;

        [Category("Sky")]
        [Description("Render a sky dome around the scene with a vertical gradient (zenith → horizon).")]
        public bool SkydomeEnabled { get; set; } = true;

        [Category("Sky")]
        [Description("Show procedural animated clouds on the sky dome.")]
        public bool ShowClouds { get; set; } = true;

        [Category("Sky")]
        [Description("Cloud scroll speed multiplier (0 = frozen, 1 = gentle breeze, 3 = windy).")]
        public double CloudSpeed { get; set; } = 1.0;

        [Category("Sky")]
        [Description("Path to an equirectangular panorama image (JPG/PNG, 2:1 aspect ratio) used as the sky background. Leave empty to use the procedural sky.")]
        [FilePathEditor(
            "Image files|*.jpg;*.jpeg;*.png;*.hdr",
            new[] { "", "builtin:sky_clear_day", "builtin:sky_sunset", "builtin:sky_snowy_mountains" },
            new[] { "(None — Procedural Sky)", "Clear Day Road", "Sunset Rocky Coast", "Snowy Mountains" })]
        [Editor("DisperSim3D.Controls.FilePathPropertyEditor, DisperSim3D.UI.Wpf",
            "HandyControl.Controls.PropertyEditorBase, HandyControl")]
        public string SkyTexturePath { get; set; } = string.Empty;

        [Category("Sky")]
        [Description("Zenith colour (top of the sky dome).")]
        public DisperSim3D.Geometry.Color SkyZenithColor { get; set; }
            = DisperSim3D.Geometry.Color.FromRgb(80, 130, 200);

        [Category("Sky")]
        [Description("Horizon colour (bottom of the sky dome — blends into the ground).")]
        public DisperSim3D.Geometry.Color SkyHorizonColor { get; set; }
            = DisperSim3D.Geometry.Color.FromRgb(220, 225, 230);

        [Category("Ground")]
        [Description("Procedural ground material drawn on the editor's ground plane.")]
        public GroundMaterial Ground { get; set; } = GroundMaterial.Grass;

        [Category("Ground")]
        [Description("Whether to overlay the metric grid (5 m minor / 25 m major) on top of the ground texture.")]
        public bool ShowGridOverlay { get; set; } = true;

        [Category("Ground")]
        [Description("Show animated grass blades swaying in the wind on the ground plane.")]
        public bool ShowGrassBlades { get; set; } = true;

        [Category("Ground")]
        [Description("Number of grass blades to generate (higher = denser but slower). Typical: 5000–30000.")]
        public int GrassBladeCount { get; set; } = 18000;

        [Category("Ground")]
        [Description("Path to a texture image (PNG/JPG) tiled on the ground plane instead of the procedural material. Leave empty to use the procedural material.")]
        [FilePathEditor(
            "Image files|*.jpg;*.jpeg;*.png",
            new[] { "", "builtin:ground_woodland" },
            new[] { "(None — Procedural)", "Woodland Terrain" })]
        [Editor("DisperSim3D.Controls.FilePathPropertyEditor, DisperSim3D.UI.Wpf",
            "HandyControl.Controls.PropertyEditorBase, HandyControl")]
        public string GroundTexturePath { get; set; } = string.Empty;

        [Category("Ground")]
        [Description("World-space tile size in metres for the ground texture. Smaller = more repetitions.")]
        public double GroundTextureTileSize { get; set; } = 25.0;

        [Category("Ground")]
        [Description("Minor grid line spacing in metres.")]
        public double GridMinorSpacing { get; set; } = 5.0;

        [Category("Ground")]
        [Description("Major grid line spacing in metres (typically 5× the minor spacing).")]
        public double GridMajorSpacing { get; set; } = 25.0;

        [Category("Ground")]
        [Description("Half-extent of the grid in metres (grid spans from −size to +size).")]
        public double GridHalfSize { get; set; } = 100.0;

        /// <summary>
        /// Compute sun azimuth and elevation from latitude, day of year,
        /// and local solar time using the standard declination/hour-angle model.
        /// </summary>
        public (double AzimuthDeg, double ElevationDeg) ComputeSolarPosition()
        {
            double latRad = Latitude * Math.PI / 180.0;

            // Solar declination (Spencer, 1971 approximation)
            double decDeg = 23.45 * Math.Sin(2.0 * Math.PI / 365.0 * (284 + DayOfYear));
            double decRad = decDeg * Math.PI / 180.0;

            // Hour angle: 0 at solar noon, −15°/hr morning, +15°/hr afternoon
            double haDeg = 15.0 * (TimeOfDayHours - 12.0);
            double haRad = haDeg * Math.PI / 180.0;

            // Solar elevation (altitude)
            double sinEl = Math.Sin(latRad) * Math.Sin(decRad) +
                           Math.Cos(latRad) * Math.Cos(decRad) * Math.Cos(haRad);
            sinEl = Math.Clamp(sinEl, -1.0, 1.0);
            double elDeg = Math.Asin(sinEl) * 180.0 / Math.PI;

            // Solar azimuth (from North, clockwise)
            double cosEl = Math.Cos(elDeg * Math.PI / 180.0);
            double azDeg;
            if (Math.Abs(cosEl) < 1e-10)
            {
                azDeg = 180.0;
            }
            else
            {
                double cosAz = (Math.Sin(decRad) - Math.Sin(latRad) * sinEl) /
                               (Math.Cos(latRad) * cosEl);
                cosAz = Math.Clamp(cosAz, -1.0, 1.0);
                azDeg = Math.Acos(cosAz) * 180.0 / Math.PI;
                if (haDeg > 0) azDeg = 360.0 - azDeg;
            }

            return (azDeg, elDeg);
        }
    }
}
