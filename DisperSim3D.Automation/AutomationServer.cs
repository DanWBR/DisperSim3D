using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DisperSim3D.Extensibility;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Automation
{
    /// <summary>
    /// A small HTTP/1.1 server on the loopback interface, written on a raw socket so
    /// it needs no URL reservation or administrator rights. One request per
    /// connection; JSON in, JSON (or a PNG) out. See <see cref="Routes"/>.
    /// </summary>
    internal sealed class AutomationServer : IDisposable
    {
        private const int MaxHeaderBytes = 64 * 1024;
        private const int MaxBodyBytes = 1024 * 1024;

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        /// <summary>The endpoints, for GET / and the README.</summary>
        internal static readonly string[] Routes =
        {
            "GET  /status                      host, version, open project, status bar",
            "GET  /scene                       the open project: name, objects, sources",
            "GET  /commands                    the extender menu commands",
            "POST /commands/run                {\"id\": \"...\", \"answers\": [\"C:\\\\file.ifx\"]}",
            "POST /project/open                {\"path\": \"C:\\\\project.dsproj\"}",
            "POST /project/save                {\"path\": \"C:\\\\project.dsproj\"} (path optional)",
            "GET  /viewport.png?width=&height= the 3D view as a PNG",
            "POST /viewport/save               {\"path\": \"C:\\\\view.png\", \"width\": 0, \"height\": 0}",
            "GET  /simulations                 the project's simulations and their runs",
            "POST /simulations                 {\"source\": \"Release 01\", \"solver\": \"GaussianPlume\" (or an extender solver's ID or code), \"windField\": \"...\", \"durationS\": 300, ...}",
            "POST /simulations/run             {\"id\": \"...\"} queues it as its Run command does",
            "GET  /simulations/status?id=      where its latest run stands",
            "POST /simulations/cancel          {\"id\": \"...\"} cancels its latest run",
            "POST /simulations/show            {\"id\": \"...\", \"timeS\": 30} shows its result in the 3D view (timeS optional: last snapshot)",
            "GET  /log?after=N                 what the server did"
        };

        private readonly IExtenderHost2 _host;
        private readonly string? _token;
        private readonly AutomationLog _log;
        private readonly string _ownCollectionId;
        private readonly SemaphoreSlim _uiGate = new(1, 1);
        private readonly CancellationTokenSource _cts = new();
        private TcpListener? _listener;

        public AutomationServer(IExtenderHost2 host, int port, string? token, AutomationLog log, string ownCollectionId)
        {
            _host = host;
            Port = port;
            _token = string.IsNullOrEmpty(token) ? null : token;
            _log = log;
            _ownCollectionId = ownCollectionId;
        }

        public int Port { get; private set; }

        public string Url => "http://127.0.0.1:" + Port + "/";

        public bool RequiresToken => _token != null;

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _log.Add("listening on " + Url + (RequiresToken ? " (token required)" : ""));
            _ = AcceptLoopAsync(_cts.Token);
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener?.Stop(); } catch { /* already stopped */ }
            _log.Add("stopped");
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener!.AcceptTcpClientAsync(ct);
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _log.Add("accept failed: " + ex.Message);
                    continue;
                }
                _ = HandleConnectionAsync(client, ct);
            }
        }

        // ── HTTP ──────────────────────────────────────────────────────────

        private sealed class Request
        {
            public string Method = "";
            public string Path = "";
            public Dictionary<string, string> Query = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
            public byte[] Body = Array.Empty<byte>();
        }

        private sealed record Response(int Status, string ContentType, byte[] Body);

        private sealed class HttpError : Exception
        {
            public HttpError(int status, string message) : base(message) { Status = status; }
            public int Status { get; }
        }

        private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                Response response;
                Request? request = null;
                try
                {
                    using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        readTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                        request = await ReadRequestAsync(stream, readTimeout.Token);
                    }
                    CheckCaller(request);
                    response = await DispatchAsync(request);
                }
                catch (HttpError ex)
                {
                    response = JsonResponse(ex.Status, new { error = ex.Message });
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _log.Add((request != null ? request.Method + " " + request.Path + " failed: " : "request failed: ") + ex);
                    response = JsonResponse(500, new { error = ex.GetType().Name + ": " + ex.Message });
                }

                try
                {
                    await WriteResponseAsync(stream, response, ct);
                }
                catch (Exception ex) when (ex is IOException || ex is OperationCanceledException)
                {
                    // The client went away.
                }
            }
        }

        private static async Task<Request> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
        {
            var buffer = new byte[8192];
            var head = new MemoryStream();
            int headerEnd = -1;
            while (headerEnd < 0)
            {
                int n = await stream.ReadAsync(buffer, ct);
                if (n == 0) throw new HttpError(400, "Connection closed before the request was complete.");
                head.Write(buffer, 0, n);
                headerEnd = IndexOfHeaderEnd(head.GetBuffer(), (int)head.Length);
                if (headerEnd < 0 && head.Length > MaxHeaderBytes) throw new HttpError(431, "Request headers too large.");
            }

            byte[] raw = head.ToArray();
            string[] lines = Encoding.ASCII.GetString(raw, 0, headerEnd).Split("\r\n");
            string[] requestLine = lines[0].Split(' ');
            if (requestLine.Length < 3) throw new HttpError(400, "Malformed request line.");

            var request = new Request { Method = requestLine[0].ToUpperInvariant() };
            string target = requestLine[1];
            int q = target.IndexOf('?');
            request.Path = Uri.UnescapeDataString(q < 0 ? target : target.Substring(0, q)).TrimEnd('/');
            if (request.Path.Length == 0) request.Path = "/";
            if (q >= 0)
            {
                foreach (string pair in target.Substring(q + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    string key = Uri.UnescapeDataString((eq < 0 ? pair : pair.Substring(0, eq)).Replace('+', ' '));
                    string value = eq < 0 ? "" : Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
                    request.Query[key] = value;
                }
            }
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) request.Headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }

            int length = 0;
            if (request.Headers.TryGetValue("Content-Length", out string? cl) && (!int.TryParse(cl, out length) || length < 0))
                throw new HttpError(400, "Bad Content-Length.");
            if (length > MaxBodyBytes) throw new HttpError(413, "Request body too large.");
            if (request.Headers.ContainsKey("Transfer-Encoding"))
                throw new HttpError(411, "Send the body with a Content-Length.");

            var body = new byte[length];
            int have = Math.Min(length, raw.Length - (headerEnd + 4));
            Buffer.BlockCopy(raw, headerEnd + 4, body, 0, have);
            while (have < length)
            {
                int n = await stream.ReadAsync(body.AsMemory(have, length - have), ct);
                if (n == 0) throw new HttpError(400, "Connection closed before the body was complete.");
                have += n;
            }
            request.Body = body;
            return request;
        }

        private static int IndexOfHeaderEnd(byte[] data, int length)
        {
            for (int i = 0; i + 3 < length; i++)
                if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
                    return i;
            return -1;
        }

        private static async Task WriteResponseAsync(NetworkStream stream, Response response, CancellationToken ct)
        {
            string head =
                "HTTP/1.1 " + response.Status + " " + ReasonPhrase(response.Status) + "\r\n" +
                "Content-Type: " + response.ContentType + "\r\n" +
                "Content-Length: " + response.Body.Length + "\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
            await stream.WriteAsync(response.Body, ct);
            await stream.FlushAsync(ct);
        }

        private static string ReasonPhrase(int status) => status switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            409 => "Conflict",
            411 => "Length Required",
            413 => "Payload Too Large",
            431 => "Request Header Fields Too Large",
            503 => "Service Unavailable",
            _ => "Error"
        };

        /// <summary>
        /// Only a local program may drive the UI. A web page cannot: browsers send an
        /// Origin header on the requests a page makes, and a page that rebinds its own
        /// host name to 127.0.0.1 still sends that name as Host.
        /// </summary>
        private void CheckCaller(Request request)
        {
            if (request.Headers.ContainsKey("Origin"))
                throw new HttpError(403, "Requests from web pages are not accepted.");

            if (request.Headers.TryGetValue("Host", out string? host))
            {
                string name = host;
                int colon = host.LastIndexOf(':');
                if (colon > 0 && !host.EndsWith("]")) name = host.Substring(0, colon);
                if (name != "127.0.0.1" && name != "localhost" && name != "[::1]")
                    throw new HttpError(403, "Host " + host + " is not accepted; connect to 127.0.0.1.");
            }

            if (_token == null) return;
            string? given = null;
            if (request.Headers.TryGetValue("X-Automation-Token", out string? t)) given = t;
            else if (request.Headers.TryGetValue("Authorization", out string? auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                given = auth.Substring(7).Trim();
            if (given == null ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(_token)))
                throw new HttpError(401, "Missing or wrong token (X-Automation-Token header).");
        }

        // ── Routes ────────────────────────────────────────────────────────

        private async Task<Response> DispatchAsync(Request r)
        {
            switch (r.Method + " " + r.Path)
            {
                case "GET /":
                    return JsonResponse(200, new { endpoints = Routes });
                case "GET /status":
                    return JsonResponse(200, Status());
                case "GET /scene":
                    return JsonResponse(200, await SceneAsync());
                case "GET /commands":
                    return JsonResponse(200, new { commands = Commands() });
                case "POST /commands/run":
                    return await OnUiGateAsync(() => RunCommandAsync(r));
                case "POST /project/open":
                    return await OnUiGateAsync(() => OpenProjectAsync(r));
                case "POST /project/save":
                    return await OnUiGateAsync(() => SaveProjectAsync(r));
                case "GET /viewport.png":
                    return await OnUiGateAsync(() => ViewportPngAsync(r));
                case "POST /viewport/save":
                    return await OnUiGateAsync(() => SaveViewportAsync(r));
                case "GET /simulations":
                    return JsonResponse(200, new { simulations = await SimulationsAsync() });
                case "POST /simulations":
                    return await OnUiGateAsync(() => AddSimulationAsync(r));
                case "POST /simulations/run":
                    return await OnUiGateAsync(() => RunSimulationAsync(r));
                case "POST /simulations/cancel":
                    return await CancelSimulationAsync(r);
                case "POST /simulations/show":
                    return await OnUiGateAsync(() => ShowSimulationResultAsync(r));
                case "GET /simulations/status":
                    return RunStatusResponse(r);
                case "GET /log":
                    long after = r.Query.TryGetValue("after", out string? a) && long.TryParse(a, out long v) ? v : 0;
                    return JsonResponse(200, new { entries = _log.Since(after) });
            }
            bool known = Routes.Any(line => line.Substring(5).Split(' ', '?')[0] == r.Path);
            throw known
                ? new HttpError(405, r.Method + " is not accepted on " + r.Path + ".")
                : new HttpError(404, "No endpoint " + r.Path + "; GET / lists them.");
        }

        /// <summary>One request at a time touches the project or the view.</summary>
        private async Task<Response> OnUiGateAsync(Func<Task<Response>> work)
        {
            await _uiGate.WaitAsync();
            try
            {
                return await work();
            }
            finally
            {
                _uiGate.Release();
            }
        }

        private object Status()
        {
            Assembly engine = typeof(Scene3D).Assembly;
            string? version = engine.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                              ?? engine.GetName().Version?.ToString();
            return new
            {
                app = "DisperSim 3D",
                version,
                host = _host.HostName,
                projectPath = _host.CurrentProjectPath,
                projectName = _host.CurrentScene?.Name,
                statusText = _host.StatusText,
                url = Url,
                processId = Environment.ProcessId
            };
        }

        private async Task<object> SceneAsync()
        {
            object? summary = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                summary = scene == null ? new { open = false } : DescribeScene(scene, _host.CurrentProjectPath);
                return Task.CompletedTask;
            });
            return summary!;
        }

        private static object DescribeScene(Scene3D s, string? path)
        {
            static double[] P(Point3D p) => new[] { p.X, p.Y, p.Z };
            static object Source(ReleaseSource3D x) => new { x.Id, x.Name, position = P(x.Position) };

            return new
            {
                open = true,
                path,
                s.Name,
                s.Description,
                counts = new
                {
                    decorations = s.Decorations?.Count ?? 0,
                    scenarios = s.DispersionScenarios?.Count ?? 0,
                    topLevelSources = s.TopLevelSources?.Count ?? 0,
                    gasDetectors = s.GasDetectors?.Count ?? 0,
                    monitorPoints = s.MonitorPoints?.Count ?? 0,
                    simulations = s.Simulations?.Count ?? 0,
                    cfdSimulations = s.CfdSimulations?.Count ?? 0,
                    dispersionStudies = s.DispersionStudies?.Count ?? 0,
                    fireStudies = s.FireStudies?.Count ?? 0,
                    cameraPresets = s.CameraPresets?.Count ?? 0
                },
                decorations = (s.Decorations ?? new()).Select(d => new { d.Id, d.Name, d.FilePath, position = P(d.Position) }),
                topLevelSources = (s.TopLevelSources ?? new()).Select(Source),
                scenarios = (s.DispersionScenarios ?? new()).Select(sc => new
                {
                    sc.Id,
                    sc.Name,
                    sources = (sc.Sources ?? new()).Select(Source)
                }),
                gasDetectors = (s.GasDetectors ?? new()).Select(g => new { g.Id, g.Name, position = P(g.Position) }),
                monitorPoints = (s.MonitorPoints ?? new()).Select(m => new { m.Id, m.Name, type = m.Type.ToString(), position = P(m.Position) })
            };
        }

        private static IEnumerable<object> Commands() =>
            ExtenderLoader.LoadAll().Collections.SelectMany(c => c.Collection.Select(e => (object)new
            {
                id = e.ID,
                displayText = e.DisplayText,
                collection = c.ID,
                collectionText = c.DisplayText
            }));

        private async Task<Response> RunCommandAsync(Request r)
        {
            JsonObject body = ReadJson(r);
            string id = RequiredString(body, "id");

            IExtenderCollection? owner = null;
            IExtender? command = null;
            foreach (IExtenderCollection c in ExtenderLoader.LoadAll().Collections)
            {
                command = c.Collection.FirstOrDefault(e => string.Equals(e.ID, id, StringComparison.OrdinalIgnoreCase));
                if (command != null) { owner = c; break; }
            }
            if (command == null) throw new HttpError(404, "No command " + id + "; GET /commands lists them.");
            if (owner!.ID == _ownCollectionId) throw new HttpError(400, "The server's own commands run from the menu only.");

            var answers = new List<string?>();
            if (body["answers"] is JsonArray arr)
                foreach (JsonNode? a in arr) answers.Add(a?.GetValue<string>());

            var host = new AutomationHost(_host, answers, _log);
            _log.Add("run " + command.ID + " with " + answers.Count + " answer(s)");
            Exception? failure = null;
            await _host.RunOnUiThreadAsync(async () =>
            {
                try
                {
                    await command.RunAsync(host);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            if (failure != null) _log.Add(command.ID + " threw " + failure);

            bool ok = failure == null && !host.Messages.Any(m => m.IsError);
            return JsonResponse(200, new
            {
                ok,
                command = command.ID,
                dialogs = host.Dialogs,
                messages = host.Messages,
                unusedAnswers = host.UnusedAnswers,
                error = failure == null ? null : failure.GetType().Name + ": " + failure.Message,
                projectPath = _host.CurrentProjectPath,
                projectName = _host.CurrentScene?.Name,
                statusText = _host.StatusText
            });
        }

        private async Task<Response> OpenProjectAsync(Request r)
        {
            string path = FullPath(RequiredString(ReadJson(r), "path"));
            if (!File.Exists(path)) throw new HttpError(404, "No file " + path + ".");
            _log.Add("open project " + path);
            await _host.OpenProjectAsync(path);
            bool ok = string.Equals(_host.CurrentProjectPath, path, StringComparison.OrdinalIgnoreCase);
            return JsonResponse(200, new { ok, projectPath = _host.CurrentProjectPath, projectName = _host.CurrentScene?.Name, statusText = _host.StatusText });
        }

        private async Task<Response> SaveProjectAsync(Request r)
        {
            JsonObject body = r.Body.Length == 0 ? new JsonObject() : ReadJson(r);
            string? given = body["path"]?.GetValue<string>();
            string? path = string.IsNullOrWhiteSpace(given) ? _host.CurrentProjectPath : FullPath(given);
            if (_host.CurrentScene == null) throw new HttpError(409, "No project is open.");
            if (path == null) throw new HttpError(400, "The project has never been saved; give a path.");
            _log.Add("save project " + path);
            await _host.SaveProjectAsync(path);
            bool ok = File.Exists(path);
            return JsonResponse(200, new { ok, projectPath = path, bytes = ok ? new FileInfo(path).Length : 0 });
        }

        private async Task<Response> ViewportPngAsync(Request r)
        {
            byte[] png = await CaptureAsync(QueryInt(r, "width"), QueryInt(r, "height"));
            return new Response(200, "image/png", png);
        }

        private async Task<Response> SaveViewportAsync(Request r)
        {
            JsonObject body = ReadJson(r);
            string path = FullPath(RequiredString(body, "path"));
            int width = body["width"]?.GetValue<int>() ?? 0;
            int height = body["height"]?.GetValue<int>() ?? 0;
            byte[] png = await CaptureAsync(width, height);
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(path, png);
            _log.Add("viewport saved to " + path);
            return JsonResponse(200, new { ok = true, path, bytes = png.Length });
        }

        private async Task<byte[]> CaptureAsync(int width, int height)
        {
            if (width < 0 || height < 0 || width > 8192 || height > 8192)
                throw new HttpError(400, "width and height go from 0 (the view's own size) to 8192.");
            byte[]? png = await _host.CaptureViewportPngAsync(width, height);
            if (png == null) throw new HttpError(503, "The 3D view gave no image; is the window visible?");
            return png;
        }

        // ── Simulations ───────────────────────────────────────────────────

        private IExtenderHost3 Host3 => _host as IExtenderHost3
            ?? throw new HttpError(501, "This DisperSim 3D cannot run simulations for extenders.");

        private async Task<List<object>> SimulationsAsync()
        {
            var host = Host3;
            var list = new List<object>();
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene != null)
                    foreach (Simulation sim in scene.Simulations)
                        list.Add(DescribeSimulation(scene, sim, host.GetSimulationRunStatus(sim.Id)));
                return Task.CompletedTask;
            });
            return list;
        }

        private static object DescribeSimulation(Scene3D scene, Simulation sim, ExtenderRunStatus? run)
        {
            var source = scene.TopLevelSources.FirstOrDefault(s => s.Id == sim.SourceId);
            var wind = scene.WindFieldScenarios.FirstOrDefault(w => w.Id == sim.WindFieldId);
            return new
            {
                sim.Id,
                sim.Name,
                solver = sim.SolverType.ToString(),
                sim.ExtenderSolverId,
                status = sim.Status.ToString(),
                source = source?.Name,
                sim.SourceId,
                windField = wind?.Name,
                sim.WindFieldId,
                windSpeedMPerS = wind?.Meteo?.WindSpeed,
                durationS = sim.SnapshotDurationS,
                timeStepS = sim.SnapshotTimeStepS,
                domainSizeM = sim.SnapshotDomainSizeM,
                gridResolution = sim.SnapshotGridResolution,
                snapshotCount = sim.SnapshotCount,
                maxConcentration = sim.MaxConcentration,
                run
            };
        }

        /// <summary>Adds a simulation with the Add Simulation dialog's defaults for what the request leaves out.</summary>
        private async Task<Response> AddSimulationAsync(Request r)
        {
            var host = Host3;
            JsonObject body = ReadJson(r);
            string sourceKey = RequiredString(body, "source");
            string solverText = RequiredString(body, "solver");
            // A built-in solver by its enum name, else an extender solver by its ID, code or alias.
            ISolverExtender? extenderSolver = null;
            if (!Enum.TryParse(solverText, ignoreCase: true, out CfdSolverType solver) || !Enum.IsDefined(solver))
            {
                extenderSolver = SolverExtenderRegistry.FindByName(solverText)
                    ?? throw new HttpError(400, "Unknown solver " + solverText + "; one of " + string.Join(", ", Enum.GetNames<CfdSolverType>())
                        + (SolverExtenderRegistry.All.Count > 0 ? " or an extender solver: " + string.Join(", ", SolverExtenderRegistry.All.Select(s => s.Id)) : "") + ".");
                solver = CfdSolverType.Extender;
            }
            else if (solver == CfdSolverType.Extender)
                throw new HttpError(400, "Name the extender solver by its ID or code.");
            string? windKey = OptionalString(body, "windField");

            object? created = null;
            string? error = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene == null) { error = "No project is open."; return Task.CompletedTask; }

                var source = scene.TopLevelSources.FirstOrDefault(s => s.Id == sourceKey)
                             ?? scene.TopLevelSources.FirstOrDefault(s => string.Equals(s.Name, sourceKey, StringComparison.OrdinalIgnoreCase));
                if (source == null) { error = "No source " + sourceKey + " in the project."; return Task.CompletedTask; }

                WindFieldScenario? wind = windKey == null
                    ? scene.WindFieldScenarios.FirstOrDefault()
                    : scene.WindFieldScenarios.FirstOrDefault(w => w.Id == windKey)
                      ?? scene.WindFieldScenarios.FirstOrDefault(w => string.Equals(w.Name, windKey, StringComparison.OrdinalIgnoreCase));
                if (windKey != null && wind == null) { error = "No wind field " + windKey + " in the project."; return Task.CompletedTask; }

                var sim = new Simulation
                {
                    Name = OptionalString(body, "name") ?? "Simulation " + (scene.Simulations.Count + 1),
                    SourceId = source.Id,
                    WindFieldId = wind?.Id,
                    SolverType = solver,
                    ExtenderSolverId = extenderSolver?.Id,
                    Status = SimulationStatus.Configured,
                    SnapshotDurationS = OptionalDouble(body, "durationS") ?? 300,
                    SnapshotTimeStepS = OptionalDouble(body, "timeStepS") ?? 0.5,
                    SnapshotDomainSizeM = OptionalDouble(body, "domainSizeM") ?? scene.GeneralSettings?.DefaultDomainSizeM ?? 200,
                    SnapshotGridResolution = (int)(OptionalDouble(body, "gridResolution") ?? scene.GeneralSettings?.DefaultGridResolution ?? 40),
                    SnapshotCount = (int)(OptionalDouble(body, "snapshotCount") ?? 20)
                };
                scene.Simulations.Add(sim);
                host.RefreshProject();
                created = DescribeSimulation(scene, sim, null);
                return Task.CompletedTask;
            });
            if (error != null) throw new HttpError(error.StartsWith("No project") ? 409 : 404, error);
            _log.Add("added simulation " + (extenderSolver?.Id ?? solver.ToString()) + " for " + sourceKey);
            return JsonResponse(200, new { ok = true, simulation = created });
        }

        private async Task<Response> RunSimulationAsync(Request r)
        {
            var host = Host3;
            string id = RequiredString(ReadJson(r), "id");
            _log.Add("run simulation " + id);
            string? refused = await host.RunSimulationAsync(id);
            if (refused != null)
            {
                _log.Add("simulation " + id + " not run: " + refused);
                throw new HttpError(409, refused);
            }
            return JsonResponse(200, new { ok = true, id, run = host.GetSimulationRunStatus(id) });
        }

        private async Task<Response> CancelSimulationAsync(Request r)
        {
            var host = Host3;
            string id = RequiredString(ReadJson(r), "id");
            string? refused = await host.CancelSimulationAsync(id);
            if (refused != null) throw new HttpError(409, refused);
            _log.Add("cancelled simulation " + id);
            return JsonResponse(200, new { ok = true, id, run = host.GetSimulationRunStatus(id) });
        }

        private async Task<Response> ShowSimulationResultAsync(Request r)
        {
            var host = Host3;
            var body = ReadJson(r);
            string id = RequiredString(body, "id");
            double? timeS = OptionalDouble(body, "timeS");
            string? refused = await host.ShowSimulationResultAsync(id, timeS);
            if (refused != null) throw new HttpError(409, refused);
            _log.Add("showing simulation " + id + (timeS.HasValue ? " at " + timeS.Value + " s" : ""));
            return JsonResponse(200, new { ok = true, id, timeS, status = host.StatusText });
        }

        private Response RunStatusResponse(Request r)
        {
            if (!r.Query.TryGetValue("id", out string? id) || string.IsNullOrWhiteSpace(id))
                throw new HttpError(400, "Give the simulation as ?id=.");
            ExtenderRunStatus? run = Host3.GetSimulationRunStatus(id);
            if (run == null) throw new HttpError(404, "Simulation " + id + " has not been run in this session.");
            return JsonResponse(200, new { id, run });
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static Response JsonResponse(int status, object value) =>
            new(status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value, Json));

        private static JsonObject ReadJson(Request r)
        {
            try
            {
                return JsonNode.Parse(r.Body) as JsonObject ?? throw new HttpError(400, "The body must be a JSON object.");
            }
            catch (JsonException ex)
            {
                throw new HttpError(400, "Invalid JSON: " + ex.Message);
            }
        }

        private static string RequiredString(JsonObject body, string name)
        {
            string? value = body[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
            if (string.IsNullOrWhiteSpace(value)) throw new HttpError(400, "\"" + name + "\" is required.");
            return value;
        }

        private static string? OptionalString(JsonObject body, string name) =>
            body[name] is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s : null;

        private static double? OptionalDouble(JsonObject body, string name)
        {
            if (body[name] is not JsonValue v) return null;
            if (v.TryGetValue(out double d)) return d;
            if (v.TryGetValue(out string? s) && double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            throw new HttpError(400, "\"" + name + "\" must be a number.");
        }

        private static string FullPath(string path)
        {
            if (!Path.IsPathFullyQualified(path)) throw new HttpError(400, "Give a full path: " + path);
            return Path.GetFullPath(path);
        }

        private static int QueryInt(Request r, string name) =>
            r.Query.TryGetValue(name, out string? s) && int.TryParse(s, out int v) ? v : 0;
    }
}
