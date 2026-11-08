using System.Text.Json.Nodes;
using DisperSim3D.Core;
using DisperSim3D.Extensibility;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Automation
{
    /// <summary>
    /// Solvers and their capabilities, fire sources and ignitions, fire simulations on a
    /// fire solver extender, monitors and detectors, and what a simulation stored.
    /// </summary>
    internal sealed partial class AutomationServer
    {
        // ── Solvers ───────────────────────────────────────────────────────

        private static object Solvers() => new
        {
            builtIn = Enum.GetNames<CfdSolverType>().Where(n => n != nameof(CfdSolverType.Extender)),
            extenders = SolverExtenderRegistry.All.Select(s => new
            {
                s.Id,
                s.Code,
                s.DisplayName,
                s.Aliases,
                s.IsSteady,
                capabilities = s.Capabilities.ToString(),
                dispersion = (s.Capabilities & SolverCapabilities.Dispersion) != 0,
                fire = (s.Capabilities & SolverCapabilities.Fire) != 0,
                options = SolverOptionValues.Of(s).Select(o => new
                {
                    o.Key,
                    o.Label,
                    o.Description,
                    kind = o.Kind.ToString(),
                    defaultValue = o.DefaultValue,
                    o.Minimum,
                    o.Maximum,
                    o.Choices,
                    o.Unit
                })
            })
        };

        // ── Fire sources and ignitions ────────────────────────────────────

        private async Task<Response> AddFireSourceAsync(Request r)
        {
            var host = Host3;
            JsonObject body = ReadJson(r);
            double[] p = RequiredPoint(body, "position");
            var fire = new FireSource
            {
                Name = OptionalString(body, "name") ?? "Fire",
                Position = new Point3D(p[0], p[1], p[2]),
                Kind = OptionalEnum<FireKind>(body, "kind") ?? FireKind.Jet,
                MassFlowRateKgS = OptionalDouble(body, "massFlowKgS") ?? 1.0,
                OrificeDiameterM = OptionalDouble(body, "orificeDiameterM") ?? 0.025,
                PoolDiameterM = OptionalDouble(body, "poolDiameterM") ?? 5.0,
                PoolBurnRateKgM2S = OptionalDouble(body, "burnRateKgM2S") ?? 0.05,
                FireballMassKg = OptionalDouble(body, "fireballMassKg") ?? 1000.0,
                HeatOfCombustionJKg = OptionalDouble(body, "heatOfCombustionJPerKg") ?? 50e6,
                RadiativeFraction = OptionalDouble(body, "radiativeFraction") ?? 0.2
            };
            if (body["direction"] != null)
            {
                double[] d = RequiredPoint(body, "direction");
                fire.Direction = new Vector3D(d[0], d[1], d[2]);
            }
            object? created = null;
            string? error = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene == null) { error = "No project is open."; return Task.CompletedTask; }
                scene.FireScenario ??= new FireScenario();
                scene.FireScenario.Sources.Add(fire);
                host.RefreshProject();
                created = DescribeFire(fire);
                return Task.CompletedTask;
            });
            if (error != null) throw new HttpError(409, error);
            _log.Add("added fire source " + fire.Name + " (" + fire.Kind + ")");
            return JsonResponse(200, new { ok = true, fireSource = created });
        }

        private static object DescribeFire(FireSource f) => new
        {
            f.Id,
            f.Name,
            kind = f.Kind.ToString(),
            position = new[] { f.Position.X, f.Position.Y, f.Position.Z },
            massFlowKgS = f.MassFlowRateKgS,
            poolDiameterM = f.PoolDiameterM,
            fireballMassKg = f.FireballMassKg
        };

        private async Task<Response> AddIgnitionAsync(Request r)
        {
            var host = Host3;
            JsonObject body = ReadJson(r);
            string simKey = RequiredString(body, "simulation");
            double[] p = RequiredPoint(body, "position");
            object? created = null;
            string? error = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene == null) { error = "No project is open."; return Task.CompletedTask; }
                var sim = FindSimulation(scene, simKey);
                if (sim == null) { error = "No simulation " + simKey + " in the project."; return Task.CompletedTask; }
                scene.Ignitions ??= new List<IgnitionEvent>();
                var ignition = new IgnitionEvent
                {
                    Name = OptionalString(body, "name") ?? "Ignition " + (scene.Ignitions.Count + 1),
                    SimulationId = sim.Id,
                    Position = new Point3D(p[0], p[1], p[2]),
                    TimeS = OptionalDouble(body, "timeS") ?? 0,
                    FlameSpeedMS = OptionalDouble(body, "flameSpeedMS") ?? 10.0
                };
                scene.Ignitions.Add(ignition);
                host.RefreshProject();
                created = new { ignition.Id, ignition.Name, simulationId = sim.Id, position = p, ignition.TimeS };
                return Task.CompletedTask;
            });
            if (error != null) throw new HttpError(error.StartsWith("No project") ? 409 : 404, error);
            _log.Add("added ignition on " + simKey);
            return JsonResponse(200, new { ok = true, ignition = created });
        }

        /// <summary>A fire simulation for a fire source or an ignition, on a fire solver extender.</summary>
        private async Task<Response> AddFireSimulationAsync(JsonObject body)
        {
            var host = Host3;
            string solverText = RequiredString(body, "solver");
            var solver = SolverExtenderRegistry.FindByName(solverText);
            if (solver == null || (solver.Capabilities & SolverCapabilities.Fire) == 0)
                throw new HttpError(400, "No fire solver " + solverText + "; GET /solvers lists those with fire = true.");
            string? fireKey = OptionalString(body, "fireSource");
            string? ignitionKey = OptionalString(body, "ignition");

            object? created = null;
            string? error = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene == null) { error = "No project is open."; return Task.CompletedTask; }
                Simulation sim;
                if (fireKey != null)
                {
                    var fire = scene.FireScenario?.Sources.FirstOrDefault(f => f.Id == fireKey)
                               ?? scene.FireScenario?.Sources.FirstOrDefault(f => string.Equals(f.Name, fireKey, StringComparison.OrdinalIgnoreCase));
                    if (fire == null) { error = "No fire source " + fireKey + " in the project."; return Task.CompletedTask; }
                    sim = FireRuns.CreateFor(scene, fire, solver);
                }
                else
                {
                    var ignition = scene.Ignitions?.FirstOrDefault(g => g.Id == ignitionKey)
                                   ?? scene.Ignitions?.FirstOrDefault(g => string.Equals(g.Name, ignitionKey, StringComparison.OrdinalIgnoreCase));
                    if (ignition == null) { error = "No ignition " + ignitionKey + " in the project."; return Task.CompletedTask; }
                    sim = FireRuns.CreateFor(scene, ignition, solver);
                }
                if (OptionalString(body, "name") is { } name) sim.Name = name;
                if (OptionalDouble(body, "durationS") is { } d) sim.SnapshotDurationS = d;
                if (OptionalDouble(body, "domainSizeM") is { } size) sim.SnapshotDomainSizeM = size;
                if (OptionalDouble(body, "gridResolution") is { } grid) sim.SnapshotGridResolution = (int)grid;
                if (OptionalDouble(body, "snapshotCount") is { } count) sim.SnapshotCount = (int)count;
                error = ApplyOptions(sim, solver, body["options"] as JsonObject);
                if (error != null) return Task.CompletedTask;
                scene.Simulations.Add(sim);
                host.RefreshProject();
                created = DescribeSimulation(scene, sim, null);
                return Task.CompletedTask;
            });
            if (error != null) throw new HttpError(error.StartsWith("No project") ? 409 : error.StartsWith("No ") ? 404 : 400, error);
            _log.Add("added fire simulation on " + solver.Id + " for " + (fireKey ?? ignitionKey));
            return JsonResponse(200, new { ok = true, simulation = created });
        }

        // ── Monitors and detectors ────────────────────────────────────────

        private async Task<Response> AddMonitorAsync(Request r)
        {
            var host = Host3;
            JsonObject body = ReadJson(r);
            double[] p = RequiredPoint(body, "position");
            var monitor = new MonitorPoint3D
            {
                Name = OptionalString(body, "name") ?? "Monitor",
                Position = new Point3D(p[0], p[1], p[2]),
                MeasuredQuantity = OptionalEnum<ViewFieldProperty>(body, "quantity") ?? ViewFieldProperty.ConcentrationKgM3
            };
            object? created = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene == null) return Task.CompletedTask;
                scene.MonitorPoints.Add(monitor);
                host.RefreshProject();
                created = new { monitor.Id, monitor.Name, position = p, quantity = monitor.MeasuredQuantity.ToString() };
                return Task.CompletedTask;
            });
            if (created == null) throw new HttpError(409, "No project is open.");
            _log.Add("added monitor " + monitor.Name);
            return JsonResponse(200, new { ok = true, monitor = created });
        }

        private async Task<Response> AddDetectorAsync(Request r)
        {
            var host = Host3;
            JsonObject body = ReadJson(r);
            double[] p = RequiredPoint(body, "position");
            var detector = new GasDetector3D
            {
                Name = OptionalString(body, "name") ?? "Detector",
                Position = new Point3D(p[0], p[1], p[2]),
                MeasuredQuantity = OptionalEnum<ViewFieldProperty>(body, "quantity") ?? ViewFieldProperty.ConcentrationKgM3
            };
            if (OptionalDouble(body, "threshold") is { } threshold)
            {
                detector.Threshold = threshold;
                detector.ThresholdKgM3 = threshold;
            }
            object? created = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene == null) return Task.CompletedTask;
                scene.GasDetectors.Add(detector);
                host.RefreshProject();
                created = new { detector.Id, detector.Name, position = p, quantity = detector.MeasuredQuantity.ToString(), detector.Threshold };
                return Task.CompletedTask;
            });
            if (created == null) throw new HttpError(409, "No project is open.");
            _log.Add("added detector " + detector.Name);
            return JsonResponse(200, new { ok = true, detector = created });
        }

        /// <summary>
        /// Replays a finished simulation through every monitor and detector from the
        /// fields it stored, as the simulation's Evaluate Monitors and Detectors command does.
        /// </summary>
        private async Task<Response> EvaluateSensorsAsync(Request r)
        {
            string key = RequiredString(ReadJson(r), "id");
            object? report = null;
            string? error = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene == null) { error = "No project is open."; return Task.CompletedTask; }
                var sim = FindSimulation(scene, key);
                if (sim == null) { error = "No simulation " + key + " in the project."; return Task.CompletedTask; }
                if (sim.Status != SimulationStatus.Completed) { error = "Simulation " + sim.Name + " has not completed."; return Task.CompletedTask; }
                var summary = DetectorEvaluator.EvaluateSimulation(scene, sim, scene.GasDetectors);
                MonitorEvaluator.EvaluateSimulation(scene, sim, scene.MonitorPoints);
                static object Series(List<MonitorSample> s) => s.Select(x => new[] { x.TimeS, x.Concentration });
                report = new
                {
                    simulation = sim.Name,
                    detectorsTriggered = summary.DetectorsTriggered,
                    detectors = scene.GasDetectors.Select(d => new
                    {
                        d.Id,
                        d.Name,
                        quantity = d.MeasuredQuantity.ToString(),
                        unit = FieldTransform.UnitFor(d.MeasuredQuantity),
                        d.Detected,
                        detectionTimeS = d.Detected ? d.DetectionTimeS : (double?)null,
                        samples = Series(d.TimeSeries)
                    }),
                    monitors = scene.MonitorPoints.Select(m => new
                    {
                        m.Id,
                        m.Name,
                        quantity = m.MeasuredQuantity.ToString(),
                        unit = FieldTransform.UnitFor(m.MeasuredQuantity),
                        samples = Series(m.TimeSeries)
                    })
                };
                Host3.RefreshProject();
                return Task.CompletedTask;
            });
            if (error != null) throw new HttpError(error.StartsWith("No project") ? 409 : error.StartsWith("No simulation") ? 404 : 409, error);
            _log.Add("evaluated monitors and detectors on " + key);
            return JsonResponse(200, report!);
        }

        // ── What a simulation stored ──────────────────────────────────────

        /// <summary>
        /// The result channels a simulation stored, with their times and range, and its
        /// surface heat load against the scene's decoration triangles.
        /// </summary>
        private async Task<Response> SimulationChannelsAsync(Request r)
        {
            if (!r.Query.TryGetValue("id", out string? key) || string.IsNullOrWhiteSpace(key))
                throw new HttpError(400, "Give the simulation as ?id=.");
            Simulation? sim = null;
            Scene3D? scene = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                scene = _host.CurrentScene;
                if (scene != null) sim = FindSimulation(scene, key);
                return Task.CompletedTask;
            });
            if (scene == null) throw new HttpError(409, "No project is open.");
            if (sim == null) throw new HttpError(404, "No simulation " + key + " in the project.");

            // Read off the UI thread: the files can be large.
            var channels = new List<object>();
            foreach (var channel in ResultChannels.All)
            {
                var result = StoredFields.Load(sim, channel);
                if (result == null) continue;
                var peak = StoredFields.Collapse(result, channel, ViewTimeMode.PeakOverTime);
                double min = double.MaxValue, max = double.MinValue;
                if (peak != null) foreach (double v in peak) { min = Math.Min(min, v); max = Math.Max(max, v); }
                channels.Add(new
                {
                    suffix = channel.Suffix,
                    name = channel.Name,
                    unit = channel.Unit,
                    times = result.TimeSteps,
                    grid = new[] { result.GridNx, result.GridNy, result.GridNz },
                    peakMin = peak == null ? (double?)null : min,
                    peakMax = peak == null ? (double?)null : max
                });
            }

            object? surface = null;
            var load = SurfaceHeatLoad.Of(sim);
            if (load != null)
            {
                int triangles = DecorationSolids.FromDecorations(scene.Decorations).Triangles.TriangleCount;
                var incident = load.Collapse(false, ViewTimeMode.PeakOverTime);
                var total = load.Collapse(true, ViewTimeMode.PeakOverTime);
                surface = new
                {
                    times = load.Times,
                    values = incident.Length,
                    sceneTriangles = triangles,
                    matchesScene = incident.Length == triangles,
                    peakIncidentKwM2 = incident.Length > 0 ? incident.Max() : 0,
                    peakTotalKwM2 = total.Length > 0 ? total.Max() : 0
                };
            }
            return JsonResponse(200, new
            {
                id = sim.Id,
                sim.Name,
                sim.CasePath,
                domainSizeM = sim.SnapshotDomainSizeM,
                domainCenter = new[] { sim.SnapshotDomainCenterX, sim.SnapshotDomainCenterY },
                gridResolution = sim.SnapshotGridResolution,
                speciesUnit = FieldTransform.StoresSpeciesAsKgPerM3(sim) ? "kg/m³" : "mass fraction",
                channels,
                surface
            });
        }

        /// <summary>Sets the options in <paramref name="options"/> (a value of null clears one); returns the first error.</summary>
        private static string? ApplyOptions(Simulation sim, ISolverExtender? solver, JsonObject? options)
        {
            if (options == null) return null;
            foreach (var (key, node) in options)
            {
                string? text = node == null ? null
                    : node.GetValueKind() == System.Text.Json.JsonValueKind.String ? node.GetValue<string>()
                    : node.ToJsonString();
                string? e = SolverOptionValues.Set(sim, solver, key, text);
                if (e != null) return e;
            }
            return null;
        }

        /// <summary>Sets options of a simulation's extender solver.</summary>
        private async Task<Response> SetSimulationOptionsAsync(Request r)
        {
            var host = Host3;
            JsonObject body = ReadJson(r);
            string key = RequiredString(body, "id");
            var options = body["options"] as JsonObject ?? throw new HttpError(400, "Give the values as \"options\": {\"key\": value}.");
            object? described = null;
            string? error = null;
            int status = 400;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene == null) { error = "No project is open."; status = 409; return Task.CompletedTask; }
                var sim = FindSimulation(scene, key);
                if (sim == null) { error = "No simulation " + key + " in the project."; status = 404; return Task.CompletedTask; }
                var solver = SolverExtenderRegistry.Find(sim.ExtenderSolverId);
                if (solver == null) { error = "Simulation " + sim.Name + " does not run on an extender solver."; return Task.CompletedTask; }
                // All or none: the values are checked on a copy first.
                var trial = new Simulation { ExtenderOptions = new List<SolverOptionValue>(sim.ExtenderOptions ?? new List<SolverOptionValue>()) };
                error = ApplyOptions(trial, solver, options);
                if (error != null) return Task.CompletedTask;
                sim.ExtenderOptions = trial.ExtenderOptions;
                host.RefreshProject();
                described = DescribeSimulation(scene, sim, host.GetSimulationRunStatus(sim.Id));
                return Task.CompletedTask;
            });
            if (error != null) throw new HttpError(status, error);
            _log.Add("set solver options of simulation " + key);
            return JsonResponse(200, new { ok = true, simulation = described });
        }

        private static Simulation? FindSimulation(Scene3D scene, string key) =>
            scene.Simulations.FirstOrDefault(s => s.Id == key)
            ?? scene.Simulations.FirstOrDefault(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));
    }
}
