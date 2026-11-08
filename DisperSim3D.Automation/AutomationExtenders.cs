using System.Net.Sockets;
using DisperSim3D.Extensibility;

namespace DisperSim3D.Automation
{
    /// <summary>
    /// The automation server as an extender: two menu commands to start and stop
    /// it, and a start at launch when DISPERSIM3D_AUTOMATION is set.
    /// <list type="bullet">
    /// <item>DISPERSIM3D_AUTOMATION=1: start the server when the main window opens.</item>
    /// <item>DISPERSIM3D_AUTOMATION_PORT: the port, 5013 by default; 0 takes any free one.</item>
    /// <item>DISPERSIM3D_AUTOMATION_TOKEN: when set, every request must carry it in X-Automation-Token.</item>
    /// </list>
    /// </summary>
    public sealed class AutomationExtenders : IExtenderCollection, IExtenderStartup
    {
        public const string CollectionId = "DisperSim3D.Automation";
        public const int DefaultPort = 5013;

        private readonly object _gate = new();
        private readonly AutomationLog _log = new();
        private AutomationServer? _server;

        public AutomationExtenders()
        {
            Collection = new IExtender[] { new StartCommand(this), new StopCommand(this) };
        }

        public string ID => CollectionId;

        public string Description => "Local HTTP server that lets scripts and tests drive DisperSim 3D.";

        public string DisplayText => "Automation";

        public ExtenderCategory Category => ExtenderCategory.Tools;

        public IReadOnlyList<IExtender> Collection { get; }

        public void OnHostStarted(IExtenderHost host)
        {
            if (!IsTrue(Environment.GetEnvironmentVariable("DISPERSIM3D_AUTOMATION"))) return;
            if (host is not IExtenderHost2 host2)
            {
                host.SetStatus("Automation server needs a newer DisperSim 3D.");
                return;
            }
            string result = Start(host2, out _);
            host.SetStatus(result);
        }

        /// <summary>Starts the server, or reports the one already running.</summary>
        private string Start(IExtenderHost2 host, out bool started)
        {
            lock (_gate)
            {
                started = false;
                if (_server != null) return "Automation server already listening on " + _server.Url;

                int port = DefaultPort;
                string? portText = Environment.GetEnvironmentVariable("DISPERSIM3D_AUTOMATION_PORT");
                if (!string.IsNullOrWhiteSpace(portText) && (!int.TryParse(portText, out port) || port < 0 || port > 65535))
                    return "Automation server not started: DISPERSIM3D_AUTOMATION_PORT=" + portText + " is not a port.";

                var server = new AutomationServer(host, port, Environment.GetEnvironmentVariable("DISPERSIM3D_AUTOMATION_TOKEN"),
                                                  _log, CollectionId);
                try
                {
                    server.Start();
                }
                catch (SocketException ex)
                {
                    server.Dispose();
                    return "Automation server not started: port " + port + " — " + ex.Message;
                }
                _server = server;
                started = true;
                return "Automation server listening on " + server.Url + (server.RequiresToken ? " (token required)" : "");
            }
        }

        private string Stop()
        {
            lock (_gate)
            {
                if (_server == null) return "Automation server is not running.";
                _server.Dispose();
                _server = null;
                return "Automation server stopped.";
            }
        }

        private static bool IsTrue(string? value) =>
            value != null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                              value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                              value.Equals("on", StringComparison.OrdinalIgnoreCase));

        private sealed class StartCommand : IExtender
        {
            private readonly AutomationExtenders _owner;

            public StartCommand(AutomationExtenders owner) { _owner = owner; }

            public string ID => CollectionId + ".Start";

            public string DisplayText => "Automation Server: Start";

            public byte[]? DisplayImage => null;

            public int InsertAtPosition => -1;

            public async Task RunAsync(IExtenderHost host)
            {
                if (host is not IExtenderHost2 host2)
                {
                    await host.ShowMessageAsync("Automation", "The automation server needs a newer DisperSim 3D.", true);
                    return;
                }
                string result = _owner.Start(host2, out bool started);
                host.SetStatus(result);
                string help = started
                    ? "\n\nEndpoints:\n" + string.Join("\n", AutomationServer.Routes)
                    : "";
                await host.ShowMessageAsync("Automation", result + help, isError: result.Contains("not started"));
            }
        }

        private sealed class StopCommand : IExtender
        {
            private readonly AutomationExtenders _owner;

            public StopCommand(AutomationExtenders owner) { _owner = owner; }

            public string ID => CollectionId + ".Stop";

            public string DisplayText => "Automation Server: Stop";

            public byte[]? DisplayImage => null;

            public int InsertAtPosition => -1;

            public Task RunAsync(IExtenderHost host)
            {
                host.SetStatus(_owner.Stop());
                return Task.CompletedTask;
            }
        }
    }
}
