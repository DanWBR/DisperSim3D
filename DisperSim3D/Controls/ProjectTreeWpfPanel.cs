using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using DisperSim3D.Models;
using SwfUserControl = System.Windows.Forms.UserControl;
using WpfTreeView = System.Windows.Controls.TreeView;
using WpfTreeViewItem = System.Windows.Controls.TreeViewItem;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfDockStyle = System.Windows.Forms.DockStyle;

namespace DisperSim3D.Controls
{
    /// <summary>
    /// WPF-based project tree hosted in a WinForms control via ElementHost.
    /// Per-node checkboxes (only for visualizable items), colored status badges, context menus.
    /// </summary>
    public class ProjectTreeWpfPanel : SwfUserControl
    {
        private readonly ElementHost _host;
        private readonly WpfTreeView _tree;
        private Scene3D _scene;

        public event EventHandler<ProjectTreeActionEventArgs> ActionRequested;
        public event EventHandler<ProjectTreeVisibilityEventArgs> VisibilityChanged;
        public event EventHandler<ProjectTreeSelectionEventArgs> SelectionChanged;

        public ProjectTreeWpfPanel()
        {
            _tree = new WpfTreeView
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.White
            };
            _tree.SelectedItemChanged += Tree_SelectedItemChanged;

            _host = new ElementHost
            {
                Dock = WpfDockStyle.Fill,
                Child = _tree
            };
            this.Controls.Add(_host);
        }

        public void BindScene(Scene3D scene)
        {
            _scene = scene;
            RefreshTree();
        }

