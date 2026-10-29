using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DisperSim3D.Core;
using DisperSim3D.Extensibility;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// The solver-extender contract end to end, with a stand-in solver registered in
    /// code: lookup by ID and alias, the project file round trip, a headless run of a
    /// simulation, the CLI-style run by name, and a benchmark sampled through the
    /// solver's point evaluator.
    ///
    /// Run via <c>DisperSim3D.CLI --solver-extender-selftest</c>.
    /// </summary>
    public static class SolverExtenderSelfTest
    {
        /// <summary>Uniform field on the grid; the point evaluator returns 1e-3·(1 + x/100) kg/m³.</summary>
        private sealed class StandInSolver : ISolverExtender
        {
            public string Id => "SelfTestSolver";
            public string Code => "TSTSLV";
            public string DisplayName => "Self-test solver";
            public IReadOnlyList<string> Aliases { get; } = new[] { "selftest-solver" };
            public bool IsSteady => true;
            public SolverExtenderRequest LastRequest;

            public SolverExtenderResult Run(SolverExtenderRequest request)
            {
                LastRequest = request;
                request.Progress?.Invoke(0.5, "stand-in solver running");
                var sc = request.Scenario;
                int n = Math.Max(2, sc.GridResolution), nz = Math.Max(1, n / 2);
                var field = new double[n, n, nz];
                for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) for (int k = 0; k < nz; k++) field[i, j, k] = 1e-3;
                var result = new OpenFoamResult
                {
                    GridNx = n, GridNy = n, GridNz = nz, DomainSizeM = sc.DomainSizeM,
                    DomainXMin = -sc.DomainSizeM, DomainXMax = sc.DomainSizeM,
                    DomainYMin = -sc.DomainSizeM, DomainYMax = sc.DomainSizeM, DomainZMax = sc.DomainSizeM,
                    IsLoaded = true, IsSteadyState = true
                };
                result.TimeSteps.Add(0);
                result.PreloadField(0, field);
                return new SolverExtenderResult
                {
                    Success = true,
                    Result = result,
                    Concentration = (x, y, z) => 1e-3 * (1 + x / 100),
                    MassFraction = (x, y, z) => 2e-3 * (1 + x / 100)
                };
            }
        }

        public static bool RunAndPrint(TextWriter w)
        {
            var solver = new StandInSolver();
            SolverExtenderRegistry.Register(solver);
            int passed = 0, total = 0;
            void Check(string name, bool ok, string detail = "")
            {
                total++; if (ok) passed++;
                w.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (string.IsNullOrEmpty(detail) ? "" : "   " + detail));
            }

            Check("registry finds the solver by ID", SolverExtenderRegistry.Find("selftestsolver") == solver);
            Check("registry finds the solver by alias", SolverExtenderRegistry.FindByName("SELFTEST-SOLVER") == solver);
            Check("registry finds the solver by code", SolverExtenderRegistry.FindByName("tstslv") == solver);
            Check("solver code resolves to the extender's",
                SolverCode.Of(CfdSolverType.Extender, solver.Id) == "TSTSLV"
                && SolverCode.DisplayName(CfdSolverType.Extender, solver.Id) == solver.DisplayName);

            var scene = BuildScene(solver.Id);
            string tmp = Path.Combine(Path.GetTempPath(), "dsim_solverext_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string project = Path.Combine(tmp, "p.xml");
                SceneFileSaver.Save(scene, project);
                var loaded = SceneFileLoader.Load(project);
                var sim = loaded.Simulations.FirstOrDefault();
                Check("project file keeps the solver type and ID",
                    sim != null && sim.SolverType == CfdSolverType.Extender && sim.ExtenderSolverId == solver.Id,
                    sim == null ? "no simulation" : sim.SolverType + " / " + sim.ExtenderSolverId);

                var log = new List<string>();
                var res = HeadlessRunner.RunSimulation(scene, scene.Simulations[0], log.Add);
                Check("headless simulation run succeeds", res.Success, res.Error ?? "");
                Check("run receives the scenario, its source and the obstacle boxes",
                    solver.LastRequest != null && solver.LastRequest.Scenario.Sources.Count == 1
                    && solver.LastRequest.Obstacles.Count == 1);
                Check("result carries the grid snapshot", res.ConcentrationField != null
                    && Math.Abs(res.MaxConcentration - 1e-3) < 1e-12,
                    string.Format(CultureInfo.InvariantCulture, "max {0:G4}", res.MaxConcentration));
                Check("monitors read the point evaluator", res.MonitorData.Count == 1
                    && Math.Abs(res.MonitorData[0].Concentrations[0] - 1.5e-3) < 1e-12);
                Check("progress reaches the log", log.Any(l => l.Contains("stand-in solver running")));

                var byName = HeadlessRunner.RunFromFile(project, "selftest-solver");
                Check("CLI-style run by alias succeeds", byName.Success, byName.Error ?? "");

                string bench = Path.Combine(tmp, "b.dsbench");
                File.WriteAllText(bench, BenchJson);
                var spec = BenchmarkLoader.Load(bench);
                Check("benchmark solver name resolves to the extender",
                    spec.ResolveSolverType() == CfdSolverType.Extender && spec.ResolveExtenderSolverId() == solver.Id);
                var report = ValidationRunner.Run(spec);
                double p0 = report.Spm?.Pairs.Count > 0 ? report.Spm.Pairs[0].Predicted : double.NaN;
                Check("benchmark sensors are sampled through the point evaluator, in the bench unit",
                    report.Success && Math.Abs(p0 - 2e-3 * 1.5) < 1e-12,
                    report.Success ? string.Format(CultureInfo.InvariantCulture, "sensor 0 = {0:G6}", p0) : report.ErrorMessage);
            }
            catch (Exception ex)
            {
                Check("no exception", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }

            w.WriteLine();
            w.WriteLine(passed == total
                ? $"OK — {passed}/{total} solver extender checks passed."
                : $"FAIL — {passed}/{total} passed.");
            return passed == total;
        }

        private static Scene3D BuildScene(string solverId)
        {
            var scene = new Scene3D();
            var gas = GasProperties.CreateMethane();
            var src = new ReleaseSource3D { Name = "src", Position = new Point3D(0, 0, 1), ReleaseRateKgPerS = 1, Gas = gas };
            scene.TopLevelSources.Add(src);
            var meteo = new MeteorologicalConditions { WindSpeed = 5, WindDirectionDeg = 270, AmbientTemperature = 293.15, AmbientPressure = 101325 };
            var wf = new WindFieldScenario { Name = "wf", Meteo = meteo };
            scene.WindFieldScenarios.Add(wf);
            scene.Decorations.Add(new Decoration3D { Name = "box", BoundingBox = new BoundingBox(new Point3D(10, -5, 0), new Point3D(20, 5, 10)) });
            scene.MonitorPoints.Add(new MonitorPoint3D { Name = "m", Position = new Point3D(50, 0, 1) });
            // The CLI -s path runs the project's dispersion scenario.
            var scenario = new DispersionScenario { Name = "scenario", Meteo = meteo, DomainSizeM = 100, GridResolution = 20 };
            scenario.Sources.Add(src);
            scene.DispersionScenarios.Add(scenario);
            scene.Simulations.Add(new Simulation
            {
                Name = "sim", SourceId = src.Id, WindFieldId = wf.Id,
                SolverType = CfdSolverType.Extender, ExtenderSolverId = solverId,
                SnapshotSource = src, SnapshotMeteo = meteo,
                SnapshotDomainSizeM = 100, SnapshotGridResolution = 20, SnapshotDurationS = 60, SnapshotTimeStepS = 1
            });
            return scene;
        }

        private const string BenchJson = @"{
  ""$schema"": ""dsbench/v1"",
  ""name"": ""solver-extender-selftest"",
  ""dataConfidence"": ""High"",
  ""source"": { ""name"": ""s"", ""gas"": { ""name"": ""tracer"", ""molarMass"": 0.029 },
              ""position"": [0, 0, 1], ""releaseRateKgPerS"": 1, ""releaseDurationS"": 600,
              ""exitTemperatureK"": 293.15, ""exitVelocityMPerS"": 0 },
  ""meteo"": { ""windSpeed"": 5, ""windDirectionDeg"": 270, ""stability"": ""D"",
             ""ambientTemperature"": 293.15, ""ambientPressure"": 101325 },
  ""domain"": { ""sizeM"": 100, ""gridResolution"": 20, ""durationS"": 60, ""timeStepS"": 1 },
  ""solver"": ""selftest-solver"",
  ""concentrationKind"": ""FinalSnapshot"",
  ""unit"": ""MassFraction"",
  ""sensors"": [ { ""name"": ""s1"", ""position"": [50, 0, 1], ""measuredKgM3"": 0.003 } ]
}";
    }
}
