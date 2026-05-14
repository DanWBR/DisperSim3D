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
                    var src = scenario.Sources != null && scenario.Sources.Count > 0
                        ? scenario.Sources[0] : null;
                    double decay = src?.Gas != null && src.Gas.HalfLifeS > 0
                        ? Math.Log(2.0) / src.Gas.HalfLifeS : 0.0;

                    var engine = new DispersionTracerEngine(wind, domain, height, nx, ny, nz,
                        diff, decay, _obstacles);

                    if (src != null)
                    {
                        // Continuous release: clamp source cells to a normalised concentration of
                        // 1.0 (post-processing rescales by mass flow rate if needed).
                        // Radius scaled to at least ~5 cells so the marching-cubes renderer has
                        // enough volume to extract an isosurface (1-2 cell-wide sources got
                        // smoothed away by interpolation, making the source look invisible).
                        double cellM = scenario.DomainSizeM * 2.0 / nx;
                        double radiusM = Math.Max(5.0 * cellM, 8.0);
                        engine.SetSphericalSource(src.Position.X, src.Position.Y, src.Position.Z,
                            radiusM: radiusM, concentration: 1.0);
                        Report(0.06, string.Format(
                            "FluidX3D source: pos=({0:F1},{1:F1},{2:F1}) m, r={3:F1} m, cell={4:F2} m, domainHalf={5:F0} m",
                            src.Position.X, src.Position.Y, src.Position.Z, radiusM, cellM, scenario.DomainSizeM));
                    }

                    // Marching timesteps. Use a CFL-respecting dt: dt <= cellSize / max|U|.
                    double maxU = MaxWindSpeed(wind);
                    if (maxU < 0.1) maxU = 0.1;
                    double cellSize = Math.Min(engine.DxM, Math.Min(engine.DyM, engine.DzM));
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

                    var result = new OpenFoamResult
                    {
                        GridNx = nx, GridNy = ny, GridNz = nz,
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
                            engine.Step(dt);
                            simT += dt;
                        }

                        // Clone the field — engine reuses its internal buffer between steps.
                        var current = engine.Snapshot();
                        var snapField = new double[nx, ny, nz];
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
