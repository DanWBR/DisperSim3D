using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Clips 3D mesh geometry along an axis-aligned plane. (The <see cref="ClipAxis"/>
    /// enum itself is defined in the engine project so models can persist their
    /// chosen axis without dragging in WPF.)
    /// </summary>
    public static class MeshClipper
    {
        /// <summary>
        /// Clips all meshes in a <see cref="Model3DGroup"/> along the specified axis and plane position,
        /// keeping geometry on one side of the plane.
        /// </summary>
        /// <param name="source">The model group to clip.</param>
        /// <param name="axis">The axis perpendicular to the clipping plane.</param>
        /// <param name="value">The position of the clipping plane along the axis.</param>
        /// <param name="keepAbove">If <c>true</c>, keeps geometry above the plane; otherwise keeps geometry below.</param>
        /// <returns>A new <see cref="Model3DGroup"/> containing the clipped geometry, or <c>null</c> if source is null.</returns>
        public static Model3DGroup ClipModel(Model3DGroup source, ClipAxis axis, double value, bool keepAbove)
        {
            if (source == null) return null;

            var result = new Model3DGroup();
            foreach (var child in source.Children)
            {
                if (child is GeometryModel3D gm && gm.Geometry is MeshGeometry3D mesh)
                {
                    var clipped = ClipMesh(mesh, axis, value, keepAbove);
                    if (clipped != null && clipped.Positions.Count > 0)
                    {
                        result.Children.Add(new GeometryModel3D
                        {
                            Geometry = clipped,
                            Material = gm.Material,
                            BackMaterial = gm.BackMaterial,
                            Transform = gm.Transform
                        });
                    }
                }
                else if (child is Model3DGroup subGroup)
                {
                    var clippedSub = ClipModel(subGroup, axis, value, keepAbove);
                    if (clippedSub != null && clippedSub.Children.Count > 0)
                        result.Children.Add(clippedSub);
                }
                else
                {
                    result.Children.Add(child.Clone());
                }
            }
            return result;
        }

        private static double GetComponent(Point3D p, ClipAxis axis)
        {
            switch (axis)
            {
                case ClipAxis.X: return p.X;
                case ClipAxis.Y: return p.Y;
                default: return p.Z;
            }
        }

        private static bool IsKept(Point3D p, ClipAxis axis, double value, bool keepAbove)
        {
            double c = GetComponent(p, axis);
            return keepAbove ? c >= value : c <= value;
        }

        private static Point3D Interpolate(Point3D a, Point3D b, ClipAxis axis, double value)
        {
            double ca = GetComponent(a, axis);
            double cb = GetComponent(b, axis);
            double denom = cb - ca;
            if (denom == 0) return a;
            double t = (value - ca) / denom;
            return new Point3D(
                a.X + t * (b.X - a.X),
                a.Y + t * (b.Y - a.Y),
                a.Z + t * (b.Z - a.Z));
        }

        private static MeshGeometry3D ClipMesh(MeshGeometry3D mesh, ClipAxis axis, double value, bool keepAbove)
        {
            var positions = new List<Point3D>();
            var indices = new List<int>();
            var posMap = new Dictionary<long, int>();

            bool hasNormals = mesh.Normals != null && mesh.Normals.Count == mesh.Positions.Count;
            bool hasTexCoords = mesh.TextureCoordinates != null && mesh.TextureCoordinates.Count == mesh.Positions.Count;
            var normals = hasNormals ? new List<Vector3D>() : null;
            var texCoords = hasTexCoords ? new List<System.Windows.Point>() : null;

            int triCount = mesh.TriangleIndices.Count / 3;
            for (int t = 0; t < triCount; t++)
            {
                int i0 = mesh.TriangleIndices[t * 3];
                int i1 = mesh.TriangleIndices[t * 3 + 1];
                int i2 = mesh.TriangleIndices[t * 3 + 2];

                Point3D p0 = mesh.Positions[i0];
                Point3D p1 = mesh.Positions[i1];
                Point3D p2 = mesh.Positions[i2];

                bool k0 = IsKept(p0, axis, value, keepAbove);
                bool k1 = IsKept(p1, axis, value, keepAbove);
                bool k2 = IsKept(p2, axis, value, keepAbove);

                int kept = (k0 ? 1 : 0) + (k1 ? 1 : 0) + (k2 ? 1 : 0);

                if (kept == 3)
                {
                    AddVertex(positions, indices, p0);
                    AddVertex(positions, indices, p1);
                    AddVertex(positions, indices, p2);
                }
                else if (kept == 0)
                {
                    continue;
                }
                else if (kept == 2)
                {
                    Point3D outside, insideA, insideB;
                    if (!k0) { outside = p0; insideA = p1; insideB = p2; }
                    else if (!k1) { outside = p1; insideA = p2; insideB = p0; }
                    else { outside = p2; insideA = p0; insideB = p1; }

                    Point3D clipA = Interpolate(outside, insideA, axis, value);
                    Point3D clipB = Interpolate(outside, insideB, axis, value);

                    AddVertex(positions, indices, insideA);
                    AddVertex(positions, indices, insideB);
                    AddVertex(positions, indices, clipA);

                    AddVertex(positions, indices, insideB);
                    AddVertex(positions, indices, clipB);
                    AddVertex(positions, indices, clipA);
                }
                else
                {
                    Point3D inside, outsideA, outsideB;
                    if (k0) { inside = p0; outsideA = p1; outsideB = p2; }
                    else if (k1) { inside = p1; outsideA = p2; outsideB = p0; }
                    else { inside = p2; outsideA = p0; outsideB = p1; }

                    Point3D clipA = Interpolate(inside, outsideA, axis, value);
                    Point3D clipB = Interpolate(inside, outsideB, axis, value);

                    AddVertex(positions, indices, inside);
                    AddVertex(positions, indices, clipA);
                    AddVertex(positions, indices, clipB);
                }
            }

            if (positions.Count == 0) return null;

            var result = new MeshGeometry3D();
            foreach (var p in positions)
                result.Positions.Add(p);
            foreach (var i in indices)
                result.TriangleIndices.Add(i);

            return result;
        }

        private static void AddVertex(List<Point3D> positions, List<int> indices, Point3D p)
        {
            int idx = positions.Count;
            positions.Add(p);
            indices.Add(idx);
        }
    }
}
