using System;
using System.ComponentModel;
using System.Globalization;

namespace DisperSim3D.Geometry
{
    /// <summary>
    /// Portable double-precision 3D point. API surface mirrors
    /// <c>System.Windows.Media.Media3D.Point3D</c> so engine code compiles
    /// unchanged after the namespace swap. The custom <see cref="TypeConverter"/>
    /// preserves the pt-BR safe "X;Y;Z" round-trip format used throughout the
    /// property grids — <c>System.Windows.Media.Media3D.Point3DConverter</c>
    /// reads "X,Y,Z" which is ambiguous with the European decimal comma.
    /// </summary>
    [TypeConverter(typeof(DisperSim3D.Core.Point3DStringConverter))]
    public struct Point3D : IEquatable<Point3D>
    {
        public double X;
        public double Y;
        public double Z;

        public Point3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>Point + Vector → translated Point.</summary>
        public static Point3D operator +(Point3D p, Vector3D v)
            => new Point3D(p.X + v.X, p.Y + v.Y, p.Z + v.Z);

        /// <summary>Point - Vector → translated Point.</summary>
        public static Point3D operator -(Point3D p, Vector3D v)
            => new Point3D(p.X - v.X, p.Y - v.Y, p.Z - v.Z);

        /// <summary>Point - Point → displacement Vector.</summary>
        public static Vector3D operator -(Point3D a, Point3D b)
            => new Vector3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static bool operator ==(Point3D a, Point3D b)
            => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

        public static bool operator !=(Point3D a, Point3D b)
            => !(a == b);

        /// <summary>Explicit cast to a position vector. Matches the WPF cast.</summary>
        public static explicit operator Vector3D(Point3D p)
            => new Vector3D(p.X, p.Y, p.Z);

#if WINDOWS
        // ── WPF interop (temporary, for the Phase 4 cutover) ─────────────
        // Implicit conversions to/from System.Windows.Media.Media3D.Point3D
        // keep the engine and the UI library compiling while the codebase
        // migrates from WPF Point3D to the portable type. Conditioned on the
        // Media3D types being available — they are while DisperSim3D.csproj
        // still has <UseWPF>true</UseWPF>; once Phase 5 drops that flag, this
        // block can be moved out to an interop extension method (see
        // DisperSim3D.UI.Wpf.Geometry.WpfInterop).
        public static implicit operator System.Windows.Media.Media3D.Point3D(Point3D p)
            => new System.Windows.Media.Media3D.Point3D(p.X, p.Y, p.Z);

        public static implicit operator Point3D(System.Windows.Media.Media3D.Point3D p)
            => new Point3D(p.X, p.Y, p.Z);
#endif

        public bool Equals(Point3D other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Point3D p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", X, Y, Z);
    }
}
