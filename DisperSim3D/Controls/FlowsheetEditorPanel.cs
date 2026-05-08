using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media.Media3D;
using WeifenLuo.WinFormsUI.Docking;
using DisperSim3D.Core;
using DisperSim3D.Dialogs;
using DisperSim3D.Models;
using DisperSim3D.PropertyAdapters;

namespace DisperSim3D.Controls
{
    public class FlowsheetEditorPanel : UserControl
    {
        private FlowsheetEditor3DControl _editor;
        private MenuStrip _menuStrip;
        private ToolStrip _simToolStrip;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;
        private ToolStripStatusLabel _dispersionTimeLabel;
        private ToolStripButton _btnRun;
        private ToolStripButton _btnPlay;
        private ToolStripButton _btnPause;
        private ToolStripButton _btnStop;
        private Timer _dispersionStatusTimer;
        private DockPanel _dockPanel;
        private PropertiesDockPanel _propertiesDock;
        private CfdSimulationsDockPanel _cfdSimDock;
        private MonitorDockPanel _monitorDock;
        private AddItemDockPanel _addItemDock;
        private ViewportDockPanel _viewportDock;
        private PropertyGrid _propertyGrid;
        private DataGridView _monitorGrid;
        private bool _monitorPanelVisible;
        private ToolStripComboBox _scenarioCombo;
        private AddItemPanel _addItemPanel;
        private bool _addItemPanelVisible;
        private bool _dispersionToolsVisible = true;
        private CfdSimulationsPanel _cfdSimPanel;
        private ToolStripMenuItem _miSelectMode;
        private ToolStripMenuItem _miSnap, _miGround, _miVectors;
        private string _resPath;

        public static string ResourcesBasePath { get; set; }

        public FlowsheetEditor3DControl Editor => _editor;
        public Scene3D Flowsheet => _editor.Flowsheet;

        public event EventHandler<string> StatusChanged;

        public FlowsheetEditorPanel()
        {
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            DiscoverResourcesPath();
            BuildUI();
        }

        private void DiscoverResourcesPath()
        {
            if (!string.IsNullOrEmpty(ResourcesBasePath) && Directory.Exists(ResourcesBasePath))
            {
                _resPath = ResourcesBasePath;
                return;
            }
            var asmDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (asmDir == null) return;
            foreach (var sub in new[] {
                Path.Combine("Resources", "Icons"),
                "Resources",
                Path.Combine("..", "Resources", "Icons"),
                Path.Combine("..", "Resources") })
            {
                var p = Path.GetFullPath(Path.Combine(asmDir, sub));
                if (Directory.Exists(p)) { _resPath = p; return; }
            }
        }

