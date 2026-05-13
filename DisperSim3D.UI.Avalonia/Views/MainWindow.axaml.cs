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

        public MainWindow()
        {
            InitializeComponent();
            StatusEnv.Text = BuildEnvLine();
            Inspector.ValueChanged += (_, _) =>
            {
                if (_scene != null && !_isDirty) { _isDirty = true; UpdateTitle(); }
            };
            RebuildTree();
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
            ProjectTree.ItemsSource = ProjectTreeBuilder.Build(_scene, name);
            UpdateTitle();
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
        }

        // ── File menu ────────────────────────────────────────────────────────
        private void MenuFileNew_Click(object? sender, RoutedEventArgs e)
        {
            _scene = new Scene3D();
            _projectPath = null;
            _isDirty = false;
            RebuildTree();
            Viewport3D.PopulateScene(_scene);
            StatusText.Text = "New project";
        }

        private async void MenuFileOpen_Click(object? sender, RoutedEventArgs e)
        {
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
                    AddItem("Configure…", "mdi-pencil-outline",
                        (_, _) => _ = EditSimulationAsync(node));
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

            // Seed the dialog's Azimuth with the project's wind direction so
            // the new source faces "downwind" — matches WinForms behaviour.
            double windDirSeed = 0;
            if (_scene.WindFieldScenarios?.Count > 0 && _scene.WindFieldScenarios[0].Meteo != null)
                windDirSeed = _scene.WindFieldScenarios[0].Meteo.WindDirectionDeg;
            else if (_scene.GeneralSettings?.DefaultMeteo != null)
                windDirSeed = _scene.GeneralSettings.DefaultMeteo.WindDirectionDeg;

            var dlg = new DispersionSourceDialog(windDirSeed);
            if (!await dlg.ShowDialog<bool>(this)) return;

            var src = dlg.BuildSource();
            _scene.TopLevelSources.Add(src);
            MarkDirtyAndRefresh("Added source: " + src.Name);
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
            // The editing constructor preserves Id; on OK the dialog mutates
            // the same instance in place so anything holding a reference (e.g.
            // a running progress panel) doesn't see it swapped out.
            var dlg = new SimulationEditorDialog(_scene, sim);
            if (!await dlg.ShowDialog<bool>(this)) return;
            MarkDirtyAndRefresh("Updated simulation: " + sim.Name);
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
