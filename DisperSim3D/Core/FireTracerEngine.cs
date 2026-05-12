using System;
using System.Threading.Tasks;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// CPU-side fire-plume transport solver. Reuses the same semi-Lagrangian
    /// advection + explicit diffusion approach as <see cref="DispersionTracerEngine"/>
    /// but advects TWO scalar fields simultaneously — temperature T (K) and a
    /// smoke / combustion-product mass fraction Y — and couples them through
    /// the Boussinesq buoyancy approximation.
    ///
    /// The buoyancy correction adds an upward velocity proportional to (T - T_amb)
    /// at each cell, so hot cells trace BACK from below (i.e., they came from
    /// lower in space — which is what physical rising plumes do). The correction
    /// is bounded so a 1500 K source cell doesn't shoot up faster than the LBM
    /// wind field itself can propagate, keeping the scheme stable.
    ///
    /// FluidX3D's GPU wind solve still runs cold (no thermal coupling in the
    /// LBM) — combining a clean ambient wind field with a CPU-side hot-plume
    /// engine avoids the bidirectional thermal/momentum coupling that previously
    /// broke FluidX3D's TEMPERATURE extension. Smoke and temperature feel the
    /// wind; the wind doesn't feel the smoke.
    /// </summary>
    public class FireTracerEngine
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
        private readonly bool[,,] _blocked;

        // Two transported scalars.
        private double[,,] _T, _Tnext;
        private double[,,] _Y, _Ynext;

        private readonly double _ambientT;
        private readonly double _thermalDiffM2PerS;
        private readonly double _speciesDiffM2PerS;
        private readonly double _thermalDecayPerS;   // radiative + entrainment cooling
        private readonly double _speciesDecayPerS;   // optional species half-life

        // Boussinesq buoyancy: v_buoy = β · g · (T − T_amb), β ≈ 1/T_amb.
        // Capped so a 1500 K cell can't translate more than ~5 m in one dt.
        private readonly double _gravityMPerS2 = 9.81;

        // Source state (clamped each step — continuous release).
        private double _sourceX, _sourceY, _sourceZ, _sourceR;
        private double _sourceT, _sourceY_smoke;
        private bool _hasSource;

        public FireTracerEngine(WindField3D wind,
            double domainHalfM, double domainHeightM,
            int nx, int ny, int nz,
            double thermalDiffusivityM2PerS,
            double speciesDiffusivityM2PerS,
            double ambientTemperatureK,
            double thermalDecayPerS = 0.0,
            double speciesDecayPerS = 0.0,
            System.Collections.Generic.IList<BoundingBox> obstacles = null)
        {
            if (wind == null) throw new ArgumentNullException(nameof(wind));
            Nx = nx; Ny = ny; Nz = nz;
            DomainHalfM = domainHalfM;
            DomainHeightM = domainHeightM;
            DxM = 2.0 * domainHalfM / nx;
            DyM = 2.0 * domainHalfM / ny;
            DzM = domainHeightM / nz;
            _thermalDiffM2PerS = thermalDiffusivityM2PerS;
            _speciesDiffM2PerS = speciesDiffusivityM2PerS;
            _ambientT = ambientTemperatureK;
            _thermalDecayPerS = thermalDecayPerS;
            _speciesDecayPerS = speciesDecayPerS;

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

            _T = new double[nx, ny, nz];
            _Tnext = new double[nx, ny, nz];
            _Y = new double[nx, ny, nz];
            _Ynext = new double[nx, ny, nz];

            // Initialise T = T_ambient everywhere (cold air at rest).
            for (int k = 0; k < nz; k++)
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                        _T[i, j, k] = _ambientT;

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

        /// <summary>Defines a continuous spherical fire source: cells within
        /// <paramref name="radiusM"/> of (x, y, z) are clamped to (sourceT, sourceY) at
        /// every step. T is in Kelvin (~1500–2000 K for hydrocarbon jet/pool fires);
        /// sourceY is the mass fraction of smoke/combustion products at the source
        /// (typically 1.0 — the source is "pure smoke" — and downwind dilution dilutes it).</summary>
        public void SetSphericalSource(double xSi, double ySi, double zSi,
            double radiusM, double sourceTemperatureK, double sourceY)
        {
            _sourceX = xSi; _sourceY = ySi; _sourceZ = zSi;
            _sourceR = radiusM;
            _sourceT = sourceTemperatureK;
            _sourceY_smoke = sourceY;
            _hasSource = true;
            CarveSourceHoleInMask();
            ApplySource();
        }

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
                        if (dx * dx + dy * dy + dz * dz <= r2)
                        {
                            _T[i, j, k] = _sourceT;
                            _Y[i, j, k] = _sourceY_smoke;
                        }
                    }
                }
            }
        }

        /// <summary>Live concentration / smoke mass-fraction field.</summary>
        public double[,,] SnapshotConcentration() => _Y;

        /// <summary>Live temperature field (K).</summary>
        public double[,,] SnapshotTemperature() => _T;

        /// <summary>Advances both transported fields by <paramref name="dtS"/> seconds
        /// with semi-Lagrangian advection (buoyancy-corrected effective velocity) +
        /// explicit diffusion + radiative-cooling decay.</summary>
        public double[,,] Step(double dtS)
        {
            // Boussinesq buoyancy: β g ΔT, with β ≈ 1/T_amb. Cap the buoyant
            // contribution so a hot cell can't translate further than half a cell
            // per dt — keeps semi-Lagrangian stable when ΔT = 1500 K.
            double betaG = _gravityMPerS2 / Math.Max(_ambientT, 200.0);
            double vBuoyMax = 0.5 * Math.Min(DxM, DzM) / Math.Max(dtS, 1e-6);

            Parallel.For(0, Nz, k =>
            {
                double z = (k + 0.5) * DzM;
                for (int j = 0; j < Ny; j++)
                {
                    double y = -DomainHalfM + (j + 0.5) * DyM;
                    for (int i = 0; i < Nx; i++)
                    {
                        double x = -DomainHalfM + (i + 0.5) * DxM;

                        // Effective vertical velocity for THIS cell, including
                        // buoyant rise. We use the CURRENT cell T as proxy for
                        // the parcel-of-air temperature in the back-trace.
                        double dT = _T[i, j, k] - _ambientT;
                        double vBuoy = betaG * dT;
                        if (vBuoy > vBuoyMax) vBuoy = vBuoyMax;
                        else if (vBuoy < -vBuoyMax) vBuoy = -vBuoyMax;

                        double sx = x - _ux[i, j, k] * dtS;
                        double sy = y - _uy[i, j, k] * dtS;
                        double sz = z - (_uz[i, j, k] + vBuoy) * dtS;

                        _Tnext[i, j, k] = SampleTrilinear(_T, sx, sy, sz, _ambientT);
                        _Ynext[i, j, k] = SampleTrilinear(_Y, sx, sy, sz, 0.0);
                    }
                }
            });

            // Swap buffers.
            var tmpT = _T; _T = _Tnext; _Tnext = tmpT;
            var tmpY = _Y; _Y = _Ynext; _Ynext = tmpY;

            // Diffusion (explicit) — separate diffusivities for T and Y.
            ExplicitDiffuse(_T, _ambientT, _thermalDiffM2PerS, dtS, isTemperature: true);
            ExplicitDiffuse(_Y, 0.0, _speciesDiffM2PerS, dtS, isTemperature: false);

            // Radiative cooling for T (first-order toward ambient) + species decay.
            if (_thermalDecayPerS > 0)
            {
                double k1 = Math.Exp(-_thermalDecayPerS * dtS);
                Parallel.For(0, Nz, k =>
                {
                    for (int j = 0; j < Ny; j++)
                        for (int i = 0; i < Nx; i++)
                            _T[i, j, k] = _ambientT + (_T[i, j, k] - _ambientT) * k1;
                });
            }
            if (_speciesDecayPerS > 0)
            {
                double k1 = Math.Exp(-_speciesDecayPerS * dtS);
                Parallel.For(0, Nz, k =>
                {
                    for (int j = 0; j < Ny; j++)
                        for (int i = 0; i < Nx; i++)
                            _Y[i, j, k] *= k1;
                });
            }

            // Zero out obstacle cells (T → ambient, Y → 0).
            Parallel.For(0, Nz, k =>
            {
                for (int j = 0; j < Ny; j++)
                    for (int i = 0; i < Nx; i++)
                        if (_blocked[i, j, k])
                        {
                            _T[i, j, k] = _ambientT;
                            _Y[i, j, k] = 0;
                        }
            });

            // Re-impose the source.
            ApplySource();
            return _Y;
        }

        private void ExplicitDiffuse(double[,,] f, double restState, double diffusivity, double dtS, bool isTemperature)
        {
            if (diffusivity <= 0) return;
            double dx2 = DxM * DxM;
            double coeffX = diffusivity * dtS / dx2;
            double coeffY = diffusivity * dtS / (DyM * DyM);
            double coeffZ = diffusivity * dtS / (DzM * DzM);
            int sub = (int)Math.Ceiling(2.0 * (coeffX + coeffY + coeffZ));
            if (sub < 1) sub = 1;
            coeffX /= sub; coeffY /= sub; coeffZ /= sub;
            var fNext = isTemperature ? _Tnext : _Ynext;
            for (int s = 0; s < sub; s++)
            {
                Parallel.For(1, Nz - 1, k =>
                {
                    for (int j = 1; j < Ny - 1; j++)
                    {
                        for (int i = 1; i < Nx - 1; i++)
                        {
                            double c0 = f[i, j, k];
                            double lap =
                                coeffX * (f[i + 1, j, k] + f[i - 1, j, k] - 2 * c0) +
                                coeffY * (f[i, j + 1, k] + f[i, j - 1, k] - 2 * c0) +
                                coeffZ * (f[i, j, k + 1] + f[i, j, k - 1] - 2 * c0);
                            fNext[i, j, k] = c0 + lap;
                        }
                    }
                });
                // Swap f ↔ fNext for next sub-step.
                if (isTemperature) { var t = _T; _T = _Tnext; _Tnext = t; f = _T; fNext = _Tnext; }
                else               { var t = _Y; _Y = _Ynext; _Ynext = t; f = _Y; fNext = _Ynext; }
            }
        }

        private double SampleTrilinear(double[,,] field, double x, double y, double z, double outsideValue)
        {
            double fi = (x + DomainHalfM) / DxM - 0.5;
            double fj = (y + DomainHalfM) / DyM - 0.5;
            double fk = z / DzM - 0.5;

            // Cells outside the domain — return the rest-state value (T_amb or 0).
            if (fi < 0 || fi > Nx - 1 || fj < 0 || fj > Ny - 1 || fk < 0 || fk > Nz - 1)
                return outsideValue;

            int i0 = (int)Math.Floor(fi);
            int j0 = (int)Math.Floor(fj);
            int k0 = (int)Math.Floor(fk);
            double dx = fi - i0;
            double dy = fj - j0;
            double dz = fk - k0;

            i0 = Clamp(i0, 0, Nx - 2);
            j0 = Clamp(j0, 0, Ny - 2);
            k0 = Clamp(k0, 0, Nz - 2);

            double c000 = field[i0, j0, k0];
            double c100 = field[i0 + 1, j0, k0];
            double c010 = field[i0, j0 + 1, k0];
            double c110 = field[i0 + 1, j0 + 1, k0];
            double c001 = field[i0, j0, k0 + 1];
            double c101 = field[i0 + 1, j0, k0 + 1];
            double c011 = field[i0, j0 + 1, k0 + 1];
            double c111 = field[i0 + 1, j0 + 1, k0 + 1];

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