        public void RefreshTree()
        {
            var expandedKeys = new HashSet<string>();
            string selectedKey = null;
            CollectExpansionState(_tree.Items, expandedKeys, ref selectedKey);

            _tree.Items.Clear();
            if (_scene == null) return;

            string projName = _scene.GeneralSettings?.Name ?? _scene.Name ?? "Project";
            var root = MakeNode(projName, NodeKind.ProjectRoot, null,
                bold: true, glyph: "☰", isContainer: true);

            root.Items.Add(MakeNode("General Settings", NodeKind.GeneralRoot, null,
                italic: true, glyph: "⚙"));

            var gases = MakeNode("Gases", NodeKind.GasesRoot, null,
                bold: true, glyph: "⚛", count: _scene.GasLibrary.Count, isContainer: true);
            foreach (var g in _scene.GasLibrary)
            {
                gases.Items.Add(MakeNode(
                    g.Name, NodeKind.GasItem, g.Id,
                    glyph: g.IsMixture ? "⚗" : "⚛",
                    statusText: g.IsMixture ? "Mixture" : "Pure",
                    statusBrush: Brushes.Gray));
            }
            root.Items.Add(gases);

            var geom = MakeNode("Geometry", NodeKind.GeometryRoot, null,
                bold: true, glyph: "□", count: _scene.Decorations.Count, isContainer: true);
            foreach (var d in _scene.Decorations)
                geom.Items.Add(MakeNode(d.Name ?? "Decoration", NodeKind.GeometryItem, d.Id,
                    glyph: "□", showCheckBox: true, isChecked: true));
            root.Items.Add(geom);

            var sources = MakeNode("Sources", NodeKind.SourcesRoot, null,
                bold: true, glyph: "▲", count: _scene.TopLevelSources.Count, isContainer: true);
            foreach (var s in _scene.TopLevelSources)
            {
                string gasName = ResolveGasName(s);
                sources.Items.Add(MakeNode(
                    s.Name ?? "Source",
                    NodeKind.SourceItem, s.Id,
                    glyph: "▲",
                    statusText: gasName,
                    statusBrush: Brushes.SteelBlue,
                    showCheckBox: true, isChecked: s.IsVisible));
            }
            root.Items.Add(sources);

            var winds = MakeNode("Wind Fields", NodeKind.WindFieldsRoot, null,
                bold: true, glyph: "≀", count: _scene.WindFieldScenarios.Count, isContainer: true);
            foreach (var wf in _scene.WindFieldScenarios)
            {
                Brush statusBrush = wf.Status == WindFieldStatus.Ready ? Brushes.SeaGreen
                    : wf.Status == WindFieldStatus.Failed ? Brushes.Crimson
                    : wf.Status == WindFieldStatus.Running ? Brushes.DarkOrange
                    : Brushes.Gray;
                winds.Items.Add(MakeNode(wf.Name, NodeKind.WindFieldItem, wf.Id,
                    glyph: "≀",
                    statusText: wf.Status.ToString(),
                    statusBrush: statusBrush,
                    showCheckBox: wf.Status == WindFieldStatus.Ready,
                    isChecked: false));
            }
            root.Items.Add(winds);

            var sims = MakeNode("Simulations", NodeKind.SimulationsRoot, null,
                bold: true, glyph: "◉", count: _scene.Simulations.Count, isContainer: true);
            foreach (var sim in _scene.Simulations)
            {
                Brush statusBrush = sim.Status == SimulationStatus.Completed ? Brushes.SeaGreen
                    : sim.Status == SimulationStatus.Failed ? Brushes.Crimson
                    : sim.Status == SimulationStatus.Running || sim.Status == SimulationStatus.Queued
                        ? Brushes.DarkOrange
                    : Brushes.Gray;
                string srcName = _scene.TopLevelSources.FirstOrDefault(s => s.Id == sim.SourceId)?.Name
                    ?? sim.SnapshotSource?.Name ?? "?";
                string wfName = _scene.WindFieldScenarios.FirstOrDefault(w => w.Id == sim.WindFieldId)?.Name
                    ?? "?";
                string solverTag = DisperSim3D.Core.SolverCode.Of(sim.SolverType);
                string label = string.Format("{0}  [{1}]  [{2} / {3}]", sim.Name, solverTag, srcName, wfName);
                sims.Items.Add(MakeNode(label, NodeKind.SimulationItem, sim.Id,
                    glyph: "◉",
                    statusText: sim.Status.ToString(),
                    statusBrush: statusBrush,
                    showCheckBox: true,
                    isChecked: sim.IsVisible));
            }
            root.Items.Add(sims);

            var views = MakeNode("Views", NodeKind.ViewsRoot, null,
                bold: true, glyph: "◈", count: _scene.Views.Count, isContainer: true);
            foreach (var v in _scene.Views)
            {
                var pinnedSim = _scene.Simulations.FirstOrDefault(s => s.Id == v.SimulationId);
                bool simReady = pinnedSim != null && pinnedSim.Status == SimulationStatus.Completed;
                Brush statusBrush = simReady ? Brushes.SeaGreen
                    : pinnedSim == null ? Brushes.Crimson : Brushes.DarkOrange;
                string statusText = pinnedSim == null ? "No sim"
                    : simReady ? pinnedSim.Name : pinnedSim.Status.ToString();
                string glyph = v.Kind == ViewKind.Isosurface ? "◯" : "▭";
                string label = string.Format("{0}  [{1}]", v.Name, v.Kind);
                views.Items.Add(MakeNode(label, NodeKind.ViewItem, v.Id,
                    glyph: glyph,
                    statusText: statusText,
                    statusBrush: statusBrush,
                    showCheckBox: true,
                    isChecked: v.IsVisible));
            }
            root.Items.Add(views);

            var monitors = MakeNode("Monitors", NodeKind.MonitorsRoot, null,
                bold: true, glyph: "⦿", count: _scene.MonitorPoints.Count, isContainer: true);
            foreach (var m in _scene.MonitorPoints)
                monitors.Items.Add(MakeNode(m.Name ?? "Monitor", NodeKind.MonitorItem, m.Name,
                    glyph: "⦿", showCheckBox: true, isChecked: true));
            root.Items.Add(monitors);

            var detectors = MakeNode("Detectors", NodeKind.DetectorsRoot, null,
                bold: true, glyph: "⛝", count: _scene.GasDetectors.Count, isContainer: true);
            foreach (var d in _scene.GasDetectors)
                detectors.Items.Add(MakeNode(d.Name ?? "Detector", NodeKind.DetectorItem, d.Id,
                    glyph: "⛝", showCheckBox: true, isChecked: d.Visible));
            root.Items.Add(detectors);

            _tree.Items.Add(root);

            if (expandedKeys.Count == 0)
                root.IsExpanded = true; // first render: open the project root by default
            else
                ApplyExpansionState(_tree.Items, expandedKeys, selectedKey);
        }

