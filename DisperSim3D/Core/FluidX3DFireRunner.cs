using System;
using System.ComponentModel;
using System.Threading;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// FluidX3D-backed transient fire-plume runner. Uses the same cold-ambient
    /// wind field as <see cref="FluidX3DRunner"/>, but transports temperature +
    /// smoke through a <see cref="FireTracerEngine"/> with Boussinesq buoyancy.
    /// Combustion chemistry is NOT modelled — the source represents a hot
    /// post-combustion zone (jet/pool fire convective plume root) at a user-set
    /// temperature, and the engine resolves how the hot smoky air rises, bends
    /// with the cross-wind, and disperses downwind. Radiative footprint is
    /// overlaid analytically through <see cref="JetFireModel"/> in the renderer.
    ///
    /// Output snapshots: <c>{time}.bin</c> (smoke mass fraction, same format as
    /// the dispersion runner so existing playback code works unchanged) plus
    /// <c>{time}_T.bin</c> for temperature in Kelvin.
    /// </summary>
    public class FluidX3DFireRunner
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

        // Default source-plume temperature when a Simulation doesn't specify one.
        // 1500 K is the convective-plume root for hydrocarbon jet/pool fires
        // (the actual flame is hotter, ~2000 K, but the entrained smoke column
        // that drives the rising plume is cooler — Drysdale, "Introduction to Fire
        // Dynamics" §4). Users can override per source.
        private const double DefaultFireTempK = 1500.0;

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
                    Report(0.02, "FluidX3D fire: resolving wind field...");
                    var wf = WindFieldResolver.FindWindFieldScenario(scene, scenario);
                    WindField3D wind = wf?.WindField;
                    if (wind == null && wf != null && wf.UseFluidX3D)
                        wind = FluidX3DWindFieldRunner.LoadFromCase(wf);
                    if (wind == null && wf != null)
                        wind = WindFieldRunner.LoadFromCase(wf);
                    if (wind == null)
                    {
                        Failed?.Invoke(this, "FluidX3D fire needs a Ready wind field; run the " +
                            "associated Wind Field first.");
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
                        scenario.SimulationDurationS / 1000.0);

                    Report(0.05, "FluidX3D fire: initialising tracer engine...");

                    double ambientT = scenario.Meteo?.AmbientTemperature > 0
                        ? scenario.Meteo.AmbientTemperature : 293.15;
                    // Thermal diffusivity of air ~2.2e-5 m²/s; species ~1e-5.
                    double thermalDiff = 2.2e-5;
                    double speciesDiff = config?.DiffusivityM2PerS > 0 ? config.DiffusivityM2PerS : 1e-5;
                    // Radiative cooling rate of an entrained smoke plume — captures the
                    // ~exp(-r/L) decay seen in optically-thin gray-gas plumes. L ~30 m
                    // for hydrocarbon smoke at the relevant scale → decay ~ 0.05/s.
                    double thermalDecay = 0.05;

                    var src = scenario.Sources != null && scenario.Sources.Count > 0
                        ? scenario.Sources[0] : null;

                    // Source plume temperature: use ExitTemperatureK from the source when
                    // set above ambient (the user can dial in 1500–2000 K to fire-ify a
                    // release), otherwise fall back to a default jet-fire convective root.
                    double sourceT = (src != null && src.ExitTemperatureK > ambientT + 50)
                        ? src.ExitTemperatureK : DefaultFireTempK;

                    var engine = new FireTracerEngine(wind, domain, height, nx, ny, nz,
                        thermalDiffusivityM2PerS: thermalDiff,
                        speciesDiffusivityM2PerS: speciesDiff,
                        ambientTemperatureK: ambientT,
                        thermalDecayPerS: thermalDecay,
                        speciesDecayPerS: 0.0,
                        obstacles: _obstacles);

                    if (src != null)
                    {
                        double cellM = scenario.DomainSizeM * 2.0 / nx;
                        double radiusM = Math.Max(5.0 * cellM, 6.0);
                        engine.SetSphericalSource(src.Position.X, src.Position.Y, src.Position.Z,
                            radiusM: radiusM, sourceTemperatureK: sourceT, sourceY: 1.0);
                        Report(0.06, string.Format(
                            "Fire source: pos=({0:F1},{1:F1},{2:F1}) m, T={3:F0} K, r={4:F1} m, cell={5:F2} m",
                            src.Position.X, src.Position.Y, src.Position.Z, sourceT, radiusM, cellM));
                    }

                    // CFL sizing — include the buoyant vertical velocity at the source
                    // temperature because that's the highest local |U| in the domain.
                    double maxU = MaxWindSpeed(wind);
                    if (maxU < 0.1) maxU = 0.1;
                    double vBuoyAtSource = 9.81 * (sourceT - ambientT) / Math.Max(ambientT, 200.0);
                    double maxLocalU = Math.Max(maxU, Math.Abs(vBuoyAtSource));
                    double cellSize = Math.Min(engine.DxM, Math.Min(engine.DyM, engine.DzM));
                    double dtMax = 0.5 * cellSize / maxLocalU;
                    int stepsPerSnap = Math.Max(1, (int)Math.Ceiling(writeInterval / dtMax));
                    double dt = writeInterval / stepsPerSnap;

                    int snapshots = (int)Math.Max(1, Math.Round(duration / writeInterval));

                    _casePath = System.IO.Path.Combine(TempManager.GetWorkDir(),
                        "DisperSim3D_fx3dfire_sim_" + (scenario.Id ?? Guid.NewGuid().ToString("N")));
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

                    for (int snap = 0; snap < snapshots; snap++)
                    {
                        if (_cancelled) break;
                        for (int sub = 0; sub < stepsPerSnap; sub++)
                        {
                            if (_cancelled) break;
                            engine.Step(dt);
                        }

                        var liveY = engine.SnapshotConcentration();
                        var liveT = engine.SnapshotTemperature();
                        var snapY = new double[nx, ny, nz];
                        var snapT = new double[nx, ny, nz];
                        Array.Copy(liveY, snapY, liveY.Length);
                        Array.Copy(liveT, snapT, liveT.Length);

                        double tSi = (snap + 1) * writeInterval;
                        result.TimeSteps.Add(tSi);
                        result.PreloadField(tSi, snapY);

                        if (!string.IsNullOrEmpty(_casePath))
                        {
                            string tStr = tSi.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                            try
                            {
                                string yBin = System.IO.Path.Combine(_casePath, tStr + ".bin");
                                OpenFoamResult.SaveBinaryField(yBin, snapY);
                                result.TimeStepPaths[tSi] = yBin;

                                string tBin = System.IO.Path.Combine(_casePath, tStr + "_T.bin");
                                OpenFoamResult.SaveBinaryField(tBin, snapT);
                            }
                            catch { /* persistence failure shouldn't fail the run */ }
                        }

                        double frac = 0.05 + 0.92 * (snap + 1) / snapshots;
                        Report(frac, "FluidX3D fire snap " + (snap + 1) + "/" + snapshots);
                    }

                    Report(0.99, "FluidX3D fire: complete");
                    Completed?.Invoke(this, result);
                }
                catch (Exception ex)
                {
                    Failed?.Invoke(this, ex.Message);
                }
            };
            _worker.RunWorkerAsync();
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
