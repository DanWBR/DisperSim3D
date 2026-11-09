using System.Text.Json.Nodes;
using DisperSim3D.Core;
using DisperSim3D.Extensibility;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Automation
{
    /// <summary>
    /// Explosions: the events, explosion simulations on a solver extender that declares
    /// <see cref="SolverCapabilities.Explosion"/>, and the pressure they recorded at the probes.
    /// </summary>
    internal sealed partial class AutomationServer
    {
        private async Task<Response> AddExplosionAsync(Request r)
        {
            var host = Host3;
            JsonObject body = ReadJson(r);
            string simKey = RequiredString(body, "simulation");
            double[] p = RequiredPoint(body, "position");
            var cloud = OptionalEnum<ExplosionCloud>(body, "cloud") ?? ExplosionCloud.Dispersed;
            double[]? boxMin = body["boxMin"] != null ? RequiredPoint(body, "boxMin") : null;
            double[]? boxMax = body["boxMax"] != null ? RequiredPoint(body, "boxMax") : null;
            if (cloud == ExplosionCloud.EquivalentBox && (boxMin == null || boxMax == null))
                throw new HttpError(400, "An equivalent-box cloud needs \"boxMin\" and \"boxMax\".");
            var congestion = new List<CongestionRegion>();
            if (body["congestion"] is JsonArray regions)
                foreach (var node in regions)
                {
                    if (node is not JsonObject o) throw new HttpError(400, "Each congestion region is an object.");
                    double[] min = RequiredPoint(o, "min"), max = RequiredPoint(o, "max");
                    congestion.Add(new CongestionRegion
                    {
                        Name = OptionalString(o, "name") ?? "Congestion " + (congestion.Count + 1),
                        Min = new Point3D(min[0], min[1], min[2]),
                        Max = new Point3D(max[0], max[1], max[2]),
                        PipeDiameterM = OptionalDouble(o, "pipeDiameterM") ?? 0.15,
                        AreaPerVolume = OptionalDouble(o, "areaPerVolume") ?? 1.0,
                    });
                }

            object? created = null;
            string? error = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene == null) { error = "No project is open."; return Task.CompletedTask; }
                var sim = FindSimulation(scene, simKey);
                if (sim == null) { error = "No simulation " + simKey + " in the project."; return Task.CompletedTask; }
                scene.Explosions ??= new List<ExplosionEvent>();
                var explosion = new ExplosionEvent
                {
                    Name = OptionalString(body, "name") ?? "Explosion " + (scene.Explosions.Count + 1),
                    SimulationId = sim.Id,
                    Cloud = cloud,
                    Position = new Point3D(p[0], p[1], p[2]),
                    TimeS = OptionalDouble(body, "timeS") ?? 0,
                    BoxMin = boxMin == null ? default : new Point3D(boxMin[0], boxMin[1], boxMin[2]),
                    BoxMax = boxMax == null ? default : new Point3D(boxMax[0], boxMax[1], boxMax[2]),
                    CellSizeM = OptionalDouble(body, "cellSizeM") ?? 0,
                    Congestion = congestion,
                };
                scene.Explosions.Add(explosion);
                host.RefreshProject();
                created = DescribeExplosion(explosion);
                return Task.CompletedTask;
            });
            if (error != null) throw new HttpError(error.StartsWith("No project") ? 409 : 404, error);
            _log.Add("added explosion on " + simKey);
            return JsonResponse(200, new { ok = true, explosion = created });
        }

        private async Task<object> ExplosionsAsync()
        {
            object result = Array.Empty<object>();
            await _host.RunOnUiThreadAsync(() =>
            {
                var scene = _host.CurrentScene;
                if (scene?.Explosions != null) result = scene.Explosions.Select(DescribeExplosion).ToList();
                return Task.CompletedTask;
            });
            return result;
        }

        private static object DescribeExplosion(ExplosionEvent x) => new
        {
            x.Id,
            x.Name,
            simulationId = x.SimulationId,
            cloud = x.Cloud.ToString(),
            position = new[] { x.Position.X, x.Position.Y, x.Position.Z },
            x.TimeS,
            boxMin = new[] { x.BoxMin.X, x.BoxMin.Y, x.BoxMin.Z },
            boxMax = new[] { x.BoxMax.X, x.BoxMax.Y, x.BoxMax.Z },
            x.CellSizeM,
            congestion = x.Congestion.Select(c => new
            {
                c.Name,
                min = new[] { c.Min.X, c.Min.Y, c.Min.Z },
                max = new[] { c.Max.X, c.Max.Y, c.Max.Z },
                c.PipeDiameterM,
                c.AreaPerVolume,
            })
        };

        /// <summary>An explosion simulation for an explosion, on an explosion solver extender.</summary>
        private async Task<Response> AddExplosionSimulationAsync(JsonObject body)
        {
            var host = Host3;
            string solverText = RequiredString(body, "solver");
            var solver = SolverExtenderRegistry.FindByName(solverText);
            if (solver == null || (solver.Capabilities & SolverCapabilities.Explosion) == 0)
                throw new HttpError(400, "No explosion solver " + solverText + "; GET /solvers lists those with explosion = true.");
            string key = RequiredString(body, "explosion");

            object? created = null;
            string? error = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene == null) { error = "No project is open."; return Task.CompletedTask; }
                var explosion = scene.Explosions?.FirstOrDefault(x => x.Id == key)
                                ?? scene.Explosions?.FirstOrDefault(x => string.Equals(x.Name, key, StringComparison.OrdinalIgnoreCase));
                if (explosion == null) { error = "No explosion " + key + " in the project."; return Task.CompletedTask; }
                var sim = ExplosionRuns.CreateFor(scene, explosion, solver);
                if (OptionalString(body, "name") is { } name) sim.Name = name;
                if (OptionalDouble(body, "durationS") is { } d) sim.SnapshotDurationS = d;
                if (OptionalDouble(body, "snapshotCount") is { } count) sim.SnapshotCount = (int)count;
                error = ApplyOptions(sim, solver, body["options"] as JsonObject);
                if (error != null) return Task.CompletedTask;
                scene.Simulations.Add(sim);
                host.RefreshProject();
                created = DescribeSimulation(scene, sim, null);
                return Task.CompletedTask;
            });
            if (error != null) throw new HttpError(error.StartsWith("No project") ? 409 : error.StartsWith("No ") ? 404 : 400, error);
            _log.Add("added explosion simulation on " + solver.Id + " for " + key);
            return JsonResponse(200, new { ok = true, simulation = created });
        }

        /// <summary>The overpressure an explosion simulation recorded at its probes: per probe the
        /// peak, its time and the positive impulse, and the series thinned to at most
        /// <c>samples</c> points (default 500; 0 leaves them out).</summary>
        private async Task<Response> SimulationProbesAsync(Request r)
        {
            string key = r.Query.TryGetValue("id", out string? id) && !string.IsNullOrEmpty(id)
                ? id : throw new HttpError(400, "Give the simulation as ?id=.");
            int samples = r.Query.TryGetValue("samples", out string? n) && int.TryParse(n, out int k) && k >= 0 ? k : 500;
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
            var series = ProbeSeries.Of(sim);
            if (series == null) throw new HttpError(404, "Simulation " + sim.Name + " recorded no probes.");

            int stride = samples > 0 ? Math.Max(1, (series.Times.Length + samples - 1) / samples) : int.MaxValue;
            var picks = samples > 0 ? Enumerable.Range(0, series.Times.Length).Where(i => i % stride == 0 || i == series.Times.Length - 1).ToList() : new List<int>();
            var probes = Enumerable.Range(0, series.Names.Count).Select(q =>
            {
                var (peak, impulse, at) = series.Summary(q);
                var pos = series.Positions[q];
                return new
                {
                    name = series.Names[q],
                    position = new[] { pos.X, pos.Y, pos.Z },
                    peakPa = peak,
                    peakTimeS = at,
                    impulsePaS = impulse,
                    pressurePa = picks.Select(i => series.Values[q][i]).ToArray(),
                };
            }).ToList();
            return JsonResponse(200, new
            {
                id = sim.Id,
                sim.Name,
                samples = series.Times.Length,
                timesS = picks.Select(i => series.Times[i]).ToArray(),
                probes,
            });
        }

        /// <summary>The blast load an explosion simulation stored on the plant's triangles, null without one.</summary>
        private static object? DescribeBlastLoad(Scene3D scene, Simulation sim)
        {
            var blast = SurfaceBlastLoad.Of(sim);
            if (blast == null) return null;
            int triangles = DecorationSolids.FromDecorations(scene.Decorations).Triangles.TriangleCount;
            var peak = blast.Collapse(false, ViewTimeMode.PeakOverTime);
            var impulse = blast.Collapse(true, ViewTimeMode.PeakOverTime);
            return new
            {
                times = blast.Paths.Keys,
                values = peak.Length,
                sceneTriangles = triangles,
                matchesScene = peak.Length == triangles,
                peakOverpressurePa = peak.Length > 0 ? peak.Max() : 0,
                peakImpulsePaS = impulse.Length > 0 ? impulse.Max() : 0,
            };
        }
    }
}
