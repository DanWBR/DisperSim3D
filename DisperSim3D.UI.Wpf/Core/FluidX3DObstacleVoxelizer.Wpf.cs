using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// WPF-coupled companion of <see cref="FluidX3DObstacleVoxelizer"/>. Walks
    /// <c>Model3DGroup</c> trees on the UI thread and produces value-typed
    /// <see cref="TriangleBundle"/> / <see cref="BoundingBox"/> outputs that
    /// the engine voxelizer (<see cref="FluidX3DObstacleVoxelizer.VoxelizeTrianglesOnGpu"/>,
    /// <see cref="FluidX3DObstacleVoxelizer.VoxelizeBoxes"/>) can consume from a
    /// background worker.
    /// </summary>
    public static class FluidX3DObstacleVoxelizerWpf
    {
        /// <summary>UI-THREAD ONLY. Walks every decoration's Model3D tree and collects
        /// all triangle vertices in world space. The result is a flat triple of float
        /// arrays ready for <see cref="VoxelizeTrianglesOnGpu"/> on a background thread.
        /// Coordinates are in SI metres; the caller converts to lattice with
        /// <see cref="FluidX3DUnits.SiToLatticeF"/>.</summary>
        public static TriangleBundle ExtractWorldTriangles(IEnumerable<Decoration3D> decos)
        {
            var p0 = new List<float>();
            var p1 = new List<float>();
            var p2 = new List<float>();
            if (decos != null)
            {
                foreach (var deco in decos)
                {
                    if (deco?.Model3D is not Model3DGroup model) continue;
                    var xform = deco.GetWorldTransform();
                    WalkGroupTriangles(model, xform, p0, p1, p2);
                }
            }
            return new TriangleBundle
            {
                P0 = p0.ToArray(),
                P1 = p1.ToArray(),
                P2 = p2.ToArray(),
                TriangleCount = p0.Count / 3
            };
        }

        private static void WalkGroupTriangles(Model3DGroup group, Transform3D worldXform,
            List<float> p0, List<float> p1, List<float> p2)
        {
            if (group == null) return;
            foreach (var child in group.Children)
            {
                if (child is Model3DGroup g)
                    WalkGroupTriangles(g, worldXform, p0, p1, p2);
                else if (child is GeometryModel3D gm && gm.Geometry is MeshGeometry3D mesh)
                    EmitTriangleVerts(mesh, gm.Transform, worldXform, p0, p1, p2);
            }
        }

        private static void EmitTriangleVerts(MeshGeometry3D mesh, Transform3D localXform,
            Transform3D worldXform, List<float> p0, List<float> p1, List<float> p2)
        {
            var positions = mesh.Positions;
            var indices = mesh.TriangleIndices;
            int triCount = indices.Count >= 3 ? indices.Count / 3 : positions.Count / 3;
            for (int t = 0; t < triCount; t++)
            {
                int i0, i1, i2;
                if (indices.Count >= 3)
                {
                    i0 = indices[t * 3];
                    i1 = indices[t * 3 + 1];
                    i2 = indices[t * 3 + 2];
                }
                else { i0 = t * 3; i1 = t * 3 + 1; i2 = t * 3 + 2; }
                if (i0 >= positions.Count || i1 >= positions.Count || i2 >= positions.Count)
                    continue;

                var v0 = positions[i0]; var v1 = positions[i1]; var v2 = positions[i2];
                if (localXform != null)
                {
                    v0 = localXform.Transform(v0); v1 = localXform.Transform(v1); v2 = localXform.Transform(v2);
                }
                if (worldXform != null)
                {
                    v0 = worldXform.Transform(v0); v1 = worldXform.Transform(v1); v2 = worldXform.Transform(v2);
                }
                p0.Add((float)v0.X); p0.Add((float)v0.Y); p0.Add((float)v0.Z);
                p1.Add((float)v1.X); p1.Add((float)v1.Y); p1.Add((float)v1.Z);
                p2.Add((float)v2.X); p2.Add((float)v2.Y); p2.Add((float)v2.Z);
            }
        }

        /// <summary>
        /// UI-THREAD ONLY. Walks the decoration's <see cref="Decoration3D.Model3D"/> tree
        /// and produces one world-space AABB per child <see cref="GeometryModel3D"/>.
        /// Falls back to the decoration's overall <see cref="Decoration3D.BoundingBox"/>
        /// if the model isn't loaded or has no geometry children.
        /// </summary>
        public static IList<BoundingBox> ExtractWorldAabbs(Decoration3D deco)
        {
            var result = new List<BoundingBox>();
            if (deco == null) return result;

            if (deco.Model3D is Model3DGroup model)
            {
                var xform = deco.GetWorldTransform();
                WalkGroup(model, xform, result);
            }

            if (result.Count == 0 && deco.BoundingBox != null)
                result.Add(deco.BoundingBox);

            return result;
        }

        private static void WalkGroup(Model3DGroup group, Transform3D worldXform,
            List<BoundingBox> sink)
        {
            if (group == null) return;
            foreach (var child in group.Children)
            {
                if (child is Model3DGroup g)
                {
                    WalkGroup(g, worldXform, sink);
                }
                else if (child is GeometryModel3D gm)
                {
                    // Descend into the triangle mesh: importers (HelixToolkit etc) usually
                    // merge complex models into ONE GeometryModel3D, so the gm.Bounds is
                    // the whole-refinery AABB — useless as an obstacle. Instead, voxelize
                    // each TRIANGLE's AABB. For a 5k-triangle refinery this produces ~5k
                    // small solid boxes that collectively approximate the real shape.
                    if (gm.Geometry is MeshGeometry3D mesh)
                        EmitTriangleAabbs(mesh, gm.Transform, worldXform, sink);
                }
            }
        }

        /// <summary>
        /// Emits one AABB per triangle of the mesh, transformed into world space via the
        /// GeometryModel3D's own transform first, then the decoration's world transform.
        /// </summary>
        private static void EmitTriangleAabbs(MeshGeometry3D mesh, Transform3D localXform,
            Transform3D worldXform, List<BoundingBox> sink)
        {
            var positions = mesh.Positions;
            var indices = mesh.TriangleIndices;
            int triCount = indices.Count >= 3 ? indices.Count / 3
                                              : positions.Count / 3; // unindexed mesh
            for (int t = 0; t < triCount; t++)
            {
                int i0, i1, i2;
                if (indices.Count >= 3)
                {
                    i0 = indices[t * 3];
                    i1 = indices[t * 3 + 1];
                    i2 = indices[t * 3 + 2];
                }
                else
                {
                    i0 = t * 3; i1 = t * 3 + 1; i2 = t * 3 + 2;
                }
                if (i0 >= positions.Count || i1 >= positions.Count || i2 >= positions.Count)
                    continue;

                var p0 = positions[i0];
                var p1 = positions[i1];
                var p2 = positions[i2];

                if (localXform != null)
                {
                    p0 = localXform.Transform(p0);
                    p1 = localXform.Transform(p1);
                    p2 = localXform.Transform(p2);
                }
                if (worldXform != null)
                {
                    p0 = worldXform.Transform(p0);
                    p1 = worldXform.Transform(p1);
                    p2 = worldXform.Transform(p2);
                }

                double mx = Math.Min(p0.X, Math.Min(p1.X, p2.X));
                double Mx = Math.Max(p0.X, Math.Max(p1.X, p2.X));
                double my = Math.Min(p0.Y, Math.Min(p1.Y, p2.Y));
                double My = Math.Max(p0.Y, Math.Max(p1.Y, p2.Y));
                double mz = Math.Min(p0.Z, Math.Min(p1.Z, p2.Z));
                double Mz = Math.Max(p0.Z, Math.Max(p1.Z, p2.Z));
                sink.Add(new BoundingBox(new System.Windows.Media.Media3D.Point3D(mx, my, mz),
                                         new System.Windows.Media.Media3D.Point3D(Mx, My, Mz)));
            }
        }
    }
}
