using System;
using System.Collections.Generic;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Voxelizes decoration geometry into FluidX3D's lattice as TYPE_S cells.
    /// Engine-portable half: takes pre-extracted <see cref="TriangleBundle"/>
    /// or <see cref="BoundingBox"/> data and dispatches to FluidX3D. The
    /// WPF-coupled half that walks <c>Model3DGroup</c> trees lives in
    /// <c>DisperSim3D.UI.Wpf</c> as a partial of the same class.
    /// </summary>
    /// <summary>Flat triangle bundle in world-space SI coords. Returned by
    /// <c>FluidX3DObstacleVoxelizer.ExtractWorldTriangles</c> for the GPU
    /// voxelization path — each array has length <c>3 * TriangleCount</c>.</summary>
    public sealed class TriangleBundle
    {
        public float[] P0;
        public float[] P1;
        public float[] P2;
        public int TriangleCount;
    }

    public static class FluidX3DObstacleVoxelizer
    {
        /// <summary>BACKGROUND-SAFE. Converts world-SI triangle vertices to lattice
        /// coords and dispatches FluidX3D's GPU raycasting voxelizer. Returns the
        /// triangle count actually processed (0 = nothing voxelized).</summary>
        public static int VoxelizeTrianglesOnGpu(TriangleBundle bundle, ulong lbmHandle,
            FluidX3DUnits units)
        {
            if (bundle == null || bundle.TriangleCount == 0) return 0;
            int n3 = bundle.TriangleCount * 3;
            var p0L = new float[n3];
            var p1L = new float[n3];
            var p2L = new float[n3];
            for (int i = 0; i < bundle.TriangleCount; i++)
            {
                int b = 3 * i;
                var (x0, y0, z0) = units.SiToLatticeF(bundle.P0[b], bundle.P0[b + 1], bundle.P0[b + 2]);
                var (x1, y1, z1) = units.SiToLatticeF(bundle.P1[b], bundle.P1[b + 1], bundle.P1[b + 2]);
                var (x2, y2, z2) = units.SiToLatticeF(bundle.P2[b], bundle.P2[b + 1], bundle.P2[b + 2]);
                p0L[b] = x0; p0L[b + 1] = y0; p0L[b + 2] = z0;
                p1L[b] = x1; p1L[b + 1] = y1; p1L[b + 2] = z1;
                p2L[b] = x2; p2L[b + 1] = y2; p2L[b + 2] = z2;
            }
            FluidX3DBridge.fx3d_voxelize_triangles(lbmHandle, p0L, p1L, p2L, (uint)bundle.TriangleCount);
            return bundle.TriangleCount;
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
