using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    public class HeadlessResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string CasePath { get; set; }
        public double MaxConcentration { get; set; }
        public int TimeStepCount { get; set; }
        public List<MonitorTimeSeries> MonitorData { get; set; } = new List<MonitorTimeSeries>();
        public double[,,] ConcentrationField { get; set; }
    }

    public class MonitorTimeSeries
    {
        public string Name { get; set; }
        public Point3D Position { get; set; }
        public List<double> Times { get; set; } = new List<double>();
        public List<double> Concentrations { get; set; } = new List<double>();
    }

    public static class HeadlessRunner
    {
        public static HeadlessResult RunGaussianPlume(Scene3D scene, int scenarioIndex = -1,
            Action<string> log = null)
        {
            var scenario = ResolveScenario(scene, scenarioIndex);
            if (scenario == null)
                return new HeadlessResult { Success = false, Error = "No dispersion scenario found" };

            log?.Invoke("Initializing Gaussian Plume (Steady-State)...");
            var engine = new GaussianPlumeEngine();
            engine.Initialize(scenario);

            log?.Invoke("Evaluating concentration field...");

            int gridRes = scenario.GridResolution;
            double domain = scenario.DomainSizeM;
            double cellSize = (domain * 2.0) / gridRes;
            int nz = gridRes / 2 > 0 ? gridRes / 2 : 1;
            var origin = new Point3D(-domain, -domain, 0);

            var field = new double[gridRes, gridRes, nz];
            double maxC = 0;

            for (int i = 0; i < gridRes; i++)
            {
                double x = origin.X + i * cellSize;
                for (int j = 0; j < gridRes; j++)
                {
                    double y = origin.Y + j * cellSize;
                    for (int k = 0; k < nz; k++)
                    {
                        double z = origin.Z + k * cellSize;
                        double c = engine.EvaluateConcentration(x, y, z);
                        field[i, j, k] = c;
                        if (c > maxC) maxC = c;
                    }
                }
            }

            log?.Invoke(string.Format(CultureInfo.InvariantCulture,
                "Done. Max concentration: {0:G6} kg/m³", maxC));

            var monitorData = EvaluateMonitors(scene, engine);

            foreach (var md in monitorData)
                log?.Invoke(string.Format(CultureInfo.InvariantCulture,
                    "  Monitor '{0}' at ({1},{2},{3}): {4:G6} kg/m³",
                    md.Name, md.Position.X, md.Position.Y, md.Position.Z,
                    md.Concentrations.Count > 0 ? md.Concentrations[0] : 0));

            return new HeadlessResult
            {
                Success = true,
                MaxConcentration = maxC,
                ConcentrationField = field,
                MonitorData = monitorData
            };
        }

        public static HeadlessResult RunGaussianPuff(Scene3D scene, int scenarioIndex = -1,
            Action<string> log = null)
        {
            var scenario = ResolveScenario(scene, scenarioIndex);
            if (scenario == null)
                return new HeadlessResult { Success = false, Error = "No dispersion scenario found" };

            log?.Invoke("Initializing Gaussian Puff...");
            var engine = new GaussianPuffEngine();
            engine.Initialize(scenario);

            double dt = scenario.TimeStepS;
            double duration = scenario.SimulationDurationS;
            int steps = (int)(duration / dt);

            var monitors = scene.MonitorPoints;
            var monitorData = new List<MonitorTimeSeries>();
            foreach (var m in monitors)
                monitorData.Add(new MonitorTimeSeries
                {
                    Name = m.Name,
                    Position = m.Position
                });

            double maxC = 0;

            for (int s = 0; s <= steps; s++)
            {
                double t = s * dt;
                engine.StepTo(t);

                for (int mi = 0; mi < monitors.Count; mi++)
                {
                    var pos = monitors[mi].Position;
                    double c = engine.EvaluateConcentration(pos.X, pos.Y, pos.Z);
                    monitorData[mi].Times.Add(t);
                    monitorData[mi].Concentrations.Add(c);
                    if (c > maxC) maxC = c;
                }

                if (s % 100 == 0)
                    log?.Invoke(string.Format(CultureInfo.InvariantCulture,
                        "  t = {0:F1} / {1:F1} s  (puffs: {2})",
                        t, duration, engine.ActivePuffs.Count));
            }

            log?.Invoke(string.Format(CultureInfo.InvariantCulture,
                "Done. Max concentration at monitors: {0:G6} kg/m³", maxC));

            return new HeadlessResult
            {
                Success = true,
                MaxConcentration = maxC,
                MonitorData = monitorData
            };
        }

        public static HeadlessResult RunCfd(Scene3D scene, CfdConfiguration cfdConfig,
            CfdSolverType? solverOverride = null, int scenarioIndex = -1,
            Action<string> log = null, CancellationToken cancel = default)
        {
            var scenario = ResolveScenario(scene, scenarioIndex);
            if (scenario == null)
                return new HeadlessResult { Success = false, Error = "No dispersion scenario found" };

            var solverType = solverOverride ?? scenario.SolverType;

            // FluidX3D solvers run entirely in-process via the GPU LBM bridge —
            // no OpenFOAM environment / external process to set up. Route them
            // through a dedicated dispatcher so RunCfd's OpenFOAM-only setup
            // (env probing, GridResolution overwrite, OpenFoamRunner) never
            // executes for those cases.
            if (IsFluidX3DSolver(solverType))
                return RunFluidX3D(scene, scenario, cfdConfig, solverType, log, cancel);

            var env = new OpenFoamEnvironment();
            env.Configure(cfdConfig.OpenFoamPath, cfdConfig.DetectedEnvironment, cfdConfig.WslDistroName);
            if (!env.IsAvailable)
                return new HeadlessResult { Success = false, Error = "OpenFOAM not available: " + env.StatusMessage };

            if (cfdConfig.GridResolution > 0)
                scenario.GridResolution = cfdConfig.GridResolution;

            log?.Invoke(string.Format("Running CFD solver: {0}", solverType));
            log?.Invoke(string.Format("  Domain: {0}m, Grid: {1}, Duration: {2}s",
                scenario.DomainSizeM, scenario.GridResolution, scenario.SimulationDurationS));

            foreach (var src in scenario.Sources)
                log?.Invoke(string.Format(CultureInfo.InvariantCulture,
                    "  Source '{0}': effective rate = {1:G6} kg/s",
                    src.Name, src.EffectiveReleaseRateKgPerS));

            var runner = new OpenFoamRunner(env);
            HeadlessResult result = null;
            var done = new ManualResetEventSlim(false);

            runner.ProgressUpdated += (s, p) =>
            {
                if (!string.IsNullOrEmpty(p.Step))
                    log?.Invoke(string.Format("  [{0:P0}] {1}", p.Fraction, p.Step));
            };
            runner.Completed += (s, ofResult) =>
            {
                log?.Invoke(string.Format("  CFD complete: {0} time steps loaded", ofResult.TimeSteps.Count));
                result = new HeadlessResult
                {
                    Success = ofResult.IsLoaded,
                    CasePath = runner.CasePath,
                    TimeStepCount = ofResult.TimeSteps.Count,
                    Error = ofResult.IsLoaded ? null : "No results could be read from the solver output"
                };

                if (ofResult.IsLoaded && ofResult.TimeSteps.Count > 0)
                {
                    var lastField = ofResult.GetField(ofResult.TimeSteps[ofResult.TimeSteps.Count - 1]);
                    if (lastField != null)
                    {
                        result.ConcentrationField = lastField;
                        double maxC = 0;
                        int nx = lastField.GetLength(0), ny = lastField.GetLength(1), nz = lastField.GetLength(2);
                        for (int i = 0; i < nx; i++)
                            for (int j = 0; j < ny; j++)
                                for (int k = 0; k < nz; k++)
                                    if (lastField[i, j, k] > maxC) maxC = lastField[i, j, k];
                        result.MaxConcentration = maxC;
                        log?.Invoke(string.Format(CultureInfo.InvariantCulture,
                            "  Max concentration: {0:G6} kg/m³", maxC));
                    }
                }

                done.Set();
            };
            runner.Failed += (s, msg) =>
            {
                result = new HeadlessResult
                {
                    Success = false,
                    Error = msg,
                    CasePath = runner.CasePath
                };
                done.Set();
            };

            // All surviving OpenFOAM solvers are transient — the old
            // steady-state pseudo-transient variants (scalarTransportFoamSteady,
            // scalarSimpleFoam, rhoSimpleFoam) were removed as redundant.
            runner.RunAsync(scenario, cfdConfig, solverType);

            while (!done.Wait(500))
            {
                if (cancel.IsCancellationRequested)
                {
                    runner.Cancel();
                    return new HeadlessResult { Success = false, Error = "Cancelled" };
                }
            }

            return result ?? new HeadlessResult { Success = false, Error = "Unknown error" };
        }

        /// <summary>True for the four GPU LBM runners that bypass OpenFOAM.</summary>
        private static bool IsFluidX3DSolver(CfdSolverType t)
        {
            return t == CfdSolverType.FluidX3DWind
                || t == CfdSolverType.FluidX3DDispersion
                || t == CfdSolverType.FluidX3DDispersionSteady
                || t == CfdSolverType.FluidX3DFire;
        }

        /// <summary>
        /// Headless driver for the FluidX3D family. Picks the right runner from
        /// <paramref name="solverType"/>, hooks its ProgressUpdated /
        /// Completed / Failed events into <paramref name="log"/>, and waits for
        /// the background worker to finish. Returns the same
        /// <see cref="HeadlessResult"/> shape as the OpenFOAM path so callers
        /// (CLI, validation harness, programmatic users) don't need to branch
        /// on the solver family.
        /// </summary>
        private static HeadlessResult RunFluidX3D(Scene3D scene, DispersionScenario scenario,
            CfdConfiguration cfdConfig, CfdSolverType solverType,
            Action<string> log, CancellationToken cancel)
        {
            // Wind-field generation is conceptually a different beast — there's
            // no DispersionScenario to drive it; the WindFieldScenario lives on
            // the scene. The headless path expects --simulation or --solver to
            // be dispersion-shaped, so steer the user to the right interface.
            if (solverType == CfdSolverType.FluidX3DWind)
            {
                return new HeadlessResult
                {
                    Success = false,
                    Error = "FluidX3D wind-field generation is not a dispersion solver — "
                          + "it precomputes a velocity field consumed by other runs. "
                          + "Run it from the UI (Wind Field Manager) or wait for the "
                          + "--run-windfield CLI mode."
                };
            }

            log?.Invoke(string.Format("Running FluidX3D solver: {0}", solverType));
            log?.Invoke(string.Format("  Domain: {0}m, Grid: {1}, Duration: {2}s",
                scenario.DomainSizeM, scenario.GridResolution, scenario.SimulationDurationS));

            // Collect obstacles the same way the UI does — every decoration's
            // pre-computed BoundingBox. The FluidX3D tracer voxelises these into
            // TYPE_S cells so the plume wraps around the geometry.
            var obstacles = new System.Collections.Generic.List<BoundingBox>();
            if (scene.Decorations != null)
                foreach (var d in scene.Decorations)
                    if (d != null && d.BoundingBox != null) obstacles.Add(d.BoundingBox);

            HeadlessResult result = null;
            var done = new ManualResetEventSlim(false);
            string casePath = null;

            EventHandler<OpenFoamProgress> onProgress = (s, p) =>
            {
                if (!string.IsNullOrEmpty(p.Step))
                    log?.Invoke(string.Format(CultureInfo.InvariantCulture,
                        "  [{0:P0}] {1}", p.Fraction, p.Step));
            };
            EventHandler<OpenFoamResult> onCompleted = (s, ofResult) =>
            {
                log?.Invoke(string.Format("  FluidX3D complete: {0} time step(s) loaded",
                    ofResult.TimeSteps.Count));
                result = new HeadlessResult
                {
                    Success = ofResult.IsLoaded,
                    CasePath = casePath,
                    TimeStepCount = ofResult.TimeSteps.Count,
                    Error = ofResult.IsLoaded ? null
                          : "FluidX3D produced no readable result."
                };

                if (ofResult.IsLoaded && ofResult.TimeSteps.Count > 0)
                {
                    var lastField = ofResult.GetField(ofResult.TimeSteps[ofResult.TimeSteps.Count - 1]);
                    if (lastField != null)
                    {
                        result.ConcentrationField = lastField;
                        double maxC = 0;
                        int nx = lastField.GetLength(0), ny = lastField.GetLength(1), nz = lastField.GetLength(2);
                        for (int i = 0; i < nx; i++)
                            for (int j = 0; j < ny; j++)
                                for (int k = 0; k < nz; k++)
                                    if (lastField[i, j, k] > maxC) maxC = lastField[i, j, k];
                        result.MaxConcentration = maxC;
                        log?.Invoke(string.Format(CultureInfo.InvariantCulture,
                            "  Max concentration: {0:G6}", maxC));
                    }
                }
                done.Set();
            };
            EventHandler<string> onFailed = (s, msg) =>
            {
                result = new HeadlessResult
                {
                    Success = false,
                    Error = msg,
                    CasePath = casePath
                };
                done.Set();
            };

            switch (solverType)
            {
                case CfdSolverType.FluidX3DDispersion:
                {
                    var runner = new FluidX3DRunner();
                    runner.ProgressUpdated += onProgress;
                    runner.Completed += onCompleted;
                    runner.Failed += onFailed;
                    runner.RunAsync(scenario, cfdConfig, scene, obstacles,
                        CfdSolverType.FluidX3DDispersion);
                    while (!done.Wait(500))
                        if (cancel.IsCancellationRequested)
                            { runner.Cancel(); return new HeadlessResult { Success = false, Error = "Cancelled" }; }
                    casePath = runner.CasePath;
                    break;
                }
                case CfdSolverType.FluidX3DDispersionSteady:
                {
                    var runner = new FluidX3DSteadyDispersionRunner();
                    runner.ProgressUpdated += onProgress;
                    runner.Completed += onCompleted;
                    runner.Failed += onFailed;
                    runner.RunAsync(scenario, cfdConfig, scene, obstacles);
                    while (!done.Wait(500))
                        if (cancel.IsCancellationRequested)
                            { runner.Cancel(); return new HeadlessResult { Success = false, Error = "Cancelled" }; }
                    casePath = runner.CasePath;
                    break;
                }
                case CfdSolverType.FluidX3DFire:
                {
                    var runner = new FluidX3DFireRunner();
                    runner.ProgressUpdated += onProgress;
                    runner.Completed += onCompleted;
                    runner.Failed += onFailed;
                    runner.RunAsync(scenario, cfdConfig, scene, obstacles);
                    while (!done.Wait(500))
                        if (cancel.IsCancellationRequested)
                            { runner.Cancel(); return new HeadlessResult { Success = false, Error = "Cancelled" }; }
                    casePath = runner.CasePath;
                    break;
                }
                default:
                    return new HeadlessResult
                    {
                        Success = false,
                        Error = "Unsupported FluidX3D solver: " + solverType
                    };
            }

            // Fix-up casePath on the result (the lambda captured null before the
            // runner assigned its own path).
            if (result != null && string.IsNullOrEmpty(result.CasePath))
                result.CasePath = casePath;
            return result ?? new HeadlessResult { Success = false, Error = "FluidX3D run produced no result" };
        }

        /// <summary>
        /// Runs a project-level <see cref="Simulation"/> headlessly. Adapts its snapshot
        /// (or live source/wind-field references when the snapshot isn't populated) into a
        /// transient <see cref="DispersionScenario"/> and delegates to <see cref="RunCfd"/>.
        /// </summary>
        public static HeadlessResult RunSimulation(Scene3D scene, Simulation sim,
            Action<string> log = null, CancellationToken cancel = default)
        {
            if (scene == null || sim == null)
                return new HeadlessResult { Success = false, Error = "scene or simulation is null" };

            var src = sim.SnapshotSource
                      ?? scene.TopLevelSources.FirstOrDefault(s => s.Id == sim.SourceId);
            if (src == null)
                return new HeadlessResult { Success = false, Error = "Source not found for simulation '" + sim.Name + "'" };

            var meteo = sim.SnapshotMeteo
                        ?? (scene.WindFieldScenarios.FirstOrDefault(w => w.Id == sim.WindFieldId)?.Meteo)
                        ?? scene.GeneralSettings?.DefaultMeteo
                        ?? new MeteorologicalConditions();

            var cfd = sim.SnapshotCfdConfig ?? new CfdConfiguration();

            // Resolve the source's gas (for cryogenic preset) when not already snapshotted.
            GasLibraryItem gas = sim.SnapshotGas;
            if (gas == null && !string.IsNullOrEmpty(src.GasRefId))
                gas = scene.GasLibrary.FirstOrDefault(g => g.Id == src.GasRefId);

            // Re-apply the per-solver atmospheric defaults — idempotent when already applied.
            CfdConfigurationPresets.ApplyForSolver(cfd, sim.SolverType, gas, meteo);

            var transient = new DispersionScenario
            {
                Id = sim.Id,
                Name = sim.Name,
                Meteo = meteo,
                SimulationDurationS = sim.SnapshotDurationS,
                TimeStepS = sim.SnapshotTimeStepS,
                SnapshotCount = sim.SnapshotCount > 0 ? sim.SnapshotCount : 20,
                DomainSizeM = sim.SnapshotDomainSizeM,
                GridResolution = sim.SnapshotGridResolution,
                SolverType = sim.SolverType,
                CfdConfig = cfd,
                WindFieldScenarioId = sim.WindFieldId
            };
            transient.Sources.Add(src);

            scene.DispersionScenarios.Add(transient);
            int prevActive = scene.ActiveScenarioIndex;
            scene.ActiveScenarioIndex = scene.DispersionScenarios.Count - 1;
            try
            {
                return RunCfd(scene, cfd, sim.SolverType, scene.ActiveScenarioIndex, log, cancel);
            }
            finally
            {
                scene.DispersionScenarios.Remove(transient);
                scene.ActiveScenarioIndex = prevActive;
            }
        }

        public static HeadlessResult RunFromFile(string xmlPath, string solverName = null,
            CfdConfiguration cfdConfig = null, int scenarioIndex = -1,
            Action<string> log = null, CancellationToken cancel = default,
            string simulationSelector = null)
        {
            log?.Invoke("Loading scene: " + xmlPath);
            var scene = SceneFileLoader.Load(xmlPath);

            // Project-level Simulation selector (.dsproj first-class path).
            if (!string.IsNullOrEmpty(simulationSelector))
            {
                var sim = scene.Simulations.FirstOrDefault(
                    s => string.Equals(s.Id, simulationSelector, StringComparison.OrdinalIgnoreCase)
                      || string.Equals(s.Name, simulationSelector, StringComparison.OrdinalIgnoreCase));
                if (sim == null)
                    return new HeadlessResult { Success = false, Error = "Simulation not found: " + simulationSelector };
                log?.Invoke("Running Simulation '" + sim.Name + "' (solver: " + sim.SolverType + ")");
                if (cfdConfig != null && sim.SnapshotCfdConfig == null)
                    sim.SnapshotCfdConfig = cfdConfig;
                return RunSimulation(scene, sim, log, cancel);
            }

            var scenario = ResolveScenario(scene, scenarioIndex);
            if (scenario == null)
                return new HeadlessResult { Success = false, Error = "No dispersion scenario in file" };

            log?.Invoke(string.Format("Scenario: '{0}', Sources: {1}",
                scenario.Name, scenario.Sources.Count));

            string solver = solverName?.ToLowerInvariant() ?? "";

            if (solver == "plume" || solver == "steadystate" || solver == "gaussian-plume")
                return RunGaussianPlume(scene, scenarioIndex, log);

            if (solver == "puff" || solver == "gaussian-puff")
                return RunGaussianPuff(scene, scenarioIndex, log);

            if (solver == "" && cfdConfig == null)
            {
                log?.Invoke("No solver specified and no CFD config — defaulting to Gaussian Plume");
                return RunGaussianPlume(scene, scenarioIndex, log);
            }

            CfdSolverType? solverType = null;
            if (!string.IsNullOrEmpty(solver))
            {
                switch (solver)
                {
                    case "rhoreactingbuoyantfoam": solverType = CfdSolverType.RhoReactingBuoyantFoam; break;
                    // FluidX3D GPU LBM family. The wind-field runner has no
                    // DispersionScenario shape so it can't be invoked through
                    // this entry point — RunFluidX3D returns a clear error.
                    case "fluidx3dwind":             solverType = CfdSolverType.FluidX3DWind; break;
                    case "fluidx3ddispersion":       solverType = CfdSolverType.FluidX3DDispersion; break;
                    case "fluidx3ddispersionsteady": solverType = CfdSolverType.FluidX3DDispersionSteady; break;
                    case "fluidx3dfire":             solverType = CfdSolverType.FluidX3DFire; break;

                    // Legacy aliases — silently route to rhoReactingBuoyantFoam
                    // so older CLI scripts and CI pipelines still work.
                    case "scalartransportfoam":
                    case "scalartransportfoamsteady":
                    case "buoyantpimplefoam":
                    case "pimplefoam":
                    case "reactingfoam":
                    case "scalarsimplefoam":
                    case "rhosimplefoam":
                        solverType = CfdSolverType.RhoReactingBuoyantFoam;
                        log?.Invoke($"[deprecation] Solver '{solver}' is no longer supported; routing to rhoReactingBuoyantFoam.");
                        break;

                    default:
                        return new HeadlessResult
                        {
                            Success = false,
                            Error = "Unknown solver: " + solverName
                        };
                }
            }

            return RunCfd(scene, cfdConfig ?? new CfdConfiguration(), solverType, scenarioIndex, log, cancel);
        }

        private static DispersionScenario ResolveScenario(Scene3D scene, int scenarioIndex)
        {
            if (scene.DispersionScenarios.Count == 0) return null;
            int idx = scenarioIndex >= 0 ? scenarioIndex : scene.ActiveScenarioIndex;
            if (idx >= scene.DispersionScenarios.Count) idx = 0;
            return scene.DispersionScenarios[idx];
        }

        private static List<MonitorTimeSeries> EvaluateMonitors(Scene3D scene, IConcentrationField engine)
        {
            var result = new List<MonitorTimeSeries>();
            foreach (var m in scene.MonitorPoints)
            {
                double c = engine.EvaluateConcentration(m.Position.X, m.Position.Y, m.Position.Z);
                var ts = new MonitorTimeSeries
                {
                    Name = m.Name,
                    Position = m.Position
                };
                ts.Times.Add(0);
                ts.Concentrations.Add(c);
                result.Add(ts);
            }
            return result;
        }
    }
}
