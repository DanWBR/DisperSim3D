using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DisperSim3D.Extensibility;
using DisperSim3D.Models;
using DisperSim3D.UI.Avalonia.Views;

namespace DisperSim3D.UI.Avalonia.Core
{
    /// <summary>
    /// The Avalonia UI's side of the extender contract. Every member marshals to
    /// the UI thread itself, so an extender may call it from a worker.
    /// </summary>
    internal sealed class AvaloniaExtenderHost : IExtenderHost
    {
        private readonly MainWindow _window;

        public AvaloniaExtenderHost(MainWindow window)
        {
            _window = window;
        }

        public string HostName => "Avalonia";

        public object? MainWindow => _window;

        public Scene3D? CurrentScene => OnUi(() => _window.CurrentScene);

        public string? CurrentProjectPath => OnUi(() => _window.CurrentProjectPath);

        public Task<string?> PickOpenFileAsync(string title, IReadOnlyList<ExtenderFileFilter> filters) =>
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                IReadOnlyList<IStorageFile> files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                    FileTypeFilter = ToFileTypes(filters)
                });
                if (files == null || files.Count == 0) return (string?)null;
                return files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
            });

        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, IReadOnlyList<ExtenderFileFilter> filters) =>
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                string? ext = filters.SelectMany(f => f.Patterns)
                                     .Select(p => p.TrimStart('*', '.'))
                                     .FirstOrDefault(p => p.Length > 0 && p != "*");
                IStorageFile? file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = title,
                    SuggestedFileName = suggestedFileName,
                    DefaultExtension = ext,
                    FileTypeChoices = ToFileTypes(filters),
                    ShowOverwritePrompt = true
                });
                if (file == null) return (string?)null;
                return file.TryGetLocalPath() ?? file.Path.LocalPath;
            });

        public Task OpenProjectAsync(string path) =>
            Dispatcher.UIThread.InvokeAsync(() => _window.OpenProjectFromExtenderAsync(path));

        public Task ShowMessageAsync(string title, string message, bool isError = false) =>
            Dispatcher.UIThread.InvokeAsync(() => ShowDialogAsync(title, message, isError));

        public void SetStatus(string text) => OnUi(() => { _window.SetStatusText(text); return true; });

        private static T OnUi<T>(Func<T> func) =>
            Dispatcher.UIThread.CheckAccess() ? func() : Dispatcher.UIThread.Invoke(func);

        private static IReadOnlyList<FilePickerFileType> ToFileTypes(IReadOnlyList<ExtenderFileFilter> filters)
        {
            if (filters == null || filters.Count == 0)
                return new[] { new FilePickerFileType("All files") { Patterns = new[] { "*" } } };
            return filters.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns.ToArray() }).ToList();
        }

        /// <summary>A plain message box: the text and an OK button.</summary>
        private async Task ShowDialogAsync(string title, string message, bool isError)
        {
            var ok = new Button
            {
                Content = "OK",
                MinWidth = 80,
                IsDefault = true,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.Parse("#0078D4")),
                Foreground = Brushes.White
            };
            var dialog = new Window
            {
                Title = title,
                Width = 520,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new global::Avalonia.Thickness(16),
                    Spacing = 14,
                    Children =
                    {
                        new SelectableTextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = isError ? new SolidColorBrush(Color.Parse("#B00020")) : null
                        },
                        ok
                    }
                }
            };
            ok.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(_window);
        }
    }
}
