using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DisperSim3D.Core;
using DisperSim3D.Models;
using DisperSim3D.UI.Avalonia.ViewModels;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Cross-platform main shell. Replaces the early diagnostics-only smoke as
    /// the startup window. Layout mirrors the WinForms <c>Scene3DEditorPanel</c>
    /// at a high level — menu bar on top, project tree on the left, viewport
    /// in the centre, inspector pane on the right, status bar on the bottom —
    /// but here it's pure Avalonia 11 and compiles for net10.0 so it runs on
    /// Windows, Linux and macOS from the same source.
    ///
    /// This is iteration 1 of the WPF→Avalonia port. The 3D viewport is a
    /// placeholder; renderers will be ported to OpenTK/Silk.NET on top of
    /// Avalonia's <c>OpenGlControlBase</c> in a follow-up. Dialogs, the
    /// property grid, and the playback bar arrive in subsequent iterations.
    /// </summary>
    public partial class MainWindow : Window
    {
        private Scene3D? _scene;
        private string? _projectPath;
        private bool _isDirty;

        // ── Simulation manager ──────────────────────────────────────────
        private Core.SimulationManager? _simulationManager;
        private SimulationManagerPanel? _simManagerPanel;

        private Core.SimulationManager SimulationManager
        {
            get
            {
                if (_simulationManager == null)
                {
                    _simulationManager = new Core.SimulationManager(2);
                    _simulationManager.JobCompleted += OnSimManagerJobCompleted;
                }
                return _simulationManager;
            }
        }

        // ── Playback state ──────────────────────────────────────────────
        private Simulation? _playbackSim;
        private OpenFoamResult? _playbackResult;
        private List<DispersionThreshold>? _playbackThresholds;
        private DispatcherTimer? _playbackTimer;
        private double _playbackTimeS;
        private double _playbackSpeedFactor = 1.0;
        private bool _playbackPlaying;

        public MainWindow()
        {
            InitializeComponent();
            StatusEnv.Text = BuildEnvLine();
            RefreshWorkDirStatus();
            StatusWorkDir.PointerPressed += (_, _) =>
            {
                try
                {
                    var dir = DisperSim3D.Core.TempManager.GetWorkDir();
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
                }
                catch { }
            };
            Inspector.ValueChanged += (_, _) =>
            {
                if (_scene == null) return;
                if (!_isDirty) { _isDirty = true; UpdateTitle(); }
                RebuildTree();
                Viewport3D.PopulateScene(_scene);
            };
            RebuildTree();

            var fpsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            fpsTimer.Tick += (_, _) => FpsLabel.Text = $"{Viewport3D.CurrentFps:F0} FPS";
            fpsTimer.Start();

            var workDirTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            workDirTimer.Tick += (_, _) => RefreshWorkDirStatus();
            workDirTimer.Start();
        }

        private static string BuildEnvLine()
        {
            string os = OperatingSystem.IsWindows() ? "Windows"
                : OperatingSystem.IsLinux() ? "Linux"
                : OperatingSystem.IsMacOS() ? "macOS"
                : "Other";
            return string.Format(CultureInfo.InvariantCulture,
                ".NET {0}  •  {1}  •  Avalonia {2}",
                Environment.Version,
                os,
                typeof(global::Avalonia.Application).Assembly.GetName().Version);
        }

        // ── Tree rebuild ─────────────────────────────────────────────────────
        private void RebuildTree()
        {
            string name = _projectPath is null
                ? "Untitled"
                : Path.GetFileNameWithoutExtension(_projectPath);
            var nodes = ProjectTreeBuilder.Build(_scene, name);
            ProjectTree.ItemsSource = nodes;
            // Subscribe to visibility toggles so the viewport updates
            SubscribeVisibilityToggles(nodes);
            UpdateTitle();
        }

        /// <summary>
        /// Walk the tree and subscribe to <see cref="ProjectTreeNode.IsVisible3D"/>
        /// changes so each checkbox toggle updates the matching
        /// <see cref="SceneObject.Visible"/> flag in the viewport.
        /// </summary>
        private void SubscribeVisibilityToggles(
            System.Collections.ObjectModel.ObservableCollection<ProjectTreeNode> roots)
        {
            foreach (var root in roots)
                WalkAndSubscribe(root);

            void WalkAndSubscribe(ProjectTreeNode node)
            {
                if (node.HasVisibilityToggle)
                {
                    node.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName != nameof(ProjectTreeNode.IsVisible3D)) return;
                        var n = (ProjectTreeNode)s!;
                        SetSceneObjectsVisibility(n.NodeId, n.IsVisible3D);
                        SyncModelVisibility(n.Tag, n.IsVisible3D);
                        if (!_isDirty) { _isDirty = true; UpdateTitle(); }
                    };
                }
                foreach (var child in node.Children)
                    WalkAndSubscribe(child);
            }
        }

        /// <summary>
        /// Map a tree node id ("src:guid", "mon:guid", "deco:guid") to the
        /// viewport's SceneObject tag ("source:0", "monitor:0", "deco:0").
        /// </summary>
        private SceneObject? FindSceneObjectForNode(string nodeId)
        {
            // nodeId = "src:guid", "fire:guid", "mon:guid", "det:guid", "deco:guid"
            // SceneObject.Tag = "source:idx", "fire:idx", "monitor:idx", "detector:idx", "deco:idx"
            int colon = nodeId.IndexOf(':');
            if (colon < 0) return null;
            string kind = nodeId.Substring(0, colon);
            string guid = nodeId.Substring(colon + 1);

            // Resolve the index by matching the GUID against the scene list
            string? sceneTag = null;
            if (_scene == null) return null;

            switch (kind)
            {
                case "src":
                    if (_scene.TopLevelSources != null)
                        for (int i = 0; i < _scene.TopLevelSources.Count; i++)
                            if (_scene.TopLevelSources[i].Id == guid) { sceneTag = "source:" + i; break; }
                    break;
                case "fire":
                    if (_scene.FireScenario?.Sources != null)
                        for (int i = 0; i < _scene.FireScenario.Sources.Count; i++)
                            if (_scene.FireScenario.Sources[i].Id == guid) { sceneTag = "fire:" + i; break; }
                    break;
                case "mon":
                    if (_scene.MonitorPoints != null)
                        for (int i = 0; i < _scene.MonitorPoints.Count; i++)
                            if (_scene.MonitorPoints[i].Id == guid) { sceneTag = "monitor:" + i; break; }
                    break;
                case "det":
                    if (_scene.GasDetectors != null)
                        for (int i = 0; i < _scene.GasDetectors.Count; i++)
                            if (_scene.GasDetectors[i].Id == guid) { sceneTag = "detector:" + i; break; }
                    break;
                case "deco":
                    if (_scene.Decorations != null)
                        for (int i = 0; i < _scene.Decorations.Count; i++)
                            if (_scene.Decorations[i].Id == guid) { sceneTag = "deco:" + i; break; }
                    break;
                case "wind":
                    foreach (var obj in Viewport3D.SceneObjects)
                        if (obj.Tag == "wind") return obj;
                    return null;
                case "view":
                    foreach (var obj in Viewport3D.SceneObjects)
                        if (obj.Tag != null && obj.Tag.EndsWith(guid)) return obj;
                    return null;
            }

            if (sceneTag == null) return null;
            foreach (var obj in Viewport3D.SceneObjects)
                if (obj.Tag == sceneTag) return obj;
            return null;
        }

        /// <summary>
        /// Toggle visibility on ALL scene objects that belong to the given
        /// tree node. Sources have a sphere + direction arrow; views may have
        /// iso/contour variants.
        /// </summary>
        private void SetSceneObjectsVisibility(string nodeId, bool visible)
        {
            int colon = nodeId.IndexOf(':');
            if (colon < 0) return;
            string kind = nodeId.Substring(0, colon);
            string guid = nodeId.Substring(colon + 1);
            if (_scene == null) return;

            bool any = false;

            switch (kind)
            {
                case "src":
                    // Source has "source:N" + "sourcearrow:N"
                    if (_scene.TopLevelSources != null)
                    {
                        for (int i = 0; i < _scene.TopLevelSources.Count; i++)
                        {
                            if (_scene.TopLevelSources[i].Id != guid) continue;
                            string tagSphere = "source:" + i;
                            string tagArrow  = "sourcearrow:" + i;
                            foreach (var obj in Viewport3D.SceneObjects)
                            {
                                if (obj.Tag == tagSphere || obj.Tag == tagArrow)
                                { obj.Visible = visible; any = true; }
                            }
                            break;
                        }
                    }
                    break;

                case "wind":
                    foreach (var obj in Viewport3D.SceneObjects)
                    {
                        if (obj.Tag == "wind")
                        { obj.Visible = visible; any = true; }
                    }
                    break;

                case "sim":
                    foreach (var obj in Viewport3D.SceneObjects)
                    {
                        if (obj.Tag != null && obj.Tag.StartsWith("dispersion:"))
                        { obj.Visible = visible; any = true; }
                    }
                    if (!visible && _playbackSim != null)
                    {
                        // Also find matching sim to check if it's the active playback
                        if (_scene?.Simulations != null)
                        {
                            var matchedSim = _scene.Simulations.FirstOrDefault(s => s.Id == guid);
                            if (matchedSim == _playbackSim)
                                StopAndHidePlayback();
                        }
                    }
                    break;

                case "view":
                    foreach (var obj in Viewport3D.SceneObjects)
                    {
                        if (obj.Tag != null && obj.Tag.EndsWith(guid))
                        { obj.Visible = visible; any = true; }
                    }
                    break;

                default:
                    // Single-object types: find and toggle
                    var sceneObj = FindSceneObjectForNode(nodeId);
                    if (sceneObj != null)
                    { sceneObj.Visible = visible; any = true; }
                    break;
            }

            if (any) Viewport3D.RequestNextFrameRendering();
        }

        private static void SyncModelVisibility(object? tag, bool visible)
        {
            switch (tag)
            {
                case ReleaseSource3D src: src.IsVisible = visible; break;
                case Simulation sim: sim.IsVisible = visible; break;
                case Models.View view: view.IsVisible = visible; break;
                case MonitorPoint3D mon: mon.Visible = visible; break;
                case GasDetector3D det: det.Visible = visible; break;
            }
        }

        private void UpdateTitle()
        {
            string baseName = _projectPath is null ? "Untitled" : Path.GetFileName(_projectPath);
            Title = "DisperSim 3D — " + baseName + (_isDirty ? " *" : "");
            MenuFileSave.IsEnabled = _scene != null;
            MenuFileSaveAs.IsEnabled = _scene != null;
        }

        // ── Inspector pane ───────────────────────────────────────────────────
        private void ProjectTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0 ||
                e.AddedItems[0] is not ProjectTreeNode node)
            {
                StatusSelection.Text = "";
                Inspector.SetTarget(null);
                return;
            }

            StatusSelection.Text = "Selected: " + node.NodeId;

            if (node.Tag is null)
            {
                Inspector.SetTarget(null,
                    "Section: " + node.Title + " (no properties; pick a child item).");
                return;
            }

            Inspector.SetTarget(node.Tag);

            if (node.Tag is Simulation sim && sim.Status == SimulationStatus.Completed)
                InitPlayback(sim);
            else if (_playbackSim != null && node.Tag is not Simulation)
                StopAndHidePlayback();
        }

        // ── Unsaved-changes guard ────────────────────────────────────────────

        private enum SavePromptResult { Save, Discard, Cancel }

        private async Task<SavePromptResult> AskSaveIfDirtyAsync()
        {
            if (!_isDirty || _scene is null)
                return SavePromptResult.Discard;

            string fileName = _projectPath is null
                ? "Untitled"
                : Path.GetFileName(_projectPath);

            var dlg = new Window
            {
                Title = "Unsaved Changes",
                Width = 440, Height = 190,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SystemDecorations = SystemDecorations.BorderOnly,
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 250))
            };

            var result = SavePromptResult.Cancel;

            var btnSave = new Button
            {
                Content = "Save", MinWidth = 90, Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                Foreground = Brushes.White, FontWeight = FontWeight.SemiBold,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
            };
            var btnDiscard = new Button
            {
                Content = "Discard", MinWidth = 90, Height = 32,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
            };
            var btnCancel = new Button
            {
                Content = "Cancel", MinWidth = 90, Height = 32,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
            };

            btnSave.Click += (_, _) => { result = SavePromptResult.Save; dlg.Close(); };
            btnDiscard.Click += (_, _) => { result = SavePromptResult.Discard; dlg.Close(); };
            btnCancel.Click += (_, _) => { result = SavePromptResult.Cancel; dlg.Close(); };

            var buttons = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8,
                Children = { btnCancel, btnDiscard, btnSave }
            };

            var icon = new Projektanker.Icons.Avalonia.Icon
            {
                Value = "mdi-alert-circle-outline",
                FontSize = 32,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 150, 0)),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 14, 0)
            };

            var textPanel = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Unsaved Changes",
                        FontSize = 15, FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = $"Save changes to \"{fileName}\" before closing?",
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                        TextWrapping = global::Avalonia.Media.TextWrapping.Wrap
                    }
                }
            };

            var header = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                Children = { icon, textPanel }
            };

            dlg.Content = new DockPanel
            {
                Margin = new Thickness(24, 20, 24, 16),
                LastChildFill = true,
                Children =
                {
                    new Border { Child = buttons, [DockPanel.DockProperty] = Dock.Bottom, Margin = new Thickness(0, 18, 0, 0) },
                    header
                }
            };

            await dlg.ShowDialog(this);
            return result;
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            if (_isDirty && _scene != null)
            {
                e.Cancel = true;
                var answer = await AskSaveIfDirtyAsync();
                if (answer == SavePromptResult.Cancel) return;
                if (answer == SavePromptResult.Save)
                {
                    if (string.IsNullOrEmpty(_projectPath))
                        MenuFileSaveAs_Click(null, new RoutedEventArgs());
                    else
                        await SaveAsync(_projectPath);
                    if (_isDirty) return;
                }
                _isDirty = false;
                Close();
            }
            else
            {
                try { DisperSim3D.Core.TempManager.PurgeOlderThan(TimeSpan.FromDays(7)); }
                catch { }
                base.OnClosing(e);
            }
        }

        private void RefreshWorkDirStatus()
        {
            try
            {
                long size = DisperSim3D.Core.TempManager.GetWorkDirSize();
                StatusWorkDir.Text = $"Work: {DisperSim3D.Core.TempManager.FormatBytes(size)}";
            }
            catch
            {
                StatusWorkDir.Text = "";
            }
        }

        // ── File menu ────────────────────────────────────────────────────────
        private async void MenuFileNew_Click(object? sender, RoutedEventArgs e)
        {
            if (_scene != null && _isDirty)
            {
                var answer = await AskSaveIfDirtyAsync();
                if (answer == SavePromptResult.Cancel) return;
                if (answer == SavePromptResult.Save)
                {
                    if (string.IsNullOrEmpty(_projectPath))
                        MenuFileSaveAs_Click(sender, e);
                    else
                        await SaveAsync(_projectPath);
                    if (_isDirty) return;
                }
            }

            _scene = new Scene3D();
            _projectPath = null;
            _isDirty = false;
            RebuildTree();
            Viewport3D.PopulateScene(_scene);
            StatusText.Text = "New project";
        }

        private async void MenuFileOpen_Click(object? sender, RoutedEventArgs e)
        {
            if (_scene != null && _isDirty)
            {
                var answer = await AskSaveIfDirtyAsync();
                if (answer == SavePromptResult.Cancel) return;
                if (answer == SavePromptResult.Save)
                {
                    if (string.IsNullOrEmpty(_projectPath))
                        MenuFileSaveAs_Click(sender, e);
                    else
                        await SaveAsync(_projectPath);
                    if (_isDirty) return;
                }
            }

            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open DisperSim 3D project",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("DisperSim 3D project")
                    {
                        Patterns = new[] { "*.dsproj", "*.xml" }
                    },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                }
            });
            if (files == null || files.Count == 0) return;

            string path = files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
            await LoadProjectAsync(path);
        }

        private async Task LoadProjectAsync(string path)
        {
            StatusText.Text = "Loading " + Path.GetFileName(path) + "...";
            try
            {
                Scene3D loaded = await Task.Run(() => SceneFileLoader.Load(path));
                _scene = loaded;
                _projectPath = path;
                _isDirty = false;
                RebuildTree();
                Viewport3D.PopulateScene(_scene);
                StatusText.Text = "Loaded " + Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                _scene = null;
                _projectPath = null;
                RebuildTree();
                Viewport3D.PopulateScene(null);
                StatusText.Text = "Load failed";
                Inspector.SetTarget(null,
                    "Failed to load " + path + "\n" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private async void MenuFileSave_Click(object? sender, RoutedEventArgs e)
        {
            if (_scene is null) return;
            if (string.IsNullOrEmpty(_projectPath))
            {
                MenuFileSaveAs_Click(sender, e);
                return;
            }
            await SaveAsync(_projectPath);
        }

        private async void MenuFileSaveAs_Click(object? sender, RoutedEventArgs e)
        {
            if (_scene is null) return;
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save DisperSim 3D project as",
                SuggestedFileName = _projectPath is null
                    ? "Untitled.dsproj"
                    : Path.GetFileName(_projectPath),
                DefaultExtension = "dsproj",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("DisperSim 3D project (.dsproj)")
                    {
                        Patterns = new[] { "*.dsproj" }
                    },
                    new FilePickerFileType("Legacy XML (.xml)")
                    {
                        Patterns = new[] { "*.xml" }
                    }
                }
            });
            if (file is null) return;

            string path = file.TryGetLocalPath() ?? file.Path.LocalPath;
            await SaveAsync(path);
        }

        private async Task SaveAsync(string path)
        {
            if (_scene is null) return;
            StatusText.Text = "Saving " + Path.GetFileName(path) + "...";
            try
            {
                await Task.Run(() =>
                {
                    SceneFileSaver.Save(_scene, path,
                        (step, frac, done) =>
                        {
                            // Marshal status updates back to the UI thread; fire
                            // and forget — the next Save call replaces text anyway.
                            Dispatcher.UIThread.Post(() =>
                            {
                                StatusText.Text = step;
                            });
                        },
                        // Bundle writer hook — needs the UI.Wpf project for the
                        // .dsproj zip workflow. The Avalonia smoke runs without
                        // it, so plain .xml saves work everywhere; .dsproj bundles
                        // will be supported once ProjectBundle.Save is decoupled
                        // from the WinForms progress reporter (it already is —
                        // we just call it directly).
                        bundleWriter: (p, sc, doc, prog) =>
                            ProjectBundle.Save(p, sc, doc, prog));
                });
                _projectPath = path;
                _isDirty = false;
                UpdateTitle();
                StatusText.Text = "Saved " + Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Save failed";
                Inspector.SetTarget(null,
                    "Failed to save " + path + "\n" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void MenuFileExit_Click(object? sender, RoutedEventArgs e) => Close();

        // ── View menu ────────────────────────────────────────────────────────
        private void MenuViewRefresh_Click(object? sender, RoutedEventArgs e)
        {
            RebuildTree();
            StatusText.Text = "Tree refreshed";
        }

        private async void MenuViewSavePreset_Click(object? sender, RoutedEventArgs e)
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }

            string defaultName = "Camera " + (_scene.CameraPresets.Count + 1);
            var dlg = new Window
            {
                Title = "Save Camera Preset",
                Width = 360, Height = 140,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SystemDecorations = SystemDecorations.BorderOnly
            };

            string? resultName = null;
            var tb = new TextBox { Text = defaultName, Margin = new Thickness(0, 0, 0, 12) };
            var btnOk = new Button { Content = "OK", Width = 80 };
            var btnCancel = new Button { Content = "Cancel", Width = 80 };
            btnOk.Click += (_, _) => { resultName = tb.Text; dlg.Close(); };
            btnCancel.Click += (_, _) => dlg.Close();

            var buttons = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8,
                Children = { btnCancel, btnOk }
            };

            dlg.Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = "Preset name:", Margin = new Thickness(0, 0, 0, 4) },
                    tb,
                    buttons
                }
            };

            await dlg.ShowDialog(this);
            if (string.IsNullOrWhiteSpace(resultName)) return;

            var preset = Viewport3D.SaveCameraPreset(resultName.Trim());
            _scene.CameraPresets.Add(preset);
            MarkDirtyAndRefresh("Saved camera preset: " + preset.Name);
        }

        private void MenuViewResetCam_Click(object? sender, RoutedEventArgs e)
        {
            Viewport3D.ResetView();
            StatusText.Text = "Camera reset to default view";
        }

        private void ApplyCameraPreset(ProjectTreeNode node)
        {
            if (node.Tag is not CameraPreset preset) return;
            Viewport3D.ApplyCameraPreset(preset);
            StatusText.Text = "Applied camera preset: " + preset.Name;
        }

        // ── Tools menu ───────────────────────────────────────────────────────
        private async void MenuToolsAddSource_Click(object? sender, RoutedEventArgs e)
            => await AddReleaseSourceAsync();

        private async void MenuToolsAddFire_Click(object? sender, RoutedEventArgs e)
            => await AddFireSourceAsync();

        private async void MenuToolsAddView_Click(object? sender, RoutedEventArgs e)
            => await AddViewAsync();

        private async void MenuToolsThresholds_Click(object? sender, RoutedEventArgs e)
            => await EditThresholdsAsync();

        private async void MenuToolsAddSim_Click(object? sender, RoutedEventArgs e)
            => await AddSimulationAsync();

        private async void MenuToolsCfd_Click(object? sender, RoutedEventArgs e)
            => await EditCfdSettingsAsync();

        private async void MenuToolsAddStudy_Click(object? sender, RoutedEventArgs e)
            => await AddDispersionStudyAsync();

        private async void MenuToolsTransient_Click(object? sender, RoutedEventArgs e)
            => await EditTransientWindAsync();

        private async void MenuToolsExceedance_Click(object? sender, RoutedEventArgs e)
            => await ShowExceedanceAsync();

        private async void MenuToolsDetectorResults_Click(object? sender, RoutedEventArgs e)
            => await ShowDetectorResultsAsync();

        private async void MenuToolsWindRose_Click(object? sender, RoutedEventArgs e)
            => await EditWindRoseAsync();

        private async void MenuToolsBatchExport_Click(object? sender, RoutedEventArgs e)
            => await ShowBatchExportAsync();

        private async void MenuToolsValidate_Click(object? sender, RoutedEventArgs e)
            => await ShowValidationAsync();

        private async void MenuToolsOptimize_Click(object? sender, RoutedEventArgs e)
            => await ShowDetectorOptimizationAsync();

        private async void MenuToolsAllocate_Click(object? sender, RoutedEventArgs e)
            => await ShowDetectorAllocationAsync();

        private async void MenuToolsDwsim_Click(object? sender, RoutedEventArgs e)
            => await EditDwsimSettingsAsync();

        private async void MenuToolsWindFieldMgr_Click(object? sender, RoutedEventArgs e)
            => await ShowWindFieldManagerAsync();

        private async void MenuToolsScenarioMgr_Click(object? sender, RoutedEventArgs e)
            => await ShowScenarioManagerAsync();

        private async void MenuToolsGpu_Click(object? sender, RoutedEventArgs e)
            => await ShowGpuSettingsAsync();

        private async void MenuToolsBuildMix_Click(object? sender, RoutedEventArgs e)
            => await BuildMixtureFromDwsimAsync();

        private async void MenuToolsWorkDir_Click(object? sender, RoutedEventArgs e)
        {
            var currentDir = DisperSim3D.Core.AppSettings.Instance.WorkingDirectory;
            long currentSize = DisperSim3D.Core.TempManager.GetWorkDirSize();

            var dlg = new Window
            {
                Title = "Working Directory",
                Width = 560, Height = 220,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SystemDecorations = SystemDecorations.BorderOnly,
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 250))
            };

            var txtPath = new TextBox
            {
                Text = currentDir,
                Watermark = "Path to working directory...",
                MinHeight = 32
            };
            var btnBrowse = new Button
            {
                Content = "Browse...", MinWidth = 80, Height = 32,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
            };
            btnBrowse.Click += async (_, _) =>
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(
                    new global::Avalonia.Platform.Storage.FolderPickerOpenOptions
                    {
                        Title = "Choose Working Directory",
                        AllowMultiple = false
                    });
                if (folders.Count > 0)
                    txtPath.Text = folders[0].Path.LocalPath;
            };

            bool saved = false;
            var btnCancel = new Button
            {
                Content = "Cancel", MinWidth = 90, Height = 32,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
            };
            var btnOk = new Button
            {
                Content = "OK", MinWidth = 90, Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                Foreground = Brushes.White, FontWeight = FontWeight.SemiBold,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
            };
            btnCancel.Click += (_, _) => dlg.Close();
            btnOk.Click += (_, _) => { saved = true; dlg.Close(); };

            var pathRow = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(btnBrowse, global::Avalonia.Controls.Dock.Right);
            pathRow.Children.Add(btnBrowse);
            pathRow.Children.Add(txtPath);

            dlg.Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"All simulation working files will be stored here.\nCurrent size: {DisperSim3D.Core.TempManager.FormatBytes(currentSize)}",
                        FontSize = 13, TextWrapping = TextWrapping.Wrap
                    },
                    pathRow,
                    new StackPanel
                    {
                        Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { btnCancel, btnOk }
                    }
                }
            };

            await dlg.ShowDialog(this);

            if (saved && !string.IsNullOrWhiteSpace(txtPath.Text))
            {
                var newDir = txtPath.Text.Trim();
                DisperSim3D.Core.AppSettings.Instance.WorkingDirectory = newDir;
                DisperSim3D.Core.AppSettings.Instance.Save();
                try { System.IO.Directory.CreateDirectory(newDir); } catch { }
                RefreshWorkDirStatus();
                StatusText.Text = $"Working directory set to: {newDir}";
            }
        }

        private async void MenuToolsCleanTemp_Click(object? sender, RoutedEventArgs e)
        {
            var (totalBytes, entryCount, byCategory) = DisperSim3D.Core.TempManager.GetSummary();

            if (totalBytes == 0)
            {
                var emptyDlg = new Window
                {
                    Title = "Clean Temp Files",
                    Width = 380, Height = 150,
                    CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    SystemDecorations = SystemDecorations.BorderOnly,
                    Background = new SolidColorBrush(Color.FromRgb(250, 250, 250))
                };
                var btnOkEmpty = new Button
                {
                    Content = "OK", MinWidth = 90, Height = 32,
                    HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
                };
                btnOkEmpty.Click += (_, _) => emptyDlg.Close();
                emptyDlg.Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock { Text = "No temporary files found.", FontSize = 14 },
                        new StackPanel
                        {
                            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                            Children = { btnOkEmpty }
                        }
                    }
                };
                await emptyDlg.ShowDialog(this);
                return;
            }

            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"DisperSim 3D is using {DisperSim3D.Core.TempManager.FormatBytes(totalBytes)} in {entryCount} temp entries:\n");
            foreach (var kv in byCategory.OrderByDescending(x => x.Value))
                lines.AppendLine($"  {kv.Key}: {DisperSim3D.Core.TempManager.FormatBytes(kv.Value)}");
            lines.AppendLine($"\nDelete all temp files older than 24 hours?");

            var dlg = new Window
            {
                Title = "Clean Temp Files",
                Width = 500, Height = 320,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SystemDecorations = SystemDecorations.BorderOnly,
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 250))
            };

            bool confirmed = false;
            bool deleteAll = false;

            var btnCancel = new Button
            {
                Content = "Cancel", MinWidth = 90, Height = 32,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
            };
            var btnClean = new Button
            {
                Content = "Clean (> 24h)", MinWidth = 120, Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                Foreground = Brushes.White, FontWeight = FontWeight.SemiBold,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
            };
            var btnAll = new Button
            {
                Content = "Delete All", MinWidth = 100, Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(200, 50, 50)),
                Foreground = Brushes.White, FontWeight = FontWeight.SemiBold,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
            };

            btnCancel.Click += (_, _) => dlg.Close();
            btnClean.Click += (_, _) => { confirmed = true; dlg.Close(); };
            btnAll.Click += (_, _) => { confirmed = true; deleteAll = true; dlg.Close(); };

            dlg.Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = lines.ToString(),
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { btnCancel, btnAll, btnClean }
                    }
                }
            };

            await dlg.ShowDialog(this);

            if (!confirmed) return;

            var (deleted, freed) = deleteAll
                ? DisperSim3D.Core.TempManager.PurgeAll()
                : DisperSim3D.Core.TempManager.PurgeOlderThan(TimeSpan.FromHours(24));

            var doneDlg = new Window
            {
                Title = "Clean Temp Files",
                Width = 380, Height = 150,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SystemDecorations = SystemDecorations.BorderOnly,
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 250))
            };
            var btnOk = new Button
            {
                Content = "OK", MinWidth = 90, Height = 32,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
            };
            btnOk.Click += (_, _) => doneDlg.Close();
            doneDlg.Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Deleted {deleted} entries, freed {DisperSim3D.Core.TempManager.FormatBytes(freed)}.",
                        FontSize = 14
                    },
                    new StackPanel
                    {
                        Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                        Children = { btnOk }
                    }
                }
            };
            await doneDlg.ShowDialog(this);
        }

        // ── Context menu wiring ──────────────────────────────────────────────
        // Shared flyout instance reused across right-clicks. Built once,
        // populated dynamically per click.
        private readonly MenuFlyout _treeFlyout = new MenuFlyout();

        /// <summary>Right-click / context-menu request on the tree. Avalonia
        /// 12's <c>ContextFlyout</c> attached to a parent control does NOT
        /// propagate to <c>TreeViewItem</c> children (the items consume the
        /// right-click), so we wire this manually via the bubbling
        /// <c>ContextRequested</c> event and call
        /// <c>flyout.ShowAt(treeViewItem)</c>.</summary>
        private void ProjectTree_ContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            if (_scene is null) return;

            // Walk up from the event source to find the TreeViewItem the user
            // right-clicked on (the source can be the row's TextBlock, the
            // Border, the chevron, etc.).
            var tvi = FindAncestor<TreeViewItem>(e.Source as Visual);
            if (tvi?.DataContext is not ProjectTreeNode node) return;

            // Force selection so the inspector pane updates AND so the menu
            // logic (which reads ProjectTree.SelectedItem indirectly through
            // the node we just resolved) is consistent.
            tvi.IsSelected = true;

            BuildTreeMenu(node);
            if (_treeFlyout.Items.Count == 0) return;     // suppress empty flyout
            _treeFlyout.ShowAt(tvi);
            e.Handled = true;
        }

        /// <summary>Double-click on a tree item reopens its editor dialog
        /// (when one exists), matching the WinForms muscle-memory of
        /// double-clicking a list row to edit.</summary>
        private void ProjectTree_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (_scene is null) return;
            var tvi = FindAncestor<TreeViewItem>(e.Source as Visual);
            if (tvi?.DataContext is not ProjectTreeNode node) return;

            int colon = node.NodeId.IndexOf(':');
            string kind = colon > 0 ? node.NodeId.Substring(0, colon) : node.NodeId;
            switch (kind)
            {
                case "gas":  _ = EditGasAsync(node); break;
                case "mon":  _ = EditMonitorAsync(node); break;
                case "fire": _ = EditFireSourceAsync(node); break;
                case "src":  _ = EditHighPressureAsync(node); break;
                case "sim":  _ = EditSimulationAsync(node); break;
                case "study":_ = EditDispersionStudyAsync(node); break;
                case "alloc":_ = EditDetectorAllocationAsync(node); break;
                case "windrose": _ = EditWindRoseAsync(); break;
                case "cam":  ApplyCameraPreset(node); break;
                // Other types don't have edit dialogs yet — fall through
                // (the inspector pane already shows their properties).
            }
        }

        private void BuildTreeMenu(ProjectTreeNode node)
        {
            _treeFlyout.Items.Clear();
            string id = node.NodeId;

            // Section nodes — id is the section key, no colon. They get an
            // "Add child…" entry that opens the matching dialog.
            switch (id)
            {
                case "sources":
                    AddItem("Add Release Source…", "mdi-water-plus-outline",
                        (_, _) => _ = AddReleaseSourceAsync());
                    return;
                case "fires":
                    AddItem("Add Fire Source…", "mdi-fire",
                        (_, _) => _ = AddFireSourceAsync());
                    return;
                case "gases":
                    AddItem("Add Gas…", "mdi-gas-cylinder",
                        (_, _) => _ = AddGasAsync());
                    return;
                case "monitors":
                    AddItem("Add Monitor…", "mdi-circle-medium",
                        (_, _) => _ = AddMonitorAsync());
                    return;
                case "views":
                    AddItem("Add View…", "mdi-image-plus-outline",
                        (_, _) => _ = AddViewAsync());
                    return;
                case "simulations":
                    AddItem("Add Simulation…", "mdi-play-circle-outline",
                        (_, _) => _ = AddSimulationAsync());
                    return;
                case "studies":
                    AddItem("Add Dispersion Study…", "mdi-chart-line",
                        (_, _) => _ = AddDispersionStudyAsync());
                    return;
                case "winds":
                    AddItem("Manage Wind Fields…", "mdi-weather-windy",
                        (_, _) => _ = ShowWindFieldManagerAsync());
                    return;
                case "allocations":
                    AddItem("New Detector Allocation…", "mdi-target",
                        (_, _) => _ = ShowDetectorAllocationAsync());
                    return;
                case "cameras":
                    AddItem("Save Camera Preset…", "mdi-camera-plus-outline",
                        (_, _) => MenuViewSavePreset_Click(null, new RoutedEventArgs()));
                    return;
                case "geometry":
                    foreach (var preset in BuiltinAssetResolver.DecorationPresets)
                    {
                        var p = preset;
                        AddItem("Add " + p.Label, p.Icon,
                            (_, _) => AddBuiltinDecoration(p));
                    }
                    AddItem("Add from OBJ File…", "mdi-file-import-outline",
                        (_, _) => _ = AddDecorationFromFileAsync());
                    return;
                case "windrose":
                    // The wind-rose section node IS the rose — there is at
                    // most one per scene. Context menu opens the editor.
                    AddItem("Edit Wind Rose…", "mdi-compass-outline",
                        (_, _) => _ = EditWindRoseAsync());
                    return;
            }

            // Leaf nodes — id has the form "kind:guid". The prefix decides
            // which dialog reopens for editing and which list we delete from.
            int colon = id.IndexOf(':');
            string kind = colon > 0 ? id.Substring(0, colon) : id;
            switch (kind)
            {
                case "src":
                    AddItem("Show in inspector", "mdi-eye-outline",
                        (_, _) => Inspector.SetTarget(node.Tag));
                    AddItem("Edit HP Leak Parameters…", "mdi-gauge",
                        (_, _) => _ = EditHighPressureAsync(node));
                    AddItem("Edit Equipment Inventory…", "mdi-toolbox-outline",
                        (_, _) => _ = EditEquipmentInventoryAsync(node));
                    AddItem("Delete", "mdi-trash-can-outline",
                        (_, _) => DeleteFromList(_scene!.TopLevelSources, node));
                    return;
                case "fire":
                    AddItem("Edit Fire Source…", "mdi-pencil-outline",
                        (_, _) => _ = EditFireSourceAsync(node));
                    AddItem("Delete", "mdi-trash-can-outline",
                        (_, _) => DeleteFireSource(node));
                    return;
                case "gas":
                    AddItem("Edit Gas…", "mdi-pencil-outline",
                        (_, _) => _ = EditGasAsync(node));
                    AddItem("Edit Mixture Components…", "mdi-test-tube",
                        (_, _) => _ = EditGasMixtureAsync(node));
                    AddItem("Delete", "mdi-trash-can-outline",
                        (_, _) => DeleteFromList(_scene!.GasLibrary, node));
                    return;
                case "mon":
                    AddItem("Edit Monitor…", "mdi-pencil-outline",
                        (_, _) => _ = EditMonitorAsync(node));
                    AddItem("Delete", "mdi-trash-can-outline",
                        (_, _) => DeleteFromList(_scene!.MonitorPoints, node));
                    return;
                case "det":
                    AddItem("Delete", "mdi-trash-can-outline",
                        (_, _) => DeleteFromList(_scene!.GasDetectors, node));
                    return;
                case "wind":
                    AddItem("Manage…", "mdi-pencil-outline",
                        (_, _) => _ = ShowWindFieldManagerForNodeAsync(node));
                    AddItem("Delete", "mdi-trash-can-outline",
                        (_, _) => DeleteFromList(_scene!.WindFieldScenarios, node));
                    return;
                case "sim":
                    AddItem("Run", "mdi-play",
                        (_, _) => RunSimulation(node));
                    AddItem("Configure…", "mdi-pencil-outline",
                        (_, _) => _ = EditSimulationAsync(node));
                    AddItem("Simulation Manager…", "mdi-playlist-play",
                        (_, _) => ShowSimulationManager());
                    AddItem("Delete", "mdi-trash-can-outline",
                        (_, _) => DeleteFromList(_scene!.Simulations, node));
                    return;
                case "study":
                    AddItem("Edit Study…", "mdi-pencil-outline",
                        (_, _) => _ = EditDispersionStudyAsync(node));
                    AddItem("Delete", "mdi-trash-can-outline",
                        (_, _) => DeleteFromList(_scene!.DispersionStudies, node));
                    return;
                case "alloc":
                    AddItem("Edit…", "mdi-pencil-outline",
                        (_, _) => _ = EditDetectorAllocationAsync(node));
                    AddItem("Delete", "mdi-trash-can-outline",
                        (_, _) => DeleteFromList(_scene!.DetectorAllocations, node));
                    return;
                case "cam":
                    AddItem("Apply Preset", "mdi-camera-outline",
                        (_, _) => ApplyCameraPreset(node));
                    AddItem("Delete", "mdi-trash-can-outline",
                        (_, _) => DeleteFromList(_scene!.CameraPresets, node));
                    return;
                case "view":
                    AddItem("Show in inspector", "mdi-eye-outline",
                        (_, _) => Inspector.SetTarget(node.Tag));
                    AddItem("Delete", "mdi-trash-can-outline",
                        (_, _) => DeleteFromList(_scene!.Views, node));
                    return;
                case "deco":
                    AddItem("Delete", "mdi-trash-can-outline",
                        (_, _) => DeleteFromList(_scene!.Decorations, node));
                    return;
            }

            void AddItem(string label, string iconName, EventHandler<RoutedEventArgs> handler)
            {
                // MenuItem.Icon takes any Control as content (Avalonia 12's
                // Fluent template renders it left of the header). Wrap the
                // Projektanker Icon control rather than the attached
                // property, which only targets ContentControl-derived hosts.
                var mi = new MenuItem
                {
                    Header = label,
                    Icon = new Projektanker.Icons.Avalonia.Icon { Value = iconName }
                };
                mi.Click += handler;
                _treeFlyout.Items.Add(mi);
            }
        }

        private static T? FindAncestor<T>(Visual? start) where T : Visual
        {
            for (var v = start; v != null; v = v.GetVisualParent())
                if (v is T t) return t;
            return null;
        }

        // ── Add/edit/delete operations ───────────────────────────────────────
        private async Task AddReleaseSourceAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }

            StatusText.Text = "Click on a surface to place the source (right-click to cancel)...";
            var tcs = new TaskCompletionSource<(System.Numerics.Vector3 pos, System.Numerics.Vector3 normal)?>();

            void OnPick(System.Numerics.Vector3 pos, System.Numerics.Vector3 normal)
            {
                tcs.TrySetResult((pos, normal));
            }
            void OnCancel()
            {
                if (!tcs.Task.IsCompleted) tcs.TrySetResult(null);
            }

            Viewport3D.PickCompleted += OnPick;
            EventHandler<global::Avalonia.Input.KeyEventArgs>? keyHandler = null;
            keyHandler = (s, e) =>
            {
                if (e.Key == global::Avalonia.Input.Key.Escape)
                {
                    Viewport3D.ExitPickMode();
                    OnCancel();
                }
            };
            KeyDown += keyHandler;

            Viewport3D.EnterPickMode();

            var result = await tcs.Task;

            Viewport3D.PickCompleted -= OnPick;
            KeyDown -= keyHandler;

            if (result == null)
            {
                StatusText.Text = "Source placement cancelled.";
                return;
            }

            var (hitPos, hitNormal) = result.Value;
            var (az, el) = NormalToAzimuthElevation(hitNormal);

            double windDirSeed = 0;
            if (_scene.WindFieldScenarios?.Count > 0 && _scene.WindFieldScenarios[0].Meteo != null)
                windDirSeed = _scene.WindFieldScenarios[0].Meteo.WindDirectionDeg;
            else if (_scene.GeneralSettings?.DefaultMeteo != null)
                windDirSeed = _scene.GeneralSettings.DefaultMeteo.WindDirectionDeg;

            var dlg = new DispersionSourceDialog(windDirSeed, az, el);
            if (!await dlg.ShowDialog<bool>(this)) return;

            var src = dlg.BuildSource();
            src.Position = new DisperSim3D.Geometry.Point3D(hitPos.X, hitPos.Y, hitPos.Z);
            _scene.TopLevelSources.Add(src);
            MarkDirtyAndRefresh("Added source: " + src.Name);
        }

        private static (double azimuthDeg, double elevationDeg) NormalToAzimuthElevation(
            System.Numerics.Vector3 n)
        {
            n = System.Numerics.Vector3.Normalize(n);
            double elRad = Math.Asin(Math.Clamp(n.Z, -1.0, 1.0));
            double azRad = Math.Atan2(n.X, n.Y);
            double azDeg = azRad * 180.0 / Math.PI;
            if (azDeg < 0) azDeg += 360.0;
            double elDeg = elRad * 180.0 / Math.PI;
            return (azDeg, elDeg);
        }

        private async Task AddGasAsync()
        {
            if (_scene is null) return;
            var dlg = new GasLibraryItemDialog();
            if (!await dlg.ShowDialog<bool>(this)) return;
            _scene.GasLibrary.Add(dlg.Result);
            MarkDirtyAndRefresh("Added gas: " + dlg.Result.Name);
        }

        private async Task EditGasAsync(ProjectTreeNode node)
        {
            if (_scene is null || node.Tag is not GasLibraryItem g) return;
            var dlg = new GasLibraryItemDialog(g);
            if (!await dlg.ShowDialog<bool>(this)) return;
            MarkDirtyAndRefresh("Updated gas: " + dlg.Result.Name);
        }

        private async Task AddMonitorAsync()
        {
            if (_scene is null) return;
            var dlg = new MonitorPointDialog();
            if (!await dlg.ShowDialog<bool>(this)) return;
            _scene.MonitorPoints.Add(dlg.BuildMonitor());
            MarkDirtyAndRefresh("Added monitor: " + dlg.MonitorName);
        }

        private async Task EditMonitorAsync(ProjectTreeNode node)
        {
            if (_scene is null || node.Tag is not MonitorPoint3D m) return;
            var dlg = new MonitorPointDialog(m.Name, m.Position.X, m.Position.Y, m.Position.Z);
            if (!await dlg.ShowDialog<bool>(this)) return;
            m.Name = dlg.MonitorName;
            m.Position = dlg.MonitorPosition;
            MarkDirtyAndRefresh("Updated monitor: " + m.Name);
        }

        // ── Fire sources ─────────────────────────────────────────────────────
        private async Task AddFireSourceAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            // Scene3D guarantees a FireScenario in its constructor, but a
            // legacy .dsproj could conceivably arrive without one — defensive
            // re-create here so the Add button always works.
            _scene.FireScenario ??= new FireScenario();

            var dlg = new FireSourceDialog();
            if (!await dlg.ShowDialog<bool>(this)) return;
            _scene.FireScenario.Sources.Add(dlg.Result);
            MarkDirtyAndRefresh("Added fire source: " + dlg.Result.Name);
        }

        private async Task EditFireSourceAsync(ProjectTreeNode node)
        {
            if (_scene is null || node.Tag is not FireSource fire) return;
            var dlg = new FireSourceDialog(fire);
            if (!await dlg.ShowDialog<bool>(this)) return;
            // FireSourceDialog returns a fresh FireSource; copy fields back
            // onto the existing instance so anything holding a reference to
            // it (e.g. a future fire renderer) sees the updated values.
            fire.Name                = dlg.Result.Name;
            fire.MassFlowRateKgS     = dlg.Result.MassFlowRateKgS;
            fire.OrificeDiameterM    = dlg.Result.OrificeDiameterM;
            fire.HeatOfCombustionJKg = dlg.Result.HeatOfCombustionJKg;
            fire.RadiativeFraction   = dlg.Result.RadiativeFraction;
            fire.IsPoolFire          = dlg.Result.IsPoolFire;
            fire.PoolDiameterM       = dlg.Result.PoolDiameterM;
            fire.PoolBurnRateKgM2S   = dlg.Result.PoolBurnRateKgM2S;
            MarkDirtyAndRefresh("Updated fire source: " + fire.Name);
        }

        /// <summary>Fire sources live inside <c>FireScenario.Sources</c>, not
        /// at the top level of <c>Scene3D</c>, so we can't reuse the generic
        /// <see cref="DeleteFromList{T}"/> path (it would need the parent
        /// collection passed in but the FireScenario could be null).</summary>
        private void DeleteFireSource(ProjectTreeNode node)
        {
            if (_scene?.FireScenario?.Sources is null) return;
            DeleteFromList(_scene.FireScenario.Sources, node);
        }

        // ── Views ────────────────────────────────────────────────────────────
        private async Task AddViewAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            var dlg = new ViewEditorDialog(_scene);
            if (!await dlg.ShowDialog<bool>(this) || dlg.Result is null) return;
            _scene.Views.Add(dlg.Result);
            MarkDirtyAndRefresh("Added view: " + dlg.Result.Name);
        }

        // ── Dispersion thresholds ────────────────────────────────────────────
        private async Task EditThresholdsAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            // Edit the active scenario's threshold list. If the project has no
            // scenarios yet (legacy file), bail with a status hint rather than
            // silently no-op'ing — there's nowhere to store the result.
            var scenario = _scene.DispersionScenario;
            if (scenario is null)
            {
                StatusText.Text = "No active dispersion scenario to edit thresholds for.";
                return;
            }

            var dlg = new ThresholdsDialog(scenario.Thresholds);
            if (!await dlg.ShowDialog<bool>(this) || dlg.Result is null) return;
            scenario.Thresholds = dlg.Result;
            MarkDirtyAndRefresh("Updated dispersion thresholds (" + dlg.Result.Count + ")");
        }

        // ── High-pressure leak parameters (on an existing source) ────────────
        private async Task EditHighPressureAsync(ProjectTreeNode node)
        {
            if (_scene is null || node.Tag is not ReleaseSource3D src) return;
            // Seed the dialog with the source's existing HP leak params (or a
            // default-constructed one for sources that aren't HP yet) so the
            // user can convert a regular source into an HP one in-place.
            var dlg = new HighPressureSourceDialog(src.HighPressureLeak);
            if (!await dlg.ShowDialog<bool>(this)) return;
            src.HighPressureLeak = dlg.Result;
            MarkDirtyAndRefresh("Updated HP leak params: " + src.Name);
        }

        // ── Simulations ──────────────────────────────────────────────────────
        private async Task AddSimulationAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            if (_scene.TopLevelSources.Count == 0)
            {
                StatusText.Text = "Add a release source before creating a simulation.";
                return;
            }
            if (_scene.WindFieldScenarios.Count == 0)
            {
                StatusText.Text = "Add a wind field scenario before creating a simulation.";
                return;
            }

            var dlg = new SimulationEditorDialog(_scene);
            if (!await dlg.ShowDialog<bool>(this) || dlg.Result is null) return;
            _scene.Simulations.Add(dlg.Result);
            MarkDirtyAndRefresh("Added simulation: " + dlg.Result.Name);
        }

        private async Task EditSimulationAsync(ProjectTreeNode node)
        {
            if (_scene is null || node.Tag is not Simulation sim) return;
            var dlg = new SimulationEditorDialog(_scene, sim);
            if (!await dlg.ShowDialog<bool>(this)) return;
            MarkDirtyAndRefresh("Updated simulation: " + sim.Name);
        }

        // ── Decoration presets ────────────────────────────────────────────────

        private void AddBuiltinDecoration(DecorationPreset preset)
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            var resolved = BuiltinAssetResolver.Resolve(preset.ObjKey);
            if (!System.IO.File.Exists(resolved))
            {
                StatusText.Text = $"Asset not found: {preset.Label}. Check the 3D Props folder.";
                return;
            }
            var deco = new DisperSim3D.Models.Decoration3D
            {
                Name = preset.Label,
                FilePath = resolved,
                Scale = preset.DefaultScale
            };
            _scene.Decorations.Add(deco);
            MarkDirtyAndRefresh("Added decoration: " + preset.Label);
        }

        private async Task AddDecorationFromFileAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select 3D Model",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("3D Models") { Patterns = new[] { "*.obj", "*.stl", "*.3ds" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });
            if (files.Count == 0) return;
            var path = files[0].Path.LocalPath;
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var deco = new DisperSim3D.Models.Decoration3D
            {
                Name = name,
                FilePath = path
            };
            _scene.Decorations.Add(deco);
            MarkDirtyAndRefresh("Added decoration: " + name);
        }

        // ── Simulation execution ─────────────────────────────────────────────

        private void RunSimulation(ProjectTreeNode node)
        {
            if (_scene is null || node.Tag is not Simulation sim) return;

            var scenario = _scene.DispersionScenario;
            if (scenario == null || scenario.Sources.Count == 0)
            {
                StatusText.Text = "No sources defined — cannot run simulation.";
                return;
            }

            var config = sim.SnapshotCfdConfig ?? scenario.CfdConfig ?? new CfdConfiguration();

            var obstacles = new List<BoundingBox>();
            foreach (var deco in _scene.Decorations)
            {
                if (deco.BoundingBox != null)
                    obstacles.Add(deco.BoundingBox);
            }

            Dictionary<string, double[]>? hpProfiles = null;
            foreach (var src in scenario.Sources)
            {
                if (src.HighPressureLeak != null)
                {
                    hpProfiles ??= new Dictionary<string, double[]>();
                    var profile = HighPressureLeakModel.ComputeBlowdownProfile(
                        src.HighPressureLeak, scenario.SimulationDurationS, scenario.TimeStepS);
                    hpProfiles[src.Id] = profile;
                }
            }

            SimulationManager.Enqueue(
                scenario, sim.SolverType, config, _scene, null,
                obstacles, hpProfiles);

            ShowSimulationManager();
            StatusText.Text = $"Simulation enqueued: {sim.Name}";
        }

        private void MenuToolsSimManager_Click(object? sender, RoutedEventArgs e)
            => ShowSimulationManager();

        private void ShowSimulationManager()
        {
            if (_simManagerPanel == null)
            {
                _simManagerPanel = new SimulationManagerPanel(SimulationManager);
                _simManagerPanel.PlayResultRequested += (_, entry) =>
                {
                    if (entry.Tag is OpenFoamResult result)
                    {
                        var fakeSim = new Simulation
                        {
                            Name = entry.Name ?? "Simulation",
                            Status = SimulationStatus.Completed,
                            CasePath = entry.CasePath
                        };
                        fakeSim.ResultTag = result;
                        InitPlayback(fakeSim);
                    }
                };
                SimManagerHost.Child = _simManagerPanel;
            }

            SimManagerHost.IsVisible = true;
        }

        private void OnSimManagerJobCompleted(object? sender, Core.SimulationJob job)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (job.Status == Core.SimulationJobStatus.Completed)
                {
                    StatusText.Text = $"Simulation completed: {job.Name}";

                    if (job.ResultEntry != null && _scene != null)
                    {
                        var sim = _scene.Simulations.FirstOrDefault(s =>
                            s.SolverType == job.SolverType && s.Name == job.Scenario?.Name);
                        if (sim != null)
                        {
                            sim.Status = SimulationStatus.Completed;
                            sim.ResultTag = job.ResultEntry.Tag;
                            sim.CasePath = job.ResultEntry.CasePath ?? "";
                            RebuildTree();
                        }
                    }
                }
                else if (job.Status == Core.SimulationJobStatus.Failed)
                {
                    StatusText.Text = $"Simulation failed: {job.Name}";
                }
            });
        }

        // ── Gas mixture editor (per gas-library item) ────────────────────────
        private async Task EditGasMixtureAsync(ProjectTreeNode node)
        {
            if (_scene is null || node.Tag is not GasLibraryItem g) return;
            // Flip Kind to Mixture if the user is opening this on a Pure gas —
            // that's the operation the dialog is meant for.
            g.Mixture ??= new GasMixture();

            var dlg = new GasMixtureDialog(g.Mixture);
            if (!await dlg.ShowDialog<bool>(this)) return;
            g.Mixture = dlg.Result;
            g.Kind = GasLibraryItemKind.Mixture;
            MarkDirtyAndRefresh("Updated mixture for: " + g.Name);
        }

        // ── Dispersion studies ───────────────────────────────────────────────
        private async Task AddDispersionStudyAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            var dlg = new DispersionStudyDialog(_scene);
            if (!await dlg.ShowDialog<bool>(this) || dlg.Result is null) return;
            _scene.DispersionStudies.Add(dlg.Result);
            MarkDirtyAndRefresh("Added study: " + dlg.Result.Name);
        }

        private async Task EditDispersionStudyAsync(ProjectTreeNode node)
        {
            if (_scene is null || node.Tag is not DispersionStudy study) return;
            // The editing constructor reuses the same study instance, so we
            // don't need to copy fields back — the Result reference is study.
            var dlg = new DispersionStudyDialog(_scene, study);
            if (!await dlg.ShowDialog<bool>(this)) return;
            MarkDirtyAndRefresh("Updated study: " + study.Name);
        }

        // ── Transient wind / ESD ─────────────────────────────────────────────
        private async Task EditTransientWindAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            var scenario = _scene.DispersionScenario;
            if (scenario is null)
            {
                StatusText.Text = "No active dispersion scenario to edit transient wind for.";
                return;
            }
            scenario.TransientWind ??= new TransientWindProfile();

            var dlg = new TransientWindDialog(scenario.TransientWind);
            if (!await dlg.ShowDialog<bool>(this)) return;
            scenario.TransientWind = dlg.Result;
            MarkDirtyAndRefresh("Updated transient wind profile (" +
                dlg.Result.Entries.Count + " entries, ESD=" +
                dlg.Result.ESDTimeS.ToString("F1") + "s)");
        }

        // ── Exceedance curves (results viewer) ───────────────────────────────
        private async Task ShowExceedanceAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }

            // The WinForms shell uses the same fixed threshold ladder; mirror
            // it here so legends and table contents match across UIs.
            double[] thresholds = { 1e-6, 1e-5, 1e-4, 1e-3, 1e-2, 0.05, 0.1, 0.5, 1.0 };
            var results = new System.Collections.Generic.List<ExceedanceCurveResult>();
            if (_scene.MonitorPoints != null)
                foreach (var m in _scene.MonitorPoints)
                    if (m.TimeSeries != null && m.TimeSeries.Count > 0)
                        results.Add(ExceedanceCurveCalculator.ComputeFromTimeSeries(m, thresholds));

            if (results.Count == 0)
            {
                StatusText.Text = "No monitor time-series available — run a transient simulation first.";
                return;
            }

            var dlg = new ExceedanceDialog(results);
            await dlg.ShowDialog(this);
        }

        // ── Wind rose ────────────────────────────────────────────────────────
        private async Task EditWindRoseAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            var dlg = new WindRoseDialog(_scene.WindRose);
            if (!await dlg.ShowDialog<bool>(this)) return;
            _scene.WindRose = dlg.Result;
            // GenerateScenarios is a side-output: when true, the caller would
            // normally synthesize one DispersionScenario per bin. For now we
            // just surface it in the status bar so users know it was honored.
            MarkDirtyAndRefresh(dlg.GenerateScenarios
                ? "Wind rose updated (" + dlg.Result.Bins.Count + " bins). Generate-scenarios flagged for next run."
                : "Wind rose updated (" + dlg.Result.Bins.Count + " bins).");
        }

        // ── Batch export (camera presets → PNGs) ─────────────────────────────
        private async Task ShowBatchExportAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            if (_scene.CameraPresets == null || _scene.CameraPresets.Count == 0)
            {
                StatusText.Text = "No camera presets saved — record at least one first.";
                return;
            }
            var dlg = new BatchExportDialog(_scene.CameraPresets);
            if (!await dlg.ShowDialog<bool>(this)) return;
            // No 3D viewport on Avalonia yet — surface the chosen settings so
            // the user sees the dialog round-tripped. A future OpenTK viewport
            // would render `dlg.SelectedPresets` into `dlg.OutputFolder` here.
            StatusText.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Batch export configured: {0} presets @ {1}×{2} → {3} " +
                "(viewport rendering pending Avalonia 3D port)",
                dlg.SelectedPresets.Count, dlg.ImageWidth, dlg.ImageHeight, dlg.OutputFolder);
        }

        // ── Validation against published benchmarks ──────────────────────────
        private async Task ShowValidationAsync()
        {
            // Validation reuses the app-level CFD config so the runner knows
            // which OpenFOAM env to invoke. Works fine without an open project.
            var dlg = new ValidationDialog(AppSettings.Instance.CreateCfdConfig());
            await dlg.ShowDialog(this);
        }

        // ── Detector results (last evaluation) ───────────────────────────────
        private async Task ShowDetectorResultsAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            if (_scene.GasDetectors == null || _scene.GasDetectors.Count == 0)
            {
                StatusText.Text = "No detectors defined — add at least one in the project.";
                return;
            }

            // Build an on-the-fly summary from the detectors' current Detected /
            // DetectionTimeS state. The renderer is responsible for running a
            // proper evaluation against a snapshot; here we just present what
            // each detector last saw.
            var triggered = _scene.GasDetectors.Count(d => d.Detected);
            var detTimes = _scene.GasDetectors
                .Where(d => d.Detected && d.DetectionTimeS >= 0)
                .Select(d => d.DetectionTimeS).ToList();
            // CoveragePercent is a computed property on the engine type
            // (= 100 * Triggered/Total) — don't try to set it explicitly.
            var result = new DetectorEvaluationResult
            {
                TotalDetectors      = _scene.GasDetectors.Count,
                DetectorsTriggered  = triggered,
                MinDetectionTimeS   = detTimes.Count > 0 ? detTimes.Min() : double.MaxValue,
                MaxDetectionTimeS   = detTimes.Count > 0 ? detTimes.Max() : 0,
                AvgDetectionTimeS   = detTimes.Count > 0 ? detTimes.Average() : 0
            };
            var dlg = new DetectorResultsDialog(result, _scene.GasDetectors);
            await dlg.ShowDialog(this);
        }

        // ── CFD settings (application-level, shared across projects) ─────────
        private async Task EditCfdSettingsAsync()
        {
            // Same wiring as the WinForms shell: the dialog edits an
            // AppSettings-derived CfdConfiguration and writes it back. The
            // OpenFoamEnvironment is constructed fresh here because the
            // Avalonia shell doesn't yet maintain a long-lived editor with a
            // persistent environment instance.
            var current = AppSettings.Instance.CreateCfdConfig();
            var dlg = new CfdSettingsDialog(current, new OpenFoamEnvironment());
            if (!await dlg.ShowDialog<bool>(this)) return;
            AppSettings.Instance.UpdateFromConfig(dlg.Result);
            StatusText.Text = "CFD settings updated (" + dlg.Result.DetectedEnvironment + ")";
        }

        // ── Equipment inventory (per source) ─────────────────────────────────
        private async Task EditEquipmentInventoryAsync(ProjectTreeNode node)
        {
            if (_scene is null || node.Tag is not ReleaseSource3D src) return;
            // The dialog mutates the source in-place on OK and rolls back on
            // Cancel — no need to copy values back here.
            var dlg = new EquipmentInventoryDialog(src);
            if (!await dlg.ShowDialog<bool>(this)) return;
            MarkDirtyAndRefresh("Updated equipment inventory: " + src.Name);
        }

        // ── Wind-field manager ───────────────────────────────────────────────
        private async Task ShowWindFieldManagerAsync(string? preselectedId = null)
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            var dlg = new WindFieldManagerDialog(_scene, new OpenFoamEnvironment(), preselectedId);
            if (!await dlg.ShowDialog<bool>(this)) return;

            // Replace the scene's scenario list with the manager's edited
            // copy. Order is preserved so views and simulations referencing
            // a wind-field Id stay valid.
            _scene.WindFieldScenarios.Clear();
            foreach (var w in dlg.Scenarios) _scene.WindFieldScenarios.Add(w);
            MarkDirtyAndRefresh("Wind field manager: " + dlg.Scenarios.Count + " scenario(s)");
        }

        private Task ShowWindFieldManagerForNodeAsync(ProjectTreeNode node)
        {
            // Extract the wind-field id from the "wind:{guid}" node id so the
            // manager opens with that row pre-selected.
            int colon = node.NodeId.IndexOf(':');
            string? id = colon > 0 ? node.NodeId.Substring(colon + 1) : null;
            return ShowWindFieldManagerAsync(id);
        }

        // ── Dispersion-scenario manager ──────────────────────────────────────
        private async Task ShowScenarioManagerAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            var dlg = new ScenarioManagerDialog(_scene.DispersionScenarios,
                _scene.ActiveScenarioIndex, _scene.WindFieldScenarios);
            if (!await dlg.ShowDialog<bool>(this)) return;

            // The manager returned an updated list + new active index;
            // swap them in on the scene.
            _scene.DispersionScenarios.Clear();
            foreach (var s in dlg.Scenarios) _scene.DispersionScenarios.Add(s);
            _scene.ActiveScenarioIndex = Math.Max(0,
                Math.Min(dlg.SelectedIndex, _scene.DispersionScenarios.Count - 1));
            MarkDirtyAndRefresh("Scenarios: " + _scene.DispersionScenarios.Count
                + " (active = " + _scene.ActiveScenarioIndex + ")");
        }

        // ── DWSIM settings (application-level) ───────────────────────────────
        private async Task EditDwsimSettingsAsync()
        {
            var dlg = new DwsimSettingsDialog();
            if (!await dlg.ShowDialog<bool>(this)) return;
            // DwsimSettingsDialog saves AppSettings + resets the cached
            // flowsheet itself; we only surface the change in the status bar.
            StatusText.Text = "DWSIM settings updated";
        }

        // ── GPU / performance settings (application-level) ───────────────────
        private async Task ShowGpuSettingsAsync()
        {
            var dlg = new GpuPerformanceSettingsDialog();
            if (!await dlg.ShowDialog<bool>(this)) return;
            // The dialog persists AppSettings itself; no scene state changes.
            StatusText.Text = "GPU/performance settings updated";
        }

        // ── Detector allocation (study-driven greedy / risk-reduction) ───────
        private async Task ShowDetectorAllocationAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            if (_scene.DispersionStudies.Count == 0)
            {
                StatusText.Text = "Define at least one Dispersion Study first.";
                return;
            }
            var dlg = new DetectorAllocationDialog(_scene);
            if (!await dlg.ShowDialog<bool>(this) || dlg.Result is null) return;
            _scene.DetectorAllocations.Add(dlg.Result);
            MarkDirtyAndRefresh("Added detector allocation: " + dlg.Result.Name
                + " (" + (dlg.Result.AllocatedPositions?.Count ?? 0) + " positions)");
        }

        private async Task EditDetectorAllocationAsync(ProjectTreeNode node)
        {
            if (_scene is null || node.Tag is not DetectorAllocation a) return;
            var dlg = new DetectorAllocationDialog(_scene, a);
            if (!await dlg.ShowDialog<bool>(this) || dlg.Result is null) return;
            // The editing constructor reuses the same DetectorAllocation
            // instance, so no swap is needed — just refresh the tree to pick
            // up the updated name / coverage.
            MarkDirtyAndRefresh("Updated detector allocation: " + dlg.Result.Name);
        }

        // ── DWSIM mixture builder (adds to the gas library) ──────────────────
        private async Task BuildMixtureFromDwsimAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            var dlg = new DwsimMixtureBuilderDialog();
            if (!await dlg.ShowDialog<bool>(this) || dlg.Result is null) return;
            _scene.GasLibrary.Add(dlg.Result);
            MarkDirtyAndRefresh("Added mixture from DWSIM: " + dlg.Result.Name);
        }

        // ── Detector placement optimisation ──────────────────────────────────
        private async Task ShowDetectorOptimizationAsync()
        {
            if (_scene is null) { StatusText.Text = "Open or create a project first."; return; }
            var completed = _scene.Simulations?.Count(s => s.Status == SimulationStatus.Completed) ?? 0;
            if (completed == 0)
            {
                StatusText.Text = "No Completed simulations — run at least one before optimising detectors.";
                return;
            }

            var dlg = new DetectorOptimizationDialog(_scene);
            if (!await dlg.ShowDialog<bool>(this)) return;
            if (dlg.ResultDetectorPositions.Count == 0)
            {
                StatusText.Text = "Optimization returned no detectors — adjust the protected region or threshold.";
                return;
            }

            // Materialise the proposed positions as concrete GasDetector3D
            // rows so the user can edit them in the inspector. Threshold
            // defaults to 0.01 kg/m³ to match the engine default; the user
            // can tighten / loosen per detector after the fact.
            int added = 0;
            foreach (var p in dlg.ResultDetectorPositions)
            {
                _scene.GasDetectors.Add(new GasDetector3D
                {
                    Name = "Opt-" + (++added).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Position = p
                });
            }
            MarkDirtyAndRefresh("Added " + added + " optimised detector position(s).");
        }

        /// <summary>Removes the item whose stable ID matches the node's
        /// trailing "kind:id" part. Works for any list whose item type
        /// carries an <c>Id</c> property — reflection avoids needing one
        /// overload per list type.</summary>
        private void DeleteFromList<T>(System.Collections.Generic.IList<T>? list, ProjectTreeNode node)
            where T : class
        {
            if (_scene is null || list is null) return;
            int colon = node.NodeId.IndexOf(':');
            if (colon < 0) return;
            string id = node.NodeId.Substring(colon + 1);

            for (int i = 0; i < list.Count; i++)
            {
                var itemId = list[i]?.GetType()
                    .GetProperty("Id")?.GetValue(list[i]) as string;
                if (itemId == id)
                {
                    list.RemoveAt(i);
                    MarkDirtyAndRefresh("Deleted " + node.Title);
                    return;
                }
            }
        }

        private void MarkDirtyAndRefresh(string status)
        {
            _isDirty = true;
            RebuildTree();
            Viewport3D.PopulateScene(_scene);
            StatusText.Text = status;
        }

        // ── Simulation playback ─────────────────────────────────────────────

        private void InitPlayback(Simulation sim)
        {
            StopPlaybackTimer();

            _playbackSim = sim;
            _playbackThresholds = sim.SnapshotThresholds ?? new List<DispersionThreshold>();

            OpenFoamResult? result = sim.ResultTag as OpenFoamResult;
            if (result == null && !string.IsNullOrEmpty(sim.CasePath)
                && Directory.Exists(sim.CasePath))
            {
                int nx = sim.SnapshotGridResolution > 0 ? sim.SnapshotGridResolution : 60;
                int ny = nx;
                int nz = Math.Max(1, nx / 2);
                double half = sim.SnapshotDomainSizeM > 0 ? sim.SnapshotDomainSizeM : 200;

                // Resolve species field name for OpenFOAM (CH4, SF6, s, etc.)
                string speciesName = "CH4";
                try
                {
                    if (sim.SnapshotSource != null)
                        speciesName = OpenFoamCaseGenerator.ResolveOpenFoamSpecies(sim.SnapshotSource);
                }
                catch { }

                // Try OpenFOAM time-directory layout first
                result = OpenFoamResultReader.ReadResults(
                    sim.CasePath, nx, ny, nz, half, scalarFieldName: speciesName);

                // Fall back to FluidX3D flat-bin layout
                if (result == null || !result.IsLoaded || result.TimeSteps.Count == 0)
                {
                    result = GlViewport.TryLoadFlatBinCasePublic(
                        sim.CasePath, ref nx, ref ny, ref nz, half, false);
                }

                if (result != null && result.IsLoaded)
                    sim.ResultTag = result;
            }

            _playbackResult = result;

            if (result == null || !result.IsLoaded || result.TimeSteps.Count == 0)
            {
                PlaybackPanel.IsVisible = false;
                LegendPanel.IsVisible = false;
                StatusText.Text = $"No result data found for '{sim.Name}'";
                return;
            }

            double lastTime = result.TimeSteps[result.TimeSteps.Count - 1];
            PlaybackTotalLabel.Text = $"/ {lastTime:F1} s  ({result.TimeSteps.Count} steps)";
            _playbackTimeS = result.TimeSteps[0];

            PlaybackPanel.IsVisible = true;
            _playbackPlaying = false;
            PlayPauseIcon.Value = "mdi-play";

            // Build effective thresholds: user-defined + built-in layers
            _playbackEffectiveThresholds = BuildEffectiveThresholds(result);
            BuildLegend();
            RenderPlaybackFrame(_playbackTimeS);
        }

        private List<DispersionThreshold>? _playbackEffectiveThresholds;

        private List<DispersionThreshold> BuildEffectiveThresholds(OpenFoamResult result)
        {
            // Find peak concentration across all time steps (sample last step)
            double maxConc = 0;
            var lastField = result.GetField(result.TimeSteps[result.TimeSteps.Count - 1]);
            if (lastField != null)
            {
                int nx = lastField.GetLength(0), ny = lastField.GetLength(1), nz = lastField.GetLength(2);
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                        for (int k = 0; k < nz; k++)
                            if (lastField[i, j, k] > maxConc) maxConc = lastField[i, j, k];
            }

            var thresholds = new List<DispersionThreshold>();

            // Add user-defined thresholds
            if (_playbackThresholds != null)
            {
                foreach (var t in _playbackThresholds)
                    if (t.Visible && t.ConcentrationValue > 0)
                        thresholds.Add(t);
            }

            // If no user thresholds, add built-in layers like WPF ComputeCloudVisual
            if (thresholds.Count == 0 && maxConc > 0)
            {
                var builtinFracs = new[] { 0.50, 0.20, 0.08, 0.03, 0.01, 0.003 };
                var builtinColors = new[]
                {
                    DisperSim3D.Geometry.Color.FromArgb(200, 220, 40, 40),    // dark red
                    DisperSim3D.Geometry.Color.FromArgb(180, 255, 100, 30),   // orange
                    DisperSim3D.Geometry.Color.FromArgb(160, 255, 200, 50),   // yellow
                    DisperSim3D.Geometry.Color.FromArgb(140, 100, 220, 100),  // green
                    DisperSim3D.Geometry.Color.FromArgb(120, 80, 160, 255),   // light blue
                    DisperSim3D.Geometry.Color.FromArgb(100, 180, 180, 220),  // pale blue
                };
                var builtinNames = new[] { "50%", "20%", "8%", "3%", "1%", "0.3%" };
                var builtinOpacity = new[] { 0.40, 0.35, 0.30, 0.25, 0.20, 0.15 };

                for (int i = 0; i < builtinFracs.Length; i++)
                {
                    thresholds.Add(new DispersionThreshold
                    {
                        Name = builtinNames[i] + " max",
                        ConcentrationValue = maxConc * builtinFracs[i],
                        Color = builtinColors[i],
                        Opacity = builtinOpacity[i],
                        Visible = true
                    });
                }
            }

            // Sort descending by concentration (render inner shells first)
            thresholds.Sort((a, b) => b.ConcentrationValue.CompareTo(a.ConcentrationValue));
            return thresholds;
        }

        private void StopAndHidePlayback()
        {
            StopPlaybackTimer();
            _playbackSim = null;
            _playbackResult = null;
            _playbackEffectiveThresholds = null;
            _playbackPlaying = false;
            PlaybackPanel.IsVisible = false;
            LegendPanel.IsVisible = false;
            Viewport3D.ClearDispersionFrame();
        }

        private void BuildLegend()
        {
            LegendItems.Children.Clear();

            var thresholds = _playbackEffectiveThresholds;
            if (thresholds == null || thresholds.Count == 0)
            {
                LegendPanel.IsVisible = false;
                return;
            }

            foreach (var th in thresholds)
            {
                if (!th.Visible) continue;

                var swatch = new Border
                {
                    Width = 14, Height = 14,
                    CornerRadius = new CornerRadius(2),
                    BorderBrush = global::Avalonia.Media.Brushes.DarkGray,
                    BorderThickness = new Thickness(0.5),
                    Background = new global::Avalonia.Media.SolidColorBrush(
                        global::Avalonia.Media.Color.FromArgb(
                            (byte)(Math.Max(th.Opacity, 0.6) * 255),
                            th.Color.R, th.Color.G, th.Color.B)),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };

                string valText;
                double v = th.ConcentrationValue;
                if (v >= 0.01) valText = v.ToString("F3", CultureInfo.InvariantCulture);
                else if (v >= 1e-6) valText = v.ToString("E2", CultureInfo.InvariantCulture);
                else valText = v.ToString("E1", CultureInfo.InvariantCulture);

                var label = new TextBlock
                {
                    Text = $"{th.Name}: {valText}",
                    FontSize = 11,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };

                var row = new StackPanel
                {
                    Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                    Margin = new Thickness(0, 2)
                };
                row.Children.Add(swatch);
                row.Children.Add(label);
                LegendItems.Children.Add(row);
            }

            LegendPanel.IsVisible = LegendItems.Children.Count > 0;
        }

        private void StopPlaybackTimer()
        {
            if (_playbackTimer != null)
            {
                _playbackTimer.Stop();
                _playbackTimer.Tick -= PlaybackTimer_Tick;
                _playbackTimer = null;
            }
        }

        private void BtnPlayPause_Click(object? sender, RoutedEventArgs e)
        {
            if (_playbackResult == null || _playbackResult.TimeSteps.Count == 0)
                return;

            if (_playbackPlaying)
            {
                _playbackPlaying = false;
                StopPlaybackTimer();
                PlayPauseIcon.Value = "mdi-play";
            }
            else
            {
                _playbackPlaying = true;
                PlayPauseIcon.Value = "mdi-pause";

                double lastTime = _playbackResult.TimeSteps[_playbackResult.TimeSteps.Count - 1];
                if (_playbackTimeS >= lastTime - 0.001)
                    _playbackTimeS = _playbackResult.TimeSteps[0];

                _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                _playbackTimer.Tick += PlaybackTimer_Tick;
                _playbackTimer.Start();
            }
        }

        private void BtnStop_Click(object? sender, RoutedEventArgs e)
        {
            if (_playbackResult == null) return;
            _playbackPlaying = false;
            StopPlaybackTimer();
            PlayPauseIcon.Value = "mdi-play";
            _playbackTimeS = _playbackResult.TimeSteps.Count > 0
                ? _playbackResult.TimeSteps[0] : 0;
            UpdatePlaybackSlider();
            RenderPlaybackFrame(_playbackTimeS);
        }

        private void PlaybackTimer_Tick(object? sender, EventArgs e)
        {
            if (_playbackResult == null || _playbackResult.TimeSteps.Count < 2)
                return;

            double firstTime = _playbackResult.TimeSteps[0];
            double lastTime = _playbackResult.TimeSteps[_playbackResult.TimeSteps.Count - 1];
            double duration = lastTime - firstTime;
            if (duration <= 0) return;

            double dtReal = 0.033 * _playbackSpeedFactor * duration / 10.0;
            _playbackTimeS += dtReal;

            if (_playbackTimeS >= lastTime)
            {
                _playbackTimeS = lastTime;
                _playbackPlaying = false;
                StopPlaybackTimer();
                PlayPauseIcon.Value = "mdi-play";
            }

            UpdatePlaybackSlider();
            RenderPlaybackFrame(_playbackTimeS);
        }

        private void PlaybackSlider_ValueChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name != "Value") return;
            if (PlaybackSlider == null || _playbackPlaying || _playbackResult == null
                || _playbackResult.TimeSteps.Count == 0) return;

            double fraction = PlaybackSlider.Value / 1000.0;
            double firstTime = _playbackResult.TimeSteps[0];
            double lastTime = _playbackResult.TimeSteps[_playbackResult.TimeSteps.Count - 1];
            _playbackTimeS = firstTime + fraction * (lastTime - firstTime);
            RenderPlaybackFrame(_playbackTimeS);
        }

        private void PlaybackSpeed_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (PlaybackSpeed == null) return;
            _playbackSpeedFactor = PlaybackSpeed.SelectedIndex switch
            {
                0 => 0.25, 1 => 0.5, 2 => 1.0, 3 => 2.0, 4 => 5.0, _ => 1.0
            };
        }

        private void UpdatePlaybackSlider()
        {
            if (_playbackResult == null || _playbackResult.TimeSteps.Count == 0) return;
            double firstTime = _playbackResult.TimeSteps[0];
            double lastTime = _playbackResult.TimeSteps[_playbackResult.TimeSteps.Count - 1];
            double duration = lastTime - firstTime;
            double fraction = duration > 0 ? (_playbackTimeS - firstTime) / duration : 0;
            PlaybackSlider.Value = fraction * 1000.0;
            PlaybackTimeLabel.Text = $"{_playbackTimeS:F1} s";
        }

        private void RenderPlaybackFrame(double timeS)
        {
            if (_playbackResult == null || _playbackResult.TimeSteps.Count == 0
                || _playbackEffectiveThresholds == null || _playbackSim == null) return;

            double bestT = _playbackResult.TimeSteps[0];
            double bestDelta = Math.Abs(timeS - bestT);
            foreach (var t in _playbackResult.TimeSteps)
            {
                double d = Math.Abs(timeS - t);
                if (d < bestDelta) { bestDelta = d; bestT = t; }
            }

            var field = _playbackResult.GetField(bestT);
            if (field == null) return;

            double half = _playbackSim.SnapshotDomainSizeM > 0
                ? _playbackSim.SnapshotDomainSizeM : 200;

            PlaybackTimeLabel.Text = $"{bestT:F1} s";

            Viewport3D.UpdateDispersionFrame(field, _playbackEffectiveThresholds, half);
        }

        // ── Help menu ────────────────────────────────────────────────────────
        private void MenuHelpDiag_Click(object? sender, RoutedEventArgs e)
        {
            var diag = new DiagnosticsWindow();
            diag.ShowDialog(this);
        }

        private async void MenuHelpAbout_Click(object? sender, RoutedEventArgs e)
        {
            var dlg = new AboutDialog();
            await dlg.ShowDialog(this);
        }
    }
}
