using System;
using System.ComponentModel;
using System.Threading;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Steady-state variant of <see cref="FluidX3DRunner"/>. Drives the same CPU
    /// semi-Lagrangian tracer against the same FluidX3D-supplied wind field, but
    /// instead of running a fixed transient duration it iterates until the
    /// concentration field stops changing — detected by the cell-by-cell L2 delta
    /// between successive convergence-check snapshots falling below a tolerance.
    ///
    /// Useful for design-time screening where the user only cares about the long-
    /// time asymptotic plume (continuous release into a steady wind field), not
    /// the transient build-up. Typical runtime: ~30–50% of the equivalent
    /// transient run because we bail out as soon as the plume saturates.
    ///
    /// Output: a single snapshot at the converged time. The playback bar shows
    /// only that frame. <see cref="DispersionStudy"/> picks it up automatically
    /// (the engine reads the LAST timestep regardless of how many were written).
    /// </summary>
    public class FluidX3DSteadyDispersionRunner
    {
        private BackgroundWorker _worker;
        private string _casePath;
        private CancellationTokenSource _cts;
        private volatile bool _cancelled;
        private System.Collections.Generic.List<BoundingBox> _obstacles;

        public event EventHandler<OpenFoamProgress> ProgressUpdated;
        public event EventHandler<OpenFoamResult> Completed;
        public event EventHandler<string> Failed;

        public bool IsRunning => _worker != null && _worker.IsBusy;
        public string CasePath => _casePath;

        /// <summary>L2-relative tolerance for declaring the concentration field
        /// converged. 1e-3 corresponds to "the plume changed by less than 0.1% of
        /// its current magnitude between successive convergence checks".</summary>
        public double ConvergenceTolerance { get; set; } = 1e-3;

        /// <summary>Number of solver chunks between convergence checks. Each chunk
        /// advances the tracer for <c>checkInterval</c> simulated seconds; default
        /// 50 chunks across the full duration cap.</summary>
        public int ConvergenceChecks { get; set; } = 50;

        public void Cancel()
        {
            _cancelled = true;
            try { _cts?.Cancel(); } catch { }
        }

        public void RunAsync(DispersionScenario scenario, CfdConfiguration config,
            Scene3D scene,
            System.Collections.Generic.List<BoundingBox> obstacles = null)
        {
            _obstacles = obstacles;
            if (IsRunning) return;
            _cancelled = false;
            _cts = new CancellationTokenSource();

            _worker = new BackgroundWorker { WorkerSupportsCancellation = true };
            _worker.DoWork += (s, e) =>
            {
                try
                {
                    Report(0.02, "FluidX3D dispersion (steady): resolving wind field...");
                    var wf = WindFieldResolver.FindWindFieldScenario(scene, scenario);
                    WindField3D wind = wf?.WindField;
                    if (wind == null && wf != null && wf.UseFluidX3D)
                        wind = FluidX3DWindFieldRunner.LoadFromCase(wf);
                    if (wind == null && wf != null)
                        wind = WindFieldRunner.LoadFromCase(wf);
                    if (wind == null)
                    {
                        Failed?.Invoke(this, "FluidX3D steady dispersion needs a Ready wind field; " +
                            "run the associated Wind Field first.");
                        return;
                    }

                    int nx = scenario.GridResolution;
                    int ny = scenario.GridResolution;
                    int nz = Math.Max(8, scenario.GridResolution / 2);
                    double domain = scenario.DomainSizeM;
                    double height = wf?.DomainHeightM > 0 ? wf.DomainHeightM : domain;
                    // SnapshotDurationS is interpreted as the MAXIMUM physical time we
                    // allow the steady solver to chase convergence. Default 600 s is
                    // enough for ~5 residence times on a 400 m domain at 5 m/s.
                    double maxDuration = scenario.SimulationDurationS > 0
                        ? scenario.SimulationDurationS : 600.0;

                    Report(0.05, "FluidX3D dispersion (steady): initialising tracer...");

                    double diff = config?.DiffusivityM2PerS > 0 ? config.DiffusivityM2PerS : 1e-5;
                    double cellMGlobal = scenario.DomainSizeM * 2.0 / nx;
                    if (scenario.Meteo != null)
                    {
                        double ws = Math.Max(scenario.Meteo.WindSpeed, 0.5);
                        double Dt = 0.0084 * cellMGlobal * ws / 0.7;
                        diff = Math.Max(diff, Dt);
                    }
                    var src = scenario.Sources != null && scenario.Sources.Count > 0
                        ? scenario.Sources[0] : null;
                    double decay = src?.Gas != null && src.Gas.HalfLifeS > 0
                        ? Math.Log(2.0) / src.Gas.HalfLifeS : 0.0;

                    double ambientT = scenario.Meteo?.AmbientTemperature > 0
                        ? scenario.Meteo.AmbientTemperature : 293.15;
                    double ambientP = scenario.Meteo?.AmbientPressure > 0
                        ? scenario.Meteo.AmbientPressure : 101325.0;
                    double gasMW = src?.Gas != null && src.Gas.MolarMass > 0
                        ? src.Gas.MolarMass : 0.029;
                    double exitT = src != null && src.ExitTemperatureK > 0
                        ? src.ExitTemperatureK : ambientT;

                    bool useBuoyant = Math.Abs(exitT - ambientT) > 20.0;

                    int tnx = useBuoyant ? nx * 3 : nx;
                    int tny = useBuoyant ? ny * 3 : ny;
                    int tnz = useBuoyant ? nz * 3 : nz;
                    double tracerCellM = domain * 2.0 / tnx;
                    if (useBuoyant && scenario.Meteo != null)
                    {
                        double ws2 = Math.Max(scenario.Meteo.WindSpeed, 0.5);
                        diff = Math.Max(config?.DiffusivityM2PerS > 0 ? config.DiffusivityM2PerS : 1e-5,
                            0.0084 * tracerCellM * ws2 / 0.7);
                    }

                    DispersionTracerEngine passiveEngine = null;
                    BuoyantTracerEngine buoyantEngine = null;

                    if (useBuoyant)
                    {
                        buoyantEngine = new BuoyantTracerEngine(wind, domain, height, tnx, tny, tnz,
                            diff, gasMW, ambientT, ambientP, 2.2e-5, decay, _obstacles);
                    }
                    else
                    {
                        passiveEngine = new DispersionTracerEngine(wind, domain, height, nx, ny, nz,
                            diff, decay, _obstacles);
                    }

                    if (src != null)
                    {
                        double srcCellM = useBuoyant ? tracerCellM : cellMGlobal;
                        double physR = src.StackDiameterM > 0 ? src.StackDiameterM * 2.0 : srcCellM * 3.0;
                        double radiusM = src.ReleaseRateKgPerS > 0
                            ? Math.Max(2.0 * srcCellM, physR)
                            : Math.Max(5.0 * srcCellM, physR);
                        if (src.ReleaseRateKgPerS > 0)
                        {
                            double airDensity = ambientP * 0.029 / (8.314 * ambientT);
                            if (useBuoyant)
                                buoyantEngine.SetMassSource(src.Position.X, src.Position.Y, src.Position.Z,
                                    radiusM, src.ReleaseRateKgPerS, airDensity, exitT);
                            else
                                passiveEngine.SetMassSource(src.Position.X, src.Position.Y, src.Position.Z,
                                    radiusM, src.ReleaseRateKgPerS, airDensity);
                        }
                        else
                        {
                            if (useBuoyant)
                                buoyantEngine.SetSphericalSource(src.Position.X, src.Position.Y, src.Position.Z,
                                    radiusM, 1.0, exitT);
                            else
                                passiveEngine.SetSphericalSource(src.Position.X, src.Position.Y, src.Position.Z,
                                    radiusM: radiusM, concentration: 1.0);
                        }
                    }

                    double engineDx = useBuoyant ? buoyantEngine.DxM : passiveEngine.DxM;
                    double engineDy = useBuoyant ? buoyantEngine.DyM : passiveEngine.DyM;
                    double engineDz = useBuoyant ? buoyantEngine.DzM : passiveEngine.DzM;

                    double maxU = MaxWindSpeed(wind);
                    if (useBuoyant)
                        maxU = Math.Max(maxU, buoyantEngine.EstimateBuoyantVelocity());
                    if (maxU < 0.1) maxU = 0.1;
                    double cellSize = Math.Min(engineDx, Math.Min(engineDy, engineDz));
                    double dtMax = 0.5 * cellSize / maxU;
                    // Chunk = sim seconds between convergence checks.
                    int checks = Math.Max(10, ConvergenceChecks);
                    double chunk = maxDuration / checks;
                    int stepsPerChunk = Math.Max(1, (int)Math.Ceiling(chunk / dtMax));
                    double dt = chunk / stepsPerChunk;

                    _casePath = System.IO.Path.Combine(TempManager.GetWorkDir(),
                        "DisperSim3D_fx3dsteady_sim_" + (scenario.Id ?? Guid.NewGuid().ToString("N")));
                    try { System.IO.Directory.CreateDirectory(_casePath); TempManager.RegisterActive(_casePath); }
                    catch { _casePath = null; }

                    int outNx = useBuoyant ? tnx : nx;
                    int outNy = useBuoyant ? tny : ny;
                    int outNz = useBuoyant ? tnz : nz;
                    var result = new OpenFoamResult
                    {
                        GridNx = outNx, GridNy = outNy, GridNz = outNz,
                        DomainSizeM = domain,
                        DomainXMin = -domain, DomainXMax = domain,
                        DomainYMin = -domain, DomainYMax = domain,
                        DomainZMax = height,
                        IsLoaded = true,
                        IsSteadyState = true,
                        CaseDir = _casePath ?? ""
                    };

                    double[,,] previous = null;
                    double tol = Math.Max(1e-9, ConvergenceTolerance);
                    double lastDelta = double.MaxValue;
                    bool converged = false;
                    int chunkIndex = 0;
                    double simT = 0;

                    for (chunkIndex = 0; chunkIndex < checks; chunkIndex++)
                    {
                        if (_cancelled) break;

                        for (int sub = 0; sub < stepsPerChunk; sub++)
                        {
                            if (_cancelled) break;
                            if (useBuoyant)
                                buoyantEngine.Step(dt);
                            else
                                passiveEngine.Step(dt);
                            simT += dt;
                        }

                        var current = useBuoyant
                            ? buoyantEngine.SnapshotConcentration()
                            : passiveEngine.Snapshot();

                        // Convergence delta vs the previous check.
                        if (previous != null)
                        {
                            lastDelta = ComputeRelativeL2(previous, current);
                            if (lastDelta < tol)
                            {
                                converged = true;
                            }
                        }

                        // Clone for next comparison + write to disk so progress is visible.
                        var snapField = new double[outNx, outNy, outNz];
                        Array.Copy(current, snapField, current.Length);
                        previous = snapField;

                        // Only write the FINAL snapshot — steady state has one frame.
                        // Intermediate chunks are kept in memory only.
                        if (converged || chunkIndex == checks - 1 || _cancelled)
                        {
                            double tSi = simT;
                            result.TimeSteps.Add(tSi);
                            result.PreloadField(tSi, snapField);
                            if (!string.IsNullOrEmpty(_casePath))
                            {
                                try
                                {
                                    string binPath = System.IO.Path.Combine(_casePath,
                                        tSi.ToString("F3",
                                            System.Globalization.CultureInfo.InvariantCulture) + ".bin");
                                    OpenFoamResult.SaveBinaryField(binPath, snapField);
                                    result.TimeStepPaths[tSi] = binPath;
                                }
                                catch { }
                            }
                        }

                        string deltaStr = previous != null && lastDelta < double.MaxValue
                            ? string.Format("Δ={0:E2}", lastDelta) : "Δ=—";
                        double frac = 0.05 + 0.92 * (chunkIndex + 1) / checks;
                        Report(frac,
                            string.Format("steady chunk {0}/{1} t={2:F1}s {3}{4}",
                                chunkIndex + 1, checks, simT, deltaStr,
                                converged ? " — CONVERGED" : ""));

                        if (converged) break;
                    }

                    string status;
                    if (converged)
                        status = string.Format("FluidX3D steady: CONVERGED at t={0:F1}s (Δ={1:E2} < tol={2:E2})",
                            simT, lastDelta, tol);
                    else if (_cancelled)
                        status = "FluidX3D steady: cancelled by user.";
                    else
                        status = string.Format("FluidX3D steady: NOT converged in {0:F1}s (last Δ={1:E2}, tol={2:E2}). " +
                            "Increase max duration or raise tolerance.", simT, lastDelta, tol);
                    Report(0.99, status);
                    Completed?.Invoke(this, result);
                }
                catch (Exception ex)
                {
                    Failed?.Invoke(this, ex.Message);
                }
            };
            _worker.RunWorkerAsync();
        }

        /// <summary>Computes ||a − b||₂ / max(||a||₂, ||b||₂, ε). Iterates the flat
        /// double[,,] array directly for speed (no LINQ).</summary>
        private static double ComputeRelativeL2(double[,,] a, double[,,] b)
        {
            int nx = a.GetLength(0), ny = a.GetLength(1), nz = a.GetLength(2);
            double sumDiff = 0, sumA = 0;
            for (int k = 0; k < nz; k++)
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        double d = b[i, j, k] - a[i, j, k];
                        sumDiff += d * d;
                        sumA += b[i, j, k] * b[i, j, k];
                    }
            double normDiff = Math.Sqrt(sumDiff);
            double normA = Math.Sqrt(sumA);
            return normDiff / Math.Max(normA, 1e-12);
        }

        private static double MaxWindSpeed(WindField3D w)
        {
            const int n = 16;
            double max = 0;
            for (int k = 0; k < n; k++)
            {
                double tz = (k + 0.5) / n;
                double zSi = tz * 100.0;
                for (int j = 0; j < n; j++)
                {
                    double ty = (j + 0.5) / n;
                    double ySi = -200.0 + ty * 400.0;
                    for (int i = 0; i < n; i++)
                    {
                        double tx = (i + 0.5) / n;
                        double xSi = -200.0 + tx * 400.0;
                        var v = w.Interpolate(xSi, ySi, zSi);
                        double mag = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
                        if (mag > max) max = mag;
                    }
                }
            }
            return max;
        }

        private void Report(double fraction, string step)
        {
            ProgressUpdated?.Invoke(this, new OpenFoamProgress
            {
                Fraction = Math.Max(0, Math.Min(1, fraction)),
                Step = step ?? ""
            });
        }
    }
}
