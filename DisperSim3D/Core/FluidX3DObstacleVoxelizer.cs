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
        /// <summary>BACKGROUND-SAFE. Marks the lattice nodes the triangles occupy as
        /// TYPE_S, the way the dispersion tracers block their cells
        /// (<see cref="DecorationSolids"/>): nodes the surfaces cross and nodes they
        /// enclose. FluidX3D's own GPU voxelizer decides inside by ray parity, which
        /// needs closed meshes — an imported site, open at its ground and edges,
        /// came out as a lattice that was solid throughout. Returns the number of
        /// solid boxes written.</summary>
        public static int VoxelizeTriangles(TriangleBundle bundle, ulong lbmHandle,
            FluidX3DUnits units, int nx, int ny, int nz, double domainHalfM, double heightM)
        {
            if (bundle == null || bundle.TriangleCount == 0) return 0;
            // Lattice node i sits at −half + i·dx (FluidX3DUnits.SiToLattice), so
            // each node owns the cell centred on it.
            double dx = units.DxSi, dz = heightM / nz;
            bool[,,] mask = DecorationSolids.FromTriangles(bundle).Rasterize(
                -domainHalfM - dx / 2, -domainHalfM - dx / 2, -dz / 2, dx, dx, dz, nx, ny, nz);
            return VoxelizeMask(mask, lbmHandle);
        }

        /// <summary>BACKGROUND-SAFE. Writes a mask of lattice nodes as TYPE_S, merged
        /// into rectangles layer by layer. Returns the number of boxes written.</summary>
        public static int VoxelizeMask(bool[,,] mask, ulong lbmHandle)
        {
            int nx = mask.GetLength(0), ny = mask.GetLength(1), nz = mask.GetLength(2);
            int boxes = 0;
            for (int k = 0; k < nz; k++)
            {
                // Runs along x in the current row, each extended over the rows above
                // for as long as the same run repeats there.
                var open = new Dictionary<(int, int), int>();
                for (int j = 0; j <= ny; j++)
                {
                    var runs = new HashSet<(int, int)>();
                    if (j < ny)
                    {
                        for (int i = 0; i < nx; i++)
                        {
                            if (!mask[i, j, k]) continue;
                            int start = i;
                            while (i + 1 < nx && mask[i + 1, j, k]) i++;
                            runs.Add((start, i));
                        }
                    }
                    foreach (var run in new List<(int, int)>(open.Keys))
                    {
                        if (runs.Contains(run)) continue;
                        FluidX3DBridge.fx3d_set_box_solid(lbmHandle, (uint)run.Item1, (uint)open[run], (uint)k,
                            (uint)run.Item2, (uint)(j - 1), (uint)k);
                        open.Remove(run);
                        boxes++;
                    }
                    foreach (var run in runs)
                        if (!open.ContainsKey(run)) open[run] = j;
                }
            }
            return boxes;
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
