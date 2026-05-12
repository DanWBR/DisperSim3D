using System;
using System.Globalization;

namespace DisperSim3D.Geometry
{
    /// <summary>
    /// Portable double-precision 3D vector. API surface mirrors
    /// <c>System.Windows.Media.Media3D.Vector3D</c> so the engine can compile
    /// on non-Windows targets (net10.0) while keeping every existing call site
    /// — only the <c>using</c> directive changes per file. Struct semantics
    /// match WPF: <see cref="Normalize"/> and <see cref="Negate"/> mutate the
    /// instance in place, which works on locals/fields but is silently lost
    /// when called on a property getter result.
    /// </summary>
    public struct Vector3D : IEquatable<Vector3D>
    {
        public double X;
        public double Y;
        public double Z;

        public Vector3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>Euclidean length (sqrt of <see cref="LengthSquared"/>).</summary>
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        /// <summary>Sum of squared components — faster than <see cref="Length"/>
        /// when only comparisons are needed.</summary>
        public double LengthSquared => X * X + Y * Y + Z * Z;

        /// <summary>In-place normalization. No-op when length is zero (matches
        /// WPF, which produces NaN — we choose a safer no-op).</summary>
        public void Normalize()
        {
            double len = Length;
            if (len > 0)
            {
                X /= len;
                Y /= len;
                Z /= len;
            }
        }

        /// <summary>In-place negation of every component.</summary>
        public void Negate()
        {
            X = -X;
            Y = -Y;
            Z = -Z;
        }

        public static Vector3D operator +(Vector3D a, Vector3D b)
            => new Vector3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vector3D operator -(Vector3D a, Vector3D b)
            => new Vector3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vector3D operator -(Vector3D v)
            => new Vector3D(-v.X, -v.Y, -v.Z);

        public static Vector3D operator *(Vector3D v, double k)
            => new Vector3D(v.X * k, v.Y * k, v.Z * k);

        public static Vector3D operator *(double k, Vector3D v)
            => new Vector3D(v.X * k, v.Y * k, v.Z * k);

        public static Vector3D operator /(Vector3D v, double k)
            => new Vector3D(v.X / k, v.Y / k, v.Z / k);

        public static bool operator ==(Vector3D a, Vector3D b)
            => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

        public static bool operator !=(Vector3D a, Vector3D b)
            => !(a == b);

        public static Vector3D CrossProduct(Vector3D a, Vector3D b)
            => new Vector3D(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);

        public static double DotProduct(Vector3D a, Vector3D b)
            => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>Returns a negated copy of <paramref name="v"/> (WPF-compatible
        /// static helper; the instance <see cref="Negate"/> mutates in place).</summary>
        public static Vector3D Negate(Vector3D v) => -v;

        /// <summary>Angle between two vectors in degrees. Returns 0 if either is
        /// zero-length. Matches the WPF formula (acos of the dot product of
        /// normalized copies).</summary>
        public static double AngleBetween(Vector3D a, Vector3D b)
        {
            double la = a.Length;
            double lb = b.Length;
            if (la <= 0 || lb <= 0) return 0;
            double cos = (a.X * b.X + a.Y * b.Y + a.Z * b.Z) / (la * lb);
            if (cos > 1) cos = 1;
            else if (cos < -1) cos = -1;
            return Math.Acos(cos) * (180.0 / Math.PI);
        }

        public bool Equals(Vector3D other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Vector3D v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", X, Y, Z);

#if WINDOWS
        // ── WPF interop (temporary, for the Phase 4 cutover) ─────────────
        // See the matching block in Point3D.cs. Lets call sites that
        // assign a portable Vector3D to a WPF parameter (HelixToolkit,
        // AxisAngleRotation3D, ModelVisual3D.Transform, etc.) compile
        // without explicit conversions while the codebase migrates.
        public static implicit operator System.Windows.Media.Media3D.Vector3D(Vector3D v)
            => new System.Windows.Media.Media3D.Vector3D(v.X, v.Y, v.Z);

        public static implicit operator Vector3D(System.Windows.Media.Media3D.Vector3D v)
            => new Vector3D(v.X, v.Y, v.Z);
#endif
    }
}
