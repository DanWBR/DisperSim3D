#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DisperSim3D.Controls;
using DisperSim3D.Extensibility;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// The WinForms shell's side of the extender contract. Every member marshals
    /// to the UI thread through the editor panel, so an extender may call it from
    /// a worker.
    /// </summary>
    public sealed class WinFormsExtenderHost : IExtenderHost2
    {
        private readonly Scene3DEditorPanel _panel;

        public WinFormsExtenderHost(Scene3DEditorPanel panel)
        {
            _panel = panel;
        }

        public string HostName => "WinForms";

        public object? MainWindow => _panel.FindForm();

        public Scene3D? CurrentScene => OnUi(() => _panel.CurrentScene);

        public string? CurrentProjectPath => OnUi(() => _panel.CurrentProjectPath);

        public Task<string?> PickOpenFileAsync(string title, IReadOnlyList<ExtenderFileFilter> filters)
        {
            return Task.FromResult(OnUi(() =>
            {
                using var dlg = new OpenFileDialog
                {
                    Title = title,
                    Filter = ToFilter(filters),
                    CheckFileExists = true
                };
                return dlg.ShowDialog(_panel.FindForm()) == DialogResult.OK ? dlg.FileName : null;
            }));
        }

        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, IReadOnlyList<ExtenderFileFilter> filters)
        {
            return Task.FromResult(OnUi(() =>
            {
                using var dlg = new SaveFileDialog
                {
                    Title = title,
                    Filter = ToFilter(filters),
                    FileName = suggestedFileName,
                    OverwritePrompt = true
                };
                string? ext = filters.SelectMany(f => f.Patterns)
                                     .Select(p => p.TrimStart('*', '.'))
                                     .FirstOrDefault(p => p.Length > 0 && p != "*");
                if (ext != null) dlg.DefaultExt = ext;
                return dlg.ShowDialog(_panel.FindForm()) == DialogResult.OK ? dlg.FileName : null;
            }));
        }

        public Task OpenProjectAsync(string path)
        {
            OnUi(() => { _panel.LoadFromFile(path); return true; });
            return Task.CompletedTask;
        }

        public Task ShowMessageAsync(string title, string message, bool isError = false)
        {
            OnUi(() => MessageBox.Show(_panel.FindForm(), message, title, MessageBoxButtons.OK,
                isError ? MessageBoxIcon.Error : MessageBoxIcon.Information));
            return Task.CompletedTask;
        }

        public void SetStatus(string text) => OnUi(() => { _panel.SetStatusText(text); return true; });

        public string StatusText => OnUi(() => _panel.StatusTextValue);

        public Task SaveProjectAsync(string path)
        {
            OnUi(() => { _panel.SaveToFile(path); return true; });
            return Task.CompletedTask;
        }

        public Task<byte[]?> CaptureViewportPngAsync(int width, int height) =>
            Task.FromResult<byte[]?>(OnUi(() => _panel.RenderViewportPng(width, height)));

        public Task RunOnUiThreadAsync(Func<Task> action)
        {
            if (!_panel.InvokeRequired) return action();

            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _panel.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await action();
                    done.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    done.TrySetException(ex);
                }
            }));
            return done.Task;
        }

        private T OnUi<T>(Func<T> func) =>
            _panel.InvokeRequired ? (T)_panel.Invoke(func) : func();

        private static string ToFilter(IReadOnlyList<ExtenderFileFilter> filters)
        {
            if (filters == null || filters.Count == 0) return "All files (*.*)|*.*";
            return string.Join("|", filters.Select(f =>
            {
                string patterns = string.Join(";", f.Patterns);
                return $"{f.Name} ({patterns})|{patterns}";
            }));
        }
    }
}
