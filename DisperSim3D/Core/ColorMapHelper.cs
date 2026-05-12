using System;
using DisperSim3D.Geometry;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Identifies the available color map palettes.
    /// </summary>
    public enum ColorMapName
    {
        /// <summary>Rainbow palette from blue through green and yellow to red.</summary>
        Jet,
        /// <summary>Perceptually uniform palette from dark purple to yellow.</summary>
        Viridis,
        /// <summary>Perceptually uniform palette from black through red-orange to pale yellow.</summary>
        Inferno,
        /// <summary>Diverging palette from cool blue through white to warm red.</summary>
        Coolwarm
    }

    /// <summary>
    /// Provides color map sampling and interpolation utilities.
    /// </summary>
    public static class ColorMapHelper
    {
        /// <summary>
        /// Samples a color from the specified color map at the given normalized position.
        /// </summary>
        /// <param name="map">The color map to sample from.</param>
        /// <param name="t">Normalized value in [0, 1]; values outside this range are clamped.</param>
        /// <returns>The interpolated <see cref="Color"/> at position <paramref name="t"/>.</returns>
        public static Color Sample(ColorMapName map, double t)
        {
            t = Math.Max(0.0, Math.Min(1.0, t));
            switch (map)
            {
                case ColorMapName.Jet: return SampleJet(t);
                case ColorMapName.Viridis: return SampleViridis(t);
                case ColorMapName.Inferno: return SampleInferno(t);
                case ColorMapName.Coolwarm: return SampleCoolwarm(t);
                default: return SampleJet(t);
            }
        }

        private static Color SampleJet(double t)
        {
            double r, g, b;
            if (t < 0.125) { r = 0; g = 0; b = 0.5 + t / 0.125 * 0.5; }
            else if (t < 0.375) { r = 0; g = (t - 0.125) / 0.25; b = 1; }
            else if (t < 0.625) { r = (t - 0.375) / 0.25; g = 1; b = 1 - (t - 0.375) / 0.25; }
            else if (t < 0.875) { r = 1; g = 1 - (t - 0.625) / 0.25; b = 0; }
            else { r = 1 - (t - 0.875) / 0.125 * 0.5; g = 0; b = 0; }
            return Color.FromRgb(ToByte(r), ToByte(g), ToByte(b));
        }

        private static Color SampleViridis(double t)
        {
            // Simplified 5-stop Viridis
            Color[] stops = {
                Color.FromRgb(68, 1, 84),
                Color.FromRgb(59, 82, 139),
                Color.FromRgb(33, 145, 140),
                Color.FromRgb(94, 201, 98),
                Color.FromRgb(253, 231, 37)
            };
            return InterpolateStops(stops, t);
        }

        private static Color SampleInferno(double t)
        {
            Color[] stops = {
                Color.FromRgb(0, 0, 4),
                Color.FromRgb(87, 16, 110),
                Color.FromRgb(188, 55, 84),
                Color.FromRgb(249, 142, 9),
                Color.FromRgb(252, 255, 164)
            };
            return InterpolateStops(stops, t);
        }

        private static Color SampleCoolwarm(double t)
        {
            Color[] stops = {
                Color.FromRgb(59, 76, 192),
                Color.FromRgb(141, 176, 254),
                Color.FromRgb(221, 221, 221),
                Color.FromRgb(245, 156, 125),
                Color.FromRgb(180, 4, 38)
            };
            return InterpolateStops(stops, t);
        }

        private static Color InterpolateStops(Color[] stops, double t)
        {
            int n = stops.Length - 1;
            double scaled = t * n;
            int idx = (int)Math.Floor(scaled);
            if (idx >= n) return stops[n];
            double frac = scaled - idx;
            return Lerp(stops[idx], stops[idx + 1], frac);
        }

        /// <summary>
        /// Linearly interpolates between two colors.
        /// </summary>
        /// <param name="a">Start color (at <paramref name="t"/> = 0).</param>
        /// <param name="b">End color (at <paramref name="t"/> = 1).</param>
        /// <param name="t">Interpolation factor, typically in [0, 1].</param>
        /// <returns>The interpolated <see cref="Color"/>.</returns>
        public static Color Lerp(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t),
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        private static byte ToByte(double v)
        {
            return (byte)(Math.Max(0, Math.Min(1, v)) * 255);
        }
    }
}
