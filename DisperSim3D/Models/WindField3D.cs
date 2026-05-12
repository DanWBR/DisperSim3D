using System;
using DisperSim3D.Geometry;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Represents a 3D wind velocity field on a uniform grid, supporting trilinear interpolation at arbitrary positions.
    /// </summary>
    public class WindField3D
    {
        private readonly double[,,] _ux, _uy, _uz;
        private readonly int _nx, _ny, _nz;
        private readonly double _xMin, _yMin;
        private readonly double _cellSizeX, _cellSizeY, _cellSizeZ;

        /// <summary>Gets the number of grid cells in the X direction.</summary>
        public int Nx => _nx;

        /// <summary>Gets the number of grid cells in the Y direction.</summary>
        public int Ny => _ny;

        /// <summary>Gets the number of grid cells in the Z direction.</summary>
        public int Nz => _nz;

        /// <summary>
        /// Initializes a new instance of the <see cref="WindField3D"/> class from velocity component arrays and domain bounds.
        /// </summary>
        /// <param name="ux">3D array of X-component velocities indexed as [i, j, k].</param>
        /// <param name="uy">3D array of Y-component velocities indexed as [i, j, k].</param>
        /// <param name="uz">3D array of Z-component velocities indexed as [i, j, k].</param>
        /// <param name="xMin">Minimum X coordinate of the domain in meters.</param>
        /// <param name="xMax">Maximum X coordinate of the domain in meters.</param>
        /// <param name="yMin">Minimum Y coordinate of the domain in meters.</param>
        /// <param name="yMax">Maximum Y coordinate of the domain in meters.</param>
        /// <param name="zMax">Maximum Z coordinate (height) of the domain in meters. The minimum Z is assumed to be 0.</param>
        public WindField3D(double[,,] ux, double[,,] uy, double[,,] uz,
            double xMin, double xMax, double yMin, double yMax, double zMax)
        {
            _ux = ux;
            _uy = uy;
            _uz = uz;
            _nx = ux.GetLength(0);
            _ny = ux.GetLength(1);
            _nz = ux.GetLength(2);
            _xMin = xMin;
            _yMin = yMin;
            _cellSizeX = (xMax - xMin) / _nx;
            _cellSizeY = (yMax - yMin) / _ny;
            _cellSizeZ = zMax / _nz;
        }

        /// <summary>
        /// Computes the wind velocity at an arbitrary point using trilinear interpolation of the grid data.
        /// Coordinates outside the domain are clamped to the nearest boundary cell.
        /// </summary>
        /// <param name="x">The X coordinate in meters.</param>
        /// <param name="y">The Y coordinate in meters.</param>
        /// <param name="z">The Z coordinate (height) in meters.</param>
        /// <returns>The interpolated wind velocity vector (Vx, Vy, Vz).</returns>
        public Vector3D Interpolate(double x, double y, double z)
        {
            double fi = (x - _xMin) / _cellSizeX - 0.5;
            double fj = (y - _yMin) / _cellSizeY - 0.5;
            double fk = z / _cellSizeZ - 0.5;

            int i0 = (int)Math.Floor(fi);
            int j0 = (int)Math.Floor(fj);
            int k0 = (int)Math.Floor(fk);

            double dx = fi - i0;
            double dy = fj - j0;
            double dz = fk - k0;

            i0 = Clamp(i0, 0, _nx - 1);
            j0 = Clamp(j0, 0, _ny - 1);
            k0 = Clamp(k0, 0, _nz - 1);
            int i1 = Math.Min(i0 + 1, _nx - 1);
            int j1 = Math.Min(j0 + 1, _ny - 1);
            int k1 = Math.Min(k0 + 1, _nz - 1);

            dx = Math.Max(0, Math.Min(1, dx));
            dy = Math.Max(0, Math.Min(1, dy));
            dz = Math.Max(0, Math.Min(1, dz));

            double vx = Trilinear(_ux, i0, j0, k0, i1, j1, k1, dx, dy, dz);
            double vy = Trilinear(_uy, i0, j0, k0, i1, j1, k1, dx, dy, dz);
            double vz = Trilinear(_uz, i0, j0, k0, i1, j1, k1, dx, dy, dz);

            return new Vector3D(vx, vy, vz);
        }

        private static double Trilinear(double[,,] f,
            int i0, int j0, int k0, int i1, int j1, int k1,
            double dx, double dy, double dz)
        {
            double c000 = f[i0, j0, k0];
            double c100 = f[i1, j0, k0];
            double c010 = f[i0, j1, k0];
            double c110 = f[i1, j1, k0];
            double c001 = f[i0, j0, k1];
            double c101 = f[i1, j0, k1];
            double c011 = f[i0, j1, k1];
            double c111 = f[i1, j1, k1];

            double c00 = c000 * (1 - dx) + c100 * dx;
            double c10 = c010 * (1 - dx) + c110 * dx;
            double c01 = c001 * (1 - dx) + c101 * dx;
            double c11 = c011 * (1 - dx) + c111 * dx;

            double c0 = c00 * (1 - dy) + c10 * dy;
            double c1 = c01 * (1 - dy) + c11 * dy;

            return c0 * (1 - dz) + c1 * dz;
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
