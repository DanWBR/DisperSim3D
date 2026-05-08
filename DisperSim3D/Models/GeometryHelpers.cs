using System;
using System.Windows.Media.Media3D;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Helper extensions for 3D geometry operations
    /// </summary>
    public static class GeometryExtensions
    {
        /// <summary>
        /// Snaps a 3D point to the nearest grid intersection based on the specified grid spacing.
        /// </summary>
        /// <param name="point">The point to snap.</param>
        /// <param name="gridSpacing">The grid cell size along each axis.</param>
        /// <returns>A new <see cref="Point3D"/> aligned to the nearest grid intersection.</returns>
        public static Point3D SnapToGrid(this Point3D point, double gridSpacing)
        {
            return new Point3D(
                Math.Round(point.X / gridSpacing) * gridSpacing,
                Math.Round(point.Y / gridSpacing) * gridSpacing,
                Math.Round(point.Z / gridSpacing) * gridSpacing
            );
        }

        /// <summary>
        /// Computes the Euclidean distance between two 3D points.
        /// </summary>
        /// <param name="p1">The first point.</param>
        /// <param name="p2">The second point.</param>
        /// <returns>The distance between <paramref name="p1"/> and <paramref name="p2"/>.</returns>
        public static double DistanceTo(this Point3D p1, Point3D p2)
        {
            return (p2 - p1).Length;
        }

        /// <summary>
        /// Returns a normalized (unit-length) copy of the vector.
        /// </summary>
        /// <param name="v">The vector to normalize.</param>
        /// <returns>A new <see cref="Vector3D"/> with the same direction and a length of 1.</returns>
        public static Vector3D Normalized(this Vector3D v)
        {
            v.Normalize();
            return v;
        }
    }

    /// <summary>
    /// Axis-aligned bounding box for collision detection
    /// </summary>
    public class BoundingBox
    {
        /// <summary>
        /// Gets or sets the minimum corner (lowest X, Y, Z) of the bounding box.
        /// </summary>
        public Point3D Min { get; set; }

        /// <summary>
        /// Gets or sets the maximum corner (highest X, Y, Z) of the bounding box.
        /// </summary>
        public Point3D Max { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BoundingBox"/> class with the specified corners.
        /// </summary>
        /// <param name="min">The minimum corner of the bounding box.</param>
        /// <param name="max">The maximum corner of the bounding box.</param>
        public BoundingBox(Point3D min, Point3D max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>
        /// Determines whether this bounding box intersects with another bounding box.
        /// </summary>
        /// <param name="other">The other bounding box to test against.</param>
        /// <returns><c>true</c> if the two bounding boxes overlap; otherwise, <c>false</c>.</returns>
        public bool Intersects(BoundingBox other)
        {
            return (Min.X <= other.Max.X && Max.X >= other.Min.X) &&
                   (Min.Y <= other.Max.Y && Max.Y >= other.Min.Y) &&
                   (Min.Z <= other.Max.Z && Max.Z >= other.Min.Z);
        }

        /// <summary>
        /// Determines whether this bounding box contains the specified point.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns><c>true</c> if the point lies within the bounding box; otherwise, <c>false</c>.</returns>
        public bool Contains(Point3D point)
        {
            return point.X >= Min.X && point.X <= Max.X &&
                   point.Y >= Min.Y && point.Y <= Max.Y &&
                   point.Z >= Min.Z && point.Z <= Max.Z;
        }

        /// <summary>
        /// Applies a 3D transform to all eight corners and returns a new axis-aligned bounding box enclosing the result.
        /// </summary>
        /// <param name="transform">The 3D transform to apply.</param>
        /// <returns>A new <see cref="BoundingBox"/> that encloses the transformed corners.</returns>
        public BoundingBox Transform(Transform3D transform)
        {
            var corners = new[]
            {
                transform.Transform(new Point3D(Min.X, Min.Y, Min.Z)),
                transform.Transform(new Point3D(Max.X, Min.Y, Min.Z)),
                transform.Transform(new Point3D(Min.X, Max.Y, Min.Z)),
                transform.Transform(new Point3D(Max.X, Max.Y, Min.Z)),
                transform.Transform(new Point3D(Min.X, Min.Y, Max.Z)),
                transform.Transform(new Point3D(Max.X, Min.Y, Max.Z)),
                transform.Transform(new Point3D(Min.X, Max.Y, Max.Z)),
                transform.Transform(new Point3D(Max.X, Max.Y, Max.Z))
            };

            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var minZ = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;
            var maxZ = double.MinValue;

            foreach (var corner in corners)
            {
                if (corner.X < minX) minX = corner.X;
                if (corner.Y < minY) minY = corner.Y;
                if (corner.Z < minZ) minZ = corner.Z;
                if (corner.X > maxX) maxX = corner.X;
                if (corner.Y > maxY) maxY = corner.Y;
                if (corner.Z > maxZ) maxZ = corner.Z;
            }

            return new BoundingBox(
                new Point3D(minX, minY, minZ),
                new Point3D(maxX, maxY, maxZ)
            );
        }
    }
}
