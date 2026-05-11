using System;
using System.Threading.Tasks;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// CPU-side passive-scalar transport solver. Given a steady wind field produced by
    /// FluidX3D (or OpenFOAM), advects a concentration tracer with semi-Lagrangian
    /// advection + explicit diffusion. Used because FluidX3D's built-in TEMPERATURE
    /// extension couples spuriously to the velocity field even with thermal expansion
    /// off, breaking the wind solution; doing the scalar transport here keeps the
    /// validated wind field intact and gives us full control over the species model.
    /// </summary>
    public class DispersionTracerEngine
    {
        public int Nx { get; }
        public int Ny { get; }
        public int Nz { get; }
        public double DxM { get; }
        public double DyM { get; }
        public double DzM { get; }
        public double DomainHalfM { get; }
        public double DomainHeightM { get; }

        private readonly double[,,] _ux, _uy, _uz;
        private readonly bool[,,] _blocked; // true = inside an obstacle, tracer stays 0 there
        private double[,,] _c;
        private double[,,] _cNext;
        private double _diffusivityM2PerS;
        private double _decayPerS;

        public DispersionTracerEngine(WindField3D wind, double domainHalfM, double domainHeightM,
            int nx, int ny, int nz,
            double diffusivityM2PerS, double decayPerS = 0.0,
            System.Collections.Generic.IList<BoundingBox> obstacles = null)
        {
            if (wind == null) throw new ArgumentNullException(nameof(wind));
            Nx = nx; Ny = ny; Nz = nz;
            DomainHalfM = domainHalfM;
            DomainHeightM = domainHeightM;
            DxM = 2.0 * domainHalfM / nx;
            DyM = 2.0 * domainHalfM / ny;
            DzM = domainHeightM / nz;
            _diffusivityM2PerS = diffusivityM2PerS;
            _decayPerS = decayPerS;

            // Sample the wind field once onto the engine's own grid (it may be a different
            // resolution than the LBM grid that produced it).
            _ux = new double[nx, ny, nz];
            _uy = new double[nx, ny, nz];
            _uz = new double[nx, ny, nz];
            for (int k = 0; k < nz; k++)
            {
                double z = (k + 0.5) * DzM;
                for (int j = 0; j < ny; j++)
                {
                    double y = -domainHalfM + (j + 0.5) * DyM;
                    for (int i = 0; i < nx; i++)
                    {
                        double x = -domainHalfM + (i + 0.5) * DxM;
                        var u = wind.Interpolate(x, y, z);
                        _ux[i, j, k] = u.X;
                        _uy[i, j, k] = u.Y;
                        _uz[i, j, k] = u.Z;
                    }
                }
            }

            _c = new double[nx, ny, nz];
            _cNext = new double[nx, ny, nz];

            // Build obstacle mask — cells whose centre is inside any BBox stay at C=0.
            // Without this, the tracer leaks through tank walls because the engine doesn't
            // know about solid geometry (we never voxelised obstacles for the CPU path).
            _blocked = new bool[nx, ny, nz];
            if (obstacles != null)
            {
                foreach (var bb in obstacles)
                {
                    if (bb == null) continue;
                    int i0 = Math.Max(0, (int)Math.Floor((bb.Min.X + domainHalfM) / DxM));
                    int i1 = Math.Min(nx - 1, (int)Math.Ceiling((bb.Max.X + domainHalfM) / DxM));
                    int j0 = Math.Max(0, (int)Math.Floor((bb.Min.Y + domainHalfM) / DyM));
                    int j1 = Math.Min(ny - 1, (int)Math.Ceiling((bb.Max.Y + domainHalfM) / DyM));
                    int k0 = Math.Max(0, (int)Math.Floor(Math.Max(0, bb.Min.Z) / DzM));
                    int k1 = Math.Min(nz - 1, (int)Math.Ceiling(Math.Max(0, bb.Max.Z) / DzM));
                    for (int k = k0; k <= k1; k++)
                        for (int j = j0; j <= j1; j++)
                            for (int i = i0; i <= i1; i++)
                                _blocked[i, j, k] = true;
                }
            }
        }

        /// <summary>Sets a spherical source: cells within <paramref name="radiusM"/> of
        /// (x,y,z) are clamped to <paramref name="concentration"/> at every step. The
        /// source also CARVES OUT a hole in the obstacle mask within the same sphere,
        /// so a leak located inside an equipment AABB (very common — the leak IS the
        /// equipment) can still vent into the surrounding atmosphere.</summary>
        public void SetSphericalSource(double xSi, double ySi, double zSi,
            double radiusM, double concentration)
        {
            _sourceX = xSi; _sourceY = ySi; _sourceZ = zSi;
            _sourceR = radiusM; _sourceC = concentration;
            _hasSource = true;
            CarveSourceHoleInMask();
            // Initial seed at t=0.
            ApplySource();
        }

        /// <summary>Clears _blocked[i,j,k] for every cell within the source sphere — so
        /// the post-step obstacle-mask pass doesn't immediately wipe out the tracer the
        /// source just injected.</summary>
        private void CarveSourceHoleInMask()
        {
            if (!_hasSource) return;
            double r2 = _sourceR * _sourceR;
            for (int k = 0; k < Nz; k++)
            {
                double z = (k + 0.5) * DzM;
                double dz = z - _sourceZ;
                if (dz * dz > r2) continue;
                for (int j = 0; j < Ny; j++)
                {
                    double y = -DomainHalfM + (j + 0.5) * DyM;
                    double dy = y - _sourceY;
                    if (dy * dy + dz * dz > r2) continue;
                    for (int i = 0; i < Nx; i++)
                    {
                        double x = -DomainHalfM + (i + 0.5) * DxM;
                        double dx = x - _sourceX;
                        if (dx * dx + dy * dy + dz * dz <= r2)
                            _blocked[i, j, k] = false;
                    }
                }
            }
        }

        private double _sourceX, _sourceY, _sourceZ, _sourceR, _sourceC;
        private bool _hasSource;

        private void ApplySource()
        {
            if (!_hasSource) return;
            double r2 = _sourceR * _sourceR;
            for (int k = 0; k < Nz; k++)
            {
                double z = (k + 0.5) * DzM;
                double dz = z - _sourceZ;
                if (dz * dz > r2) continue;
                for (int j = 0; j < Ny; j++)
                {
                    double y = -DomainHalfM + (j + 0.5) * DyM;
                    double dy = y - _sourceY;
                    if (dy * dy + dz * dz > r2) continue;
                    for (int i = 0; i < Nx; i++)
                    {
                        double x = -DomainHalfM + (i + 0.5) * DxM;
                        double dx = x - _sourceX;
                        // Source always injects, even into cells the obstacle mask would
                        // block. A leak position frequently coincides with the equipment
                        // bbox (it IS the equipment that's leaking) — masking it out
                        // produced zero plume. The post-step mask still prevents the
                        // tracer from accumulating elsewhere inside obstacles.
                        if (dx * dx + dy * dy + dz * dz <= r2)
                            _c[i, j, k] = _sourceC;
                    }
                }
            }
        }

        /// <summary>Returns the current concentration field (live reference).</summary>
        public double[,,] Snapshot() => _c;

        /// <summary>Advances the tracer by dt seconds. Returns the current concentration field.</summary>
        public double[,,] Step(double dtS)
        {
            // Semi-Lagrangian advection: for each cell, trace back along velocity to find
            // where the tracer "came from", then trilinearly interpolate.
            Parallel.For(0, Nz, k =>
            {
                double z = (k + 0.5) * DzM;
                for (int j = 0; j < Ny; j++)
                {
                    double y = -DomainHalfM + (j + 0.5) * DyM;
                    for (int i = 0; i < Nx; i++)
                    {
                        double x = -DomainHalfM + (i + 0.5) * DxM;
                        double sx = x - _ux[i, j, k] * dtS;
                        double sy = y - _uy[i, j, k] * dtS;
                        double sz = z - _uz[i, j, k] * dtS;
                        _cNext[i, j, k] = SampleTrilinear(sx, sy, sz);
                    }
                }
            });

            // Swap buffers, then apply explicit diffusion + decay.
            var tmp = _c; _c = _cNext; _cNext = tmp;

            if (_diffusivityM2PerS > 0)
            {
                double dx2 = DxM * DxM;
                double coeffX = _diffusivityM2PerS * dtS / dx2;
                double coeffY = _diffusivityM2PerS * dtS / (DyM * DyM);
                double coeffZ = _diffusivityM2PerS * dtS / (DzM * DzM);
                // Stability: sum of coeffs * 2 must be < 1. Subdivide dt if needed.
                int sub = (int)Math.Ceiling(2.0 * (coeffX + coeffY + coeffZ));
                if (sub < 1) sub = 1;
                coeffX /= sub; coeffY /= sub; coeffZ /= sub;
                for (int s = 0; s < sub; s++) ExplicitDiffusionStep(coeffX, coeffY, coeffZ);
            }

            if (_decayPerS > 0)
            {
                double k1 = Math.Exp(-_decayPerS * dtS);
                Parallel.For(0, Nz, k =>
                {
                    for (int j = 0; j < Ny; j++)
                        for (int i = 0; i < Nx; i++)
                            _c[i, j, k] *= k1;
                });
            }

            // Zero out obstacle cells — the tracer can't physically exist inside a solid.
            Parallel.For(0, Nz, k =>
            {
                for (int j = 0; j < Ny; j++)
                    for (int i = 0; i < Nx; i++)
                        if (_blocked[i, j, k]) _c[i, j, k] = 0;
            });

            // Re-impose the source each step (continuous release).
            ApplySource();
            return _c;
        }

        private void ExplicitDiffusionStep(double cx, double cy, double cz)
        {
            Parallel.For(1, Nz - 1, k =>
            {
                for (int j = 1; j < Ny - 1; j++)
                {
                    for (int i = 1; i < Nx - 1; i++)
                    {
                        double c0 = _c[i, j, k];
                        double lap =
                            cx * (_c[i + 1, j, k] + _c[i - 1, j, k] - 2 * c0) +
                            cy * (_c[i, j + 1, k] + _c[i, j - 1, k] - 2 * c0) +
                            cz * (_c[i, j, k + 1] + _c[i, j, k - 1] - 2 * c0);
                        _cNext[i, j, k] = c0 + lap;
                    }
                }
            });
            var tmp = _c; _c = _cNext; _cNext = tmp;
        }

        private double SampleTrilinear(double x, double y, double z)
        {
            // Convert SI position to fractional grid index.
            double fi = (x + DomainHalfM) / DxM - 0.5;
            double fj = (y + DomainHalfM) / DyM - 0.5;
            double fk = z / DzM - 0.5;

            int i0 = (int)Math.Floor(fi);
            int j0 = (int)Math.Floor(fj);
            int k0 = (int)Math.Floor(fk);
            double dx = fi - i0;
            double dy = fj - j0;
            double dz = fk - k0;

            i0 = Clamp(i0, 0, Nx - 2);
            j0 = Clamp(j0, 0, Ny - 2);
            k0 = Clamp(k0, 0, Nz - 2);

            double c000 = _c[i0, j0, k0];
            double c100 = _c[i0 + 1, j0, k0];
            double c010 = _c[i0, j0 + 1, k0];
            double c110 = _c[i0 + 1, j0 + 1, k0];
            double c001 = _c[i0, j0, k0 + 1];
            double c101 = _c[i0 + 1, j0, k0 + 1];
            double c011 = _c[i0, j0 + 1, k0 + 1];
            double c111 = _c[i0 + 1, j0 + 1, k0 + 1];

            double c00 = c000 * (1 - dx) + c100 * dx;
            double c10 = c010 * (1 - dx) + c110 * dx;
            double c01 = c001 * (1 - dx) + c101 * dx;
            double c11 = c011 * (1 - dx) + c111 * dx;
            double c0 = c00 * (1 - dy) + c10 * dy;
            double c1 = c01 * (1 - dy) + c11 * dy;
            return c0 * (1 - dz) + c1 * dz;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
