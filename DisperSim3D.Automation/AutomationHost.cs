using DisperSim3D.Extensibility;
using DisperSim3D.Models;

namespace DisperSim3D.Automation
{
    /// <summary>
    /// The host an extender command sees when it runs through the server: the real
    /// host, except that file dialogs take their answers from the request, in order,
    /// and messages are recorded instead of shown. Nothing waits for a person.
    /// </summary>
    internal sealed class AutomationHost : IExtenderHost2
    {
        private readonly IExtenderHost2 _inner;
        private readonly Queue<string?> _answers;
        private readonly AutomationLog _log;

        public AutomationHost(IExtenderHost2 inner, IEnumerable<string?> answers, AutomationLog log)
        {
            _inner = inner;
            _answers = new Queue<string?>(answers);
            _log = log;
        }

        /// <summary>The dialogs the command opened, with the answer each got.</summary>
        public List<DialogRecord> Dialogs { get; } = new();

        /// <summary>The messages the command would have shown.</summary>
        public List<MessageRecord> Messages { get; } = new();

        /// <summary>Answers the command never asked for.</summary>
        public int UnusedAnswers => _answers.Count;

        internal sealed record DialogRecord(string Kind, string Title, string? Answer);

        internal sealed record MessageRecord(string Title, string Text, bool IsError);

        public string HostName => _inner.HostName;

        public object? MainWindow => _inner.MainWindow;

        public Scene3D? CurrentScene => _inner.CurrentScene;

        public string? CurrentProjectPath => _inner.CurrentProjectPath;

        public string StatusText => _inner.StatusText;

        public Task<string?> PickOpenFileAsync(string title, IReadOnlyList<ExtenderFileFilter> filters) =>
            Task.FromResult(Answer("open", title));

        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, IReadOnlyList<ExtenderFileFilter> filters) =>
            Task.FromResult(Answer("save", title));

        public Task OpenProjectAsync(string path)
        {
            _log.Add("open project " + path);
            return _inner.OpenProjectAsync(path);
        }

        public Task SaveProjectAsync(string path)
        {
            _log.Add("save project " + path);
            return _inner.SaveProjectAsync(path);
        }

        public Task ShowMessageAsync(string title, string message, bool isError = false)
        {
            Messages.Add(new MessageRecord(title, message, isError));
            _log.Add((isError ? "error: " : "message: ") + title + " — " + message);
            return Task.CompletedTask;
        }

        public void SetStatus(string text) => _inner.SetStatus(text);

        public Task<byte[]?> CaptureViewportPngAsync(int width, int height) => _inner.CaptureViewportPngAsync(width, height);

        public Task RunOnUiThreadAsync(Func<Task> action) => _inner.RunOnUiThreadAsync(action);

        /// <summary>The next answer; a missing or empty one cancels the dialog.</summary>
        private string? Answer(string kind, string title)
        {
            string? answer = _answers.Count > 0 ? _answers.Dequeue() : null;
            if (string.IsNullOrWhiteSpace(answer)) answer = null;
            Dialogs.Add(new DialogRecord(kind, title, answer));
            _log.Add(kind + " dialog \"" + title + "\" → " + (answer ?? "(cancelled)"));
            return answer;
        }
    }
}
