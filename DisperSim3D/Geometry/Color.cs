using System;

namespace DisperSim3D.Geometry
{
    /// <summary>
    /// Portable 32-bit ARGB color. API surface mirrors
    /// <c>System.Windows.Media.Color</c> so engine code (thresholds,
    /// environment settings, work-plane grid) can hold colors without
    /// depending on WPF. Implicit conversions to/from WPF Color are
    /// available while the engine still targets <c>net10.0-windows</c>;
    /// once Phase 5 retargets to <c>net10.0</c>, the WPF half is dropped
    /// and UI code converts at the boundary via
    /// <c>DisperSim3D.UI.Wpf.Geometry.WpfInterop.ToWpf()</c>.
    /// </summary>
    public struct Color : IEquatable<Color>
    {
        public byte A;
        public byte R;
        public byte G;
        public byte B;

        public Color(byte a, byte r, byte g, byte b)
        {
            A = a; R = r; G = g; B = b;
        }

        /// <summary>Opaque RGB constructor — matches <c>Color.FromRgb</c>.</summary>
        public static Color FromRgb(byte r, byte g, byte b) => new Color(255, r, g, b);

        /// <summary>Scaled (0..1) red component — matches WPF <c>ScR</c>.</summary>
        public float ScR => R / 255f;
        /// <summary>Scaled (0..1) green component — matches WPF <c>ScG</c>.</summary>
        public float ScG => G / 255f;
        /// <summary>Scaled (0..1) blue component — matches WPF <c>ScB</c>.</summary>
        public float ScB => B / 255f;
        /// <summary>Scaled (0..1) alpha component — matches WPF <c>ScA</c>.</summary>
        public float ScA => A / 255f;

        /// <summary>ARGB constructor — matches <c>Color.FromArgb</c>.</summary>
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new Color(a, r, g, b);

        public bool Equals(Color other) => A == other.A && R == other.R && G == other.G && B == other.B;
        public override bool Equals(object obj) => obj is Color c && Equals(c);
        public override int GetHashCode() => (A << 24) | (R << 16) | (G << 8) | B;

        public static bool operator ==(Color a, Color b) => a.Equals(b);
        public static bool operator !=(Color a, Color b) => !a.Equals(b);

        public override string ToString()
            => string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", A, R, G, B);

        /// <summary>Parses a hex color string of the form <c>#RRGGBB</c> or
        /// <c>#AARRGGBB</c> (case-insensitive, optional leading '#'). Throws
        /// <see cref="FormatException"/> on malformed input.</summary>
        public static Color Parse(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                throw new FormatException("Empty color string.");
            string s = hex.Trim();
            if (s.StartsWith("#")) s = s.Substring(1);
            byte a = 255, r, g, b;
            if (s.Length == 6)
            {
                r = Convert.ToByte(s.Substring(0, 2), 16);
                g = Convert.ToByte(s.Substring(2, 2), 16);
                b = Convert.ToByte(s.Substring(4, 2), 16);
            }
            else if (s.Length == 8)
            {
                a = Convert.ToByte(s.Substring(0, 2), 16);
                r = Convert.ToByte(s.Substring(2, 2), 16);
                g = Convert.ToByte(s.Substring(4, 2), 16);
                b = Convert.ToByte(s.Substring(6, 2), 16);
            }
            else
            {
                throw new FormatException("Color string must be #RRGGBB or #AARRGGBB. Got: " + hex);
            }
            return new Color(a, r, g, b);
        }

#if WINDOWS
        public static implicit operator System.Windows.Media.Color(Color c)
            => System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B);

        public static implicit operator Color(System.Windows.Media.Color c)
            => new Color(c.A, c.R, c.G, c.B);
#endif
    }

    /// <summary>Named color constants mirroring the most common
    /// <c>System.Windows.Media.Colors</c> entries actually used by the
    /// engine. Add new entries here as they're needed.</summary>
    public static class Colors
    {
        public static readonly Color Transparent = Color.FromArgb(0, 255, 255, 255);
        public static readonly Color Black       = Color.FromRgb(0, 0, 0);
        public static readonly Color White       = Color.FromRgb(255, 255, 255);
        public static readonly Color Red         = Color.FromRgb(255, 0, 0);
        public static readonly Color Green       = Color.FromRgb(0, 128, 0);
        public static readonly Color Blue        = Color.FromRgb(0, 0, 255);
        public static readonly Color Yellow      = Color.FromRgb(255, 255, 0);
        public static readonly Color Orange      = Color.FromRgb(255, 165, 0);
        public static readonly Color Gray        = Color.FromRgb(128, 128, 128);
        public static readonly Color LightGray   = Color.FromRgb(211, 211, 211);
        public static readonly Color DarkGray    = Color.FromRgb(169, 169, 169);
        public static readonly Color Cyan        = Color.FromRgb(0, 255, 255);
        public static readonly Color Magenta     = Color.FromRgb(255, 0, 255);
    }
}
