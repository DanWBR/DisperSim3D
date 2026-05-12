using System;
using DisperSim3D.Geometry;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Provides trilinear interpolation of concentration values from a 3D OpenFOAM scalar field.
    /// Implements <see cref="IConcentrationField"/> for integration with the dispersion visualization pipeline.
    /// </summary>
    public class OpenFoamConcentrationField : IConcentrationField
    {
        private readonly double[,,] _field;
        private readonly Point3D _origin;
        private readonly double _cellSize;
        private readonly int _nx, _ny, _nz;

        private readonly double _cellSizeX, _cellSizeY, _cellSizeZ;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenFoamConcentrationField"/> class
        /// with a symmetric domain centered at the origin.
        /// </summary>
        /// <param name="field">The 3D scalar field array indexed as [x, y, z].</param>
        /// <param name="domainSize">The half-size of the domain in meters. The domain spans from -domainSize to +domainSize in X and Y, and from 0 to 2*domainSize in Z.</param>
        /// <param name="gridRes">The grid resolution (number of cells per axis used to compute cell size).</param>
        public OpenFoamConcentrationField(double[,,] field, double domainSize, int gridRes)
        {
            _field = field;
            _nx = field.GetLength(0);
            _ny = field.GetLength(1);
            _nz = field.GetLength(2);
            _cellSize = (domainSize * 2.0) / gridRes;
            _cellSizeX = _cellSize;
            _cellSizeY = _cellSize;
            _cellSizeZ = _cellSize;
            _origin = new Point3D(-domainSize, -domainSize, 0);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenFoamConcentrationField"/> class
        /// with explicit domain bounds.
        /// </summary>
        /// <param name="field">The 3D scalar field array indexed as [x, y, z].</param>
        /// <param name="xMin">Domain minimum X coordinate in meters.</param>
        /// <param name="xMax">Domain maximum X coordinate in meters.</param>
        /// <param name="yMin">Domain minimum Y coordinate in meters.</param>
        /// <param name="yMax">Domain maximum Y coordinate in meters.</param>
        /// <param name="zMax">Domain maximum Z coordinate in meters (Z starts at 0).</param>
        public OpenFoamConcentrationField(double[,,] field,
            double xMin, double xMax, double yMin, double yMax, double zMax)
        {
            _field = field;
            _nx = field.GetLength(0);
            _ny = field.GetLength(1);
            _nz = field.GetLength(2);
            _cellSizeX = (_nx > 0) ? (xMax - xMin) / _nx : 1;
            _cellSizeY = (_ny > 0) ? (yMax - yMin) / _ny : 1;
            _cellSizeZ = (_nz > 0) ? zMax / _nz : 1;
            _cellSize = _cellSizeX;
            _origin = new Point3D(xMin, yMin, 0);
        }

        /// <summary>
        /// Evaluates the concentration at the specified world-space coordinates using trilinear interpolation.
        /// Falls back to nearest-cell lookup for points outside the interpolation interior.
        /// </summary>
        /// <param name="x">The X coordinate in meters.</param>
        /// <param name="y">The Y coordinate in meters.</param>
        /// <param name="z">The Z coordinate in meters.</param>
        /// <returns>The interpolated concentration value at the given point.</returns>
        public double EvaluateConcentration(double x, double y, double z)
        {
            double fi = (x - _origin.X) / _cellSizeX;
            double fj = (y - _origin.Y) / _cellSizeY;
            double fk = (z - _origin.Z) / _cellSizeZ;

            int i0 = (int)Math.Floor(fi);
            int j0 = (int)Math.Floor(fj);
            int k0 = (int)Math.Floor(fk);

            if (i0 < 0 || i0 >= _nx - 1 || j0 < 0 || j0 >= _ny - 1 || k0 < 0 || k0 >= _nz - 1)
            {
                int ic = Math.Max(0, Math.Min(_nx - 1, (int)Math.Round(fi)));
                int jc = Math.Max(0, Math.Min(_ny - 1, (int)Math.Round(fj)));
                int kc = Math.Max(0, Math.Min(_nz - 1, (int)Math.Round(fk)));
                return _field[ic, jc, kc];
            }

            double dx = fi - i0;
            double dy = fj - j0;
            double dz = fk - k0;

            double c000 = _field[i0, j0, k0];
            double c100 = _field[i0 + 1, j0, k0];
            double c010 = _field[i0, j0 + 1, k0];
            double c110 = _field[i0 + 1, j0 + 1, k0];
            double c001 = _field[i0, j0, k0 + 1];
            double c101 = _field[i0 + 1, j0, k0 + 1];
            double c011 = _field[i0, j0 + 1, k0 + 1];
            double c111 = _field[i0 + 1, j0 + 1, k0 + 1];

            double c00 = c000 * (1 - dx) + c100 * dx;
            double c10 = c010 * (1 - dx) + c110 * dx;
            double c01 = c001 * (1 - dx) + c101 * dx;
            double c11 = c011 * (1 - dx) + c111 * dx;

            double c0 = c00 * (1 - dy) + c10 * dy;
            double c1 = c01 * (1 - dy) + c11 * dy;

            return c0 * (1 - dz) + c1 * dz;
        }
    }
}