        private static string KeyOf(WpfTreeViewItem item)
        {
            var tref = item?.Tag as TreeRef;
            if (tref == null) return null;
            return tref.Kind + "|" + (tref.ItemId ?? "");
        }

        private static void CollectExpansionState(System.Windows.Controls.ItemCollection items,
            HashSet<string> expandedKeys, ref string selectedKey)
        {
            foreach (var obj in items)
            {
                var tvi = obj as WpfTreeViewItem;
                if (tvi == null) continue;
                string k = KeyOf(tvi);
                if (k != null && tvi.IsExpanded) expandedKeys.Add(k);
                if (tvi.IsSelected && k != null) selectedKey = k;
                CollectExpansionState(tvi.Items, expandedKeys, ref selectedKey);
            }
        }

        private static void ApplyExpansionState(System.Windows.Controls.ItemCollection items,
            HashSet<string> expandedKeys, string selectedKey)
        {
            foreach (var obj in items)
            {
                var tvi = obj as WpfTreeViewItem;
                if (tvi == null) continue;
                string k = KeyOf(tvi);
                if (k != null && expandedKeys.Contains(k))
                    tvi.IsExpanded = true;
                if (k != null && k == selectedKey)
                    tvi.IsSelected = true;
                ApplyExpansionState(tvi.Items, expandedKeys, selectedKey);
            }
        }

        private string ResolveGasName(ReleaseSource3D src)
        {
            if (!string.IsNullOrEmpty(src.GasRefId))
            {
                var item = _scene.GasLibrary.FirstOrDefault(g => g.Id == src.GasRefId);
                if (item != null) return item.Name;
            }
            return src.Gas?.Name ?? "(no gas)";
        }

        private WpfTreeViewItem MakeNode(string label, NodeKind kind, string itemId,
            string glyph = null, bool bold = false, bool italic = false,
            int? count = null, string statusText = null, Brush statusBrush = null,
            bool showCheckBox = false, bool isChecked = false, bool isContainer = false)
        {
            var item = new WpfTreeViewItem
            {
                Tag = new TreeRef(kind, itemId),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
                Padding = new Thickness(2, 2, 2, 2),
                Header = BuildHeader(label, glyph, count, statusText, statusBrush,
                    showCheckBox, isChecked, kind, itemId)
            };
            item.ContextMenu = BuildContextMenu(kind, itemId);
            item.MouseDoubleClick += (s, e) =>
            {
                // Only fire for the directly-clicked TreeViewItem (avoid bubbling from children)
                if (s != e.OriginalSource && !(e.OriginalSource is System.Windows.Controls.TextBlock)) return;
                var action = MapKindToEditAction(kind);
                if (action.HasValue)
                {
                    ActionRequested?.Invoke(this,
                        new ProjectTreeActionEventArgs(action.Value, itemId));
                    e.Handled = true;
                }
            };
            return item;
        }

