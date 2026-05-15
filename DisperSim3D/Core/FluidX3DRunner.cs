using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// FluidX3D-backed transient dispersion. The wind field comes from a FluidX3D
    /// GPU LBM run (or a loaded windfield.bin), and species transport runs on the
    /// CPU using <see cref="DispersionTracerEngine"/>. FluidX3D's own TEMPERATURE
    /// extension couples spuriously to the velocity lattice in this DLL's build,
    /// so we keep that off and solve advection-diffusion ourselves.
    /// Mirrors <see cref="OpenFoamRunner"/>'s event surface so
    /// <c>SimulationManager.RunCfdAsync</c> dispatches without other changes.
    /// </summary>
    public class FluidX3DRunner
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

        public void Cancel()
        {
            _cancelled = true;
            try { _cts?.Cancel(); } catch { }
        }

        public void RunAsync(DispersionScenario scenario, CfdConfiguration config,
            Scene3D scene,
            System.Collections.Generic.List<BoundingBox> obstacles = null,
            CfdSolverType solverType = CfdSolverType.FluidX3DDispersion)
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
                    Report(0.02, "FluidX3D dispersion: resolving wind field...");

                    // Resolve wind field. Prefer a pre-run one referenced by the scenario;
                    // otherwise warn and bail. We do NOT run a fresh wind field here — that
                    // would couple two solvers in one call. Encourage the user to run the
                    // wind field first.
                    var wf = WindFieldResolver.FindWindFieldScenario(scene, scenario);
                    WindField3D wind = wf?.WindField;
                    if (wind == null && wf != null && wf.UseFluidX3D)
                        wind = FluidX3DWindFieldRunner.LoadFromCase(wf);
                    if (wind == null && wf != null)
                        wind = WindFieldRunner.LoadFromCase(wf);
                    if (wind == null)
                    {
                        Failed?.Invoke(this, "FluidX3D dispersion needs a Ready wind field; " +
                            "run the associated Wind Field first (set UseFluidX3D=true to use the GPU solver).");
                        return;
                    }

                    int nx = scenario.GridResolution;
                    int ny = scenario.GridResolution;
                    int nz = Math.Max(8, scenario.GridResolution / 2);
                    double domain = scenario.DomainSizeM;
                    double height = wf?.DomainHeightM > 0 ? wf.DomainHeightM : domain;
                    double duration = scenario.SimulationDurationS;
                    int snapCount = scenario.SnapshotCount > 0 ? scenario.SnapshotCount : 20;
                    double writeInterval = Math.Max(scenario.SimulationDurationS / snapCount,
                        scenario.SimulationDurationS / 1000.0); // hard cap at 1000 snapshots

                    Report(0.05, "FluidX3D dispersion: initialising tracer engine...");

                    double diff = config?.DiffusivityM2PerS > 0 ? config.DiffusivityM2PerS : 1e-5;
                    double cellMGlobal = scenario.DomainSizeM * 2.0 / nx;
                    if (scenario.Meteo != null)
                    {
                        double ws = Math.Max(scenario.Meteo.WindSpeed, 0.5);
                        // Subgrid turbulent diffusivity: Dt = (Cs²/Sct) · Δ · U
                        // Cs=0.092 (standard Smagorinsky for shear flows), Sct=0.7
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
                        Report(0.05, string.Format(
                            "FluidX3D buoyant tracer: MW={0:F4} kg/mol, Texit={1:F1} K, Tamb={2:F1} K, grid={3}x{4}x{5} cell={6:F1}m",
                            gasMW, exitT, ambientT, tnx, tny, tnz, tracerCellM));
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

                        double exitVel = src.ExitVelocityMPerS > 0 ? src.ExitVelocityMPerS
                            : src.ComputedExitVelocity;
                        bool isPool = useBuoyant && src.ReleaseRateKgPerS > 0
                            && exitVel < 1.0 && src.StackDiameterM > 1.0
                            && src.Position.Z <= cellMGlobal;

                        if (src.ReleaseRateKgPerS > 0)
                        {
                            double airDensity = ambientP * 0.029 / (8.314 * ambientT);
                            if (isPool)
                            {
                                double poolR = Math.Max(src.StackDiameterM / 2.0, tracerCellM);
                                buoyantEngine.SetPoolSource(src.Position.X, src.Position.Y,
                                    poolR, src.ReleaseRateKgPerS, airDensity, exitT);
                                Report(0.06, string.Format(
                                    "FluidX3D pool source: pos=({0:F1},{1:F1}) m, poolR={2:F1} m, Q={3:E3} kg/s, D={4:E3} m²/s, buoyant",
                                    src.Position.X, src.Position.Y, poolR, src.ReleaseRateKgPerS, diff));
                            }
                            else if (useBuoyant)
                            {
                                buoyantEngine.SetMassSource(src.Position.X, src.Position.Y, src.Position.Z,
                                    radiusM, src.ReleaseRateKgPerS, airDensity, exitT);
                                Report(0.06, string.Format(
                                    "FluidX3D mass source: pos=({0:F1},{1:F1},{2:F1}) m, r={3:F3} m, Q={4:E3} kg/s, D={5:E3} m²/s, cell={6:F3} m, buoyant",
                                    src.Position.X, src.Position.Y, src.Position.Z, radiusM,
                                    src.ReleaseRateKgPerS, diff, cellMGlobal));
                            }
                            else
                            {
                                passiveEngine.SetMassSource(src.Position.X, src.Position.Y, src.Position.Z,
                                    radiusM, src.ReleaseRateKgPerS, airDensity);
                                Report(0.06, string.Format(
                                    "FluidX3D mass source: pos=({0:F1},{1:F1},{2:F1}) m, r={3:F3} m, Q={4:E3} kg/s, D={5:E3} m²/s, cell={6:F3} m",
                                    src.Position.X, src.Position.Y, src.Position.Z, radiusM,
                                    src.ReleaseRateKgPerS, diff, cellMGlobal));
                            }
                        }
                        else
                        {
                            if (useBuoyant)
                            {
                                buoyantEngine.SetSphericalSource(src.Position.X, src.Position.Y, src.Position.Z,
                                    radiusM, 1.0, exitT);
                            }
                            else
                            {
                                passiveEngine.SetSphericalSource(src.Position.X, src.Position.Y, src.Position.Z,
                                    radiusM: radiusM, concentration: 1.0);
                            }
                            Report(0.06, string.Format(
                                "FluidX3D source: pos=({0:F1},{1:F1},{2:F1}) m, r={3:F1} m, cell={4:F2} m{5}",
                                src.Position.X, src.Position.Y, src.Position.Z, radiusM, cellMGlobal,
                                useBuoyant ? ", buoyant" : ""));
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
                    double dtSnap = Math.Min(writeInterval, dtMax);
                    int stepsPerSnap = Math.Max(1, (int)Math.Ceiling(writeInterval / dtMax));
                    double dt = writeInterval / stepsPerSnap;

                    int snapshots = (int)Math.Max(1, Math.Round(duration / writeInterval));

                    // Persist snapshots to disk so the result survives a save/reload of the
                    // project, and so the .dsproj bundler can pick them up. One <time>.bin
                    // per snapshot, written via the same OpenFoamResult.SaveBinaryField
                    // helper the Gaussian Puff path uses — that way LoadCfdSimulation can
                    // reuse its existing scan-the-directory path.
                    _casePath = System.IO.Path.Combine(TempManager.GetWorkDir(),
                        "DisperSim3D_fx3d_sim_" + (scenario.Id ?? Guid.NewGuid().ToString("N")));
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
                        CaseDir = _casePath ?? ""
                    };

                    double simT = 0;
                    for (int snap = 0; snap < snapshots; snap++)
                    {
                        if (_cancelled) break;
                        for (int sub = 0; sub < stepsPerSnap; sub++)
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
                        var snapField = new double[outNx, outNy, outNz];
                        Array.Copy(current, snapField, current.Length);

                        // Use exact-multiple time so TimeSteps = [15, 30, ..., 300] for
                        // duration=300, writeInterval=15 — float accumulation of simT drifts
                        // and the playback bar's max time ends up at 285 instead of 300.
                        double tSi = (snap + 1) * writeInterval;
                        result.TimeSteps.Add(tSi);
                        result.PreloadField(tSi, snapField);

                        // Write to disk for persistence — same .bin format as Gaussian Puff,
                        // filename = <time>.bin so the existing scan path picks it up on reload.
                        if (!string.IsNullOrEmpty(_casePath))
                        {
                            string binPath = System.IO.Path.Combine(_casePath,
                                tSi.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ".bin");
                            try
                            {
                                OpenFoamResult.SaveBinaryField(binPath, snapField);
                                result.TimeStepPaths[tSi] = binPath;
                            }
                            catch { /* persistence failure shouldn't fail the run */ }
                        }

                        double frac = 0.05 + 0.92 * (snap + 1) / snapshots;
                        Report(frac, "FluidX3D dispersion snap " + (snap + 1) + "/" + snapshots);
                    }

                    Report(0.99, "FluidX3D dispersion: complete");
                    Completed?.Invoke(this, result);
                }
                catch (Exception ex)
                {
                    Failed?.Invoke(this, ex.Message);
                }
            };
            _worker.RunWorkerAsync();
        }

        // Legacy overload kept for the SimulationManager dispatch which doesn't have Scene.
        public void RunAsync(DispersionScenario scenario, CfdConfiguration config,
            CfdSolverType solverType = CfdSolverType.FluidX3DDispersion)
        {
            RunAsync(scenario, config, /*scene*/ null, /*obstacles*/ null, solverType);
        }

        private static double MaxWindSpeed(WindField3D w)
        {
            // Sparse sample of the domain to estimate the peak speed for CFL sizing.
            // 16³ ≈ 4k Interpolate calls — fast and good enough for picking dt.
            const int n = 16;
            double max = 0;
            for (int k = 0; k < n; k++)
            {
                double tz = (k + 0.5) / n; // 0..1
                double zSi = tz * 100.0; // domain-height guess; engine doesn't care about exact bound here
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
                Fraction = fraction,
                Step = step,
                LogLine = step
            });
        }
    }
}
