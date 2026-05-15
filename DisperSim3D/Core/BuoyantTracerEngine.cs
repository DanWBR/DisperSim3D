using System;
using System.Threading.Tasks;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// CPU-side buoyant scalar transport solver. Advects concentration (mass
    /// fraction) and temperature on the same grid, with density-based buoyancy
    /// and gravity-current lateral spreading for dense/cryogenic gas clouds.
    ///
    /// Buoyancy:
    ///   M_mix = 1 / (Y/M_gas + (1-Y)/M_air)
    ///   rho_mix = P * M_mix / (R * T)
    ///   v_buoy = g * (rho_air - rho_mix) / rho_air    [positive = upward]
    ///
    /// Gravity-current spreading (dense gas only):
    ///   g' = g * (rho_mix - rho_air) / rho_air
    ///   U_gc = C_gc * sqrt(g' * dz)                   [front speed]
    ///   Direction: -grad(rho) / |grad(rho)|            [outward from dense core]
    ///
    /// BFECC (Back and Forth Error Compensation and Correction) advection
    /// reduces numerical diffusion from first to second order, using three
    /// semi-Lagrangian passes per timestep instead of one.
    /// </summary>
    public class BuoyantTracerEngine
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

        private double[,,] _Y, _Ynext;
        private double[,,] _T, _Tnext;

        private readonly double[,,] _rho;
        private readonly double[,,] _vxEff, _vyEff, _vzEff;
        private readonly double[,,] _yOrig, _tOrig;

        private readonly double _ambientT;
        private readonly double _ambientP;
        private readonly double _gasMolarMass;
        private readonly double _speciesDiffM2PerS;
        private readonly double _thermalDiffM2PerS;
        private readonly double _decayPerS;

        private const double AirMolarMass = 0.029;
        private const double RGas = 8.314;
        private const double Gravity = 9.81;
        private const double GravityCurrentCoeff = 0.5;

        private double _sourceX, _sourceY, _sourceZ, _sourceR;
        private double _sourceT;
        private double _sourceC;
        private bool _hasSource;
        private bool _massInjection;
        private bool _poolSource;
        private int _poolMaxK;
        private double _injectionRatePerCellPerS;
        private int _sourceCellCount;
        private double _lastDt;

        public BuoyantTracerEngine(WindField3D wind,
            double domainHalfM, double domainHeightM,
            int nx, int ny, int nz,
            double speciesDiffusivityM2PerS,
            double gasMolarMassKgPerMol,
            double ambientTemperatureK,
            double ambientPressurePa = 101325.0,
            double thermalDiffusivityM2PerS = 2.2e-5,
            double decayPerS = 0.0,
            System.Collections.Generic.IList<BoundingBox> obstacles = null)
        {
            if (wind == null) throw new ArgumentNullException(nameof(wind));
            Nx = nx; Ny = ny; Nz = nz;
            DomainHalfM = domainHalfM;
            DomainHeightM = domainHeightM;
            DxM = 2.0 * domainHalfM / nx;
            DyM = 2.0 * domainHalfM / ny;
            DzM = domainHeightM / nz;
            _speciesDiffM2PerS = speciesDiffusivityM2PerS;
            _thermalDiffM2PerS = thermalDiffusivityM2PerS;
            _gasMolarMass = gasMolarMassKgPerMol > 0 ? gasMolarMassKgPerMol : AirMolarMass;
            _ambientT = ambientTemperatureK > 0 ? ambientTemperatureK : 293.15;
            _ambientP = ambientPressurePa > 0 ? ambientPressurePa : 101325.0;
            _decayPerS = decayPerS;

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

            _Y = new double[nx, ny, nz];
            _Ynext = new double[nx, ny, nz];
            _T = new double[nx, ny, nz];
            _Tnext = new double[nx, ny, nz];
            for (int k = 0; k < nz; k++)
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                        _T[i, j, k] = _ambientT;

            _rho = new double[nx, ny, nz];
            _vxEff = new double[nx, ny, nz];
            _vyEff = new double[nx, ny, nz];
            _vzEff = new double[nx, ny, nz];
            _yOrig = new double[nx, ny, nz];
            _tOrig = new double[nx, ny, nz];

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

        public void SetMassSource(double xSi, double ySi, double zSi,
            double radiusM, double releaseRateKgPerS, double airDensityKgPerM3,
            double exitTemperatureK)
        {
            _sourceX = xSi; _sourceY = ySi; _sourceZ = zSi;
            _sourceR = radiusM;
            _sourceT = exitTemperatureK;
            _sourceC = 0;
            _hasSource = true;
            _massInjection = true;
            CarveSourceHoleInMask();

            _sourceCellCount = 0;
            double r2 = radiusM * radiusM;
            for (int k = 0; k < Nz; k++)
            {
                double z = (k + 0.5) * DzM;
                double dz2 = (z - _sourceZ); dz2 *= dz2;
                if (dz2 > r2) continue;
                for (int j = 0; j < Ny; j++)
                {
                    double y = -DomainHalfM + (j + 0.5) * DyM;
                    double dy2 = (y - _sourceY); dy2 *= dy2;
                    if (dy2 + dz2 > r2) continue;
                    for (int i = 0; i < Nx; i++)
                    {
                        double x = -DomainHalfM + (i + 0.5) * DxM;
                        double dx2 = (x - _sourceX); dx2 *= dx2;
                        if (dx2 + dy2 + dz2 <= r2)
                            _sourceCellCount++;
                    }
                }
            }
            if (_sourceCellCount < 1) _sourceCellCount = 1;
            double cellVol = DxM * DyM * DzM;
            _injectionRatePerCellPerS = releaseRateKgPerS / (airDensityKgPerM3 * cellVol * _sourceCellCount);
        }

        /// <summary>
        /// Pool evaporation source: injects mass only in ground-level cells
        /// (k=0..1) within the pool horizontal radius. Models LNG/LPG spills
        /// where vapor rises from the liquid surface at near-zero velocity.
        /// </summary>
        public void SetPoolSource(double xSi, double ySi,
            double poolRadiusM, double releaseRateKgPerS, double airDensityKgPerM3,
            double exitTemperatureK)
        {
            _sourceX = xSi; _sourceY = ySi; _sourceZ = 0;
            _sourceR = poolRadiusM;
            _sourceT = exitTemperatureK;
            _sourceC = 0;
            _hasSource = true;
            _massInjection = true;
            _poolSource = true;
            _poolMaxK = Math.Max(0, Math.Min(Nz - 1, (int)Math.Ceiling(DzM / DzM)));

            double r2 = poolRadiusM * poolRadiusM;
            _sourceCellCount = 0;
            for (int k = 0; k <= _poolMaxK; k++)
            {
                for (int j = 0; j < Ny; j++)
                {
                    double y = -DomainHalfM + (j + 0.5) * DyM;
                    double dy2 = (y - _sourceY); dy2 *= dy2;
                    if (dy2 > r2) continue;
                    for (int i = 0; i < Nx; i++)
                    {
                        double x = -DomainHalfM + (i + 0.5) * DxM;
                        double dx2 = (x - _sourceX); dx2 *= dx2;
                        if (dx2 + dy2 <= r2)
                        {
                            _sourceCellCount++;
                            _blocked[i, j, k] = false;
                        }
                    }
                }
            }
            if (_sourceCellCount < 1) _sourceCellCount = 1;
            double cellVol = DxM * DyM * DzM;
            _injectionRatePerCellPerS = releaseRateKgPerS / (airDensityKgPerM3 * cellVol * _sourceCellCount);
        }

        public void SetSphericalSource(double xSi, double ySi, double zSi,
            double radiusM, double concentration, double exitTemperatureK)
        {
            _sourceX = xSi; _sourceY = ySi; _sourceZ = zSi;
            _sourceR = radiusM;
            _sourceT = exitTemperatureK;
            _sourceC = concentration;
            _hasSource = true;
            _massInjection = false;
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
                double dz2 = (z - _sourceZ); dz2 *= dz2;
                if (dz2 > r2) continue;
                for (int j = 0; j < Ny; j++)
                {
                    double y = -DomainHalfM + (j + 0.5) * DyM;
                    double dy2 = (y - _sourceY); dy2 *= dy2;
                    if (dy2 + dz2 > r2) continue;
                    for (int i = 0; i < Nx; i++)
                    {
                        double x = -DomainHalfM + (i + 0.5) * DxM;
                        double dx2 = (x - _sourceX); dx2 *= dx2;
                        if (dx2 + dy2 + dz2 <= r2)
                            _blocked[i, j, k] = false;
                    }
                }
            }
        }

        private void ApplySource()
        {
            if (!_hasSource) return;
            double r2 = _sourceR * _sourceR;
            double dC = _massInjection ? _injectionRatePerCellPerS * _lastDt : 0;
            double dT = _sourceT - _ambientT;
            int kMax = _poolSource ? _poolMaxK : Nz - 1;
            for (int k = 0; k <= kMax; k++)
            {
                double z = (k + 0.5) * DzM;
                double dz2 = _poolSource ? 0 : (z - _sourceZ) * (z - _sourceZ);
                if (!_poolSource && dz2 > r2) continue;
                for (int j = 0; j < Ny; j++)
                {
                    double y = -DomainHalfM + (j + 0.5) * DyM;
                    double dy2 = (y - _sourceY); dy2 *= dy2;
                    double hor2 = dy2 + (_poolSource ? 0 : dz2);
                    if (hor2 > r2) continue;
                    for (int i = 0; i < Nx; i++)
                    {
                        double x = -DomainHalfM + (i + 0.5) * DxM;
                        double dx2 = (x - _sourceX); dx2 *= dx2;
                        double dist2 = _poolSource ? dx2 + dy2 : dx2 + dy2 + dz2;
                        if (dist2 <= r2)
                        {
                            if (_massInjection)
                            {
                                _Y[i, j, k] += dC;
                                double yClamp = Math.Min(_Y[i, j, k], 1.0);
                                _T[i, j, k] = _ambientT + yClamp * dT;
                            }
                            else
                            {
                                _Y[i, j, k] = _sourceC;
                                _T[i, j, k] = _sourceT;
                            }
                        }
                    }
                }
            }
        }

        public double[,,] SnapshotConcentration() => _Y;

        public double[,,] SnapshotTemperature() => _T;

        public double EstimateBuoyantVelocity()
        {
            double rhoAir = _ambientP * AirMolarMass / (RGas * _ambientT);
            double rhoGas = _ambientP * _gasMolarMass / (RGas * Math.Max(_sourceT, 50.0));
            double vBuoy = Math.Abs(Gravity * (rhoAir - rhoGas) / rhoAir);
            double dRho = Math.Abs(rhoGas - rhoAir);
            double gPrime = Gravity * dRho / rhoAir;
            double vGc = GravityCurrentCoeff * Math.Sqrt(gPrime * DzM);
            return Math.Max(vBuoy, vGc);
        }

        public double[,,] Step(double dtS)
        {
            _lastDt = dtS;
            double rhoAir = _ambientP * AirMolarMass / (RGas * _ambientT);
            double vMax = 0.5 * Math.Min(DxM, Math.Min(DyM, DzM)) / Math.Max(dtS, 1e-6);

            // ---- Phase 1: density field ----
            Parallel.For(0, Nz, k =>
            {
                for (int j = 0; j < Ny; j++)
                    for (int i = 0; i < Nx; i++)
                    {
                        double yy = _Y[i, j, k];
                        if (yy > 1e-12)
                        {
                            double yClamp = Math.Min(yy, 1.0);
                            double invMmix = yClamp / _gasMolarMass + (1.0 - yClamp) / AirMolarMass;
                            _rho[i, j, k] = _ambientP / (RGas * Math.Max(_T[i, j, k], 50.0) * invMmix);
                        }
                        else
                        {
                            _rho[i, j, k] = rhoAir;
                        }
                    }
            });

            // ---- Phase 2: effective velocity (wind + buoyancy + gravity current) ----
            Parallel.For(0, Nz, k =>
            {
                for (int j = 0; j < Ny; j++)
                    for (int i = 0; i < Nx; i++)
                    {
                        double localRho = _rho[i, j, k];
                        double deltaRho = localRho - rhoAir;

                        double vBuoy = 0;
                        double vGcX = 0, vGcY = 0;

                        if (Math.Abs(deltaRho) > 1e-6)
                        {
                            vBuoy = Gravity * (rhoAir - localRho) / rhoAir;

                            if (deltaRho > 0)
                            {
                                double gPrime = Gravity * deltaRho / rhoAir;
                                double uScale = GravityCurrentCoeff * Math.Sqrt(gPrime * DzM);

                                int im = i > 0 ? i - 1 : 0;
                                int ip = i < Nx - 1 ? i + 1 : Nx - 1;
                                int jm = j > 0 ? j - 1 : 0;
                                int jp = j < Ny - 1 ? j + 1 : Ny - 1;
                                double spanX = (ip - im) * DxM;
                                double spanY = (jp - jm) * DyM;

                                double gradX = spanX > 0 ? (_rho[ip, j, k] - _rho[im, j, k]) / spanX : 0;
                                double gradY = spanY > 0 ? (_rho[i, jp, k] - _rho[i, jm, k]) / spanY : 0;
                                double gradMag = Math.Sqrt(gradX * gradX + gradY * gradY);

                                if (gradMag > 1e-12)
                                {
                                    vGcX = -uScale * gradX / gradMag;
                                    vGcY = -uScale * gradY / gradMag;
                                }
                            }
                        }

                        _vxEff[i, j, k] = ClampD(_ux[i, j, k] + vGcX, -vMax, vMax);
                        _vyEff[i, j, k] = ClampD(_uy[i, j, k] + vGcY, -vMax, vMax);
                        _vzEff[i, j, k] = ClampD(_uz[i, j, k] + vBuoy, -vMax, vMax);
                    }
            });

            // ---- Phase 3: BFECC advection ----
            Array.Copy(_Y, _yOrig, _Y.Length);
            Array.Copy(_T, _tOrig, _T.Length);

            // Pass 1 — forward advect
            AdvectForward(_Y, _Ynext, 0.0, dtS);
            AdvectForward(_T, _Tnext, _ambientT, dtS);

            // Pass 2 — reverse advect (trace forward in time to estimate error)
            AdvectReverse(_Ynext, _Y, 0.0, dtS);
            AdvectReverse(_Tnext, _T, _ambientT, dtS);

            // Error correction: phi_star = 1.5*phi_orig - 0.5*phi_hat, clamped
            Parallel.For(0, Nz, k =>
            {
                for (int j = 0; j < Ny; j++)
                    for (int i = 0; i < Nx; i++)
                    {
                        double yStar = 1.5 * _yOrig[i, j, k] - 0.5 * _Y[i, j, k];
                        _Y[i, j, k] = ClampToNeighborRange(_yOrig, i, j, k, yStar);

                        double tStar = 1.5 * _tOrig[i, j, k] - 0.5 * _T[i, j, k];
                        _T[i, j, k] = ClampToNeighborRange(_tOrig, i, j, k, tStar);
                    }
            });

            // Pass 3 — forward advect corrected field
            AdvectForward(_Y, _Ynext, 0.0, dtS);
            AdvectForward(_T, _Tnext, _ambientT, dtS);

            var tmpY = _Y; _Y = _Ynext; _Ynext = tmpY;
            var tmpT = _T; _T = _Tnext; _Tnext = tmpT;

            // ---- Phase 4: diffusion, decay, obstacles, source ----
            ExplicitDiffuse(_Y, 0.0, _speciesDiffM2PerS, dtS, isY: true);
            ExplicitDiffuse(_T, _ambientT, _thermalDiffM2PerS, dtS, isY: false);

            if (_decayPerS > 0)
            {
                double factor = Math.Exp(-_decayPerS * dtS);
                Parallel.For(0, Nz, k =>
                {
                    for (int j = 0; j < Ny; j++)
                        for (int i = 0; i < Nx; i++)
                            _Y[i, j, k] *= factor;
                });
            }

            Parallel.For(0, Nz, k =>
            {
                for (int j = 0; j < Ny; j++)
                    for (int i = 0; i < Nx; i++)
                        if (_blocked[i, j, k])
                        {
                            _Y[i, j, k] = 0;
                            _T[i, j, k] = _ambientT;
                        }
            });

            ApplySource();
            return _Y;
        }

        private void AdvectForward(double[,,] src, double[,,] dst, double outsideVal, double dt)
        {
            Parallel.For(0, Nz, k =>
            {
                double z = (k + 0.5) * DzM;
                for (int j = 0; j < Ny; j++)
                {
                    double y = -DomainHalfM + (j + 0.5) * DyM;
                    for (int i = 0; i < Nx; i++)
                    {
                        double x = -DomainHalfM + (i + 0.5) * DxM;
                        double sx = x - _vxEff[i, j, k] * dt;
                        double sy = y - _vyEff[i, j, k] * dt;
                        double sz = z - _vzEff[i, j, k] * dt;
                        dst[i, j, k] = SampleTrilinear(src, sx, sy, sz, outsideVal);
                    }
                }
            });
        }

        private void AdvectReverse(double[,,] src, double[,,] dst, double outsideVal, double dt)
        {
            Parallel.For(0, Nz, k =>
            {
                double z = (k + 0.5) * DzM;
                for (int j = 0; j < Ny; j++)
                {
                    double y = -DomainHalfM + (j + 0.5) * DyM;
                    for (int i = 0; i < Nx; i++)
                    {
                        double x = -DomainHalfM + (i + 0.5) * DxM;
                        double sx = x + _vxEff[i, j, k] * dt;
                        double sy = y + _vyEff[i, j, k] * dt;
                        double sz = z + _vzEff[i, j, k] * dt;
                        dst[i, j, k] = SampleTrilinear(src, sx, sy, sz, outsideVal);
                    }
                }
            });
        }

        private double ClampToNeighborRange(double[,,] field, int i, int j, int k, double val)
        {
            double lo = field[i, j, k], hi = lo;
            if (i > 0) { double v = field[i - 1, j, k]; if (v < lo) lo = v; if (v > hi) hi = v; }
            if (i < Nx - 1) { double v = field[i + 1, j, k]; if (v < lo) lo = v; if (v > hi) hi = v; }
            if (j > 0) { double v = field[i, j - 1, k]; if (v < lo) lo = v; if (v > hi) hi = v; }
            if (j < Ny - 1) { double v = field[i, j + 1, k]; if (v < lo) lo = v; if (v > hi) hi = v; }
            if (k > 0) { double v = field[i, j, k - 1]; if (v < lo) lo = v; if (v > hi) hi = v; }
            if (k < Nz - 1) { double v = field[i, j, k + 1]; if (v < lo) lo = v; if (v > hi) hi = v; }
            return val < lo ? lo : (val > hi ? hi : val);
        }

        private void ExplicitDiffuse(double[,,] f, double restState, double diffusivity, double dtS, bool isY)
        {
            if (diffusivity <= 0) return;
            double coeffX = diffusivity * dtS / (DxM * DxM);
            double coeffY = diffusivity * dtS / (DyM * DyM);
            double coeffZ = diffusivity * dtS / (DzM * DzM);
            int sub = (int)Math.Ceiling(2.0 * (coeffX + coeffY + coeffZ));
            if (sub < 1) sub = 1;
            coeffX /= sub; coeffY /= sub; coeffZ /= sub;
            var fNext = isY ? _Ynext : _Tnext;
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
                if (isY) { var t = _Y; _Y = _Ynext; _Ynext = t; f = _Y; fNext = _Ynext; }
                else     { var t = _T; _T = _Tnext; _Tnext = t; f = _T; fNext = _Tnext; }
            }
        }

        private double SampleTrilinear(double[,,] field, double x, double y, double z, double outsideValue)
        {
            double fi = (x + DomainHalfM) / DxM - 0.5;
            double fj = (y + DomainHalfM) / DyM - 0.5;
            double fk = z / DzM - 0.5;

            if (fk < 0) fk = 0;
            if (fi < 0 || fi > Nx - 1 || fj < 0 || fj > Ny - 1 || fk > Nz - 1)
                return outsideValue;

            int i0 = (int)Math.Floor(fi);
            int j0 = (int)Math.Floor(fj);
            int k0 = (int)Math.Floor(fk);
            double dx = fi - i0;
            double dy = fj - j0;
            double dz = fk - k0;

            i0 = ClampI(i0, 0, Nx - 2);
            j0 = ClampI(j0, 0, Ny - 2);
            k0 = ClampI(k0, 0, Nz - 2);

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

        private static int ClampI(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        private static double ClampD(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