        private static ProjectTreeAction? MapKindToEditAction(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.GeneralRoot: return ProjectTreeAction.EditGeneralSettings;
                case NodeKind.GasItem: return ProjectTreeAction.EditGas;
                case NodeKind.GeometryItem: return ProjectTreeAction.EditGeometry;
                case NodeKind.SourceItem: return ProjectTreeAction.EditSource;
                case NodeKind.WindFieldItem: return ProjectTreeAction.EditWindField;
                case NodeKind.SimulationItem: return ProjectTreeAction.EditSimulation;
                case NodeKind.ViewItem: return ProjectTreeAction.EditView;
                case NodeKind.MonitorItem: return ProjectTreeAction.EditMonitor;
                case NodeKind.DetectorItem: return ProjectTreeAction.EditDetector;
                default: return null;
            }
        }

        private object BuildHeader(string label, string glyph, int? count,
            string statusText, Brush statusBrush,
            bool showCheckBox, bool isChecked, NodeKind kind, string itemId)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            if (showCheckBox)
            {
                var cb = new CheckBox
                {
                    IsChecked = isChecked,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                cb.Checked += (s, e) => RaiseVisibility(kind, itemId, true);
                cb.Unchecked += (s, e) => RaiseVisibility(kind, itemId, false);
                sp.Children.Add(cb);
            }

            if (!string.IsNullOrEmpty(glyph))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = glyph,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.DimGray,
                    FontSize = 14
                });
            }

            sp.Children.Add(new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center
            });

            if (count.HasValue)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = " (" + count.Value + ")",
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray,
                    FontSize = 11
                });
            }

            if (!string.IsNullOrEmpty(statusText))
            {
                sp.Children.Add(new Border
                {
                    Margin = new Thickness(8, 0, 0, 0),
                    Padding = new Thickness(4, 1, 4, 1),
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(Color.FromArgb(30,
                        ((SolidColorBrush)(statusBrush ?? Brushes.Gray)).Color.R,
                        ((SolidColorBrush)(statusBrush ?? Brushes.Gray)).Color.G,
                        ((SolidColorBrush)(statusBrush ?? Brushes.Gray)).Color.B)),
                    Child = new TextBlock
                    {
                        Text = statusText,
                        FontSize = 10,
                        Foreground = statusBrush ?? Brushes.Gray
                    },
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            return sp;
        }

        private ContextMenu BuildContextMenu(NodeKind kind, string itemId)
        {
            var menu = new ContextMenu();
            void AddItem(string text, ProjectTreeAction action)
            {
                var mi = new WpfMenuItem { Header = text };
                mi.Click += (s, e) => ActionRequested?.Invoke(this,
                    new ProjectTreeActionEventArgs(action, itemId));
                menu.Items.Add(mi);
            }

            switch (kind)
            {
                case NodeKind.GeneralRoot:
                    AddItem("Edit...", ProjectTreeAction.EditGeneralSettings); break;
                case NodeKind.GasesRoot:
                    AddItem("Add Pure Gas...", ProjectTreeAction.AddPureGas);
                    AddItem("Add Mixture...", ProjectTreeAction.AddMixture);
                    break;
                case NodeKind.GasItem:
                    AddItem("Edit...", ProjectTreeAction.EditGas);
                    AddItem("Duplicate", ProjectTreeAction.DuplicateGas);
                    AddItem("Delete", ProjectTreeAction.DeleteGas);
                    break;
                case NodeKind.GeometryRoot:
                    AddItem("Import 3D Model...", ProjectTreeAction.ImportGeometry); break;
                case NodeKind.GeometryItem:
                    AddItem("Edit...", ProjectTreeAction.EditGeometry);
                    AddItem("Delete", ProjectTreeAction.DeleteGeometry);
                    break;
                case NodeKind.SourcesRoot:
                    AddItem("Add Source...", ProjectTreeAction.AddSource); break;
                case NodeKind.SourceItem:
                    AddItem("Edit...", ProjectTreeAction.EditSource);
                    AddItem("Use in New Simulation...", ProjectTreeAction.NewSimulationFromSource);
                    AddItem("Duplicate", ProjectTreeAction.DuplicateSource);
                    AddItem("Delete", ProjectTreeAction.DeleteSource);
                    break;
                case NodeKind.WindFieldsRoot:
                    AddItem("Add Wind Field...", ProjectTreeAction.AddWindField);
                    AddItem("Open Manager...", ProjectTreeAction.OpenWindFieldManager);
                    break;
                case NodeKind.WindFieldItem:
                    AddItem("Edit...", ProjectTreeAction.EditWindField);
                    AddItem("Run", ProjectTreeAction.RunWindField);
                    AddItem("Open Case Folder", ProjectTreeAction.OpenWindFieldCase);
                    AddItem("Delete", ProjectTreeAction.DeleteWindField);
                    break;
                case NodeKind.SimulationsRoot:
                    AddItem("New Simulation...", ProjectTreeAction.AddSimulation); break;
                case NodeKind.SimulationItem:
                    AddItem("Configure & Run", ProjectTreeAction.RunSimulation);
                    AddItem("Re-run", ProjectTreeAction.RerunSimulation);
                    AddItem("View Results", ProjectTreeAction.ViewSimulationResults);
                    AddItem("Open Case Folder", ProjectTreeAction.OpenSimulationCase);
                    AddItem("Edit...", ProjectTreeAction.EditSimulation);
                    AddItem("Delete", ProjectTreeAction.DeleteSimulation);
                    break;
                case NodeKind.ViewsRoot:
                    AddItem("Add View...", ProjectTreeAction.AddView); break;
                case NodeKind.ViewItem:
                    AddItem("Edit...", ProjectTreeAction.EditView);
                    AddItem("Duplicate", ProjectTreeAction.DuplicateView);
                    AddItem("Delete", ProjectTreeAction.DeleteView);
                    break;
                case NodeKind.MonitorsRoot:
                    AddItem("Add Monitor Point...", ProjectTreeAction.AddMonitor); break;
                case NodeKind.MonitorItem:
                    AddItem("Edit...", ProjectTreeAction.EditMonitor);
                    AddItem("Delete", ProjectTreeAction.DeleteMonitor);
                    break;
                case NodeKind.DetectorsRoot:
                    AddItem("Add Detector...", ProjectTreeAction.AddDetector); break;
                case NodeKind.DetectorItem:
                    AddItem("Edit...", ProjectTreeAction.EditDetector);
                    AddItem("Delete", ProjectTreeAction.DeleteDetector);
                    break;
            }

            return menu.Items.Count == 0 ? null : menu;
        }

        private void Tree_SelectedItemChanged(object sender,
            RoutedPropertyChangedEventArgs<object> e)
        {
            var item = e.NewValue as WpfTreeViewItem;
            if (item == null) return;
            var tref = item.Tag as TreeRef;
            if (tref == null) return;

            object selected = ResolveSelectedObject(tref);
            string title = ResolveSelectedTitle(tref, selected);
            SelectionChanged?.Invoke(this, new ProjectTreeSelectionEventArgs(selected, title, tref.Kind, tref.ItemId));
        }

        private object ResolveSelectedObject(TreeRef tref)
        {
            if (_scene == null) return null;
            switch (tref.Kind)
            {
                case NodeKind.GeneralRoot: return _scene.GeneralSettings;
                case NodeKind.GasItem: return _scene.GasLibrary.FirstOrDefault(g => g.Id == tref.ItemId);
                case NodeKind.GeometryItem: return _scene.Decorations.FirstOrDefault(d => d.Id == tref.ItemId);
                case NodeKind.SourceItem: return _scene.TopLevelSources.FirstOrDefault(s => s.Id == tref.ItemId);
                case NodeKind.WindFieldItem: return _scene.WindFieldScenarios.FirstOrDefault(w => w.Id == tref.ItemId);
                case NodeKind.SimulationItem: return _scene.Simulations.FirstOrDefault(s => s.Id == tref.ItemId);
                case NodeKind.ViewItem: return _scene.Views.FirstOrDefault(v => v.Id == tref.ItemId);
                case NodeKind.MonitorItem: return _scene.MonitorPoints.FirstOrDefault(m => m.Name == tref.ItemId);
                case NodeKind.DetectorItem: return _scene.GasDetectors.FirstOrDefault(d => d.Id == tref.ItemId);
                default: return null;
            }
        }

        private string ResolveSelectedTitle(TreeRef tref, object selected)
        {
            if (selected == null) return null;
            switch (tref.Kind)
            {
                case NodeKind.GeneralRoot: return "General Settings";
                case NodeKind.GasItem: return ((GasLibraryItem)selected).Name;
                case NodeKind.GeometryItem: return ((Decoration3D)selected).Name;
                case NodeKind.SourceItem: return ((ReleaseSource3D)selected).Name;
                case NodeKind.WindFieldItem: return ((WindFieldScenario)selected).Name;
                case NodeKind.SimulationItem: return ((Simulation)selected).Name;
                case NodeKind.ViewItem: return ((View)selected).Name;
                case NodeKind.MonitorItem: return ((MonitorPoint3D)selected).Name;
                case NodeKind.DetectorItem: return ((GasDetector3D)selected).Name;
                default: return null;
            }
        }

        private void RaiseVisibility(NodeKind kind, string itemId, bool visible)
        {
            ProjectTreeTarget target;
            switch (kind)
            {
                case NodeKind.GeometryItem: target = ProjectTreeTarget.Geometry; break;
                case NodeKind.SourceItem: target = ProjectTreeTarget.Source; break;
                case NodeKind.WindFieldItem: target = ProjectTreeTarget.WindField; break;
                case NodeKind.SimulationItem: target = ProjectTreeTarget.Simulation; break;
                case NodeKind.ViewItem: target = ProjectTreeTarget.View; break;
                case NodeKind.MonitorItem: target = ProjectTreeTarget.Monitor; break;
                case NodeKind.DetectorItem: target = ProjectTreeTarget.Detector; break;
                default: return;
            }
            VisibilityChanged?.Invoke(this,
                new ProjectTreeVisibilityEventArgs(target, itemId, visible));
        }

        private class TreeRef
        {
            public NodeKind Kind;
            public string ItemId;
            public TreeRef(NodeKind k, string id) { Kind = k; ItemId = id; }
        }

        private enum NodeKind
        {
            ProjectRoot,
            GeneralRoot,
            GasesRoot, GasItem,
            GeometryRoot, GeometryItem,
            SourcesRoot, SourceItem,
            WindFieldsRoot, WindFieldItem,
            SimulationsRoot, SimulationItem,
            ViewsRoot, ViewItem,
            MonitorsRoot, MonitorItem,
            DetectorsRoot, DetectorItem
        }
    }

    public class ProjectTreeSelectionEventArgs : EventArgs
    {
        public object Selected { get; }
        public string Title { get; }
        public int KindOrdinal { get; }
        public string ItemId { get; }
        internal ProjectTreeSelectionEventArgs(object selected, string title, object kind, string id)
        {
            Selected = selected; Title = title; KindOrdinal = (int)kind; ItemId = id;
        }
    }

    public enum ProjectTreeAction
    {
        EditGeneralSettings,
        AddPureGas, AddMixture, EditGas, DuplicateGas, DeleteGas,
        ImportGeometry, EditGeometry, DeleteGeometry,
        AddSource, EditSource, DuplicateSource, DeleteSource, NewSimulationFromSource,
        AddWindField, EditWindField, RunWindField, OpenWindFieldCase, DeleteWindField, OpenWindFieldManager,
        AddSimulation, EditSimulation, RunSimulation, RerunSimulation,
        ViewSimulationResults, OpenSimulationCase, DeleteSimulation,
        AddView, EditView, DuplicateView, DeleteView,
        AddMonitor, EditMonitor, DeleteMonitor,
        AddDetector, EditDetector, DeleteDetector
    }

    public class ProjectTreeActionEventArgs : EventArgs
    {
        public ProjectTreeAction Action { get; }
        public string ItemId { get; }
        public ProjectTreeActionEventArgs(ProjectTreeAction action, string id) { Action = action; ItemId = id; }
    }

    public enum ProjectTreeTarget
    {
        None,
        Geometry,
        Source,
        WindField,
        Simulation,
        View,
        Monitor,
        Detector
    }

    public class ProjectTreeVisibilityEventArgs : EventArgs
    {
        public ProjectTreeTarget Target { get; }
        public string ItemId { get; }
        public bool Visible { get; }
        public ProjectTreeVisibilityEventArgs(ProjectTreeTarget target, string id, bool visible)
        {
            Target = target; ItemId = id; Visible = visible;
        }
    }
}
