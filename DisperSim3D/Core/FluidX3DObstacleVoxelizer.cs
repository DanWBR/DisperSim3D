using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Voxelizes decoration geometry into FluidX3D's lattice as TYPE_S cells. Walking
    /// the model tree must happen on the WPF UI thread because <c>Model3DGroup</c> is
    /// a <c>DependencyObject</c>; <see cref="ExtractWorldAabbs"/> performs that walk
    /// and returns plain value-typed <see cref="BoundingBox"/> objects that the runner
    /// can safely consume from a background worker.
    /// </summary>
    public static class FluidX3DObstacleVoxelizer
    {
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

            if (deco.Model3D != null)
            {
                var xform = deco.GetWorldTransform();
                WalkGroup(deco.Model3D, xform, result);
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
                sink.Add(new BoundingBox(new Point3D(mx, my, mz), new Point3D(Mx, My, Mz)));
            }
        }

        /// <summary>BACKGROUND-THREAD SAFE. Emits each AABB in the list as a TYPE_S box
        /// on the LBM lattice. Returns the number of boxes that actually made it (after
        /// sub-cell skips).</summary>
        public static int VoxelizeBoxes(IEnumerable<BoundingBox> aabbs, ulong lbmHandle,
            FluidX3DUnits units)
        {
            if (aabbs == null) return 0;
            int n = 0;
            foreach (var bb in aabbs)
            {
                if (bb == null) continue;
                var (xMin, yMin, zMin) = units.SiToLattice(bb.Min.X, bb.Min.Y, Math.Max(0, bb.Min.Z));
                var (xMax, yMax, zMax) = units.SiToLattice(bb.Max.X, bb.Max.Y, Math.Max(0, bb.Max.Z));
                uint x0 = xMin < xMax ? xMin : xMax;
                uint x1 = xMin < xMax ? xMax : xMin;
                uint y0 = yMin < yMax ? yMin : yMax;
                uint y1 = yMin < yMax ? yMax : yMin;
                uint z0 = zMin < zMax ? zMin : zMax;
                uint z1 = zMin < zMax ? zMax : zMin;
                // Skip degenerate sub-cell boxes — below voxel resolution.
                if (x1 - x0 < 1 && y1 - y0 < 1 && z1 - z0 < 1) continue;
                FluidX3DBridge.fx3d_set_box_solid(lbmHandle, x0, y0, z0, x1, y1, z1);
                n++;
            }
            return n;
        }
    }
}
