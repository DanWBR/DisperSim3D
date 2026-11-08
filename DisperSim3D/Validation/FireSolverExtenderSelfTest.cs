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
    /// The fire side of the solver-extender contract end to end, with a stand-in fire
    /// solver registered in code that writes uniform, time-dependent fields to every
    /// result channel and a heat load per decoration triangle: the capability flag, the
    /// fire request for a fire source and for an ignition, the channel registry and the
    /// surface file, monitors, detectors, the fire study and a fire bench reading the
    /// stored fields, the model fields' project round trip, and the species unit of an
    /// extender's result.
    ///
    /// Run via <c>DisperSim3D.CLI --fire-solver-selftest</c>.
    /// </summary>
    public static class FireSolverExtenderSelfTest
    {
        /// <summary>
        /// Writes, at t = 1 and 2 s, uniform fields: T = 300 + 10t K, Q = 5t kW/m²,
        /// Soot = 1e-4·t kg/m³, CO = 1e-3, CO2 = 0.01, O2 = 0.2095 − 0.01t, Vis = 30 − 10t m,
        /// and on triangle i an incident flux of i·t and a total of i·t + 2 kW/m².
        /// Returns no grid, only its folder, on the grid and centre the request asks for.
        /// </summary>
        private sealed class StandInFireSolver : ISolverExtender
        {
            public string Id => "SelfTestFireSolver";
            public string Code => "TSTFIR";
            public string DisplayName => "Self-test fire solver";
            public IReadOnlyList<string> Aliases { get; } = new[] { "selftest-fire" };
            public bool IsSteady => false;
            public bool ReadsWindField => false;
            public SolverCapabilities Capabilities => SolverCapabilities.Fire;
            public SolverExtenderRequest LastRequest;

            public SolverExtenderResult Run(SolverExtenderRequest request)
            {
                LastRequest = request;
                if (request.Fire == null) return SolverExtenderResult.Fail("not a fire request");
                int n = request.Scenario.GridResolution, nz = Math.Max(8, n / 2);
                string dir = Path.Combine(Path.GetTempPath(), "dsim_firesolver_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                int triangles = DecorationSolids.FromDecorations(request.Scene.Decorations).Triangles.TriangleCount;
                foreach (double t in new[] { 1.0, 2.0 })
                {
                    void Write(ResultChannel c, double v)
                    {
                        var f = new double[n, n, nz];
                        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) for (int k = 0; k < nz; k++) f[i, j, k] = v;
                        OpenFoamResult.SaveBinaryField(Path.Combine(dir, c.FileName(t)), f);
                    }
                    Write(ResultChannels.Temperature, 300 + 10 * t);
                    Write(ResultChannels.Radiation, 5 * t);
                    Write(ResultChannels.Soot, 1e-4 * t);
                    Write(ResultChannels.CarbonMonoxide, 1e-3);
                    Write(ResultChannels.CarbonDioxide, 0.01);
                    Write(ResultChannels.Oxygen, 0.2095 - 0.01 * t);
                    Write(ResultChannels.Visibility, 30 - 10 * t);
                    var incident = Enumerable.Range(0, triangles).Select(i => i * t).ToArray();
                    SurfaceHeatLoad.Write(Path.Combine(dir, SurfaceHeatLoad.FileName(t)), incident,
                        incident.Select(q => q + 2).ToArray());
                }
                request.Progress?.Invoke(1, "stand-in fire solver done");
                return new SolverExtenderResult { Success = true, CasePath = dir };
            }
        }

        public static bool RunAndPrint(TextWriter w)
        {
            var solver = new StandInFireSolver();
            SolverExtenderRegistry.Register(solver);
            int passed = 0, total = 0;
            void Check(string name, bool ok, string detail = "")
            {
                total++; if (ok) passed++;
                w.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (string.IsNullOrEmpty(detail) ? "" : "   " + detail));
            }
            var inv = CultureInfo.InvariantCulture;
            var cleanup = new List<string>();
            string tmp = Path.Combine(Path.GetTempPath(), "dsim_firetest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            cleanup.Add(tmp);

            try
            {
                Check("a fire solver is listed by its capability",
                    SolverExtenderRegistry.WithCapability(SolverCapabilities.Fire).Contains(solver)
                    && !SolverExtenderRegistry.WithCapability(SolverCapabilities.Dispersion).Contains(solver));

                var scene = BuildScene(tmp, out var fire);
                int triangles = DecorationSolids.FromDecorations(scene.Decorations).Triangles.TriangleCount;

                // ── Fire source run ──
                var sim = FireRuns.CreateFor(scene, fire, solver);
                scene.Simulations.Add(sim);
                var run = HeadlessRunner.RunSimulation(scene, sim);
                Check("headless fire run succeeds", run.Success, run.Error ?? "");
                if (!run.Success) return Finish(w, passed, total, cleanup);
                cleanup.Add(run.CasePath);
                Check("the request names the fire source and centres the grid on it",
                    solver.LastRequest.Fire?.Source == fire && solver.LastRequest.Fire.Ignition == null
                    && solver.LastRequest.DomainCenterX == fire.Position.X && solver.LastRequest.DomainCenterY == fire.Position.Y
                    && solver.LastRequest.Scenario.Sources.Count == 0);

                sim.Status = SimulationStatus.Completed;
                sim.CasePath = run.CasePath;
                FireRuns.ApplyResultGrid(sim, run.ResultGrid);
                Check("the grid read from the channels keeps the requested centre",
                    sim.SnapshotDomainCenterX == fire.Position.X && sim.SnapshotGridResolution == 20,
                    string.Format(inv, "centre ({0}, {1}), n = {2}", sim.SnapshotDomainCenterX, sim.SnapshotDomainCenterY, sim.SnapshotGridResolution));

                foreach (var (channel, expectedPeak) in new (ResultChannel, double)[]
                {
                    (ResultChannels.Temperature, 320), (ResultChannels.Radiation, 10), (ResultChannels.Soot, 2e-4),
                    (ResultChannels.CarbonMonoxide, 1e-3), (ResultChannels.CarbonDioxide, 0.01),
                    (ResultChannels.Oxygen, 0.1895), (ResultChannels.Visibility, 10)
                })
                {
                    var stored = StoredFields.Load(sim, channel);
                    var peak = StoredFields.Collapse(stored, channel, ViewTimeMode.PeakOverTime);
                    double v = peak?[3, 4, 2] ?? double.NaN;
                    Check("channel " + (channel.Suffix) + " reads back, peak over time " + (channel.LowIsHazard ? "lowest" : "highest"),
                        stored?.TimeSteps.Count == 2 && Math.Abs(v - expectedPeak) < 1e-9,
                        string.Format(inv, "{0:G6} {1}", v, channel.Unit));
                }
                Check("the species channel ignores the suffixed files", !StoredFields.Has(sim, ResultChannels.Species));

                var load = SurfaceHeatLoad.Of(sim);
                var incident = load?.Collapse(false, ViewTimeMode.PeakOverTime);
                var totalFlux = load?.Collapse(true, ViewTimeMode.FinalSnapshot);
                Check("surface heat load has one pair per decoration triangle",
                    load != null && triangles == 2 && incident.Length == triangles
                    && incident[1] == 2 && totalFlux[1] == 4,
                    load == null ? "no surface file" : string.Format(inv, "{0} triangles, {1} values", triangles, incident.Length));

                // ── Sensors read the stored fields ──
                var detectors = DetectorEvaluator.EvaluateSimulation(scene, sim, scene.GasDetectors);
                var radDet = scene.GasDetectors[0];
                var o2Det = scene.GasDetectors[1];
                Check("a radiation detector reads the stored Q, not the analytic models",
                    radDet.TimeSeries.Count == 2 && Math.Abs(radDet.TimeSeries[1].Concentration - 10) < 1e-9
                    && radDet.Detected && radDet.DetectionTimeS == 2,
                    radDet.TimeSeries.Count > 0 ? string.Format(inv, "{0:G4} kW/m² at t = 2, analytic {1:G4}",
                        radDet.TimeSeries.Last().Concentration,
                        FieldTransform.RadiationAtPoint(scene, radDet.Position.X, radDet.Position.Y, radDet.Position.Z)) : "no samples");
                Check("an oxygen detector triggers on a low reading",
                    o2Det.Detected && o2Det.DetectionTimeS == 2 && detectors.DetectorsTriggered == 2);

                MonitorEvaluator.EvaluateSimulation(scene, sim, scene.MonitorPoints);
                var tMon = scene.MonitorPoints[0];
                var sootMon = scene.MonitorPoints[1];
                Check("a temperature monitor reads the stored T over time",
                    tMon.TimeSeries.Count == 2 && Math.Abs(tMon.TimeSeries[0].Concentration - 310) < 1e-9
                    && Math.Abs(tMon.TimeSeries[1].Concentration - 320) < 1e-9);
                Check("a soot monitor reads the stored soot",
                    sootMon.TimeSeries.Count == 2 && Math.Abs(sootMon.TimeSeries[1].Concentration - 2e-4) < 1e-12);

                // ── Dose integrated over the stored times ──
                // A fire that dies out: 10 kW/m² at t = 0, nothing from t = 10 s on. A person
                // there from t = 0 for the 20 s escape time takes the triangle under the
                // dose rate, 5 s at 10 kW/m², not 20 s at the peak.
                string dying = Path.Combine(tmp, "dying");
                Directory.CreateDirectory(dying);
                foreach (var (t, q) in new[] { (0.0, 10.0), (10.0, 0.0) })
                {
                    var f = new double[20, 20, 10];
                    for (int i = 0; i < 20; i++) for (int j = 0; j < 20; j++) for (int k = 0; k < 10; k++) f[i, j, k] = q;
                    OpenFoamResult.SaveBinaryField(Path.Combine(dying, ResultChannels.Radiation.FileName(t)), f);
                }
                var dyingSim = new Simulation
                {
                    Name = "dying", SolverType = CfdSolverType.Extender, Status = SimulationStatus.Completed,
                    CasePath = dying, SnapshotDomainSizeM = 30, SnapshotGridResolution = 20
                };
                var doseField = FieldTransform.BuildAnalyticField(scene, ViewFieldProperty.ThermalDose, 20, 20, 10, 30, dyingSim);
                Check("the stored dose integrates the radiation over the exposure window",
                    Math.Abs(doseField[5, 5, 5] - ThermalDose.Dose(10, 5)) < 1e-6 * ThermalDose.Dose(10, 5),
                    string.Format(inv, "{0:E4} (q·t at the peak would be {1:E4})", doseField[5, 5, 5], ThermalDose.Dose(10, 20)));
                var doseSeries = StoredFields.Measure(scene, dyingSim, ViewFieldProperty.ThermalDose, 0, 0, 2);
                Check("a dose monitor reads the window from each stored time",
                    doseSeries.Count == 2 && Math.Abs(doseSeries[0].Concentration - ThermalDose.Dose(10, 5)) < 1e-6 * ThermalDose.Dose(10, 5)
                    && doseSeries[1].Concentration == 0);
                Check("one stored time is held for the whole window, as a steady fire",
                    Math.Abs(ThermalDose.WorstWindowDose(new[] { 3.0 }, new[] { 10.0 }, 20) - ThermalDose.Dose(10, 20)) < 1e-6 * ThermalDose.Dose(10, 20));

                // ── Fire study ──
                var study = new FireStudy { HarmQuantity = ViewFieldProperty.ThermalRadiationKwM2, HarmThreshold = 7.5, DomainHalfM = 30, GridResolution = 20 };
                study.FireSourceIds.Add(fire.Id);
                var studyResult = FireStudyEngine.Evaluate(scene, study);
                var row = studyResult.Rows.FirstOrDefault();
                double cell = 2.0 * sim.SnapshotDomainSizeM / 20;
                double expectedVolume = 20 * 20 * 10 * cell * cell * cell;
                Check("the fire study scores the stored radiation of the fire's simulation",
                    row != null && row.Note.Contains("stored") && Math.Abs(row.ConsequenceM3 - expectedVolume) < 1e-6 * expectedVolume,
                    row == null ? "no row" : string.Format(inv, "{0:F0} m³ (expected {1:F0}); {2}", row.ConsequenceM3, expectedVolume, row.Note));

                // ── Fire bench routed to the extender ──
                string bench = Path.Combine(tmp, "b.fbench");
                File.WriteAllText(bench, BenchJson);
                var benchResult = FireBenchmarkRunner.Run(FireBenchmarkRunner.Load(bench));
                var flux = benchResult.Checks.FirstOrDefault();
                Check("a .fbench naming a fire solver samples its stored radiation",
                    flux != null && Math.Abs(flux.Predicted - 10) < 1e-9 && benchResult.ChecksPassed,
                    flux == null ? string.Join("; ", benchResult.Notes) : string.Format(inv, "predicted {0:G4} kW/m²", flux.Predicted));
                foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), "dsim_firesolver_*"))
                    if (!cleanup.Contains(dir)) cleanup.Add(dir);

                // ── Ignition run ──
                var dispersion = new Simulation
                {
                    Name = "cloud", SolverType = CfdSolverType.Extender, ExtenderSolverId = "none",
                    Status = SimulationStatus.Completed, CasePath = tmp, SnapshotSource = scene.TopLevelSources[0],
                    SnapshotDomainSizeM = 40, SnapshotDomainCenterX = 5, SnapshotGridResolution = 20
                };
                scene.Simulations.Add(dispersion);
                var ignition = new IgnitionEvent { Name = "ign", SimulationId = dispersion.Id, Position = new Point3D(5, 0, 1), TimeS = 10 };
                scene.Ignitions.Add(ignition);
                var ignSim = FireRuns.CreateFor(scene, ignition, solver);
                scene.Simulations.Add(ignSim);
                var ignRun = HeadlessRunner.RunSimulation(scene, ignSim);
                if (ignRun.CasePath != null) cleanup.Add(ignRun.CasePath);
                Check("an ignition run gets the ignition, its dispersion simulation, the gas and its grid",
                    ignRun.Success && solver.LastRequest.Fire?.Ignition == ignition
                    && solver.LastRequest.Fire.IgnitedSimulation == dispersion
                    && solver.LastRequest.Fire.Gas?.EffectiveHeatOfCombustionJPerKg == 46.4e6
                    && solver.LastRequest.DomainCenterX == 5 && solver.LastRequest.Scenario.DomainSizeM == 40,
                    ignRun.Error ?? "");
                dispersion.Status = SimulationStatus.Running;
                var refused = HeadlessRunner.RunSimulation(scene, ignSim);
                Check("an ignition of an unfinished dispersion run is refused", !refused.Success && refused.Error.Contains("first"), refused.Error ?? "");

                // ── Project round trip of the new model fields ──
                string project = Path.Combine(tmp, "p.xml");
                SceneFileSaver.Save(scene, project);
                var loaded = SceneFileLoader.Load(project);
                var lf = loaded.FireScenario.Sources.FirstOrDefault(f => f.Id == fire.Id);
                var lsim = loaded.Simulations.FirstOrDefault(s => s.Id == sim.Id);
                var lign = loaded.Simulations.FirstOrDefault(s => s.Id == ignSim.Id);
                var lgas = loaded.GasLibrary.FirstOrDefault(g => g.Kind == GasLibraryItemKind.Pure)?.PureGas;
                var lmix = loaded.GasLibrary.FirstOrDefault(g => g.Kind == GasLibraryItemKind.Mixture);
                Check("project keeps a fireball's kind and mass", lf?.Kind == FireKind.Fireball && lf.FireballMassKg == 2500);
                Check("project keeps a fire simulation's fire source and ignition",
                    lsim?.FireSourceId == fire.Id && lign?.IgnitionId == ignition.Id && lsim.IsFireRun);
                Check("project keeps a gas's combustion fields",
                    lgas != null && lgas.HeatOfCombustionJPerKg == 46.4e6 && lgas.CarbonAtoms == 3 && lgas.HydrogenAtoms == 8
                    && lgas.SootYield == 0.024 && lgas.CoYield == 0.005 && lgas.LaminarBurningVelocityMPerS == 0.46);
                Check("project keeps what monitors and detectors measure",
                    loaded.GasDetectors[1].MeasuredQuantity == ViewFieldProperty.O2VolumeFraction && loaded.GasDetectors[1].Threshold == 0.195
                    && loaded.MonitorPoints[1].MeasuredQuantity == ViewFieldProperty.SootDensityKgM3
                    && loaded.GasDetectors[0].Id == scene.GasDetectors[0].Id);
                Check("project keeps a mixture component's combustion fields",
                    lmix?.Mixture.Components[1].SootYield == 0.03);
                string old = File.ReadAllText(project)
                    .Replace(" Kind=\"Fireball\"", "").Replace(" FireballMass=\"2500\"", "").Replace("IsPool=\"false\"", "IsPool=\"true\"")
                    .Replace(" HeatComb=\"46400000\"", "").Replace(" SootYield=\"0.024\"", "");
                string oldPath = Path.Combine(tmp, "old.xml");
                File.WriteAllText(oldPath, old);
                var oldScene = SceneFileLoader.Load(oldPath);
                var oldGas = oldScene.GasLibrary.First(g => g.Kind == GasLibraryItemKind.Pure).PureGas;
                Check("a file without the new fields loads: IsPool, and gas data from the name",
                    oldScene.FireScenario.Sources[0].Kind == FireKind.Pool
                    && oldGas.HeatOfCombustionJPerKg == 0 && oldGas.EffectiveHeatOfCombustionJPerKg == 46.0e6
                    && oldGas.EffectiveSootYield == 0.024);

                // ── Mixture rule and species units ──
                var mix = new GasLibraryItem { Name = "mix", Kind = GasLibraryItemKind.Mixture, Mixture = new GasMixture() };
                mix.Mixture.Components.Add(new GasComponent { Name = "Methane", MolarMass = 0.016, MoleFraction = 0.5 });
                mix.Mixture.Components.Add(new GasComponent { Name = "Propane", MolarMass = 0.044, MoleFraction = 0.5 });
                var mg = mix.AsGasProperties();
                double heat = (0.016 * 50.1e6 + 0.044 * 46.0e6) / 0.060;
                Check("a mixture's heat of combustion is mass-weighted and its atoms mole-weighted",
                    Math.Abs(mg.HeatOfCombustionJPerKg - heat) < 1 && mg.CarbonAtoms == 2 && mg.HydrogenAtoms == 6
                    && Math.Abs(mg.SootYield - 0.044 * 0.024 / 0.060) < 1e-12,
                    string.Format(inv, "{0:G6} J/kg, C{1} H{2}", mg.HeatOfCombustionJPerKg, mg.CarbonAtoms, mg.HydrogenAtoms));
                Check("an extender's species is kg/m³ and becomes a mass fraction over the air density",
                    FieldTransform.StoresSpeciesAsKgPerM3(sim) && Math.Abs(FieldTransform.SpeciesAsMassFraction(1.205, sim) - 1) < 1e-12
                    && !FieldTransform.StoresSpeciesAsKgPerM3(new Simulation { SolverType = CfdSolverType.FluidX3DDispersion }));
            }
            catch (Exception ex)
            {
                Check("no exception", false, ex.GetType().Name + ": " + ex.Message);
            }
            return Finish(w, passed, total, cleanup);
        }

        private static bool Finish(TextWriter w, int passed, int total, List<string> cleanup)
        {
            foreach (var dir in cleanup)
                try { if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            w.WriteLine();
            w.WriteLine(passed == total
                ? $"OK — {passed}/{total} fire solver extender checks passed."
                : $"FAIL — {passed}/{total} passed.");
            return passed == total;
        }

        /// <summary>A fireball, a two-triangle wall, two detectors, two monitors, a propane release and a mixture.</summary>
        private static Scene3D BuildScene(string dir, out FireSource fire)
        {
            var scene = new Scene3D();
            string stl = Path.Combine(dir, "wall.stl");
            File.WriteAllText(stl, string.Join("\n",
                "solid wall",
                "facet normal 1 0 0", "outer loop", "vertex 0 0 0", "vertex 0 10 0", "vertex 0 10 10", "endloop", "endfacet",
                "facet normal 1 0 0", "outer loop", "vertex 0 0 0", "vertex 0 10 10", "vertex 0 0 10", "endloop", "endfacet",
                "endsolid wall"));
            scene.Decorations.Add(new Decoration3D { Name = "wall", FilePath = stl, Position = new Point3D(20, 0, 0), Scale = 1 });

            var meteo = new MeteorologicalConditions { WindSpeed = 2, WindDirectionDeg = 270, AmbientTemperature = 293.15, AmbientPressure = 101325 };
            scene.WindFieldScenarios.Add(new WindFieldScenario { Name = "wf", Meteo = meteo });
            scene.GeneralSettings.DefaultDomainSizeM = 30;
            scene.GeneralSettings.DefaultGridResolution = 20;

            fire = new FireSource { Name = "ball", Kind = FireKind.Fireball, FireballMassKg = 2500, Position = new Point3D(4, -2, 0) };
            scene.FireScenario.Sources.Add(fire);

            var propane = new GasProperties
            {
                Name = "Propane", MolarMass = 0.044, HeatOfCombustionJPerKg = 46.4e6, CarbonAtoms = 3, HydrogenAtoms = 8,
                SootYield = 0.024, CoYield = 0.005, LaminarBurningVelocityMPerS = 0.46
            };
            var lib = GasLibraryItem.FromGasProperties(propane);
            scene.GasLibrary.Add(lib);
            var mixture = GasLibraryItem.FromMixture("Mix", new GasMixture());
            mixture.Mixture.Components.Add(new GasComponent { Name = "Methane", MolarMass = 0.016, MoleFraction = 0.9 });
            mixture.Mixture.Components.Add(new GasComponent { Name = "Heavy", MolarMass = 0.1, MoleFraction = 0.1, SootYield = 0.03 });
            scene.GasLibrary.Add(mixture);
            scene.TopLevelSources.Add(new ReleaseSource3D { Name = "release", Position = new Point3D(5, 0, 1), GasRefId = lib.Id, Gas = propane });

            scene.GasDetectors.Add(new GasDetector3D { Name = "rad", Position = new Point3D(10, 0, 2), MeasuredQuantity = ViewFieldProperty.ThermalRadiationKwM2, Threshold = 7.5 });
            scene.GasDetectors.Add(new GasDetector3D { Name = "o2", Position = new Point3D(8, 3, 2), MeasuredQuantity = ViewFieldProperty.O2VolumeFraction, Threshold = 0.195 });
            scene.MonitorPoints.Add(new MonitorPoint3D { Name = "T", Position = new Point3D(6, 1, 3), MeasuredQuantity = ViewFieldProperty.Temperature });
            scene.MonitorPoints.Add(new MonitorPoint3D { Name = "soot", Position = new Point3D(6, 1, 3), MeasuredQuantity = ViewFieldProperty.SootDensityKgM3 });
            return scene;
        }

        private const string BenchJson = @"{
  ""$schema"": ""fbench/v1"",
  ""name"": ""fire-solver-selftest"",
  ""dataConfidence"": ""High"",
  ""fire"": { ""kind"": ""jet"", ""fuel"": ""methane"", ""massFlowRateKgS"": 1, ""position"": [0, 0, 1] },
  ""ambient"": { ""windSpeed"": 2 },
  ""radiometers"": [ { ""name"": ""r1"", ""position"": [10, 0, 1], ""measuredKwM2"": 8 } ],
  ""solver"": ""selftest-fire"",
  ""domain"": { ""sizeM"": 30, ""gridResolution"": 20, ""durationS"": 2 }
}";
    }
}
