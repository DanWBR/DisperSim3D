#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DisperSim3D.Core;
using DisperSim3D.Extensibility;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.CLI
{
    /// <summary>
    /// Loads the automation extender the way the desktop UIs do, starts it against
    /// a scripted host with no window, and drives every endpoint over HTTP. When
    /// DISPERSIM3D_AUTOMATION_TEST_IFX names an in:Flux project and the importer is
    /// installed, it also runs the importer through the server with its dialog
    /// answered.
    /// </summary>
    internal static class AutomationSelfTest
    {
        private const string CollectionId = "DisperSim3D.Automation";

        public static int Run(string? extenderDir, TextWriter output)
        {
            return RunAsync(extenderDir, output).GetAwaiter().GetResult();
        }

        private static async Task<int> RunAsync(string? extenderDir, TextWriter output)
        {
            int passed = 0, failed = 0;
            void Check(bool ok, string what, string? detail = null)
            {
                if (ok) passed++; else failed++;
                output.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what + (ok || detail == null ? "" : "  — " + detail));
            }

            if (!string.IsNullOrWhiteSpace(extenderDir))
            {
                string existing = Environment.GetEnvironmentVariable("DISPERSIM3D_EXTENDERS") ?? "";
                Environment.SetEnvironmentVariable("DISPERSIM3D_EXTENDERS",
                    Path.GetFullPath(extenderDir) + (existing.Length > 0 ? Path.PathSeparator + existing : ""));
            }

            int port = FreePort();
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            Environment.SetEnvironmentVariable("DISPERSIM3D_AUTOMATION", "1");
            Environment.SetEnvironmentVariable("DISPERSIM3D_AUTOMATION_PORT", port.ToString());
            Environment.SetEnvironmentVariable("DISPERSIM3D_AUTOMATION_TOKEN", token);

            var loaded = ExtenderLoader.LoadAll();
            var automation = loaded.Collections.FirstOrDefault(c => c.ID == CollectionId);
            Check(automation is IExtenderStartup, "automation extender loads and starts with the host",
                  string.Join("; ", loaded.Messages));
            if (automation is not IExtenderStartup startup) return 1;

            string work = Path.Combine(Path.GetTempPath(), "dispersim3d-automation-selftest-" + Environment.ProcessId);
            Directory.CreateDirectory(work);
            var host = new ScriptedHost();
            try
            {
                startup.OnHostStarted(host);
                Check(host.StatusText.Contains("listening on http://127.0.0.1:" + port), "server starts from DISPERSIM3D_AUTOMATION", host.StatusText);

                using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port + "/"), Timeout = TimeSpan.FromMinutes(5) };
                http.DefaultRequestHeaders.Add("X-Automation-Token", token);

                // ── Callers ──────────────────────────────────────────────
                using (var bare = new HttpClient { BaseAddress = http.BaseAddress })
                {
                    var r = await bare.GetAsync("status");
                    Check(r.StatusCode == HttpStatusCode.Unauthorized, "a request without the token is refused", ((int)r.StatusCode).ToString());
                }
                var withOrigin = new HttpRequestMessage(HttpMethod.Get, "status");
                withOrigin.Headers.Add("Origin", "http://example.com");
                Check((await http.SendAsync(withOrigin)).StatusCode == HttpStatusCode.Forbidden, "a request from a web page (Origin) is refused");
                var rebound = new HttpRequestMessage(HttpMethod.Get, "status");
                rebound.Headers.Host = "attacker.example:" + port;
                Check((await http.SendAsync(rebound)).StatusCode == HttpStatusCode.Forbidden, "a request for another host name is refused");

                // ── Discovery ────────────────────────────────────────────
                JsonNode? index = await GetJson(http, "");
                Check(index?["endpoints"] is JsonArray { Count: > 5 }, "GET / lists the endpoints");
                Check((await http.GetAsync("nothing-here")).StatusCode == HttpStatusCode.NotFound, "an unknown path is 404");
                Check((await http.DeleteAsync("status")).StatusCode == HttpStatusCode.MethodNotAllowed, "a wrong method is 405");

                JsonNode? status = await GetJson(http, "status");
                Check(status?["host"]?.GetValue<string>() == "SelfTest", "GET /status names the host", status?.ToJsonString());

                JsonNode? commands = await GetJson(http, "commands");
                var ids = (commands?["commands"] as JsonArray ?? new JsonArray()).Select(c => c?["id"]?.GetValue<string>()).ToList();
                Check(ids.Contains(CollectionId + ".Start") && ids.Contains(CollectionId + ".Stop"), "GET /commands lists the extender commands",
                      string.Join(", ", ids));

                // ── Project ──────────────────────────────────────────────
                string saved = Path.Combine(work, "selftest.dsproj");
                JsonNode? save = await PostJson(http, "project/save", new { path = saved });
                Check(save?["ok"]?.GetValue<bool>() == true && File.Exists(saved), "POST /project/save writes the project", save?.ToJsonString());

                host.Scene = new Scene3D { Name = "Something else" };
                JsonNode? open = await PostJson(http, "project/open", new { path = saved });
                Check(open?["ok"]?.GetValue<bool>() == true && open?["projectName"]?.GetValue<string>() == "Automation self-test",
                      "POST /project/open loads it back", open?.ToJsonString());

                JsonNode? scene = await GetJson(http, "scene");
                Check(scene?["name"]?.GetValue<string>() == "Automation self-test" &&
                      scene?["counts"]?["gasDetectors"]?.GetValue<int>() == 1 &&
                      scene?["gasDetectors"]?[0]?["position"]?[2]?.GetValue<double>() == 1.5,
                      "GET /scene describes the project", scene?.ToJsonString());

                // ── Simulations ──────────────────────────────────────────
                var badSolver = await Post(http, "simulations", new { source = "S-1", solver = "NoSuchSolver" });
                Check(badSolver.StatusCode == HttpStatusCode.BadRequest, "an unknown solver is 400");
                var noSource = await Post(http, "simulations", new { source = "Nowhere", solver = "GaussianPlume" });
                Check(noSource.StatusCode == HttpStatusCode.NotFound, "an unknown source is 404");
                JsonNode? added = await PostJson(http, "simulations",
                    new { source = "s-1", solver = "gaussianplume", name = "Plume", durationS = 120, gridResolution = 30 });
                string? simId = added?["simulation"]?["id"]?.GetValue<string>();
                Check(added?["ok"]?.GetValue<bool>() == true && simId != null &&
                      added?["simulation"]?["solver"]?.GetValue<string>() == "GaussianPlume" &&
                      added?["simulation"]?["windField"]?.GetValue<string>() == "W-1" &&
                      added?["simulation"]?["gridResolution"]?.GetValue<int>() == 30 &&
                      host.Scene.Simulations.Count == 1 && host.Refreshes == 1,
                      "POST /simulations adds one, by source name, with the first wind field", added?.ToJsonString());
                JsonNode? sims = await GetJson(http, "simulations");
                Check(sims?["simulations"]?[0]?["source"]?.GetValue<string>() == "S-1" && sims?["simulations"]?[0]?["run"] == null,
                      "GET /simulations lists it, not yet run", sims?.ToJsonString());
                var runMissing = await Post(http, "simulations/run", new { id = "nope" });
                Check(runMissing.StatusCode == HttpStatusCode.Conflict, "running an unknown simulation is refused");
                JsonNode? ran = await PostJson(http, "simulations/run", new { id = simId });
                Check(ran?["ok"]?.GetValue<bool>() == true && ran?["run"]?["state"]?.GetValue<string>() == "Queued" &&
                      host.RunRequests.Contains(simId!), "POST /simulations/run queues it in the host", ran?.ToJsonString());
                JsonNode? runStatus = await GetJson(http, "simulations/status?id=" + simId);
                Check(runStatus?["run"]?["state"]?.GetValue<string>() == "Queued", "GET /simulations/status reports the run");
                JsonNode? cancelledRun = await PostJson(http, "simulations/cancel", new { id = simId });
                Check(cancelledRun?["run"]?["state"]?.GetValue<string>() == "Cancelled", "POST /simulations/cancel cancels it", cancelledRun?.ToJsonString());
                var cancelAgain = await Post(http, "simulations/cancel", new { id = simId });
                Check(cancelAgain.StatusCode == HttpStatusCode.Conflict, "cancelling a finished run is refused");

                var missing = await Post(http, "project/open", new { path = Path.Combine(work, "missing.dsproj") });
                Check(missing.StatusCode == HttpStatusCode.NotFound, "opening a missing file is 404");
                var relative = await Post(http, "project/open", new { path = "relative.dsproj" });
                Check(relative.StatusCode == HttpStatusCode.BadRequest, "a relative path is refused");
                var badJson = await http.PostAsync("project/open", new StringContent("{not json", Encoding.UTF8, "application/json"));
                Check(badJson.StatusCode == HttpStatusCode.BadRequest, "malformed JSON is 400");

                // ── Viewport ─────────────────────────────────────────────
                var png = await http.GetAsync("viewport.png?width=64&height=48");
                byte[] pngBytes = await png.Content.ReadAsByteArrayAsync();
                Check(png.Content.Headers.ContentType?.MediaType == "image/png" && PngWriter.IsPng(pngBytes) && PngSize(pngBytes) == (64, 48),
                      "GET /viewport.png returns the view at the requested size");
                string shot = Path.Combine(work, "shots", "view.png");
                JsonNode? saveShot = await PostJson(http, "viewport/save", new { path = shot, width = 32, height = 24 });
                Check(saveShot?["ok"]?.GetValue<bool>() == true && PngWriter.IsPng(File.ReadAllBytes(shot)), "POST /viewport/save writes a PNG");
                host.ViewAvailable = false;
                Check((await http.GetAsync("viewport.png")).StatusCode == HttpStatusCode.ServiceUnavailable, "no view is 503");
                host.ViewAvailable = true;

                // ── Commands ─────────────────────────────────────────────
                var own = await Post(http, "commands/run", new { id = CollectionId + ".Stop" });
                Check(own.StatusCode == HttpStatusCode.BadRequest, "the server's own commands cannot be run remotely");
                var unknown = await Post(http, "commands/run", new { id = "No.Such.Command" });
                Check(unknown.StatusCode == HttpStatusCode.NotFound, "an unknown command is 404");

                string? ifx = Environment.GetEnvironmentVariable("DISPERSIM3D_AUTOMATION_TEST_IFX");
                const string importId = "DisperSim3D.InFlux.Import";
                if (!ids.Contains(importId))
                {
                    output.WriteLine("  skip  in:Flux import: the importer is not installed");
                }
                else
                {
                    JsonNode? cancelled = await PostJson(http, "commands/run", new { id = importId, answers = Array.Empty<string>() });
                    Check(cancelled?["ok"]?.GetValue<bool>() == true && cancelled?["dialogs"]?[0]?["answer"] == null,
                          "a command whose dialog gets no answer sees it cancelled", cancelled?.ToJsonString());

                    if (string.IsNullOrWhiteSpace(ifx) || !File.Exists(ifx))
                    {
                        output.WriteLine("  skip  in:Flux import: set DISPERSIM3D_AUTOMATION_TEST_IFX to an .ifx file");
                    }
                    else
                    {
                        JsonNode? import = await PostJson(http, "commands/run", new { id = importId, answers = new[] { ifx } });
                        string? path = import?["projectPath"]?.GetValue<string>();
                        Check(import?["ok"]?.GetValue<bool>() == true && path != null && path.EndsWith(".dsproj") && File.Exists(path) &&
                              import?["dialogs"]?[0]?["answer"]?.GetValue<string>() == ifx,
                              "the in:Flux importer runs with its dialog answered", import?.ToJsonString());
                        JsonNode? imported = await GetJson(http, "scene");
                        output.WriteLine("        imported: " + imported?["name"] + ", " + imported?["counts"]?.ToJsonString(new JsonSerializerOptions()));
                    }
                }

                JsonNode? log = await GetJson(http, "log");
                Check(log?["entries"] is JsonArray { Count: > 0 }, "GET /log reports what the server did");

                // ── Stop ─────────────────────────────────────────────────
                await automation.Collection.First(c => c.ID == CollectionId + ".Stop").RunAsync(host);
                bool refused;
                try
                {
                    await http.GetAsync("status");
                    refused = false;
                }
                catch (HttpRequestException)
                {
                    refused = true;
                }
                Check(refused && host.StatusText.Contains("stopped"), "the Stop command closes the port", host.StatusText);
            }
            catch (Exception ex)
            {
                Check(false, "self-test ran to the end", ex.ToString());
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch { /* best effort */ }
            }

            Check(host.UnexpectedCalls.Count == 0, "the server never opened a real dialog", string.Join("; ", host.UnexpectedCalls));
            output.WriteLine();
            output.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static async Task<JsonNode?> GetJson(HttpClient http, string path)
        {
            var r = await http.GetAsync(path);
            return JsonNode.Parse(await r.Content.ReadAsStringAsync());
        }

        private static Task<HttpResponseMessage> Post(HttpClient http, string path, object body) =>
            http.PostAsync(path, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

        private static async Task<JsonNode?> PostJson(HttpClient http, string path, object body)
        {
            var r = await Post(http, path, body);
            return JsonNode.Parse(await r.Content.ReadAsStringAsync());
        }

        private static (int, int) PngSize(byte[] png) =>
            (png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19], png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23]);

        /// <summary>
        /// A host with no window: the project lives in memory, saving and opening go
        /// through the engine as the UIs do, and the "view" is a generated gradient.
        /// Its dialogs must never be reached — the server answers them itself.
        /// </summary>
        private sealed class ScriptedHost : IExtenderHost3
        {
            private readonly SemaphoreSlim _ui = new(1, 1);

            public ScriptedHost()
            {
                Scene = new Scene3D { Name = "Automation self-test" };
                Scene.GasDetectors.Add(new GasDetector3D { Name = "GD-1", Position = new Point3D(10, 20, 1.5) });
                Scene.TopLevelSources.Add(new ReleaseSource3D { Name = "S-1", Position = new Point3D(0, 0, 0) });
                Scene.WindFieldScenarios.Add(new WindFieldScenario { Name = "W-1" });
            }

            public Scene3D Scene { get; set; }

            public bool ViewAvailable { get; set; } = true;

            public List<string> UnexpectedCalls { get; } = new();

            public int Refreshes { get; private set; }

            public List<string> RunRequests { get; } = new();

            private readonly Dictionary<string, ExtenderRunStatus> _runs = new();

            public void RefreshProject() => Refreshes++;

            /// <summary>Records the request as a queued run, as a host's simulation manager would.</summary>
            public Task<string?> RunSimulationAsync(string simulationId)
            {
                if (!Scene.Simulations.Exists(s => s.Id == simulationId))
                    return Task.FromResult<string?>("No simulation " + simulationId + " in the project.");
                RunRequests.Add(simulationId);
                _runs[simulationId] = new ExtenderRunStatus("Queued", 0, "Queued", null);
                return Task.FromResult<string?>(null);
            }

            public ExtenderRunStatus? GetSimulationRunStatus(string simulationId) =>
                _runs.TryGetValue(simulationId, out var run) ? run : null;

            public Task<string?> CancelSimulationAsync(string simulationId)
            {
                if (!_runs.TryGetValue(simulationId, out var run)) return Task.FromResult<string?>("Not run.");
                if (run.State != "Queued" && run.State != "Running") return Task.FromResult<string?>("Already " + run.State + ".");
                _runs[simulationId] = new ExtenderRunStatus("Cancelled", run.Progress, "Cancelled", null);
                return Task.FromResult<string?>(null);
            }

            public string HostName => "SelfTest";

            public object? MainWindow => null;

            public Scene3D? CurrentScene => Scene;

            public string? CurrentProjectPath { get; private set; }

            public string StatusText { get; private set; } = "";

            public Task<string?> PickOpenFileAsync(string title, IReadOnlyList<ExtenderFileFilter> filters)
            {
                UnexpectedCalls.Add("open dialog " + title);
                return Task.FromResult<string?>(null);
            }

            public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, IReadOnlyList<ExtenderFileFilter> filters)
            {
                UnexpectedCalls.Add("save dialog " + title);
                return Task.FromResult<string?>(null);
            }

            public Task ShowMessageAsync(string title, string message, bool isError = false)
            {
                UnexpectedCalls.Add("message " + title + ": " + message);
                return Task.CompletedTask;
            }

            public async Task OpenProjectAsync(string path)
            {
                Scene = await Task.Run(() => SceneFileLoader.Load(path));
                CurrentProjectPath = path;
                StatusText = "Opened " + Path.GetFileName(path);
            }

            public async Task SaveProjectAsync(string path)
            {
                Scene3D scene = Scene;
                await Task.Run(() => SceneFileSaver.Save(scene, path,
                    bundleWriter: (p, sc, doc, prog) => ProjectBundle.Save(p, sc, doc, prog)));
                CurrentProjectPath = path;
                StatusText = "Saved " + Path.GetFileName(path);
            }

            public void SetStatus(string text) => StatusText = text;

            public Task<byte[]?> CaptureViewportPngAsync(int width, int height)
            {
                if (!ViewAvailable) return Task.FromResult<byte[]?>(null);
                if (width <= 0 || height <= 0) { width = 80; height = 60; }
                var rgba = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int i = (y * width + x) * 4;
                        rgba[i] = (byte)(255 * x / width);
                        rgba[i + 1] = (byte)(255 * y / height);
                        rgba[i + 2] = 128;
                        rgba[i + 3] = 255;
                    }
                return Task.FromResult<byte[]?>(PngWriter.EncodeRgba(rgba, width, height));
            }

            /// <summary>One action at a time, as on a UI thread.</summary>
            public async Task RunOnUiThreadAsync(Func<Task> action)
            {
                await _ui.WaitAsync();
                try
                {
                    await action();
                }
                finally
                {
                    _ui.Release();
                }
            }
        }
    }
}