        private Image Img(string name)
        {
            if (_resPath == null) return null;
            try
            {
                var path = Path.Combine(_resPath, name);
                if (File.Exists(path))
                    return Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));
            }
            catch { }
            return null;
        }

        private void BuildUI()
        {
            var dpiScale = this.DeviceDpi / 96f;
            var toolFont = new System.Drawing.Font("Segoe UI", 9f);

            // === StatusStrip ===
            _statusStrip = new StatusStrip { Font = toolFont };
            _statusLabel = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            _dispersionTimeLabel = new ToolStripStatusLabel("");
            _statusStrip.Items.Add(_statusLabel);
            _statusStrip.Items.Add(_dispersionTimeLabel);

            // === MenuStrip ===
            _menuStrip = new MenuStrip { Font = toolFont };

            // --- File menu ---
            var menuFile = new ToolStripMenuItem("&File");
            menuFile.DropDownItems.AddRange(new ToolStripItem[] {
                new ToolStripMenuItem("New (Clear)", Img("new.png"), (s, e) => DoClear()),
                new ToolStripMenuItem("Open...", Img("folder_go.png"), (s, e) => DoLoad()),
                new ToolStripMenuItem("Save...", Img("disk.png"), (s, e) => DoSave()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Import 3D Model...", Img("icons8-import.png"), (s, e) => DoImport3D()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Batch Export Images...", Img("icons8-export.png"), (s, e) => DoBatchExport())
            });

            // --- Edit menu ---
            var menuEdit = new ToolStripMenuItem("&Edit");
            _miSelectMode = new ToolStripMenuItem("Select Mode", Img("cursor.png")) { CheckOnClick = true, Checked = true };
            _miSelectMode.Click += (s, e) => { _editor.CurrentEditMode = EditMode.Select; UncheckAllModes(); UpdateStatus("Mode: Select"); };
            menuEdit.DropDownItems.AddRange(new ToolStripItem[] {
                _miSelectMode,
                new ToolStripSeparator(),
                new ToolStripMenuItem("Delete", Img("cross.png"), (s, e) => { if (_editor.SelectedDecoration != null) _editor.DeleteSelectedDecoration(); }),
                new ToolStripMenuItem("Scale +", Img("zoom_in.png"), (s, e) => { if (_editor.SelectedDecoration != null) _editor.ScaleSelectedDecoration(1.2); }),
                new ToolStripMenuItem("Scale -", Img("zoom_out.png"), (s, e) => { if (_editor.SelectedDecoration != null) _editor.ScaleSelectedDecoration(1.0 / 1.2); })
            });

            // --- Insert menu ---
            var menuInsert = new ToolStripMenuItem("&Insert");
            var miContour = new ToolStripMenuItem("Contour Plane", Img("layers.png"));
            miContour.DropDownItems.AddRange(new ToolStripItem[] {
                new ToolStripMenuItem("XY Plane", Img("shape_align_bottom.png"), (s, e) => { EnsureScenario().ContourPlanes.Add(new ContourPlaneConfig { Axis = ContourAxis.XY, Position = 2.0 }); UpdateStatus("Contour plane added (XY)"); }),
                new ToolStripMenuItem("XZ Plane", Img("shape_align_middle.png"), (s, e) => { EnsureScenario().ContourPlanes.Add(new ContourPlaneConfig { Axis = ContourAxis.XZ, Position = 2.0 }); UpdateStatus("Contour plane added (XZ)"); }),
                new ToolStripMenuItem("YZ Plane", Img("shape_align_left.png"), (s, e) => { EnsureScenario().ContourPlanes.Add(new ContourPlaneConfig { Axis = ContourAxis.YZ, Position = 2.0 }); UpdateStatus("Contour plane added (YZ)"); }),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Remove Last Contour", Img("delete.png"), (s, e) =>
                {
                    var scenario = _editor.Flowsheet.DispersionScenario;
                    if (scenario != null && scenario.ContourPlanes.Count > 0)
                    {
                        scenario.ContourPlanes.RemoveAt(scenario.ContourPlanes.Count - 1);
                        UpdateStatus("Contour plane removed");
                    }
                })
            });
            menuInsert.DropDownItems.AddRange(new ToolStripItem[] {
                new ToolStripMenuItem("Release Source...", Img("icons8-humidity.png"), (s, e) => DoAddSource()),
                new ToolStripMenuItem("Monitor Point...", Img("icons8-location.png"), (s, e) => DoAddMonitor()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Fire Source...", Img("lightning.png"), (s, e) => DoAddFireSource()),
                new ToolStripMenuItem("Gas Detector", Img("icons8-pressure_gauge.png"), (s, e) => DoAddDetector()),
                new ToolStripMenuItem("HP Leak...", Img("icons8-petrol.png"), (s, e) => DoConfigureHPLeak()),
                new ToolStripSeparator(),
                miContour,
                new ToolStripMenuItem("Streamline Seed", Img("icons8-vector.png"), (s, e) =>
                {
                    var scenario = EnsureScenario();
                    scenario.StreamlineSeedPoints.Add(new Point3D(0, 0, 2));
                    UpdateStatus("Streamline seed added (" + scenario.StreamlineSeedPoints.Count + " total)");
                })
            });

            // --- Dispersion menu ---
            var menuDispersion = new ToolStripMenuItem("&Dispersion");
            menuDispersion.DropDownItems.AddRange(new ToolStripItem[] {
                new ToolStripMenuItem("Manage Scenarios...", Img("icons8-layers.png"), (s, e) => DoManageScenarios()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Meteorological Conditions...", Img("icons8-weather.png"), (s, e) => DoMeteo()),
                new ToolStripMenuItem("Gas Mixture...", Img("icons8-test_tube.png"), (s, e) => DoGasMixture()),
                new ToolStripMenuItem("Wind Rose...", Img("icons8-wind.png"), (s, e) => DoWindRose()),
                new ToolStripMenuItem("Wind Profile...", Img("icons8-realtime.png"), (s, e) => DoTransientWind()),
                new ToolStripMenuItem("Thresholds...", Img("icons8-slider.png"), (s, e) => DoThresholds()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("CFD Settings...", Img("cog.png"), (s, e) => DoCfdSettings()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Exceedance Curves...", Img("icons8-combo_chart.png"), (s, e) => DoExceedanceCurves()),
                new ToolStripMenuItem("Detector Results...", Img("icons8-scatter_plot.png"), (s, e) => DoShowDetectorResults()),
                new ToolStripMenuItem("Export Monitor CSV...", Img("card_export.png"), (s, e) => DoExportMonitorCsv())
            });

            // --- View menu ---
            var menuView = new ToolStripMenuItem("&View");
            var miCamera = new ToolStripMenuItem("Camera", Img("application_view_icons.png"));
            miCamera.DropDownItems.AddRange(new ToolStripItem[] {
                new ToolStripMenuItem("Isometric", Img("shape_handles.png"), (s, e) => _editor.CurrentCameraMode = CameraMode.Isometric),
                new ToolStripMenuItem("Top", Img("shape_align_bottom.png"), (s, e) => _editor.CurrentCameraMode = CameraMode.TopDown),
                new ToolStripMenuItem("Front", Img("shape_align_center.png"), (s, e) => _editor.CurrentCameraMode = CameraMode.Front),
                new ToolStripMenuItem("Side", Img("shape_align_left.png"), (s, e) => _editor.CurrentCameraMode = CameraMode.Side)
            });
            _miSnap = new ToolStripMenuItem("Snap to Grid", Img("shape_align_middle.png")) { CheckOnClick = true, Checked = true };
            _miSnap.Click += (s, e) => { _editor.SnapToGrid = _miSnap.Checked; UpdateStatus("Snap to grid: " + _miSnap.Checked); };
            _miGround = new ToolStripMenuItem("Ground Plane", Img("shape_square.png")) { CheckOnClick = true, Checked = true };
            _miGround.Click += (s, e) => { _editor.ShowGroundPlane = _miGround.Checked; };
            _miVectors = new ToolStripMenuItem("Vector Field", Img("icons8-vector.png")) { CheckOnClick = true };
            _miVectors.Click += (s, e) => { _editor.ShowVectorField = _miVectors.Checked; };
            menuView.DropDownItems.AddRange(new ToolStripItem[] {
                miCamera,
                new ToolStripMenuItem("Save Camera Preset", Img("icons8-save_as.png"), (s, e) => DoSaveCameraPreset()),
                new ToolStripSeparator(),
                _miSnap, _miGround, _miVectors,
                new ToolStripSeparator(),
                new ToolStripMenuItem("Properties Panel", Img("table.png"), (s, e) => ShowDockPanel(_propertiesDock, DockState.DockRight)),
                new ToolStripMenuItem("Add Item Panel", Img("add.png"), (s, e) => ToggleAddItemPanel(true)),
                new ToolStripMenuItem("CFD Simulations", Img("control_play_blue.png"), (s, e) => ToggleCfdSimPanel(true)),
                new ToolStripMenuItem("Monitors", Img("icons8-ecg.png"), (s, e) => ToggleMonitorPanel(true))
            });

            _menuStrip.Items.AddRange(new ToolStripItem[] { menuFile, menuEdit, menuInsert, menuDispersion, menuView });

            // === Simulation ToolStrip ===
            _simToolStrip = new ToolStrip
            {
                Font = toolFont,
                ImageScalingSize = new System.Drawing.Size((int)(20 * dpiScale), (int)(20 * dpiScale)),
                AutoSize = true,
                Padding = new Padding((int)(2 * dpiScale))
            };

            _scenarioCombo = new ToolStripComboBox("Scenario") { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, ToolTipText = "Active scenario" };
            _scenarioCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_scenarioCombo.SelectedIndex >= 0)
                {
                    _editor.Flowsheet.ActiveScenarioIndex = _scenarioCombo.SelectedIndex;
                    UpdateStatus("Scenario: " + _scenarioCombo.SelectedItem);
                }
            };

            var solverCombo = new ToolStripComboBox("Solver") { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, ToolTipText = "Solver type" };
            solverCombo.Items.AddRange(new object[] { "Gaussian Puff", "CFD (OpenFOAM)" });
            solverCombo.SelectedIndex = 0;
            solverCombo.SelectedIndexChanged += (s, e) =>
            {
                var sc = _editor.Flowsheet.DispersionScenario;
                if (sc != null)
                    sc.SolverType = solverCombo.SelectedIndex == 0
                        ? CfdSolverType.GaussianPuff
                        : CfdSolverType.ScalarTransportFoam;
            };

            _btnRun = new ToolStripButton("Run", Img("control_play_blue.png")) { ToolTipText = "Run CFD / Gaussian Puff simulation" };
            _btnRun.Click += (s, e) =>
            {
                var sc = _editor.Flowsheet.DispersionScenario;
                if (sc == null || sc.Sources.Count == 0) return;
                if (sc.SolverType == CfdSolverType.ScalarTransportFoam)
                {
                    if (sc.CfdConfig == null)
                        sc.CfdConfig = AppSettings.Instance.CreateCfdConfig();
                    _cfdSimPanel.ShowSolveProgress();
                    ShowDockPanel(_cfdSimDock, DockState.DockBottom);
                    _editor.StartCfdSolve(sc.CfdConfig);
                }
                else
                {
                    _cfdSimPanel.ShowSolveProgress();
                    ShowDockPanel(_cfdSimDock, DockState.DockBottom);
                    _editor.RunGaussianPuffAsync();
                }
                UpdatePlaybackButtons();
            };

            _btnPlay = new ToolStripButton("Play", Img("control_play.png")) { ToolTipText = "Play back results", Enabled = false };
            _btnPlay.Click += (s, e) =>
            {
                var ds = _editor.DispersionState;
                if (ds == DispersionSimulationState.Paused)
                    _editor.ResumeDispersion();
                else if (ds == DispersionSimulationState.Stopped &&
                         _editor.CfdResult != null && _editor.CfdResult.IsLoaded)
                    _editor.StartCfdPlayback();
                UpdatePlaybackButtons();
            };

            _btnPause = new ToolStripButton("Pause", Img("icons8-pause.png")) { ToolTipText = "Pause playback", Enabled = false };
            _btnPause.Click += (s, e) => { _editor.PauseDispersion(); UpdatePlaybackButtons(); };

            _btnStop = new ToolStripButton("Stop", Img("control_stop.png")) { ToolTipText = "Stop", Enabled = false };
            _btnStop.Click += (s, e) =>
            {
                _editor.CfdRunner?.Cancel();
                _editor.CancelGaussianPuff();
                _editor.StopDispersion();
                UpdatePlaybackButtons();
                _dispersionTimeLabel.Text = "";
            };

            var speedCombo = new ToolStripComboBox("Speed") { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60, ToolTipText = "Animation speed" };
            speedCombo.Items.AddRange(new object[] { "0.25x", "0.5x", "1x", "2x", "5x", "10x" });
            speedCombo.SelectedIndex = 2;
            speedCombo.SelectedIndexChanged += (s, e) =>
            {
                double[] speeds = { 0.25, 0.5, 1.0, 2.0, 5.0, 10.0 };
                _editor.AnimationSpeedFactor = speeds[speedCombo.SelectedIndex];
            };

            var nudGroundLevel = new ToolStripTextBox { Text = "0", Width = 45, ToolTipText = "Ground level elevation (m)" };
            nudGroundLevel.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    double val;
                    if (double.TryParse(nudGroundLevel.Text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out val))
                    {
                        _editor.GroundLevel = val;
                        UpdateStatus("Ground level: " + val + " m");
                    }
                    e.SuppressKeyPress = true;
                }
            };

            var nudGroundSize = new ToolStripTextBox { Text = "200", Width = 45, ToolTipText = "Ground plane size (m)" };
            nudGroundSize.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    double val;
                    if (double.TryParse(nudGroundSize.Text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out val) && val > 0)
                    {
                        _editor.GroundSize = val;
                        UpdateStatus("Ground size: " + val + " m");
                    }
                    e.SuppressKeyPress = true;
                }
            };

            _simToolStrip.Items.AddRange(new ToolStripItem[] {
                new ToolStripLabel("Scenario:"), _scenarioCombo,
                new ToolStripSeparator(),
                new ToolStripLabel("Solver:"), solverCombo,
                new ToolStripSeparator(),
                _btnRun, _btnPlay, _btnPause, _btnStop,
                new ToolStripSeparator(),
                new ToolStripLabel("Speed:"), speedCombo,
                new ToolStripSeparator(),
                new ToolStripLabel("Ground Z:"), nudGroundLevel,
                new ToolStripLabel("Size:"), nudGroundSize
            });

            // --- Editor control ---
            _editor = new FlowsheetEditor3DControl { Dock = DockStyle.Fill };

            _editor.EditModeChanged += (s, e) => { UpdateStatus("Mode: " + _editor.CurrentEditMode); UncheckAllModes(); };

            _editor.MonitorDataUpdated += (s, e) => UpdateMonitorGrid();

            _editor.PointPicked += (s, pt) =>
            {
                if (_addItemPanelVisible && _addItemPanel.IsPickActive)
                {
                    _addItemPanel.SetPickedPosition(pt);
                }
            };

            _editor.SelectedUnitChanged += (s, e) =>
            {
                if (_editor.SelectedDecoration != null)
                {
                    UpdateStatus("Decoration: " + _editor.SelectedDecoration.Name);
                    ShowDecorationProperties(_editor.SelectedDecoration);
                }
                else if (_editor.SelectedSource != null)
                {
                    var src = _editor.SelectedSource;
                    UpdateStatus("Release Source: " + src.Name);
                    ShowSourceProperties(src);
                }
                else
                {
                    UpdateStatus("No selection");
                    ClearPropertyGrid();
                }
            };

            // --- Properties dock panel ---
            _propertiesDock = new PropertiesDockPanel();
            _propertyGrid = _propertiesDock.PropertyGrid;
            _propertyGrid.PropertyValueChanged += (s2, e2) => _editor.RefreshViewport();

            // --- Monitor dock panel ---
            _monitorDock = new MonitorDockPanel();
            _monitorGrid = _monitorDock.MonitorGrid;

            // --- Add Item dock panel ---
            _addItemPanel = new AddItemPanel();
            _addItemPanel.PickRequested += (s, pt) =>
            {
                _editor.CurrentEditMode = EditMode.Select;
            };
            _addItemPanel.ItemAdded += AddItemPanel_ItemAdded;
            _addItemPanel.Cancelled += (s, ev) =>
            {
                ToggleAddItemPanel(false);
                _editor.CurrentEditMode = EditMode.Select;
            };
            _addItemDock = new AddItemDockPanel(_addItemPanel);
            _addItemDock.DockStateChanged += (s, ev) =>
            {
                _addItemPanelVisible = _addItemDock.DockState != DockState.Hidden;
            };

            // --- CFD Simulations dock panel ---
            _cfdSimPanel = new CfdSimulationsPanel();
            _cfdSimDock = new CfdSimulationsDockPanel(_cfdSimPanel);
            _cfdSimPanel.CancelSolveRequested += (s, ev) =>
            {
                _editor.CfdRunner?.Cancel();
                _editor.CancelGaussianPuff();
            };
            _cfdSimPanel.PlayRequested += (s, entry) =>
            {
                if (!_editor.LoadCfdSimulation(entry))
                    MessageBox.Show("Could not load results from:\n" + entry.CasePath,
                        "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    UpdatePlaybackButtons();
            };
            _cfdSimPanel.DeleteRequested += (s, entry) =>
            {
                if (MessageBox.Show("Delete simulation '" + entry.Name + "'?",
                    "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _editor.Flowsheet.CfdSimulations.Remove(entry);
                    _cfdSimPanel.RemoveEntry(entry);
                    if (entry.CasePath != null && System.IO.Directory.Exists(entry.CasePath))
                    {
                        try { System.IO.Directory.Delete(entry.CasePath, true); } catch { }
                    }
                }
            };
            _cfdSimPanel.OpenFolderRequested += (s, entry) =>
            {
                if (entry.CasePath != null && System.IO.Directory.Exists(entry.CasePath))
                    System.Diagnostics.Process.Start("explorer.exe", entry.CasePath);
            };
            _editor.CfdProgressUpdated += (s, p) =>
            {
                _cfdSimPanel.UpdateProgress(p);
                if (p.IsError)
                {
                    ShowDockPanel(_cfdSimDock, DockState.DockBottom);
                    _cfdSimPanelUserVisible = true;
                    _dispersionTimeLabel.Text = "CFD failed";
                    _btnRun.Enabled = true;
                    _btnStop.Enabled = false;
                }
                else
                {
                    UpdatePlaybackButtons();
                }
            };
            _editor.CfdSolveCompleted += (s, entry) =>
            {
                _cfdSimPanel.AddEntry(entry);
                if (entry.HasResults)
                    _cfdSimPanel.HideSolveProgress();
                _cfdSimPanelUserVisible = true;
                ShowDockPanel(_cfdSimDock, DockState.DockBottom);
                UpdatePlaybackButtons();
            };

            // --- Viewport dock panel ---
            _viewportDock = new ViewportDockPanel(_editor);

            // --- DockPanel layout ---
            _dockPanel = new DockPanel
            {
                Dock = DockStyle.Fill,
                DocumentStyle = DocumentStyle.DockingSdi,
                DefaultFloatWindowSize = new System.Drawing.Size(400, 300),
                Theme = new WeifenLuo.WinFormsUI.Docking.VS2015LightTheme()
            };

            // --- DPI scaling for strips ---
            _simToolStrip.ApplyDpiScaling(dpiScale);
            _menuStrip.ApplyDpiScaling(dpiScale);

            // --- Assemble ---
            this.Controls.Add(_dockPanel);
            this.Controls.Add(_simToolStrip);
            this.Controls.Add(_menuStrip);
            this.Controls.Add(_statusStrip);

            // Show dock contents (order matters: document first, then panels)
            _viewportDock.Show(_dockPanel, DockState.Document);
            _propertiesDock.Show(_dockPanel, DockState.DockRight);
            _cfdSimDock.Show(_dockPanel, DockState.DockBottom);
            _cfdSimDock.DockState = DockState.Hidden;
            _monitorDock.Show(_dockPanel, DockState.DockBottom);
            _monitorDock.DockState = DockState.Hidden;
            _addItemDock.Show(_dockPanel, DockState.DockLeft);
            _addItemDock.DockState = DockState.Hidden;
        }

        #region Public API

        public bool DispersionToolsVisible
        {
            get => _dispersionToolsVisible;
            set
            {
                _dispersionToolsVisible = value;
                SetDispersionToolsVisible(value);
            }
        }

        private void SetDispersionToolsVisible(bool visible)
        {
            _dispersionToolsVisible = visible;
            _simToolStrip.Visible = visible;
            _dispersionTimeLabel.Visible = visible;
            if (!visible)
            {
                _monitorDock.DockState = DockState.Hidden;
                _monitorPanelVisible = false;
            }
        }

        public void SaveToFile(string filePath)
        {
            _editor.SaveToFile(filePath);
            UpdateStatus("Saved: " + filePath);
        }

        public void LoadFromFile(string filePath)
        {
            _editor.LoadFromFile(filePath);
            UpdateStatus("Loaded: " + filePath);
        }

        public void ClearFlowsheet()
        {
            _editor.ClearFlowsheet();
            ClearPropertyGrid();
            UpdateStatus("Flowsheet cleared");
        }

        public bool HandleKeyDown(Keys keyCode, bool ctrl)
        {
            switch (keyCode)
            {
                case Keys.S:
                    if (!ctrl) { _editor.CurrentEditMode = EditMode.Select; UncheckAllModes(); }
                    return true;
                case Keys.G:
                    _editor.SnapToGrid = !_editor.SnapToGrid;
                    _miSnap.Checked = _editor.SnapToGrid;
                    UpdateStatus("Snap: " + _editor.SnapToGrid);
                    return true;
                case Keys.Oemplus:
                case Keys.Add:
                    if (_editor.SelectedDecoration != null) _editor.ScaleSelectedDecoration(1.2);
                    return true;
                case Keys.OemMinus:
                case Keys.Subtract:
                    if (_editor.SelectedDecoration != null) _editor.ScaleSelectedDecoration(1.0 / 1.2);
                    return true;
            }
            return false;
        }

        #endregion

        #region Actions

        private void DoSave()
        {
            using (var dlg = new SaveFileDialog { Filter = "XML files (*.xml)|*.xml", DefaultExt = "xml" })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                    SaveToFile(dlg.FileName);
            }
        }

        private void DoLoad()
        {
            using (var dlg = new OpenFileDialog { Filter = "XML files (*.xml)|*.xml" })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                    LoadFromFile(dlg.FileName);
            }
        }

        private void DoClear()
        {
            if (MessageBox.Show("Clear the entire flowsheet?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                ClearFlowsheet();
        }

        private void DoImport3D()
        {
            using (var fileDlg = new OpenFileDialog
            {
                Filter = Core.ModelLoader.GetSupportedFormatsFilter(),
                Title = "Import 3D Model"
            })
            {
                if (fileDlg.ShowDialog() != DialogResult.OK) return;

                var model = _editor.LoadModelFile(fileDlg.FileName);
                if (model == null)
                {
                    MessageBox.Show("Failed to load the 3D model file.", "Import Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                using (var importDlg = new ImportModelDialog(model, fileDlg.FileName))
                {
                    if (importDlg.ShowDialog() == DialogResult.OK)
                    {
                        var pos = new Point3D(importDlg.PosX, importDlg.PosY, importDlg.PosZ);
                        var rot = new Vector3D(importDlg.RotX, importDlg.RotY, importDlg.RotZ);
                        var deco = _editor.ImportDecorationWithTransform(fileDlg.FileName, model, pos, rot, importDlg.ModelScale);
                        if (deco != null)
                            UpdateStatus("Imported: " + deco.Name);
                    }
                }
            }
        }

        private void DoAddSource()
        {
            double windDir = 0;
            var sc = _editor.Flowsheet.DispersionScenario;
            if (sc != null)
                windDir = sc.Meteo.WindDirectionDeg;
            using (var dlg = new DispersionSourceDialog(windDir))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _editor.PendingSourceTemplate = new ReleaseSource3D
                    {
                        Name = dlg.SourceName,
                        Gas = dlg.Gas,
                        ReleaseRateKgPerS = dlg.ReleaseRateKgPerS,
                        ReleaseDurationS = dlg.ReleaseDurationS,
                        PuffIntervalS = dlg.PuffIntervalS,
                        ReleaseHeightOffset = dlg.HeightOffset,
                        ReleaseAzimuthDeg = dlg.AzimuthDeg,
                        ReleaseElevationDeg = dlg.ElevationDeg
                    };
                    _editor.CurrentEditMode = EditMode.PlaceReleaseSource;
                    UncheckAllModes();
                    UpdateStatus("Click to place: " + dlg.SourceName);
                }
            }
        }

        private void DoAddMonitor()
        {
            using (var dlg = new MonitorPointDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _editor.PendingMonitorTemplate = new Models.MonitorPoint3D
                    {
                        Name = dlg.MonitorName
                    };
                    _editor.CurrentEditMode = EditMode.PlaceMonitorPoint;
                    UncheckAllModes();
                    UpdateStatus("Click to place monitor: " + dlg.MonitorName);
                }
            }
        }

        private void DoExportMonitorCsv()
        {
            if (_editor.Flowsheet.MonitorPoints.Count == 0)
            {
                MessageBox.Show("No monitor points defined.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dlg = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", DefaultExt = "csv" })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _editor.ExportMonitorDataToCsv(dlg.FileName);
                    UpdateStatus("Monitor data exported: " + dlg.FileName);
                }
            }
        }

        private void DoSaveCameraPreset()
        {
            string name = "Camera " + (_editor.Flowsheet.CameraPresets.Count + 1);
            var preset = _editor.SaveCurrentCameraPreset(name);
            if (preset != null)
                UpdateStatus("Camera preset saved: " + name);
        }

        private void DoBatchExport()
        {
            var presets = _editor.Flowsheet.CameraPresets;
            if (presets.Count == 0)
            {
                MessageBox.Show("No camera presets saved. Save camera positions first.", "Batch Export",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new BatchExportDialog(presets))
            {
                if (dlg.ShowDialog() == DialogResult.OK && dlg.SelectedPresets.Count > 0)
                {
                    foreach (var preset in dlg.SelectedPresets)
                    {
                        _editor.ApplyCameraPreset(preset);
                        string path = System.IO.Path.Combine(dlg.OutputFolder,
                            preset.Name.Replace(" ", "_") + ".png");
                        _editor.ExportViewportImage(path, dlg.ImageWidth, dlg.ImageHeight);
                    }
                    UpdateStatus("Exported " + dlg.SelectedPresets.Count + " images");
                }
            }
        }

        private void DoWindRose()
        {
            using (var dlg = new WindRoseDialog(_editor.Flowsheet.WindRose))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _editor.Flowsheet.WindRose = dlg.Result;
                    _editor.RefreshViewport();
                    UpdateStatus("Wind rose updated (" + dlg.Result.Bins.Count + " bins)");

                    if (dlg.GenerateScenarios && dlg.Result.Bins.Count > 0)
                    {
                        var fs = _editor.Flowsheet;
                        foreach (var bin in dlg.Result.Bins)
                        {
                            var sc = new Models.DispersionScenario
                            {
                                Name = "Wind " + bin.DirectionDeg + "°",
                                Meteo = new Models.MeteorologicalConditions
                                {
                                    WindDirectionDeg = bin.DirectionDeg,
                                    WindSpeed = bin.WindSpeed,
                                    StabilityClass = bin.StabilityClass
                                }
                            };
                            fs.DispersionScenarios.Add(sc);
                        }
                        RefreshScenarioCombo();
                        UpdateStatus("Generated " + dlg.Result.Bins.Count + " scenarios from wind rose");
                    }
                }
            }
        }

        private void DoManageScenarios()
        {
            var fs = _editor.Flowsheet;
            if (fs.DispersionScenarios.Count == 0)
                fs.DispersionScenarios.Add(new Models.DispersionScenario());

            using (var dlg = new ScenarioManagerDialog(fs.DispersionScenarios, fs.ActiveScenarioIndex))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    fs.DispersionScenarios.Clear();
                    fs.DispersionScenarios.AddRange(dlg.Scenarios);
                    fs.ActiveScenarioIndex = dlg.SelectedIndex;
                    RefreshScenarioCombo();
                    UpdateStatus("Scenarios updated. Active: " + (fs.DispersionScenario?.Name ?? "none"));
                }
            }
        }

        public void RefreshScenarioCombo()
        {
            _scenarioCombo.Items.Clear();
            foreach (var sc in _editor.Flowsheet.DispersionScenarios)
                _scenarioCombo.Items.Add(sc.Name ?? "Scenario");
            if (_scenarioCombo.Items.Count > 0)
                _scenarioCombo.SelectedIndex = Math.Min(_editor.Flowsheet.ActiveScenarioIndex, _scenarioCombo.Items.Count - 1);
        }

        private void DoAddFireSource()
        {
            using (var dlg = new FireSourceDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _editor.PendingFireTemplate = dlg.Result;
                    _editor.CurrentEditMode = EditMode.PlaceFireSource;
                    UncheckAllModes();
                    UpdateStatus("Click to place fire: " + dlg.Result.Name);
                }
            }
        }

        private void DoAddDetector()
        {
            string name = "Detector" + (_editor.Flowsheet.GasDetectors.Count + 1);
            _editor.PendingDetectorTemplate = new Models.GasDetector3D { Name = name };
            _editor.CurrentEditMode = EditMode.PlaceGasDetector;
            UncheckAllModes();
            UpdateStatus("Click to place detector: " + name);
        }

        private void DoShowDetectorResults()
        {
            var dets = _editor.Flowsheet.GasDetectors;
            if (dets.Count == 0)
            {
                MessageBox.Show("No gas detectors defined.", "Detector Results",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = Core.DetectorEvaluator.ComputeResults(dets);
            using (var dlg = new DetectorResultsDialog(result, dets))
            {
                dlg.ShowDialog();
            }
        }

        private void DoConfigureHPLeak()
        {
            var scenario = _editor.Flowsheet.DispersionScenario;
            if (scenario == null || scenario.Sources.Count == 0)
            {
                MessageBox.Show("Add a release source first.", "HP Leak",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new HighPressureSourceDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    scenario.Sources[scenario.Sources.Count - 1].HighPressureLeak = dlg.Result;
                    UpdateStatus("HP leak configured for last source");
                }
            }
        }

        private void DoTransientWind()
        {
            var scenario = EnsureScenario();
            using (var dlg = new TransientWindDialog(scenario.TransientWind))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    scenario.TransientWind = dlg.Result;
                    UpdateStatus("Transient wind profile updated (" + dlg.Result.Entries.Count + " entries)" +
                        (dlg.Result.ESDTimeS >= 0 ? ", ESD at " + dlg.Result.ESDTimeS + "s" : ""));
                }
            }
        }

        private void DoGasMixture()
        {
            var scenario = EnsureScenario();
            using (var dlg = new GasMixtureDialog(scenario.GasMixture))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    scenario.GasMixture = dlg.Result;
                    UpdateStatus("Gas mixture updated (" + dlg.Result.Components.Count + " components)");
                }
            }
        }

        private void DoExceedanceCurves()
        {
            var monitors = _editor.Flowsheet.MonitorPoints;
            if (monitors.Count == 0)
            {
                MessageBox.Show("No monitor points with data. Run a simulation first.", "Exceedance",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool hasData = false;
            foreach (var m in monitors)
                if (m.TimeSeries.Count > 0) { hasData = true; break; }
            if (!hasData)
            {
                MessageBox.Show("No time-series data. Run a simulation with monitors first.", "Exceedance",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            double[] thresholds = { 1e-6, 1e-5, 1e-4, 1e-3, 1e-2, 0.05, 0.1, 0.5, 1.0 };
            var results = new System.Collections.Generic.List<Core.ExceedanceCurveResult>();
            foreach (var m in monitors)
            {
                if (m.TimeSeries.Count > 0)
                    results.Add(Core.ExceedanceCurveCalculator.ComputeFromTimeSeries(m, thresholds));
            }

            using (var dlg = new ExceedanceDialog(results))
            {
                dlg.ShowDialog();
            }
        }

        private void DoCfdSettings()
        {
            var scenario = EnsureScenario();
            if (scenario.CfdConfig == null)
                scenario.CfdConfig = AppSettings.Instance.CreateCfdConfig();

            using (var dlg = new CfdSettingsDialog(scenario.CfdConfig, _editor.CfdEnvironment))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    scenario.CfdConfig = dlg.Result;
                    AppSettings.Instance.UpdateFromConfig(dlg.Result);
                    UpdateStatus("CFD settings updated (" + dlg.Result.DetectedEnvironment + ")");
                }
            }
        }

        private void DoMeteo()
        {
            var scenario = EnsureScenario();
            using (var dlg = new MeteorologicalDialog(scenario.Meteo))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    scenario.Meteo = dlg.Result;
                    UpdateStatus("Meteo updated");
                }
            }
        }

        private void DoThresholds()
        {
            var scenario = EnsureScenario();
            using (var dlg = new ThresholdsDialog(scenario.Thresholds))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    scenario.Thresholds = dlg.Result;
                    UpdateStatus("Thresholds updated");
                }
            }
        }

        #endregion

        #region Property Grid

        private void ShowDecorationProperties(Decoration3D deco)
        {
            _propertiesDock.Text = "Properties - " + deco.Name;
            _propertyGrid.SelectedObject = new DecorationPropertyAdapter(deco, () =>
            {
                _editor.RefreshViewport();
                _propertiesDock.Text = "Properties - " + deco.Name;
            });
        }

        private void ShowSourceProperties(ReleaseSource3D source)
        {
            _propertiesDock.Text = "Properties - " + source.Name;
            _propertyGrid.SelectedObject = new PropertyAdapters.ReleaseSourcePropertyAdapter(source, () =>
            {
                _editor.RefreshViewport();
                _propertiesDock.Text = "Properties - " + source.Name;
            });
        }

        private void ClearPropertyGrid()
        {
            _propertiesDock.Text = "Properties";
            _propertyGrid.SelectedObject = null;
        }

        #endregion

        #region Helpers

        private DispersionScenario EnsureScenario()
        {
            if (_editor.Flowsheet.DispersionScenario == null)
                _editor.Flowsheet.DispersionScenario = new DispersionScenario();
            return _editor.Flowsheet.DispersionScenario;
        }

        private bool _cfdSimPanelUserVisible;

        private void ToggleCfdSimPanel(bool visible)
        {
            _cfdSimPanelUserVisible = visible;
            if (visible)
            {
                ShowDockPanel(_cfdSimDock, DockState.DockBottom);
                _cfdSimPanel.RefreshList(_editor.Flowsheet.CfdSimulations);
            }
            else
            {
                _cfdSimDock.DockState = DockState.Hidden;
            }
        }

        private void UpdatePlaybackButtons()
        {
            var state = _editor.DispersionState;
            bool isStopped = state == DispersionSimulationState.Stopped;
            bool isRunning = state == DispersionSimulationState.Running;
            bool isPaused = state == DispersionSimulationState.Paused;
            bool isSolving = state == DispersionSimulationState.SolvingCfd;

            _btnRun.Enabled = isStopped;
            _btnPlay.Enabled = isPaused || (isStopped && _editor.CfdResult != null && _editor.CfdResult.IsLoaded);
            _btnPause.Enabled = isRunning;
            _btnStop.Enabled = !isStopped;

            if (isSolving)
            {
                _cfdSimPanel.EnsureSolveProgressVisible();
                ShowDockPanel(_cfdSimDock, DockState.DockBottom);
                _dispersionTimeLabel.Text = "CFD solving...";
            }
            else if (isRunning)
            {
                _cfdSimPanel.HideSolveProgress();
                if (_dispersionStatusTimer == null)
                {
                    _dispersionStatusTimer = new Timer { Interval = 200 };
                    _dispersionStatusTimer.Tick += (s, e) =>
                    {
                        var ds = _editor.DispersionState;
                        if (ds == DispersionSimulationState.Running ||
                            ds == DispersionSimulationState.Paused)
                        {
                            var sc = _editor.Flowsheet.DispersionScenario;
                            _dispersionTimeLabel.Text = string.Format("T = {0:F1} s / {1:F0} s",
                                _editor.DispersionTimeS, sc != null ? sc.SimulationDurationS : 0);
                        }
                        else
                        {
                            _dispersionStatusTimer.Stop();
                        }
                    };
                }
                _dispersionStatusTimer.Start();
            }
            else if (isStopped)
            {
                _cfdSimPanel.HideSolveProgress();
                if (!_cfdSimPanelUserVisible)
                    _cfdSimDock.DockState = DockState.Hidden;
                _dispersionTimeLabel.Text = "";
            }
        }

        private void UncheckAllModes()
        {
            _miSelectMode.Checked = _editor.CurrentEditMode == EditMode.Select;
        }

        private void ToggleAddItemPanel(bool visible)
        {
            _addItemPanelVisible = visible;
            if (visible)
            {
                ShowDockPanel(_addItemDock, DockState.DockLeft);
                var scenario = _editor.Flowsheet.DispersionScenario;
                _addItemPanel.SetExistingSources(scenario?.Sources);
            }
            else
            {
                _addItemDock.DockState = DockState.Hidden;
            }
        }

        private void AddItemPanel_ItemAdded(object sender, AddItemEventArgs e)
        {
            var fs = _editor.Flowsheet;
            var scenario = EnsureScenario();

            switch (e.Type)
            {
                case AddItemType.GasLeakOrEmission:
                    if (e.ReleaseSource != null)
                    {
                        scenario.Sources.Add(e.ReleaseSource);
                        UpdateStatus("Added release source: " + e.Name);
                    }
                    break;

                case AddItemType.HighPressureGasLeak:
                    if (e.ReleaseSource != null)
                    {
                        scenario.Sources.Add(e.ReleaseSource);
                        UpdateStatus("Added HP leak source: " + e.Name);
                    }
                    break;

                case AddItemType.JetFire:
                case AddItemType.PoolFire:
                    if (e.FireSource != null)
                    {
                        fs.FireScenario.Sources.Add(e.FireSource);
                        UpdateStatus("Added fire source: " + e.Name);
                    }
                    break;

                case AddItemType.GasDetector:
                    if (e.GasDetector != null)
                    {
                        fs.GasDetectors.Add(e.GasDetector);
                        UpdateStatus("Added gas detector: " + e.Name);
                    }
                    break;

                case AddItemType.MonitorPoint:
                    if (e.MonitorPoint != null)
                    {
                        fs.MonitorPoints.Add(e.MonitorPoint);
                        UpdateStatus("Added monitor: " + e.Name);
                    }
                    break;

                case AddItemType.DispersionSimulation:
                    if (e.Scenario != null)
                    {
                        var existingScenario = fs.DispersionScenario;
                        if (existingScenario != null)
                        {
                            foreach (var src in existingScenario.Sources)
                            {
                                if (!e.Scenario.Sources.Contains(src))
                                    e.Scenario.Sources.Add(src);
                            }
                        }

                        int idx = fs.DispersionScenarios.Count;
                        fs.DispersionScenarios.Add(e.Scenario);
                        fs.ActiveScenarioIndex = idx;
                        RefreshScenarioCombo();
                        UpdateStatus("Added scenario: " + e.Name);

                        if (e.AutoRun)
                        {
                            _editor.StartDispersion();
                            UpdatePlaybackButtons();
                        }
                    }
                    break;
            }

            _editor.RefreshViewport();
        }

        private void ToggleMonitorPanel(bool visible)
        {
            _monitorPanelVisible = visible;
            if (visible)
            {
                ShowDockPanel(_monitorDock, DockState.DockBottom);
                UpdateMonitorGrid();
            }
            else
            {
                _monitorDock.DockState = DockState.Hidden;
            }
        }

        private void UpdateMonitorGrid()
        {
            if (!_monitorPanelVisible || _monitorGrid == null) return;

            var monitors = _editor.Flowsheet.MonitorPoints;
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            _monitorGrid.Rows.Clear();
            foreach (var m in monitors)
            {
                string pos = string.Format(inv, "({0:F1}, {1:F1}, {2:F1})",
                    m.Position.X, m.Position.Y, m.Position.Z);
                string conc = m.LastConcentration.ToString("E3", inv);
                string minMax = m.Type != Models.MonitorType.Point
                    ? string.Format(inv, "{0:E2} / {1:E2}", m.LastMinConcentration, m.LastMaxConcentration)
                    : "-";
                _monitorGrid.Rows.Add(m.Name, m.Type.ToString(), pos, conc, minMax);
            }
        }

        private void ShowDockPanel(DockContent panel, DockState defaultState)
        {
            if (panel.DockState == DockState.Hidden || panel.DockState == DockState.Unknown)
                panel.Show(_dockPanel, defaultState);
            panel.Activate();
        }

        private void UpdateStatus(string message)
        {
            _statusLabel.Text = message;
            StatusChanged?.Invoke(this, message);
        }

        #endregion
    }
}
