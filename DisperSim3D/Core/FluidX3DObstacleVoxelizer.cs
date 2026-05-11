using System.Collections.Generic;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Voxelizes <see cref="Decoration3D.BoundingBox"/> AABBs into FluidX3D's lattice as
    /// TYPE_S (solid) cells. STL mesh voxelization is future work — for now any decoration
    /// is approximated by the AABB the loader already computed.
    /// </summary>
    public static class FluidX3DObstacleVoxelizer
    {
        public static void Voxelize(IEnumerable<Decoration3D> decorations, ulong lbmHandle,
            FluidX3DUnits units)
        {
            if (decorations == null) return;
            foreach (var d in decorations)
            {
                var bb = d?.BoundingBox;
                if (bb == null) continue;

                var (xMin, yMin, zMin) = units.SiToLattice(bb.Min.X, bb.Min.Y, System.Math.Max(0, bb.Min.Z));
                var (xMax, yMax, zMax) = units.SiToLattice(bb.Max.X, bb.Max.Y, System.Math.Max(0, bb.Max.Z));

                // Ensure ordering (SiToLattice clamps; min could equal max).
                uint x0 = xMin < xMax ? xMin : xMax;
                uint x1 = xMin < xMax ? xMax : xMin;
                uint y0 = yMin < yMax ? yMin : yMax;
                uint y1 = yMin < yMax ? yMax : yMin;
                uint z0 = zMin < zMax ? zMin : zMax;
                uint z1 = zMin < zMax ? zMax : zMin;

                FluidX3DBridge.fx3d_set_box_solid(lbmHandle, x0, y0, z0, x1, y1, z1);
            }
        }
    }
}
