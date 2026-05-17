using System;
using System.Collections.Generic;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// GPU port of <see cref="BuoyantTracerEngine"/> — phase 1 foundation.
    /// Wraps the FluidX3D <c>fx3d_tracer_*</c> C ABI behind the same public
    /// interface as the CPU engine so callers (FluidX3DRunner, validation
    /// harness) can switch back and forth.
    ///
    /// PHASE 1 (this commit) implements end-to-end host ↔ device plumbing:
    /// allocates GPU buffers, uploads the wind field once, runs a
    /// single-pass forward semi-Lagrangian advection kernel per Step, masks
    /// obstacle cells, reads results back. No BFECC correction, no buoyancy,
    /// no diffusion, no source injection. Calling Step is a no-op for the
    /// concentration field unless something seeded it (e.g. via the test
    /// helper <see cref="SetInitialConcentrationForTest"/>) — this engine
    /// is NOT a drop-in replacement for the CPU one until the missing
    /// kernels land in follow-up phases.
    ///
    /// Use <see cref="SelfTest"/> to verify the GPU pipeline is alive.
    /// </summary>
    public sealed class BuoyantTracerEngineGpu : IBuoyantTracerEngine
    {
        public int Nx { get; }
        public int Ny { get; }
        public int Nz { get; }
        public double DxM { get; }
        public double DyM { get; }
        public double DzM { get; }
        public double DomainHalfM { get; }
        public double DomainHeightM { get; }

        private ulong _handle;
        private readonly int _n;
        private readonly float[] _scratch;
        private readonly double _ambientT;
        private bool _disposed;

        public BuoyantTracerEngineGpu(WindField3D wind,
            double domainHalfM, double domainHeightM,
            int nx, int ny, int nz,
            double speciesDiffusivityM2PerS,
            double gasMolarMassKgPerMol,
            double ambientTemperatureK,
            double ambientPressurePa = 101325.0,
            double thermalDiffusivityM2PerS = 2.2e-5,
            IList<BoundingBox> obstacles = null,
            int deviceId = -1)
        {
            if (wind == null) throw new ArgumentNullException(nameof(wind));
            Nx = nx; Ny = ny; Nz = nz;
            DomainHalfM = domainHalfM;
            DomainHeightM = domainHeightM;
            DxM = 2.0 * domainHalfM / nx;
            DyM = 2.0 * domainHalfM / ny;
            DzM = domainHeightM / nz;
            _ambientT = ambientTemperatureK > 0 ? ambientTemperatureK : 293.15;
            _n = nx * ny * nz;
            _scratch = new float[_n];

            _handle = FluidX3DBridge.fx3d_tracer_create(
                (uint)nx, (uint)ny, (uint)nz,
                (float)domainHalfM, (float)domainHeightM,
                (float)gasMolarMassKgPerMol,
                (float)ambientTemperatureK, (float)ambientPressurePa,
                (float)speciesDiffusivityM2PerS, (float)thermalDiffusivityM2PerS,
                deviceId);
            if (_handle == 0UL)
                throw new InvalidOperationException(
                    "fx3d_tracer_create returned 0 — no OpenCL device or program compile failed.");

            // Sample the supplied wind field at cell centres and upload.
            var ux = new float[_n];
            var uy = new float[_n];
            var uz = new float[_n];
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
                        int n = i + nx * (j + ny * k);
                        ux[n] = (float)u.X;
                        uy[n] = (float)u.Y;
                        uz[n] = (float)u.Z;
                    }
                }
            }
            FluidX3DBridge.fx3d_tracer_set_wind(_handle, ux, uy, uz);

            if (obstacles != null && obstacles.Count > 0)
            {
                var blocked = new byte[_n];
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
                                blocked[i + nx * (j + ny * k)] = 1;
                }
                FluidX3DBridge.fx3d_tracer_set_obstacles(_handle, blocked);
            }
        }

        public void SetMassSource(double xSi, double ySi, double zSi,
            double radiusM, double releaseRateKgPerS, double airDensityKgPerM3,
            double exitTemperatureK)
        {
            ThrowIfDisposed();
            FluidX3DBridge.fx3d_tracer_set_source_sphere(_handle,
                (float)xSi, (float)ySi, (float)zSi, (float)radiusM,
                (float)releaseRateKgPerS, (float)airDensityKgPerM3, (float)exitTemperatureK);
        }

        public void SetPoolSource(double xSi, double ySi,
            double poolRadiusM, double releaseRateKgPerS, double airDensityKgPerM3,
            double exitTemperatureK)
        {
            ThrowIfDisposed();
            FluidX3DBridge.fx3d_tracer_set_source_pool(_handle,
                (float)xSi, (float)ySi, (float)poolRadiusM,
                (float)releaseRateKgPerS, (float)airDensityKgPerM3, (float)exitTemperatureK);
        }

        /// <summary>Fixed-concentration sphere source — overwrites Y to <paramref name="concentration"/>
        /// and T to <paramref name="exitTemperatureK"/> inside the sphere each step. Mirrors
        /// the CPU engine's SetSphericalSource. Phase-2 stopgap: re-uses the mass-injection
        /// sphere kernel with a tiny radius around a single ApplySource pass on host —
        /// for benches that use this path (test-only today) it's slower than ideal but
        /// keeps the API identical to the CPU engine.</summary>
        public void SetSphericalSource(double xSi, double ySi, double zSi,
            double radiusM, double concentration, double exitTemperatureK)
        {
            ThrowIfDisposed();
            _fixedSource = new (xSi, ySi, zSi, radiusM, concentration, exitTemperatureK);
            ApplyFixedSourceOnHost();
        }

        private (double X, double Y, double Z, double R, double C, double T)? _fixedSource;

        /// <summary>Apply the fixed-concentration sphere by reading Y/T,
        /// overwriting cells inside the sphere, and re-uploading. Host
        /// roundtrip is slow but only used for benches that don't have
        /// a mass-injection rate (rare; mostly test setups).</summary>
        private void ApplyFixedSourceOnHost()
        {
            if (_fixedSource == null) return;
            var src = _fixedSource.Value;
            FluidX3DBridge.fx3d_tracer_read_concentration(_handle, _scratch);
            double r2 = src.R * src.R;
            for (int k = 0; k < Nz; k++)
            {
                double z = (k + 0.5) * DzM;
                double dz2 = (z - src.Z) * (z - src.Z);
                if (dz2 > r2) continue;
                for (int j = 0; j < Ny; j++)
                {
                    double y = -DomainHalfM + (j + 0.5) * DyM;
                    double dy2 = (y - src.Y) * (y - src.Y);
                    if (dy2 + dz2 > r2) continue;
                    for (int i = 0; i < Nx; i++)
                    {
                        double x = -DomainHalfM + (i + 0.5) * DxM;
                        double dx2 = (x - src.X) * (x - src.X);
                        if (dx2 + dy2 + dz2 <= r2)
                            _scratch[i + Nx * (j + Ny * k)] = (float)src.C;
                    }
                }
            }
            FluidX3DBridge.fx3d_tracer_set_initial_concentration(_handle, _scratch);
        }

        /// <summary>Mirrors the CPU engine's EstimateBuoyantVelocity. Used by
        /// the runner to estimate the maximum effective velocity for CFL
        /// dt selection — purely host-side.</summary>
        public double EstimateBuoyantVelocity()
        {
            const double airM = 0.029;
            const double R = 8.314;
            const double gravity = 9.81;
            const double cgc = 0.5;
            double rhoAir = 101325.0 * airM / (R * _ambientT);
            // Approximate using the configured source exit temperature when set.
            double sourceT = _fixedSource?.T ?? _ambientT;
            double sourceM = airM; // unknown without re-reading config; use air as conservative default
            double rhoGas = 101325.0 * sourceM / (R * Math.Max(sourceT, 50.0));
            double vBuoy = Math.Abs(gravity * (rhoAir - rhoGas) / rhoAir);
            double dRho = Math.Abs(rhoGas - rhoAir);
            double gPrime = gravity * dRho / rhoAir;
            double vGc = cgc * Math.Sqrt(gPrime * DzM);
            return Math.Max(vBuoy, vGc);
        }

        /// <summary>Direct host → device upload of an initial concentration field.
        /// Used by <see cref="SelfTest"/> to seed a Gaussian blob without going through
        /// a source kernel (which isn't ported yet). NOT in the CPU engine's API.</summary>
        public void SetInitialConcentrationForTest(double[,,] Y)
        {
            ThrowIfDisposed();
            if (Y == null) throw new ArgumentNullException(nameof(Y));
            for (int k = 0; k < Nz; k++)
                for (int j = 0; j < Ny; j++)
                    for (int i = 0; i < Nx; i++)
                        _scratch[i + Nx * (j + Ny * k)] = (float)Y[i, j, k];
            FluidX3DBridge.fx3d_tracer_set_initial_concentration(_handle, _scratch);
        }

        public double[,,] Step(double dtS)
        {
            ThrowIfDisposed();
            int rc = FluidX3DBridge.fx3d_tracer_step(_handle, (float)dtS);
            if (rc != 0)
                throw new InvalidOperationException($"fx3d_tracer_step returned {rc}");
            // Fixed-concentration sources have to be re-applied each step
            // (CPU engine does this inside Step → ApplySource). The mass
            // and pool sources are handled by the GPU kernel directly.
            if (_fixedSource != null) ApplyFixedSourceOnHost();
            return SnapshotConcentration();
        }

        public double[,,] SnapshotConcentration()
        {
            ThrowIfDisposed();
            FluidX3DBridge.fx3d_tracer_read_concentration(_handle, _scratch);
            var Y = new double[Nx, Ny, Nz];
            for (int k = 0; k < Nz; k++)
                for (int j = 0; j < Ny; j++)
                    for (int i = 0; i < Nx; i++)
                        Y[i, j, k] = _scratch[i + Nx * (j + Ny * k)];
            return Y;
        }

        public double[,,] SnapshotTemperature()
        {
            ThrowIfDisposed();
            FluidX3DBridge.fx3d_tracer_read_temperature(_handle, _scratch);
            var T = new double[Nx, Ny, Nz];
            for (int k = 0; k < Nz; k++)
                for (int j = 0; j < Ny; j++)
                    for (int i = 0; i < Nx; i++)
                        T[i, j, k] = _scratch[i + Nx * (j + Ny * k)];
            return T;
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_handle != 0UL) FluidX3DBridge.fx3d_tracer_destroy(_handle);
            _handle = 0UL;
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BuoyantTracerEngineGpu));
        }

        // ──────────────────────────────────────────────────────────────────
        // Self-test: seed a Gaussian blob, advect for one step in uniform
        // wind, verify the blob's centre-of-mass moved by U*dt in +X. Used
        // as a smoke test that the GPU pipeline (host → device → kernel →
        // host) is functioning end-to-end.
        // ──────────────────────────────────────────────────────────────────
        public static (bool ok, string message) SelfTest()
        {
            const int N = 32;
            const double half = 10.0;       // ±10 m horizontal
            const double height = 20.0;     // 20 m vertical
            const double dx = 2.0 * half / N;
            const double U = 5.0;           // uniform +x wind, m/s
            const double dt = 0.05;         // 0.25 m advection per step — sub-cell

            // A uniform wind field, no obstacles.
            var ux = new double[N, N, N];
            var uy = new double[N, N, N];
            var uz = new double[N, N, N];
            for (int k = 0; k < N; k++)
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++)
                        ux[i, j, k] = U;
            var wind = new WindField3D(ux, uy, uz, -half, half, -half, half, height);

            using var gpu = new BuoyantTracerEngineGpu(wind,
                domainHalfM: half, domainHeightM: height,
                nx: N, ny: N, nz: N,
                speciesDiffusivityM2PerS: 0, // diffusion not implemented yet
                gasMolarMassKgPerMol: 0.029,
                ambientTemperatureK: 293.15);

            // Initial: narrow Gaussian centred at origin.
            var Y0 = new double[N, N, N];
            const double sigma = 1.0; // metres
            double xcSeed = 0, ycSeed = 0, zcSeed = height * 0.5;
            for (int k = 0; k < N; k++)
            {
                double z = (k + 0.5) * (height / N);
                double dz2 = (z - zcSeed) * (z - zcSeed);
                for (int j = 0; j < N; j++)
                {
                    double y = -half + (j + 0.5) * dx;
                    double dy2 = (y - ycSeed) * (y - ycSeed);
                    for (int i = 0; i < N; i++)
                    {
                        double x = -half + (i + 0.5) * dx;
                        double dx2 = (x - xcSeed) * (x - xcSeed);
                        double r2 = dx2 + dy2 + dz2;
                        Y0[i, j, k] = Math.Exp(-r2 / (2 * sigma * sigma));
                    }
                }
            }
            gpu.SetInitialConcentrationForTest(Y0);

            // Advect 10 steps → total displacement U*dt*10 = 2.5 m.
            const int steps = 10;
            for (int s = 0; s < steps; s++) gpu.Step(dt);

            // Compute concentration-weighted centre-of-mass on X.
            var Y = gpu.SnapshotConcentration();
            double num = 0, den = 0;
            for (int k = 0; k < N; k++)
            {
                double z = (k + 0.5) * (height / N);
                double dz2 = (z - zcSeed) * (z - zcSeed);
                for (int j = 0; j < N; j++)
                {
                    double y = -half + (j + 0.5) * dx;
                    double dy2 = (y - ycSeed) * (y - ycSeed);
                    for (int i = 0; i < N; i++)
                    {
                        double x = -half + (i + 0.5) * dx;
                        // Only weight points within a few sigma to filter noise.
                        if (dx2_safe(x, dy2 + dz2, 6 * sigma) > 0) continue;
                        double w = Y[i, j, k];
                        num += x * w;
                        den += w;
                    }
                }
            }
            if (den < 1e-12) return (false, "GPU output is empty (all zeros) — kernel may not have run");
            double xCom = num / den;
            double expected = xcSeed + U * dt * steps;
            double err = Math.Abs(xCom - expected);
            // BFECC-less semi-Lagrangian is diffusive but its centre-of-mass
            // should track the analytic translation closely. Allow 1 cell
            // tolerance (sub-cell error is expected from trilinear sampling).
            double tol = dx;
            bool ok = err < tol;
            return (ok, $"GPU CoM(x) = {xCom:F3} m, expected {expected:F3} m, |err| = {err:F3} m, tol = {tol:F3} m → {(ok ? "PASS" : "FAIL")}");
        }

        private static double dx2_safe(double x, double otherD2, double cutoff)
        {
            // Returns 0 if (x^2 + otherD2) <= cutoff^2, else positive (skip).
            double r2 = x * x + otherD2;
            return r2 > cutoff * cutoff ? 1 : 0;
        }

        // ──────────────────────────────────────────────────────────────────
        // Cross-validation: run identical setup on CPU and GPU engines, then
        // diff the Y and T fields cell-by-cell. Validates that the full
        // GPU pipeline (advection + BFECC + density + buoyancy + diffusion +
        // source) matches the C# baseline within FP single-precision noise.
        // ──────────────────────────────────────────────────────────────────
        public static (bool ok, string message) CrossValidateVsCpu()
        {
            const int N = 16;
            const double half = 25.0;
            const double height = 25.0;
            const double dx = 2.0 * half / N;
            const double dt = 0.2;
            const int steps = 10;
            const double Uwind = 1.0;

            // Wind field uniform +x.
            var ux = new double[N, N, N];
            var uy = new double[N, N, N];
            var uz = new double[N, N, N];
            for (int k = 0; k < N; k++)
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++)
                        ux[i, j, k] = Uwind;
            var wind = new WindField3D(ux, uy, uz, -half, half, -half, half, height);

            const double speciesDiff = 1e-3;
            const double thermalDiff = 1e-5;
            const double gasM = 0.01604;        // CH4
            const double ambientT = 293.15;
            const double ambientP = 101325.0;

            // Sphere source: 0.5 kg/s of cold (T=111K) CH4 inside r=4m around origin.
            const double srcX = 0, srcY = 0, srcZ = 5.0;
            const double srcR = 4.0;
            const double srcRate = 0.5;
            const double srcExitT = 111.0;
            const double airRho = ambientP * 0.029 / (8.314 * ambientT); // ≈ 1.20

            var cpu = new BuoyantTracerEngine(wind,
                domainHalfM: half, domainHeightM: height,
                nx: N, ny: N, nz: N,
                speciesDiffusivityM2PerS: speciesDiff,
                gasMolarMassKgPerMol: gasM,
                ambientTemperatureK: ambientT, ambientPressurePa: ambientP,
                thermalDiffusivityM2PerS: thermalDiff);
            cpu.SetMassSource(srcX, srcY, srcZ, srcR, srcRate, airRho, srcExitT);

            using var gpu = new BuoyantTracerEngineGpu(wind,
                domainHalfM: half, domainHeightM: height,
                nx: N, ny: N, nz: N,
                speciesDiffusivityM2PerS: speciesDiff,
                gasMolarMassKgPerMol: gasM,
                ambientTemperatureK: ambientT, ambientPressurePa: ambientP,
                thermalDiffusivityM2PerS: thermalDiff);
            gpu.SetMassSource(srcX, srcY, srcZ, srcR, srcRate, airRho, srcExitT);

            for (int s = 0; s < steps; s++)
            {
                cpu.Step(dt);
                gpu.Step(dt);
            }

            var Yc = cpu.SnapshotConcentration();
            var Yg = gpu.SnapshotConcentration();
            var Tc = cpu.SnapshotTemperature();
            var Tg = gpu.SnapshotTemperature();

            double yAbsMax = 0, yRelMax = 0, tAbsMax = 0, tRelMax = 0;
            double yMaxCpu = 0, yMaxGpu = 0;
            for (int k = 0; k < N; k++)
            {
                for (int j = 0; j < N; j++)
                {
                    for (int i = 0; i < N; i++)
                    {
                        double yC = Yc[i, j, k], yG = Yg[i, j, k];
                        double tC = Tc[i, j, k], tG = Tg[i, j, k];
                        double yAbs = Math.Abs(yC - yG);
                        double tAbs = Math.Abs(tC - tG);
                        if (yAbs > yAbsMax) yAbsMax = yAbs;
                        if (tAbs > tAbsMax) tAbsMax = tAbs;
                        // Relative error only where the magnitude is non-trivial.
                        if (Math.Abs(yC) > 1e-6 && yAbs / Math.Abs(yC) > yRelMax) yRelMax = yAbs / Math.Abs(yC);
                        if (Math.Abs(tC) > 1.0 && tAbs / Math.Abs(tC) > tRelMax) tRelMax = tAbs / Math.Abs(tC);
                        if (yC > yMaxCpu) yMaxCpu = yC;
                        if (yG > yMaxGpu) yMaxGpu = yG;
                    }
                }
            }

            // Baseline-doc tolerance for the initial port is 5 % per sensor.
            // For a 16³ smoke test we expect tighter agreement.
            const double yRelTol = 0.05;
            const double tRelTol = 0.01;
            bool ok = yRelMax < yRelTol && tRelMax < tRelTol;
            return (ok,
                $"GPU vs CPU after {steps} steps, N={N}:" +
                $"\n    Y max abs={yAbsMax:E3}, max rel={yRelMax * 100:F2} % (tol {yRelTol * 100:F1} %)" +
                $"\n    T max abs={tAbsMax:E3}, max rel={tRelMax * 100:F2} % (tol {tRelTol * 100:F1} %)" +
                $"\n    peak Y: CPU={yMaxCpu:F4}, GPU={yMaxGpu:F4}" +
                $"\n    → {(ok ? "PASS" : "FAIL")}");
        }
    }
}
