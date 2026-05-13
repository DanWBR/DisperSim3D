#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>BatchExportDialog</c>.
    /// Lets the user pick which camera presets to render and into which folder,
    /// at the chosen pixel dimensions. On OK the dialog exposes the selected
    /// presets, image size, and output folder via plain properties. The actual
    /// rendering loop lives in the caller — the Avalonia 3D viewport is still
    /// a placeholder, so this dialog is wired up to also be usable from any
    /// future WPF→Avalonia bridge.
    /// </summary>
    public partial class BatchExportDialog : Window
    {
        private readonly ObservableCollection<PresetRow> _rows = new();

        public List<CameraPreset> SelectedPresets { get; private set; } = new();
        public int ImageWidth { get; private set; } = 1920;
        public int ImageHeight { get; private set; } = 1080;
        public string OutputFolder { get; private set; } = "";

        public BatchExportDialog() : this(new List<CameraPreset>()) { }

        public BatchExportDialog(List<CameraPreset> presets)
        {
            InitializeComponent();

            TxtFolder.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            // ItemsControl with a CheckBox per row — no DataGrid needed for
            // such a simple list, and it keeps keyboard / space-bar toggling
            // intuitive. All presets default to checked, matching WinForms.
            foreach (var p in presets)
                _rows.Add(new PresetRow { Preset = p, Name = p.Name ?? "(unnamed)", IsChecked = true });
            PresetList.ItemsSource = _rows;
            PresetList.ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<PresetRow>(
                (row, _) =>
                {
                    var cb = new CheckBox
                    {
                        Content = row.Name,
                        IsChecked = row.IsChecked,
                        Margin = new global::Avalonia.Thickness(4, 1)
                    };
                    cb.IsCheckedChanged += (_, _) => row.IsChecked = cb.IsChecked == true;
                    return cb;
                });
        }

        private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select output folder",
                AllowMultiple = false
            });
            if (folders == null || folders.Count == 0) return;
            TxtFolder.Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            ImageWidth   = (int)(NudWidth.Value ?? 1920m);
            ImageHeight  = (int)(NudHeight.Value ?? 1080m);
            OutputFolder = TxtFolder.Text ?? "";
            SelectedPresets = new List<CameraPreset>();
            foreach (var row in _rows)
                if (row.IsChecked) SelectedPresets.Add(row.Preset);
            Close(true);
        }

        /// <summary>Light row wrapper so the IsChecked toggle can stay editable
        /// after the ItemTemplate fires; tracking it on the engine CameraPreset
        /// directly would persist into the saved project, which we don't want.</summary>
        private sealed class PresetRow
        {
            public CameraPreset Preset { get; set; } = new CameraPreset();
            public string Name { get; set; } = "";
            public bool IsChecked { get; set; }
        }
    }
}
