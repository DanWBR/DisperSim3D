using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Media.Media3D;
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
            var renderer = new DispersionRenderer();
            renderer.Initialize(scenario);

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

            bool isSteady = solverType == CfdSolverType.ScalarTransportFoamSteady
                         || solverType == CfdSolverType.ScalarSimpleFoam
                         || solverType == CfdSolverType.RhoSimpleFoam;

            if (isSteady)
                runner.RunSteadyAsync(scenario, cfdConfig, solverType);
            else
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

        public static HeadlessResult RunFromFile(string xmlPath, string solverName = null,
            CfdConfiguration cfdConfig = null, int scenarioIndex = -1,
            Action<string> log = null, CancellationToken cancel = default)
        {
            log?.Invoke("Loading scene: " + xmlPath);
            var scene = SceneFileLoader.Load(xmlPath);

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
                    case "scalartransportfoam": solverType = CfdSolverType.ScalarTransportFoam; break;
                    case "buoyantpimplefoam": solverType = CfdSolverType.BuoyantPimpleFoam; break;
                    case "pimplefoam": solverType = CfdSolverType.PimpleFoam; break;
                    case "reactingfoam": solverType = CfdSolverType.ReactingFoam; break;
                    case "scalarsimplefoam": solverType = CfdSolverType.ScalarSimpleFoam; break;
                    case "rhosimplefoam": solverType = CfdSolverType.RhoSimpleFoam; break;
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
