using System.Windows.Media.Media3D;
using PortablePoint3D = DisperSim3D.Geometry.Point3D;

namespace DisperSim3D.Models
{
    /// <summary>
    /// WPF-specific extensions for the engine's <see cref="BoundingBox"/>. The
    /// engine itself can't depend on <c>Transform3D</c>, so the transform-based
    /// overload lives here next to the WPF renderers and Decoration3D extension
    /// methods that need it.
    /// </summary>
    public static class BoundingBoxWpfExtensions
    {
        /// <summary>Applies a WPF 3D transform to all eight corners and returns
        /// a new axis-aligned bounding box enclosing the result.</summary>
        public static BoundingBox Transform(this BoundingBox bb, Transform3D transform)
        {
            var corners = new[]
            {
                transform.Transform(new Point3D(bb.Min.X, bb.Min.Y, bb.Min.Z)),
                transform.Transform(new Point3D(bb.Max.X, bb.Min.Y, bb.Min.Z)),
                transform.Transform(new Point3D(bb.Min.X, bb.Max.Y, bb.Min.Z)),
                transform.Transform(new Point3D(bb.Max.X, bb.Max.Y, bb.Min.Z)),
                transform.Transform(new Point3D(bb.Min.X, bb.Min.Y, bb.Max.Z)),
                transform.Transform(new Point3D(bb.Max.X, bb.Min.Y, bb.Max.Z)),
                transform.Transform(new Point3D(bb.Min.X, bb.Max.Y, bb.Max.Z)),
                transform.Transform(new Point3D(bb.Max.X, bb.Max.Y, bb.Max.Z))
            };

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

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
                new PortablePoint3D(minX, minY, minZ),
                new PortablePoint3D(maxX, maxY, maxZ));
        }
    }
}
