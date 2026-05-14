using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Media.Media3D;
using WeifenLuo.WinFormsUI.Docking;
using DisperSim3D.Core;
using DisperSim3D.Dialogs;
using DisperSim3D.Models;
using DisperSim3D.PropertyAdapters;

namespace DisperSim3D.Controls
{
    public class Scene3DEditorPanel : UserControl
    {
        private Scene3DEditorControl _editor;
        private MenuStrip _menuStrip;
        private ToolStrip _simToolStrip;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;
        private ToolStripStatusLabel _dispersionTimeLabel;
        private ToolStripProgressBar _ioProgressBar;
        private ToolStripStatusLabel _workDirLabel;
        private Timer _workDirTimer;
        private ToolStripButton _btnRun;
        private ToolStripButton _btnPlay;
        private ToolStripButton _btnPause;
        private ToolStripButton _btnStop;
        private Timer _dispersionStatusTimer;
        private DockPanel _dockPanel;
        private PropertiesDockPanel _propertiesDock;
        private ProjectTreeDockPanel _projectTreeDock;
        private ProjectTreeWpfPanel _projectTreePanel;
        private CfdSimulationsDockPanel _cfdSimDock;
        private MonitorDockPanel _monitorDock;
        private AddItemDockPanel _addItemDock;
        private ViewportDockPanel _viewportDock;
        private PropertyGridWpfPanel _propertyGrid;
        private DataGridView _monitorGrid;
        private bool _monitorPanelVisible;
        private ToolStripComboBox _scenarioCombo;
        private ToolStripComboBox _solverCombo;
        private AddItemPanel _addItemPanel;
        private bool _addItemPanelVisible;
        private bool _dispersionToolsVisible = true;
        private CfdSimulationsPanel _cfdSimPanel;
        private SimulationManagerDockPanel _simManagerDock;
        private ToolStripMenuItem _miSelectMode;
        private ToolStripMenuItem _miSnap, _miGround, _miVectors, _miWindArrows;
        private ToolStripMenuItem _miRecentFiles;
        private string _resPath;

        public static string ResourcesBasePath { get; set; }

        public Scene3DEditorControl Editor => _editor;
        public Scene3D Scene => _editor.Scene;

        public event EventHandler<string> StatusChanged;

        public Scene3DEditorPanel()
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
            // Hidden until SaveToFile / LoadFromFile fires the first
            // ProjectIoProgress event; the host hides it on Done=true.
            _ioProgressBar = new ToolStripProgressBar
            {
                Name = "ioProgressBar",
                Visible = false,
                Width = (int)(160 * dpiScale),
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous,
                Alignment = ToolStripItemAlignment.Right
            };
            _workDirLabel = new ToolStripStatusLabel("")
            {
                ForeColor = System.Drawing.Color.Gray,
                Alignment = ToolStripItemAlignment.Right
            };
            _statusStrip.Items.Add(_statusLabel);
            _statusStrip.Items.Add(_ioProgressBar);
            _statusStrip.Items.Add(_dispersionTimeLabel);
            _statusStrip.Items.Add(_workDirLabel);
            RefreshWorkDirStatus();

            _workDirTimer = new Timer { Interval = 30000 };
            _workDirTimer.Tick += (s, e) => RefreshWorkDirStatus();
            _workDirTimer.Start();

            // === MenuStrip ===
            _menuStrip = new MenuStrip { Font = toolFont };

            // --- File menu ---
            var menuFile = new ToolStripMenuItem("&File");
            _miRecentFiles = new ToolStripMenuItem("Recent &Files", Img("folder_go.png"));
            // Contents are rebuilt on demand from AppSettings.RecentFiles every time
            // the submenu opens, so the list reflects the latest MRU state even
            // across save/load operations performed in this session.
            _miRecentFiles.DropDownOpening += (s, e) => RebuildRecentFilesMenu();
            menuFile.DropDownItems.AddRange(new ToolStripItem[] {
                new ToolStripMenuItem("New (Clear)", Img("new.png"), (s, e) => DoClear()),
                new ToolStripMenuItem("Open...", Img("folder_go.png"), (s, e) => DoLoad()),
                _miRecentFiles,
                new ToolStripMenuItem("Save...", Img("disk.png"), (s, e) => DoSave()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Import 3D Model...", Img("icons8-import.png"), (s, e) => DoImport3D()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Batch Export Images...", Img("icons8-export.png"), (s, e) => DoBatchExport()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Working Directory...", null, (s, e) => DoWorkingDirectory()),
                new ToolStripMenuItem("Clean Temp Files...", null, (s, e) => DoCleanTempFiles()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("E&xit", Img("cross.png"), (s, e) => DoExit())
            });

            // --- Edit menu ---
            var menuEdit = new ToolStripMenuItem("&Edit");
            _miSelectMode = new ToolStripMenuItem("Select Mode", Img("cursor.png")) { CheckOnClick = true, Checked = true };
            _miSelectMode.Click += (s, e) => { _editor.CurrentEditMode = EditMode.Select; UncheckAllModes(); UpdateStatus("Mode: Select"); };
            menuEdit.DropDownItems.AddRange(new ToolStripItem[] {
                _miSelectMode,
                new ToolStripSeparator(),
                new ToolStripMenuItem("Delete", Img("cross.png"), (s, e) => _editor.DeleteSelected()),
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
                    var scenario = _editor.Scene.DispersionScenario;
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

            // --- Simulate menu ---
            // Replaces the old "Dispersion" menu. Legacy items that edited the
            // single project-level DispersionScenario (Meteo, Gas Mixture, Wind
            // Profile, Thresholds, Manage Scenarios) were removed — the new
            // pipeline configures those per Source / Wind Field / Simulation / View.
            var menuSimulate = new ToolStripMenuItem("&Simulate");
            menuSimulate.DropDownItems.AddRange(new ToolStripItem[] {
                // Run group
                new ToolStripMenuItem("New Simulation...", Img("icons8-vector.png"), (s, e) => DoNewSimulation(null)),
                new ToolStripMenuItem("Manage Wind Fields...", Img("icons8-wind.png"), (s, e) => DoManageWindFields()),
                new ToolStripMenuItem("Simulation Manager...", Img("table.png"), (s, e) => DoShowSimulationManager()),
                new ToolStripSeparator(),
                // Tools group
                new ToolStripMenuItem("Wind Rose...", Img("icons8-wind.png"), (s, e) => DoWindRose()),
                new ToolStripMenuItem("Optimize Detector Placement...", Img("icons8-ecg.png"), (s, e) => DoOptimizeDetectors()),
                new ToolStripMenuItem("Validate against Benchmarks...", Img("icons8-ecg.png"), (s, e) => DoValidateBenchmarks()),
                new ToolStripSeparator(),
                // Results group
                new ToolStripMenuItem("Exceedance Curves...", Img("icons8-combo_chart.png"), (s, e) => DoExceedanceCurves()),
                new ToolStripMenuItem("Detector Results...", Img("icons8-scatter_plot.png"), (s, e) => DoShowDetectorResults()),
                new ToolStripMenuItem("Export Monitor CSV...", Img("card_export.png"), (s, e) => DoExportMonitorCsv()),
                new ToolStripSeparator(),
                // Application-level settings group
                new ToolStripMenuItem("CFD Settings (Application)...", Img("cog.png"), (s, e) => DoCfdSettings()),
                new ToolStripMenuItem("DWSIM Settings (Application)...", Img("cog.png"), (s, e) => DoDwsimSettings()),
                new ToolStripMenuItem("GPU & Performance Settings (Application)...", Img("cog.png"), (s, e) => DoGpuPerfSettings())
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
            _miWindArrows = new ToolStripMenuItem("Wind Field Arrows", Img("icons8-wind.png")) { CheckOnClick = true };
            _miWindArrows.Click += (s, e) =>
            {
                _editor.ToggleWindFieldArrows(_miWindArrows.Checked);
                if (!_editor.IsWindFieldArrowsVisible) _miWindArrows.Checked = false;
            };
            menuView.DropDownItems.AddRange(new ToolStripItem[] {
                miCamera,
                new ToolStripMenuItem("Save Camera Preset", Img("icons8-save_as.png"), (s, e) => DoSaveCameraPreset()),
                new ToolStripSeparator(),
                _miSnap, _miGround, _miVectors, _miWindArrows,
                new ToolStripSeparator(),
                new ToolStripMenuItem("Environment Settings...", null, (s, e) => DoShowEnvironmentSettings()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Project Tree", Img("application_view_columns.png"), (s, e) => ShowDockPanel(_projectTreeDock, DockState.DockLeft)),
                new ToolStripMenuItem("Properties Panel", Img("table.png"), (s, e) => ShowDockPanel(_propertiesDock, DockState.DockRight)),
                new ToolStripMenuItem("Add Item Panel", Img("add.png"), (s, e) => ToggleAddItemPanel(true)),
                new ToolStripMenuItem("Simulation Manager", Img("table.png"), (s, e) => DoShowSimulationManager()),
                new ToolStripMenuItem("Monitors", Img("icons8-ecg.png"), (s, e) => ToggleMonitorPanel(true))
            });

            // --- Help menu ---
            var menuHelp = new ToolStripMenuItem("&Help");
            menuHelp.DropDownItems.AddRange(new ToolStripItem[] {
                new ToolStripMenuItem("Online Documentation...", null,
                    (s, e) => OpenUrl("https://github.com/DanWBR/dispersim3d")),
                new ToolStripMenuItem("Report an Issue...", null,
                    (s, e) => OpenUrl("https://github.com/DanWBR/dispersim3d/issues/new")),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Run IOGP 434-01 self-test...", null,
                    (s, e) => DoRunIogpSelfTest()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("About DisperSim 3D...", null, (s, e) => DoAbout())
            });

            _menuStrip.Items.AddRange(new ToolStripItem[] { menuFile, menuEdit, menuInsert, menuSimulate, menuView, menuHelp });

            // === Simulation ToolStrip ===
            _simToolStrip = new ToolStrip
            {
                Font = toolFont,
                ImageScalingSize = new System.Drawing.Size((int)(20 * dpiScale), (int)(20 * dpiScale)),
                AutoSize = true,
                Padding = new Padding((int)(2 * dpiScale))
            };

            _scenarioCombo = new ToolStripComboBox("Scenario") { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, AutoSize = false, ToolTipText = "Active scenario" };
            _scenarioCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_scenarioCombo.SelectedIndex >= 0)
                {
                    _editor.Scene.ActiveScenarioIndex = _scenarioCombo.SelectedIndex;
                    SyncSolverCombo();
                    UpdateStatus("Scenario: " + _scenarioCombo.SelectedItem);
                }
            };

            _solverCombo = new ToolStripComboBox("Solver") { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, AutoSize = false, ToolTipText = "Solver type" };
            _solverCombo.Items.AddRange(new object[] {
                "Gaussian Puff (Transient)",
                "Gaussian Plume (Steady-State)",
                "CFD Transient (scalarTransportFoam)",
                "CFD Steady (scalarTransportFoam)",
                "CFD Steady (simpleFoam + scalar)",
                "CFD Transient (pimpleFoam)",
                "CFD Transient (buoyantPimpleFoam)",
                "CFD Transient (reactingFoam)",
                "CFD Steady (rhoSimpleFoam)",
                "CFD Transient (rhoReactingBuoyantFoam) — universal"
            });
            _solverCombo.SelectedIndex = 0;
            _solverCombo.SelectedIndexChanged += (s, e) =>
            {
                var sc = _editor.Scene.DispersionScenario;
                if (sc != null)
                {
                    switch (_solverCombo.SelectedIndex)
                    {
                        case 0: sc.SolverType = CfdSolverType.GaussianPuff; break;
                        case 1: sc.SolverType = CfdSolverType.GaussianPlume; break;
                        case 2: sc.SolverType = CfdSolverType.ScalarTransportFoam; break;
                        case 3: sc.SolverType = CfdSolverType.ScalarTransportFoamSteady; break;
                        case 4: sc.SolverType = CfdSolverType.ScalarSimpleFoam; break;
                        case 5: sc.SolverType = CfdSolverType.PimpleFoam; break;
                        case 6: sc.SolverType = CfdSolverType.BuoyantPimpleFoam; break;
                        case 7: sc.SolverType = CfdSolverType.ReactingFoam; break;
                        case 8: sc.SolverType = CfdSolverType.RhoSimpleFoam; break;
                        case 9: sc.SolverType = CfdSolverType.RhoReactingBuoyantFoam; break;
                    }
                }
            };

            _btnRun = new ToolStripButton("Run", Img("control_play_blue.png")) { ToolTipText = "Run dispersion simulation" };
            _btnRun.Click += (s, e) =>
            {
                var sc = _editor.Scene.DispersionScenario;
                if (sc == null || sc.Sources.Count == 0) return;

                switch (_solverCombo.SelectedIndex)
                {
                    case 0: sc.SolverType = CfdSolverType.GaussianPuff; break;
                    case 1: sc.SolverType = CfdSolverType.GaussianPlume; break;
                    case 2: sc.SolverType = CfdSolverType.ScalarTransportFoam; break;
                    case 3: sc.SolverType = CfdSolverType.ScalarTransportFoamSteady; break;
                    case 4: sc.SolverType = CfdSolverType.ScalarSimpleFoam; break;
                }

                if (sc.CfdConfig == null)
                    sc.CfdConfig = AppSettings.Instance.CreateCfdConfig();

                string validationError = WindFieldResolver.ValidateForDispersion(_editor.Scene, sc);
                if (!string.IsNullOrEmpty(validationError))
                {
                    MessageBox.Show(this, validationError, "Wind field required",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _editor.EnqueueSimulation(sc.SolverType, sc.CfdConfig);
                DoShowSimulationManager();
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

            var speedCombo = new ToolStripComboBox("Speed") { DropDownStyle = ComboBoxStyle.DropDownList, Width = 40, AutoSize = false, ToolTipText = "Animation speed" };
            speedCombo.Items.AddRange(new object[] { "0.25x", "0.5x", "1x", "2x", "5x", "10x" });
            speedCombo.SelectedIndex = 2;
            speedCombo.SelectedIndexChanged += (s, e) =>
            {
                double[] speeds = { 0.25, 0.5, 1.0, 2.0, 5.0, 10.0 };
                _editor.AnimationSpeedFactor = speeds[speedCombo.SelectedIndex];
            };

            var nudGroundLevel = new ToolStripTextBox { Text = "0", Width = 40, AutoSize = false, ToolTipText = "Ground level elevation (m)" };
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

            var nudGroundSize = new ToolStripTextBox { Text = "200", Width = 40, AutoSize = false, ToolTipText = "Ground plane size (m)" };
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

            var btnEditScenario = new ToolStripButton(Img("cog.png"))
            {
                ToolTipText = "Edit selected scenario",
                DisplayStyle = ToolStripItemDisplayStyle.Image
            };
            if (btnEditScenario.Image == null)
                btnEditScenario.Text = "...";
            btnEditScenario.Click += (s, e) => DoManageScenarios();

            var btnSolverSettings = new ToolStripButton(Img("cog.png"))
            {
                ToolTipText = "Solver settings (CFD or Gaussian meteorology)",
                DisplayStyle = ToolStripItemDisplayStyle.Image
            };
            if (btnSolverSettings.Image == null)
                btnSolverSettings.Text = "...";
            btnSolverSettings.Click += (s, e) => DoSolverSettings();

            _simToolStrip.Items.AddRange(new ToolStripItem[] {
                new ToolStripLabel("Scenario:"), _scenarioCombo, btnEditScenario,
                new ToolStripSeparator(),
                new ToolStripLabel("Solver:"), _solverCombo, btnSolverSettings,
                new ToolStripSeparator(),
                _btnRun, _btnPlay, _btnPause, _btnStop,
                new ToolStripSeparator(),
                new ToolStripLabel("Speed:"), speedCombo,
                new ToolStripSeparator(),
                new ToolStripLabel("Ground Z:"), nudGroundLevel,
                new ToolStripLabel("Size:"), nudGroundSize
            });

            // --- Editor control ---
            _editor = new Scene3DEditorControl { Dock = DockStyle.Fill };

            _editor.EditModeChanged += (s, e) => { UpdateStatus("Mode: " + _editor.CurrentEditMode); UncheckAllModes(); };
            _editor.ProjectIoProgress += Editor_ProjectIoProgress;

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
                    if (_propertiesDock.IsHidden) _propertiesDock.Show(_dockPanel);
                    else _propertiesDock.Activate();
                }
                else if (_editor.SelectedSource != null)
                {
                    var src = _editor.SelectedSource;
                    UpdateStatus("Release Source: " + src.Name);
                    ShowSourceProperties(src);
                    if (_propertiesDock.IsHidden) _propertiesDock.Show(_dockPanel);
                    else _propertiesDock.Activate();
                }
                else
                {
                    UpdateStatus("No selection");
                    ClearPropertyGrid();
                }
            };

            _editor.ObjectPlaced += Editor_ObjectPlaced;

            // --- Properties dock panel ---
            _propertiesDock = new PropertiesDockPanel();
            _propertyGrid = _propertiesDock.PropertyGrid;

            _projectTreePanel = new ProjectTreeWpfPanel();
            _projectTreePanel.ActionRequested += ProjectTree_ActionRequested;
            _projectTreePanel.SelectionChanged += ProjectTree_SelectionChanged;
            _projectTreePanel.VisibilityChanged += ProjectTree_VisibilityChanged;
            _projectTreeDock = new ProjectTreeDockPanel(_projectTreePanel);
            _propertyGrid.PropertyValueChanged += (s2, e2) =>
            {
                if (_propertyGrid.SelectedObject is WindFieldScenario wfSel
                    && _editor.IsWindFieldArrowsVisible)
                {
                    _editor.ShowWindFieldArrows(wfSel, silent: true);
                }
                if (_propertyGrid.SelectedObject is DisperSim3D.Models.View)
                {
                    _editor.RefreshViews();
                    RefreshProjectTree();
                }
                // Live environment updates: rebuilding the sun, sky dome and ground
                // brush is cheap, so we trigger it on every commit. This is the only
                // hook that makes the Environment property panel feel real-time —
                // ApplyEnvironment rebuilds the lighting + sky + ground visuals.
                if (_propertyGrid.SelectedObject is EnvironmentSettings)
                {
                    try { _editor?.ApplyEnvironment(); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ENV] ApplyEnvironment FAILED: {ex}");
                    }
                }
                // Any edit on an object whose label appears in the tree (project name,
                // gas/source/sim names, …) should refresh the tree so it stays in sync.
                var pvc = e2 as PropertyValueChangedEventArgs;
                if ((pvc?.ChangedItem?.PropertyDescriptor?.Name == "Name")
                    || _propertyGrid.SelectedObject is ProjectSettings)
                {
                    RefreshProjectTree();
                }
                // SolverType change → user can manually invoke
                // "Apply atmospheric defaults" from the simulation context menu in the tree.
                try { _editor.RefreshViewport(); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ENV] RefreshViewport FAILED: {ex}");
                }
            };

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
            _cfdSimPanel.PlayRequested += (s, entry) =>
            {
                if (!_editor.LoadCfdSimulation(entry))
                {
                    MessageBox.Show("Could not load results from:\n" + entry.CasePath,
                        "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    string simType = entry.SolverType ?? "OpenFOAM";
                    _cfdSimPanel.ShowPlaybackControls(simType, true);
                    UpdatePlaybackButtons();
                }
            };
            _cfdSimPanel.DeleteRequested += (s, entry) =>
            {
                if (MessageBox.Show("Delete simulation '" + entry.Name + "'?",
                    "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _editor.Scene.CfdSimulations.Remove(entry);
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
                if (p.IsError)
                {
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
                {
                    string simType = entry.SolverType ?? "OpenFOAM";
                    bool isDynamic = entry.TimeStepCount > 1;
                    _cfdSimPanel.ShowPlaybackControls(simType, isDynamic);
                }

                // Find any project Simulation that's running and update its status
                var sim = _editor.Scene.Simulations.FirstOrDefault(sm =>
                    (sm.Status == SimulationStatus.Running || sm.Status == SimulationStatus.Queued) &&
                    (sm.Id == entry.Id || sm.Name == entry.ScenarioName || sm.Name == entry.Name));
                if (sim != null)
                {
                    sim.Status = entry.HasResults ? SimulationStatus.Completed : SimulationStatus.Failed;
                    sim.CompletedAt = DateTime.Now;
                    sim.CasePath = entry.CasePath;
                    sim.TimeStepCount = entry.TimeStepCount;
                    sim.Progress = 1.0;
                    if (!entry.HasResults && string.IsNullOrEmpty(sim.StatusMessage))
                        sim.StatusMessage = "Solver finished without results";
                }

                UpdatePlaybackButtons();
                RefreshProjectTree();
            };

            _cfdSimPanel.PlayPauseClicked += (s, ev) =>
            {
                var ds = _editor.DispersionState;
                if (ds == DispersionSimulationState.Running)
                    _editor.PauseDispersion();
                else if (ds == DispersionSimulationState.Paused)
                    _editor.ResumeDispersion();
                else if (ds == DispersionSimulationState.Stopped &&
                         _editor.CfdResult != null && _editor.CfdResult.IsLoaded)
                    _editor.StartCfdPlayback();
                UpdatePlaybackButtons();
            };
            _cfdSimPanel.StopPlaybackClicked += (s, ev) =>
            {
                _editor.CfdRunner?.Cancel();
                _editor.CancelGaussianPuff();
                _editor.SimulationManager.CancelAll();
                _editor.StopDispersion();
                _cfdSimPanel.HidePlaybackControls();
                UpdatePlaybackButtons();
                _dispersionTimeLabel.Text = "";
            };
            _cfdSimPanel.RewindClicked += (s, ev) =>
            {
                _editor.RewindDispersion();
                UpdatePlaybackButtons();
            };
            _cfdSimPanel.SeekRequested += (s, fraction) =>
            {
                _editor.SeekCfdPlayback(fraction);
            };

            // --- Viewport dock panel ---
            _viewportDock = new ViewportDockPanel(_editor);

            _viewportDock.PlaybackBar.PlayClicked += (s, e) =>
            {
                var ds = _editor.DispersionState;
                if (ds == DispersionSimulationState.Paused) _editor.ResumeDispersion();
                else if (ds == DispersionSimulationState.Stopped &&
                         _editor.CfdResult != null && _editor.CfdResult.IsLoaded)
                    _editor.StartCfdPlayback();
                UpdatePlaybackBarState();
            };
            _viewportDock.PlaybackBar.PauseClicked += (s, e) =>
            {
                _editor.PauseDispersion();
                UpdatePlaybackBarState();
            };
            _viewportDock.PlaybackBar.StopClicked += (s, e) =>
            {
                _editor.StopDispersion();
                UpdatePlaybackBarState();
            };
            _viewportDock.PlaybackBar.SpeedChanged += (s, factor) => _editor.AnimationSpeedFactor = factor;
            _viewportDock.PlaybackBar.SeekRequested += (s, fraction) =>
            {
                _editor.SeekCfdPlayback(fraction);
                // After a seek the underlying playback time/index changes but the bar's
                // time label and slider position don't auto-refresh — push the new state
                // to the UI explicitly so the user sees the result of their drag.
                UpdatePlaybackBarState();
            };

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

            _simToolStrip.Visible = false;

            // --- Assemble ---
            this.Controls.Add(_dockPanel);
            this.Controls.Add(_menuStrip);
            this.Controls.Add(_statusStrip);

            // Show dock contents (order matters: document first, then panels)
            _viewportDock.Show(_dockPanel, DockState.Document);
            _projectTreeDock.Show(_dockPanel, DockState.DockLeft);
            _projectTreePanel.BindScene(_editor.Scene);
            _propertiesDock.Show(_dockPanel, DockState.DockRight);
            _monitorDock.Show(_dockPanel, DockState.DockBottom);
            _monitorDock.DockState = DockState.Hidden;
            _addItemDock.Show(_dockPanel, DockState.DockLeft);
            _addItemDock.DockState = DockState.Hidden;

            RefreshScenarioCombo();
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
            AppSettings.Instance.AddRecentFile(filePath);
        }

        public void LoadFromFile(string filePath)
        {
            _editor.LoadFromFile(filePath);
            RefreshScenarioCombo();
            RefreshProjectTree();
            UpdateStatus("Loaded: " + filePath);
            AppSettings.Instance.AddRecentFile(filePath);
        }

        public void ClearScene()
        {
            _editor.ClearScene();
            ClearPropertyGrid();
            RefreshScenarioCombo();
            RefreshProjectTree();
            UpdateStatus("Scene cleared");
        }

        public void RefreshProjectTree()
        {
            if (_projectTreePanel != null)
                _projectTreePanel.BindScene(_editor.Scene);
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
            using (var dlg = new SaveFileDialog
            {
                Filter = "DisperSim 3D Project (*.dsproj)|*.dsproj|Legacy XML (*.xml)|*.xml",
                DefaultExt = "dsproj"
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                    SaveToFile(dlg.FileName);
            }
        }

        private void DoLoad()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "DisperSim 3D Project (*.dsproj;*.xml)|*.dsproj;*.xml|"
                       + "DisperSim 3D Bundle (*.dsproj)|*.dsproj|"
                       + "Legacy XML (*.xml)|*.xml"
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                    LoadFromFile(dlg.FileName);
            }
        }

        /// <summary>Loads a project from a Recent Files menu entry. Trips a friendly
        /// error and removes the path from the MRU list when the file no longer
        /// exists on disk, so a stale recents list self-heals on use.</summary>
        private void DoLoadRecent(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!System.IO.File.Exists(path))
            {
                MessageBox.Show(
                    "The file no longer exists at:\n\n" + path +
                    "\n\nIt will be removed from the Recent Files list.",
                    "File not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                AppSettings.Instance.RemoveRecentFile(path);
                return;
            }
            try
            {
                LoadFromFile(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load:\n\n" + path + "\n\n" + ex.Message,
                    "Load error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Rebuilds the File → Recent Files submenu from
        /// <see cref="AppSettings.RecentFiles"/>. Called from the submenu's
        /// <c>DropDownOpening</c> event, so the list is always live.</summary>
        private void RebuildRecentFilesMenu()
        {
            if (_miRecentFiles == null) return;
            _miRecentFiles.DropDownItems.Clear();

            var list = AppSettings.Instance.RecentFiles;
            if (list == null || list.Count == 0)
            {
                _miRecentFiles.DropDownItems.Add(
                    new ToolStripMenuItem("(No recent files)") { Enabled = false });
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                string path = list[i];
                // Access key: &1..&9 for the first nine, then plain text.
                string accel = i < 9 ? "&" + (i + 1) + "  " : "    ";
                string display = accel + System.IO.Path.GetFileName(path)
                    + "    [" + EllipsifyMiddle(path, 60) + "]";
                var item = new ToolStripMenuItem(display)
                {
                    ToolTipText = path
                };
                string captured = path; // closure capture
                item.Click += (s, e) => DoLoadRecent(captured);
                _miRecentFiles.DropDownItems.Add(item);
            }

            _miRecentFiles.DropDownItems.Add(new ToolStripSeparator());
            _miRecentFiles.DropDownItems.Add(new ToolStripMenuItem(
                "&Clear list", null, (s, e) => AppSettings.Instance.ClearRecentFiles()));
        }

        /// <summary>Compacts <paramref name="text"/> to at most
        /// <paramref name="maxChars"/> by replacing the middle with "..." while
        /// keeping both ends intact. Used to fit long file paths into the menu
        /// strip without horizontal scrolling.</summary>
        private static string EllipsifyMiddle(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text ?? string.Empty;
            int keep = Math.Max(4, (maxChars - 3) / 2);
            return text.Substring(0, keep) + "..." + text.Substring(text.Length - keep);
        }

        private void DoClear()
        {
            if (MessageBox.Show("Clear the entire scene?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                ClearScene();
        }

        private void DoWorkingDirectory()
        {
            using (var dlg = new FolderBrowserDialog
            {
                Description = "Choose the DisperSim 3D working directory for simulation files.",
                SelectedPath = DisperSim3D.Core.AppSettings.Instance.WorkingDirectory,
                ShowNewFolderButton = true
            })
            {
                long size = DisperSim3D.Core.TempManager.GetWorkDirSize();
                dlg.Description += $"\n\nCurrent size: {DisperSim3D.Core.TempManager.FormatBytes(size)}";

                if (dlg.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    DisperSim3D.Core.AppSettings.Instance.WorkingDirectory = dlg.SelectedPath;
                    DisperSim3D.Core.AppSettings.Instance.Save();
                    try { System.IO.Directory.CreateDirectory(dlg.SelectedPath); } catch { }
                    RefreshWorkDirStatus();
                    UpdateStatus($"Working directory set to: {dlg.SelectedPath}");
                }
            }
        }

        private void DoCleanTempFiles()
        {
            var (totalBytes, entryCount, byCategory) = DisperSim3D.Core.TempManager.GetSummary();

            if (totalBytes == 0)
            {
                MessageBox.Show("No temporary files found.", "Clean Temp Files",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"DisperSim 3D is using {DisperSim3D.Core.TempManager.FormatBytes(totalBytes)} in {entryCount} temp entries:\n");
            foreach (var kv in byCategory.OrderByDescending(x => x.Value))
                sb.AppendLine($"  {kv.Key}: {DisperSim3D.Core.TempManager.FormatBytes(kv.Value)}");
            sb.AppendLine("\nDelete all temp files older than 24 hours?\n");
            sb.AppendLine("Yes = delete entries older than 24 h");
            sb.AppendLine("No = delete ALL entries (including recent)");
            sb.AppendLine("Cancel = do nothing");

            var answer = MessageBox.Show(sb.ToString(), "Clean Temp Files",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel) return;

            var (deleted, freed) = answer == DialogResult.No
                ? DisperSim3D.Core.TempManager.PurgeAll()
                : DisperSim3D.Core.TempManager.PurgeOlderThan(TimeSpan.FromHours(24));

            MessageBox.Show($"Deleted {deleted} entries, freed {DisperSim3D.Core.TempManager.FormatBytes(freed)}.",
                "Clean Temp Files", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshWorkDirStatus();
        }

        private void RefreshWorkDirStatus()
        {
            try
            {
                long size = DisperSim3D.Core.TempManager.GetWorkDirSize();
                _workDirLabel.Text = $"Work: {DisperSim3D.Core.TempManager.FormatBytes(size)}";
            }
            catch { _workDirLabel.Text = ""; }
        }

        private void DoExit()
        {
            try { DisperSim3D.Core.TempManager.PurgeOlderThan(TimeSpan.FromDays(7)); }
            catch { }
            // Close the host form if any (DisperSim3D.App wraps the panel in a Form);
            // otherwise fall back to terminating the application loop.
            var form = this.FindForm();
            if (form != null) form.Close();
            else Application.Exit();
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

                using (var importDlg = new ImportModelDialog(model, fileDlg.FileName, _editor.GroundSize))
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
            _editor.CurrentEditMode = EditMode.PlaceReleaseSource;
            UncheckAllModes();
            UpdateStatus("Click to place release source");
        }

        private void DoAddMonitor()
        {
            _editor.CurrentEditMode = EditMode.PlaceMonitorPoint;
            UncheckAllModes();
            UpdateStatus("Click to place monitor point");
        }

        private void DoExportMonitorCsv()
        {
            if (_editor.Scene.MonitorPoints.Count == 0)
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
            string name = "Camera " + (_editor.Scene.CameraPresets.Count + 1);
            var preset = _editor.SaveCurrentCameraPreset(name);
            if (preset != null)
                UpdateStatus("Camera preset saved: " + name);
        }

        private void DoBatchExport()
        {
            var presets = _editor.Scene.CameraPresets;
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
            using (var dlg = new WindRoseDialog(_editor.Scene.WindRose))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _editor.Scene.WindRose = dlg.Result;
                    _editor.RefreshViewport();
                    UpdateStatus("Wind rose updated (" + dlg.Result.Bins.Count + " bins)");

                    if (dlg.GenerateScenarios && dlg.Result.Bins.Count > 0)
                    {
                        var fs = _editor.Scene;
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
            var fs = _editor.Scene;
            if (fs.DispersionScenarios.Count == 0)
                fs.DispersionScenarios.Add(new Models.DispersionScenario());

            using (var dlg = new ScenarioManagerDialog(fs.DispersionScenarios, fs.ActiveScenarioIndex,
                fs.WindFieldScenarios))
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

        private void ProjectTree_ActionRequested(object sender, ProjectTreeActionEventArgs e)
        {
            var scene = _editor.Scene;
            switch (e.Action)
            {
                case ProjectTreeAction.EditGeneralSettings:
                    DoEditGeneralSettings();
                    break;
                case ProjectTreeAction.AddPureGas:
                    DoAddGas(false);
                    break;
                case ProjectTreeAction.AddMixture:
                    DoAddGas(true);
                    break;
                case ProjectTreeAction.AddMixtureFromDwsim:
                    DoAddGasFromDwsim();
                    break;
                case ProjectTreeAction.EditGas:
                    DoEditGas(e.ItemId);
                    break;
                case ProjectTreeAction.DuplicateGas:
                    DoDuplicateGas(e.ItemId);
                    break;
                case ProjectTreeAction.DeleteGas:
                    DoDeleteGas(e.ItemId);
                    break;
                case ProjectTreeAction.ImportGeometry:
                    DoImport3D();
                    break;
                case ProjectTreeAction.AddSource:
                    DoAddSourceFromTree();
                    break;
                case ProjectTreeAction.EditSource:
                    DoEditSource(e.ItemId);
                    break;
                case ProjectTreeAction.EditSourceInventory:
                    DoEditSourceInventory(e.ItemId);
                    break;
                case ProjectTreeAction.DeleteSource:
                    DoDeleteSource(e.ItemId);
                    break;
                case ProjectTreeAction.NewSimulationFromSource:
                    DoNewSimulation(e.ItemId);
                    break;
                case ProjectTreeAction.OpenWindFieldManager:
                case ProjectTreeAction.AddWindField:
                    DoManageWindFields();
                    break;
                case ProjectTreeAction.EditWindField:
                    DoEditWindField(e.ItemId);
                    break;
                case ProjectTreeAction.RunWindField:
                    DoRunWindField(e.ItemId);
                    break;
                case ProjectTreeAction.OpenWindFieldCase:
                    DoOpenWindFieldCase(e.ItemId);
                    break;
                case ProjectTreeAction.DeleteWindField:
                    DoDeleteWindField(e.ItemId);
                    break;
                case ProjectTreeAction.AddSimulation:
                    DoNewSimulation(null);
                    break;
                case ProjectTreeAction.RunSimulation:
                    DoConfigureAndRunSimulation(e.ItemId);
                    break;
                case ProjectTreeAction.RerunSimulation:
                    DoRunSimulation(e.ItemId);
                    break;
                case ProjectTreeAction.EditSimulation:
                    DoEditSimulation(e.ItemId);
                    break;
                case ProjectTreeAction.DeleteSimulation:
                    DoDeleteSimulation(e.ItemId);
                    break;
                case ProjectTreeAction.AddMonitor:
                    DoAddMonitorFromTree();
                    break;
                case ProjectTreeAction.EditMonitor:
                    DoEditMonitor(e.ItemId);
                    break;
                case ProjectTreeAction.DeleteMonitor:
                    DoDeleteMonitor(e.ItemId);
                    break;
                case ProjectTreeAction.AddDetector:
                    DoAddDetectorFromTree();
                    break;
                case ProjectTreeAction.EditDetector:
                    DoEditDetector(e.ItemId);
                    break;
                case ProjectTreeAction.DeleteDetector:
                    DoDeleteDetector(e.ItemId);
                    break;
                case ProjectTreeAction.AddDispersionStudy:
                    DoAddDispersionStudy();
                    break;
                case ProjectTreeAction.EditDispersionStudy:
                    DoEditDispersionStudy(e.ItemId);
                    break;
                case ProjectTreeAction.DuplicateDispersionStudy:
                    DoDuplicateDispersionStudy(e.ItemId);
                    break;
                case ProjectTreeAction.DeleteDispersionStudy:
                    DoDeleteDispersionStudy(e.ItemId);
                    break;
                case ProjectTreeAction.AddDetectorAllocation:
                    DoAddDetectorAllocation();
                    break;
                case ProjectTreeAction.EditDetectorAllocation:
                    DoEditDetectorAllocation(e.ItemId);
                    break;
                case ProjectTreeAction.RunDetectorAllocation:
                    DoRunDetectorAllocation(e.ItemId);
                    break;
                case ProjectTreeAction.ApplyDetectorAllocation:
                    DoApplyDetectorAllocation(e.ItemId);
                    break;
                case ProjectTreeAction.DeleteDetectorAllocation:
                    DoDeleteDetectorAllocation(e.ItemId);
                    break;
                case ProjectTreeAction.EditGeometry:
                    DoEditGeometry(e.ItemId);
                    break;
                case ProjectTreeAction.DeleteGeometry:
                    DoDeleteGeometry(e.ItemId);
                    break;
                case ProjectTreeAction.DuplicateSource:
                    DoDuplicateSource(e.ItemId);
                    break;
                case ProjectTreeAction.ViewSimulationResults:
                case ProjectTreeAction.OpenSimulationCase:
                    DoOpenSimulationCase(e.ItemId);
                    break;
                case ProjectTreeAction.AddView:
                    DoAddView();
                    break;
                case ProjectTreeAction.EditView:
                    DoEditView(e.ItemId);
                    break;
                case ProjectTreeAction.DuplicateView:
                    DoDuplicateView(e.ItemId);
                    break;
                case ProjectTreeAction.DeleteView:
                    DoDeleteView(e.ItemId);
                    break;
            }
            RefreshProjectTree();
        }

        private void ProjectTree_VisibilityChanged(object sender, ProjectTreeVisibilityEventArgs e)
        {
            var scene = _editor.Scene;
            switch (e.Target)
            {
                case ProjectTreeTarget.WindField:
                    var wf = scene.WindFieldScenarios.FirstOrDefault(w => w.Id == e.ItemId);
                    if (wf == null) return;
                    if (e.Visible)
                    {
                        if (wf.Status != WindFieldStatus.Ready)
                        {
                            UpdateStatus("Wind field '" + wf.Name + "' is not ready (status: " + wf.Status + ")");
                            return;
                        }
                        _editor.ShowWindFieldArrows(wf, silent: true);
                        UpdateStatus("Showing wind field: " + wf.Name);
                    }
                    else
                    {
                        _editor.HideWindFieldArrows();
                        UpdateStatus("Hidden wind field: " + wf.Name);
                    }
                    break;

                case ProjectTreeTarget.Simulation:
                    var sim = scene.Simulations.FirstOrDefault(x => x.Id == e.ItemId);
                    if (sim == null) return;
                    sim.IsVisible = e.Visible;
                    if (e.Visible)
                    {
                        if (sim.Status != SimulationStatus.Completed)
                        {
                            UpdateStatus("Simulation '" + sim.Name + "' has no results yet (" + sim.Status + ")");
                            return;
                        }
                        var entry = _editor.Scene.CfdSimulations.FirstOrDefault(en =>
                            en.Id == sim.Id || en.Name == sim.Name || en.ScenarioName == sim.Name);
                        if (entry == null)
                        {
                            UpdateStatus("No result entry found for simulation '" + sim.Name + "'");
                            return;
                        }
                        if (_editor.LoadCfdSimulation(entry))
                        {
                            bool isTransient = entry.TimeStepCount > 1;
                            if (isTransient) _editor.StartCfdPlayback();
                            UpdatePlaybackBarState(sim.Name);
                            UpdatePlaybackButtons();
                            UpdateStatus("Showing results: " + sim.Name);
                        }
                        else
                        {
                            UpdateStatus("Failed to load results from: " + entry.CasePath);
                        }
                    }
                    else
                    {
                        _editor.StopDispersion();
                        UpdatePlaybackBarState();
                        UpdatePlaybackButtons();
                        UpdateStatus("Stopped: " + sim.Name);
                    }
                    break;

                case ProjectTreeTarget.Source:
                    var src = scene.TopLevelSources.FirstOrDefault(s => s.Id == e.ItemId)
                        ?? scene.DispersionScenarios.SelectMany(d => d.Sources).FirstOrDefault(s => s.Id == e.ItemId);
                    if (src == null)
                    {
                        UpdateStatus("Source not found: " + e.ItemId);
                        return;
                    }
                    src.IsVisible = e.Visible;
                    // Also update any duplicate references (legacy migration may leave the same source in DispersionScenario.Sources)
                    foreach (var ds in scene.DispersionScenarios)
                        foreach (var s in ds.Sources)
                            if (s.Id == src.Id) s.IsVisible = e.Visible;
                    _editor.RefreshViewport();
                    UpdateStatus((e.Visible ? "Showing" : "Hidden") + " source: " + src.Name);
                    break;

                case ProjectTreeTarget.View:
                    var v = scene.Views.FirstOrDefault(x => x.Id == e.ItemId);
                    if (v == null) return;
                    v.IsVisible = e.Visible;
                    _editor.RefreshViews();
                    UpdateStatus((e.Visible ? "Showing" : "Hidden") + " view: " + v.Name);
                    break;

                case ProjectTreeTarget.DispersionStudy:
                    var st = scene.DispersionStudies?.FirstOrDefault(x => x.Id == e.ItemId);
                    if (st == null) return;
                    st.IsVisible = e.Visible;
                    _editor.RefreshViewport();
                    UpdateStatus((e.Visible ? "Showing" : "Hidden") + " study: " + st.Name);
                    break;

                case ProjectTreeTarget.DetectorAllocation:
                    var al = scene.DetectorAllocations?.FirstOrDefault(x => x.Id == e.ItemId);
                    if (al == null) return;
                    al.IsVisible = e.Visible;
                    _editor.RefreshViewport();
                    UpdateStatus((e.Visible ? "Showing" : "Hidden") + " allocation: " + al.Name);
                    break;

                default:
                    UpdateStatus("Visibility toggle for " + e.Target + " not implemented yet");
                    break;
            }
        }

        private void ProjectTree_SelectionChanged(object sender, ProjectTreeSelectionEventArgs e)
        {
            if (e.Selected != null && _propertyGrid != null)
            {
                if (e.Selected is ReleaseSource3D selSrc)
                {
                    // Same curated view that the 3D-viewport click uses — keeps the property
                    // set in the tree consistent with the one shown when picking the marker.
                    _propertyGrid.SelectedObject = new PropertyAdapters.ReleaseSourcePropertyAdapter(selSrc, () =>
                    {
                        _editor.RefreshViewport();
                        if (_propertiesDock != null)
                            _propertiesDock.Text = "Properties - " + selSrc.Name;
                    });
                }
                else
                {
                    _propertyGrid.SelectedObject = e.Selected;
                }
            }
            if (e.Selected is ReleaseSource3D src)
                _editor.SelectedSource = src;

            if (_propertiesDock != null)
                _propertiesDock.Text = string.IsNullOrEmpty(e.Title)
                    ? "Properties"
                    : "Properties - " + e.Title;

            UpdatePlaybackBarState(e.Title);
        }

        private void UpdatePlaybackBarState(string title = null)
        {
            var bar = _viewportDock?.PlaybackBar;
            if (bar == null) return;

            var state = _editor.DispersionState;
            bool isStopped = state == DispersionSimulationState.Stopped;
            bool isRunning = state == DispersionSimulationState.Running;
            bool isPaused = state == DispersionSimulationState.Paused;
            bool isSolving = state == DispersionSimulationState.SolvingCfd;
            bool isSteadyComplete = state == DispersionSimulationState.SteadyStateComplete;

            bool hasPlayable = (_editor.CfdResult != null && _editor.CfdResult.IsLoaded);
            // Steady-state results have a single converged snapshot — playback timeline
            // has no meaning. Hide the bar completely; the result is already displayed.
            bool isSteadyResult = hasPlayable && _editor.CfdResult.IsSteadyState;
            bool show = (isRunning || isPaused || isSolving || hasPlayable) && !isSteadyResult;
            bar.Visible = show;
            if (!show) return;

            bar.SetTitle(title ?? "Playback");
            bar.SetButtons(
                playEnabled: isPaused || (isStopped && hasPlayable),
                pauseEnabled: isRunning,
                stopEnabled: !isStopped && !isSteadyComplete);

            if (isSolving)
            {
                bar.SetTimeText("CFD solving...");
                bar.SetSliderEnabled(false);
            }
            else if (isRunning || isPaused)
            {
                double cur = _editor.DispersionTimeS;
                double tot = _editor.SimulationTotalDurationS;
                bar.SetTimeText(string.Format("T = {0:F1} s / {1:F0} s", cur, tot));
                bar.SetSliderEnabled(true);
                if (tot > 0) bar.SetProgress(cur / tot);
            }
            else if (hasPlayable)
            {
                // Stopped but a result is loaded — keep the bar interactive so the user
                // can scrub through frames after playback finishes / before pressing play.
                double cur = _editor.DispersionTimeS;
                double tot = _editor.SimulationTotalDurationS;
                bar.SetTimeText(string.Format("T = {0:F1} s / {1:F0} s", cur, tot));
                bar.SetSliderEnabled(true);
                if (tot > 0) bar.SetProgress(cur / tot);
            }
            else
            {
                bar.SetTimeText("Ready");
                bar.SetSliderEnabled(hasPlayable);
            }
        }

        private void DoEditGeneralSettings()
        {
            var s = _editor.Scene.GeneralSettings ?? (_editor.Scene.GeneralSettings = new ProjectSettings());
            using (var dlg = new MeteorologicalDialog(s.DefaultMeteo))
            {
                if (dlg.ShowDialog() == DialogResult.OK && dlg.Result != null)
                {
                    s.DefaultMeteo = dlg.Result;
                    UpdateStatus("General settings updated");
                }
            }
        }

        private void DoAddGas(bool mixture)
        {
            var item = mixture
                ? GasLibraryItem.FromMixture("New Mixture", new GasMixture())
                : GasLibraryItem.FromGasProperties(GasProperties.CreateMethane());
            using (var dlg = new GasLibraryItemDialog(item))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _editor.Scene.GasLibrary.Add(dlg.Result);
                    UpdateStatus("Added gas: " + dlg.Result.Name);
                }
            }
        }

        /// <summary>Opens the DWSIM-driven mixture builder: lets the user pick compounds
        /// from DWSIM's database, set mole fractions, run a Peng-Robinson 1978 flash,
        /// and adds the resulting <see cref="GasLibraryItem"/> to the project library.</summary>
        private void DoAddGasFromDwsim()
        {
            using (var dlg = new Dialogs.DwsimMixtureBuilderDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK && dlg.Result != null)
                {
                    _editor.Scene.GasLibrary.Add(dlg.Result);
                    UpdateStatus("Added DWSIM mixture: " + dlg.Result.Name);
                    RefreshProjectTree();
                }
            }
        }

        private void DoEditGas(string id)
        {
            var item = _editor.Scene.GasLibrary.FirstOrDefault(g => g.Id == id);
            if (item == null) return;
            using (var dlg = new GasLibraryItemDialog(item))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    var idx = _editor.Scene.GasLibrary.IndexOf(item);
                    _editor.Scene.GasLibrary[idx] = dlg.Result;
                    UpdateStatus("Gas updated: " + dlg.Result.Name);
                }
            }
        }

        private void DoDuplicateGas(string id)
        {
            var item = _editor.Scene.GasLibrary.FirstOrDefault(g => g.Id == id);
            if (item == null) return;
            GasLibraryItem copy;
            if (item.Kind == GasLibraryItemKind.Mixture)
                copy = GasLibraryItem.FromMixture(item.Name + " (copy)", item.Mixture);
            else
                copy = GasLibraryItem.FromGasProperties(item.PureGas);
            copy.Name = item.Name + " (copy)";
            _editor.Scene.GasLibrary.Add(copy);
        }

        private void DoDeleteGas(string id)
        {
            var item = _editor.Scene.GasLibrary.FirstOrDefault(g => g.Id == id);
            if (item == null) return;
            if (MessageBox.Show("Delete gas '" + item.Name + "'?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _editor.Scene.GasLibrary.Remove(item);
        }

        private void DoAddSourceFromTree()
        {
            _editor.CurrentEditMode = EditMode.PlaceReleaseSource;
            UncheckAllModes();
            UpdateStatus("Click on the map to place the source...");
        }

        private void DoEditSource(string id)
        {
            var src = _editor.Scene.TopLevelSources.FirstOrDefault(s => s.Id == id);
            if (src == null) return;
            ShowSourceProperties(src);
            UpdateStatus("Edit source via Properties panel");
        }

        /// <summary>Opens the IOGP 434-01 equipment-inventory editor for the
        /// source identified by <paramref name="id"/>. Risk-based detector
        /// allocations consume the configured leak frequency.</summary>
        private void DoEditSourceInventory(string id)
        {
            var src = _editor.Scene.TopLevelSources.FirstOrDefault(s => s.Id == id);
            if (src == null) return;
            using (var dlg = new EquipmentInventoryDialog(src))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    UpdateStatus("Inventory updated for '" + (src.Name ?? id)
                        + "' → " + src.EffectiveLeakFrequencyPerYear.ToString("E2",
                            System.Globalization.CultureInfo.InvariantCulture) + " events/yr");
                    RefreshProjectTree();
                }
            }
        }

        private void DoDeleteSource(string id)
        {
            var src = _editor.Scene.TopLevelSources.FirstOrDefault(s => s.Id == id);
            if (src == null) return;
            if (MessageBox.Show("Delete source '" + src.Name + "'?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _editor.Scene.TopLevelSources.Remove(src);
        }

        private void DoNewSimulation(string preselectedSourceId)
        {
            using (var dlg = new SimulationEditorDialog(_editor.Scene, preselectedSourceId))
            {
                if (dlg.ShowDialog() == DialogResult.OK && dlg.Result != null)
                {
                    var sim = dlg.Result;
                    if (sim.SnapshotCfdConfig == null)
                        sim.SnapshotCfdConfig = new CfdConfiguration();
                    var gas = ResolveGasForSimulation(sim);
                    var meteo = ResolveMeteoForSimulation(sim);
                    CfdConfigurationPresets.ApplyForSolver(sim.SnapshotCfdConfig, sim.SolverType, gas, meteo);
                    _editor.Scene.Simulations.Add(sim);
                    UpdateStatus("Simulation created: " + sim.Name);
                }
            }
        }

        private GasLibraryItem ResolveGasForSimulation(Simulation sim)
        {
            if (sim == null || string.IsNullOrEmpty(sim.SourceId)) return null;
            var src = _editor.Scene.TopLevelSources.FirstOrDefault(s => s.Id == sim.SourceId);
            if (src == null || string.IsNullOrEmpty(src.GasRefId)) return null;
            return _editor.Scene.GasLibrary.FirstOrDefault(g => g.Id == src.GasRefId);
        }

        private MeteorologicalConditions ResolveMeteoForSimulation(Simulation sim)
        {
            if (sim != null && sim.SnapshotMeteo != null) return sim.SnapshotMeteo;
            if (sim != null && !string.IsNullOrEmpty(sim.WindFieldId))
            {
                var wf = _editor.Scene.WindFieldScenarios.FirstOrDefault(w => w.Id == sim.WindFieldId);
                if (wf != null) return wf.Meteo;
            }
            return _editor.Scene.GeneralSettings != null
                ? _editor.Scene.GeneralSettings.DefaultMeteo
                : null;
        }

        private void OfferAtmosphericPresetReapply(Simulation sim)
        {
            if (sim == null || sim.SnapshotCfdConfig == null) return;
            var result = MessageBox.Show(
                "Apply validated atmospheric defaults for " + sim.SolverType + "?\n\n" +
                "Yes overrides Sc_t, sigma_eps, Ceps3, ground BC and atmospheric BL switch.\n" +
                "No keeps your current CFD settings.",
                "Atmospheric defaults",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;
            var gas = ResolveGasForSimulation(sim);
            var meteo = ResolveMeteoForSimulation(sim);
            CfdConfigurationPresets.ApplyForSolver(sim.SnapshotCfdConfig, sim.SolverType, gas, meteo);
            _propertyGrid.Refresh();
        }

        private void DoEditSimulation(string id)
        {
            var sim = _editor.Scene.Simulations.FirstOrDefault(s => s.Id == id);
            if (sim == null) return;
            _propertyGrid.SelectedObject = sim;
            if (_propertiesDock != null)
            {
                _propertiesDock.Text = "Properties - " + (sim.Name ?? "Simulation");
                if (_propertiesDock.IsHidden) _propertiesDock.Show(_dockPanel);
                else _propertiesDock.Activate();
            }
        }

        private void DoAddView()
        {
            using (var dlg = new Dialogs.ViewEditorDialog(_editor.Scene))
            {
                if (dlg.ShowDialog() == DialogResult.OK && dlg.Result != null)
                {
                    _editor.Scene.Views.Add(dlg.Result);
                    _editor.RefreshViews();
                    UpdateStatus("Added view: " + dlg.Result.Name);
                }
            }
        }

        private void DoEditView(string id)
        {
            var v = _editor.Scene.Views.FirstOrDefault(x => x.Id == id);
            if (v == null) return;
            _propertyGrid.SelectedObject = v;
        }

        private void DoDuplicateView(string id)
        {
            var v = _editor.Scene.Views.FirstOrDefault(x => x.Id == id);
            if (v == null) return;
            var copy = new DisperSim3D.Models.View
            {
                Name = v.Name + " (copy)",
                Kind = v.Kind,
                SimulationId = v.SimulationId,
                FieldProperty = v.FieldProperty,
                TimeMode = v.TimeMode,
                SpecificTimeS = v.SpecificTimeS,
                IsVisible = v.IsVisible,
                Opacity = v.Opacity,
                IsoValue = v.IsoValue,
                IsoColor = v.IsoColor,
                PlanePosition = v.PlanePosition,
                ColorMap = v.ColorMap,
                MinValue = v.MinValue,
                MaxValue = v.MaxValue,
                SampleResolution = v.SampleResolution
            };
            _editor.Scene.Views.Add(copy);
            _editor.RefreshViews();
        }

        private void DoDeleteView(string id)
        {
            var v = _editor.Scene.Views.FirstOrDefault(x => x.Id == id);
            if (v == null) return;
            if (MessageBox.Show("Delete view '" + v.Name + "'?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _editor.Scene.Views.Remove(v);
            _editor.RefreshViews();
        }

        private void DoDeleteSimulation(string id)
        {
            var sim = _editor.Scene.Simulations.FirstOrDefault(s => s.Id == id);
            if (sim == null) return;
            if (MessageBox.Show("Delete simulation '" + sim.Name + "'?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _editor.Scene.Simulations.Remove(sim);
        }

        /// <summary>Opens the SimulationEditorDialog pre-filled with the simulation's
        /// current snapshot params; on OK applies them in-place and triggers a run.
        /// Used by the "Configure &amp; Run" context-menu action — distinct from
        /// "Re-run" which skips the dialog (calls <see cref="DoRunSimulation"/> directly).</summary>
        private void DoConfigureAndRunSimulation(string id)
        {
            var sim = _editor.Scene.Simulations.FirstOrDefault(s => s.Id == id);
            if (sim == null) return;
            using (var dlg = new SimulationEditorDialog(_editor.Scene, sim))
            {
                if (dlg.ShowDialog() == DialogResult.OK && dlg.Result != null)
                {
                    if (sim.SnapshotCfdConfig == null)
                        sim.SnapshotCfdConfig = new CfdConfiguration();
                    var gas = ResolveGasForSimulation(sim);
                    var meteo = ResolveMeteoForSimulation(sim);
                    CfdConfigurationPresets.ApplyForSolver(sim.SnapshotCfdConfig, sim.SolverType, gas, meteo);
                    UpdateStatus("Simulation configured: " + sim.Name);
                    DoRunSimulation(id);
                }
            }
        }

        private void DoRunSimulation(string id)
        {
            var sim = _editor.Scene.Simulations.FirstOrDefault(s => s.Id == id);
            if (sim == null) return;
            if (SimulationRunner.RunSnapshot(sim, _editor.Scene, _editor, m => UpdateStatus(m)))
                DoShowSimulationManager();
            RefreshProjectTree();
        }

        private void DoAddMonitorFromTree()
        {
            using (var dlg = new MonitorPointDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _editor.Scene.MonitorPoints.Add(new MonitorPoint3D
                    {
                        Name = dlg.MonitorName,
                        Position = new System.Windows.Media.Media3D.Point3D(dlg.PosX, dlg.PosY, dlg.PosZ)
                    });
                }
            }
        }

        private void DoEditMonitor(string id)
        {
            var m = _editor.Scene.MonitorPoints.FirstOrDefault(x => x.Name == id || x.Id == id);
            if (m == null) return;
            using (var dlg = new MonitorPointDialog(m.Name, m.Position.X, m.Position.Y, m.Position.Z))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    m.Name = dlg.MonitorName;
                    m.Position = new System.Windows.Media.Media3D.Point3D(dlg.PosX, dlg.PosY, dlg.PosZ);
                    _editor.RefreshViewport();
                }
            }
        }

        private void DoDeleteMonitor(string id)
        {
            var m = _editor.Scene.MonitorPoints.FirstOrDefault(x => x.Name == id || x.Id == id);
            if (m == null) return;
            if (MessageBox.Show("Delete monitor '" + m.Name + "'?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _editor.Scene.MonitorPoints.Remove(m);
            _editor.RefreshViewport();
        }

        private void DoEditDetector(string id)
        {
            var d = _editor.Scene.GasDetectors.FirstOrDefault(x => x.Id == id);
            if (d == null) return;
            _propertyGrid.SelectedObject = d;
        }

        private void DoDeleteDetector(string id)
        {
            var d = _editor.Scene.GasDetectors.FirstOrDefault(x => x.Id == id);
            if (d == null) return;
            if (MessageBox.Show("Delete detector '" + d.Name + "'?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _editor.Scene.GasDetectors.Remove(d);
            _editor.RefreshViewport();
        }

        // ── Dispersion Studies ──

        private void DoAddDispersionStudy()
        {
            using (var dlg = new Dialogs.DispersionStudyDialog(_editor.Scene))
            {
                if (dlg.ShowDialog() == DialogResult.OK && dlg.Result != null)
                {
                    _editor.Scene.DispersionStudies.Add(dlg.Result);
                    UpdateStatus("Created dispersion study: " + dlg.Result.Name);
                    RefreshProjectTree();
                }
            }
        }

        private void DoEditDispersionStudy(string id)
        {
            var st = _editor.Scene.DispersionStudies.FirstOrDefault(s => s.Id == id);
            if (st == null) return;
            using (var dlg = new Dialogs.DispersionStudyDialog(_editor.Scene, st))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    UpdateStatus("Study updated: " + st.Name);
                    _editor.InvalidateStudyVisual(st.Id);
                    _editor.RefreshViewport();
                    RefreshProjectTree();
                }
            }
        }

        private void DoDuplicateDispersionStudy(string id)
        {
            var st = _editor.Scene.DispersionStudies.FirstOrDefault(s => s.Id == id);
            if (st == null) return;
            var copy = new DispersionStudy
            {
                Name = st.Name + " (copy)",
                Description = st.Description,
                DetectionQuantity = st.DetectionQuantity,
                DetectionThreshold = st.DetectionThreshold,
                SimulationIds = new System.Collections.Generic.List<string>(st.SimulationIds),
                IsVisible = st.IsVisible
            };
            _editor.Scene.DispersionStudies.Add(copy);
            RefreshProjectTree();
        }

        private void DoDeleteDispersionStudy(string id)
        {
            var st = _editor.Scene.DispersionStudies.FirstOrDefault(s => s.Id == id);
            if (st == null) return;
            // Warn if any allocation depends on this study.
            var dependents = _editor.Scene.DetectorAllocations
                .Where(a => a.DispersionStudyId == st.Id).ToList();
            string msg = "Delete dispersion study '" + st.Name + "'?";
            if (dependents.Count > 0)
                msg += "\n\nWarning: " + dependents.Count
                    + " Detector Allocation(s) reference this study and will become orphaned.";
            if (MessageBox.Show(msg, "Confirm", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
            _editor.Scene.DispersionStudies.Remove(st);
            RefreshProjectTree();
        }

        // ── Detector Allocations ──

        private void DoAddDetectorAllocation()
        {
            if (_editor.Scene.DispersionStudies.Count == 0)
            {
                MessageBox.Show("Create a Dispersion Study first.", "Detector Allocation",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dlg = new Dialogs.DetectorAllocationDialog(_editor.Scene))
            {
                if (dlg.ShowDialog() == DialogResult.OK && dlg.Result != null)
                {
                    _editor.Scene.DetectorAllocations.Add(dlg.Result);
                    UpdateStatus("Created detector allocation: " + dlg.Result.Name);
                    RefreshProjectTree();
                }
            }
        }

        private void DoEditDetectorAllocation(string id)
        {
            var a = _editor.Scene.DetectorAllocations.FirstOrDefault(x => x.Id == id);
            if (a == null) return;
            using (var dlg = new Dialogs.DetectorAllocationDialog(_editor.Scene, a))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    UpdateStatus("Allocation updated: " + a.Name);
                    _editor.InvalidateAllocationVisual(a.Id);
                    _editor.RefreshViewport();
                    RefreshProjectTree();
                }
            }
        }

        private void DoRunDetectorAllocation(string id)
        {
            // Just opens the dialog — the user clicks Run there. Avoids running the
            // greedy solver synchronously on the message-pump thread without feedback.
            DoEditDetectorAllocation(id);
        }

        private void DoApplyDetectorAllocation(string id)
        {
            var a = _editor.Scene.DetectorAllocations.FirstOrDefault(x => x.Id == id);
            if (a == null) return;
            if (a.AllocatedPositions == null || a.AllocatedPositions.Count == 0)
            {
                MessageBox.Show("This allocation has no positions yet. Run it first.",
                    "Apply allocation", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int baseCount = _editor.Scene.GasDetectors.Count;
            for (int i = 0; i < a.AllocatedPositions.Count; i++)
            {
                _editor.Scene.GasDetectors.Add(new GasDetector3D
                {
                    Name = a.Name + " #" + (i + 1),
                    Position = a.AllocatedPositions[i]
                });
            }
            int added = _editor.Scene.GasDetectors.Count - baseCount;
            UpdateStatus("Applied: created " + added + " gas detectors from '" + a.Name + "'.");
            _editor.RefreshViewport();
            RefreshProjectTree();
        }

        private void DoDeleteDetectorAllocation(string id)
        {
            var a = _editor.Scene.DetectorAllocations.FirstOrDefault(x => x.Id == id);
            if (a == null) return;
            if (MessageBox.Show("Delete allocation '" + a.Name + "'?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _editor.Scene.DetectorAllocations.Remove(a);
            RefreshProjectTree();
        }

        private void DoEditGeometry(string id)
        {
            var deco = _editor.Scene.Decorations.FirstOrDefault(x => x.Id == id);
            if (deco == null) return;
            _propertyGrid.SelectedObject = new DecorationPropertyAdapter(deco, () => _editor.RefreshViewport());
        }

        private void DoDeleteGeometry(string id)
        {
            var deco = _editor.Scene.Decorations.FirstOrDefault(x => x.Id == id);
            if (deco == null) return;
            if (MessageBox.Show("Delete '" + deco.Name + "'?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _editor.Scene.Decorations.Remove(deco);
            _editor.RefreshViewport();
        }

        private void DoDuplicateSource(string id)
        {
            var src = _editor.Scene.TopLevelSources.FirstOrDefault(x => x.Id == id);
            if (src == null) return;
            var copy = new ReleaseSource3D
            {
                Name = src.Name + " (copy)",
                Gas = src.Gas,
                GasRefId = src.GasRefId,
                Position = new System.Windows.Media.Media3D.Point3D(src.Position.X + 5, src.Position.Y, src.Position.Z),
                ReleaseRateKgPerS = src.ReleaseRateKgPerS,
                PuffIntervalS = src.PuffIntervalS,
                ReleaseHeightOffset = src.ReleaseHeightOffset,
                ReleaseAzimuthDeg = src.ReleaseAzimuthDeg,
                ReleaseElevationDeg = src.ReleaseElevationDeg
            };
            _editor.Scene.TopLevelSources.Add(copy);
            _editor.RefreshViewport();
        }

        private void DoOpenSimulationCase(string id)
        {
            var sim = _editor.Scene.Simulations.FirstOrDefault(x => x.Id == id);
            if (sim == null || string.IsNullOrEmpty(sim.CasePath)) return;
            if (!System.IO.Directory.Exists(sim.CasePath))
            {
                MessageBox.Show("Case folder no longer exists: " + sim.CasePath,
                    "Simulation", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + sim.CasePath + "\""); }
            catch (Exception ex) { UpdateStatus("Failed to open folder: " + ex.Message); }
        }

        private void DoAddDetectorFromTree()
        {
            var det = new GasDetector3D { Name = "Detector " + (_editor.Scene.GasDetectors.Count + 1) };
            _editor.Scene.GasDetectors.Add(det);
            _propertyGrid.SelectedObject = det;
        }

        private void DoOptimizeDetectors()
        {
            using (var dlg = new DetectorOptimizationDialog(_editor.Scene))
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                if (dlg.ResultDetectorPositions == null || dlg.ResultDetectorPositions.Count == 0)
                {
                    UpdateStatus("Optimization returned no detectors.");
                    return;
                }
                int baseCount = _editor.Scene.GasDetectors.Count;
                for (int i = 0; i < dlg.ResultDetectorPositions.Count; i++)
                {
                    _editor.Scene.GasDetectors.Add(new GasDetector3D
                    {
                        Name = "OptDet " + (baseCount + i + 1),
                        Position = dlg.ResultDetectorPositions[i]
                    });
                }
                _editor.RefreshViewport();
                RefreshProjectTree();
                UpdateStatus("Added " + dlg.ResultDetectorPositions.Count + " optimised detectors.");
            }
        }

        private void DoValidateBenchmarks()
        {
            var envCfg = AppSettings.Instance.CreateCfdConfig();
            using (var dlg = new ValidationDialog(envCfg))
            {
                dlg.ShowDialog();
            }
        }

        private void DoEditWindField(string id)
        {
            var fs = _editor.Scene;
            var wf = fs.WindFieldScenarios.FirstOrDefault(w => w.Id == id);
            if (wf == null) return;
            using (var dlg = new WindFieldManagerDialog(fs, _editor.CfdEnvironment, id))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    fs.WindFieldScenarios.Clear();
                    fs.WindFieldScenarios.AddRange(dlg.Scenarios);
                    UpdateStatus("Wind field updated");
                }
            }
        }

        private void DoRunWindField(string id)
        {
            var fs = _editor.Scene;
            var wf = fs.WindFieldScenarios.FirstOrDefault(w => w.Id == id);
            if (wf == null) return;

            wf.CfdConfig = AppSettings.Instance.CreateCfdConfig();

            var obstacles = new System.Collections.Generic.List<BoundingBox>();
            foreach (var deco in fs.Decorations)
                if (deco.BoundingBox != null) obstacles.Add(deco.BoundingBox);

            // FluidX3D path: pre-compute per-mesh world-space AABBs on the UI thread, since
            // Model3DGroup is a WPF DependencyObject and the BackgroundWorker can't touch it
            // (cross-thread access throws). We walk the model children here and pass plain
            // value-typed BoundingBoxes to the runner.
            System.Collections.Generic.List<BoundingBox> fluidObstacles = null;
            Core.TriangleBundle fluidTriangles = null;
            if (wf.UseFluidX3D)
            {
                fluidObstacles = new System.Collections.Generic.List<BoundingBox>();
                foreach (var deco in fs.Decorations)
                {
                    var boxes = Core.FluidX3DObstacleVoxelizerWpf.ExtractWorldAabbs(deco);
                    fluidObstacles.AddRange(boxes);
                }
                // GPU-side: extract a flat world-space triangle list. FluidX3D's
                // voxelize_mesh_on_device raycasts every cell against the mesh on the GPU,
                // producing accurate occupancy for curved surfaces (tanks, vessels) that
                // the per-triangle AABB approach can only approximate.
                fluidTriangles = Core.FluidX3DObstacleVoxelizerWpf.ExtractWorldTriangles(fs.Decorations);
            }

            var dpiF = this.DeviceDpi / 96f;
            var dlg = new System.Windows.Forms.Form
            {
                Text = "Running wind field: " + wf.Name,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F),
                ClientSize = new System.Drawing.Size((int)(460 * dpiF), (int)(110 * dpiF)),
                Padding = new Padding((int)(10 * dpiF))
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = false
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(22 * dpiF)));
            var lbl = new Label
            {
                AutoSize = false, Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Text = "Starting..."
            };
            var pb = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0, Maximum = 100
            };
            layout.Controls.Add(lbl, 0, 0);
            layout.Controls.Add(pb, 0, 1);
            dlg.Controls.Add(layout);

            var worker = new System.ComponentModel.BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += (s, e) =>
            {
                // FluidX3D path: GPU LBM, no OpenFOAM environment. The AABB list was
                // built on the UI thread above — passing it as plain BoundingBoxes keeps
                // the runner thread-safe.
                if (wf.UseFluidX3D)
                {
                    var fx = new FluidX3DWindFieldRunner();
                    fx.Run(wf, fluidObstacles, fluidTriangles,
                        (frac, msg) => worker.ReportProgress((int)(frac * 100), msg));
                    return;
                }
                var runner = new WindFieldRunner(_editor.CfdEnvironment);
                runner.Run(wf, obstacles, (frac, msg) => worker.ReportProgress((int)(frac * 100), msg));
            };
            worker.ProgressChanged += (s, e) =>
            {
                pb.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
                lbl.Text = (string)e.UserState ?? "";
            };
            worker.RunWorkerCompleted += (s, e) =>
            {
                dlg.Close();
                UpdateStatus(string.Format("Wind field '{0}': {1} {2}", wf.Name, wf.Status,
                    string.IsNullOrEmpty(wf.StatusMessage) ? "" : "— " + wf.StatusMessage));
                RefreshProjectTree();
            };
            worker.RunWorkerAsync();
            dlg.ShowDialog(this);
        }

        private void DoOpenWindFieldCase(string id)
        {
            var wf = _editor.Scene.WindFieldScenarios.FirstOrDefault(w => w.Id == id);
            if (wf == null || string.IsNullOrEmpty(wf.CasePath)) return;
            if (!System.IO.Directory.Exists(wf.CasePath))
            {
                MessageBox.Show(this, "Case folder no longer exists: " + wf.CasePath,
                    "Wind Field", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + wf.CasePath + "\""); }
            catch (Exception ex) { UpdateStatus("Failed to open folder: " + ex.Message); }
        }

        private void DoDeleteWindField(string id)
        {
            var wf = _editor.Scene.WindFieldScenarios.FirstOrDefault(w => w.Id == id);
            if (wf == null) return;
            if (MessageBox.Show("Delete wind field '" + wf.Name + "'? Simulations referencing it will fail to run.",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _editor.Scene.WindFieldScenarios.Remove(wf);
        }

        private void DoManageWindFields()
        {
            var fs = _editor.Scene;
            using (var dlg = new WindFieldManagerDialog(fs, _editor.CfdEnvironment))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    fs.WindFieldScenarios.Clear();
                    fs.WindFieldScenarios.AddRange(dlg.Scenarios);
                    UpdateStatus("Wind field scenarios updated (" + fs.WindFieldScenarios.Count + ")");
                }
            }
        }

        public void RefreshScenarioCombo()
        {
            _scenarioCombo.Items.Clear();
            foreach (var sc in _editor.Scene.DispersionScenarios)
                _scenarioCombo.Items.Add(sc.Name ?? "Scenario");
            if (_scenarioCombo.Items.Count > 0)
                _scenarioCombo.SelectedIndex = Math.Min(_editor.Scene.ActiveScenarioIndex, _scenarioCombo.Items.Count - 1);
            SyncSolverCombo();
        }

        private void SyncSolverCombo()
        {
            var sc = _editor.Scene.DispersionScenario;
            if (sc == null) return;
            switch (sc.SolverType)
            {
                case CfdSolverType.GaussianPuff: _solverCombo.SelectedIndex = 0; break;
                case CfdSolverType.GaussianPlume: _solverCombo.SelectedIndex = 1; break;
                case CfdSolverType.ScalarTransportFoam: _solverCombo.SelectedIndex = 2; break;
                case CfdSolverType.ScalarTransportFoamSteady: _solverCombo.SelectedIndex = 3; break;
                case CfdSolverType.ScalarSimpleFoam: _solverCombo.SelectedIndex = 4; break;
                case CfdSolverType.PimpleFoam: _solverCombo.SelectedIndex = 5; break;
                case CfdSolverType.BuoyantPimpleFoam: _solverCombo.SelectedIndex = 6; break;
                case CfdSolverType.ReactingFoam: _solverCombo.SelectedIndex = 7; break;
                case CfdSolverType.RhoSimpleFoam: _solverCombo.SelectedIndex = 8; break;
                case CfdSolverType.RhoReactingBuoyantFoam: _solverCombo.SelectedIndex = 9; break;
            }
        }

        private void Editor_ObjectPlaced(object sender, ObjectPlacedEventArgs e)
        {
            switch (e.PlacementType)
            {
                case EditMode.PlaceReleaseSource:
                    var src = (ReleaseSource3D)e.PlacedObject;
                    double windDir = _editor.Scene.GeneralSettings?.DefaultMeteo?.WindDirectionDeg
                        ?? _editor.Scene.DispersionScenario?.Meteo?.WindDirectionDeg ?? 0;
                    // Seed the dialog with the orientation Scene3DEditorControl
                    // already derived from the surface normal under the cursor
                    // (otherwise the dialog's default 0/0 would silently
                    // overwrite the perpendicular-to-surface direction).
                    using (var dlg = new DispersionSourceDialog(windDir,
                        src.ReleaseAzimuthDeg, src.ReleaseElevationDeg))
                    {
                        dlg.Text = "Configure Release Source";
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            src.Name = dlg.SourceName;
                            src.Gas = dlg.Gas;
                            src.ReleaseRateKgPerS = dlg.ReleaseRateKgPerS;
                            src.PuffIntervalS = dlg.PuffIntervalS;
                            src.ReleaseHeightOffset = dlg.HeightOffset;
                            src.ReleaseAzimuthDeg = dlg.AzimuthDeg;
                            src.ReleaseElevationDeg = dlg.ElevationDeg;

                            if (dlg.Gas != null)
                            {
                                var libItem = _editor.Scene.GasLibrary.FirstOrDefault(g =>
                                    g.Kind == GasLibraryItemKind.Pure && g.PureGas != null &&
                                    g.PureGas.Name == dlg.Gas.Name &&
                                    g.PureGas.MolarMass == dlg.Gas.MolarMass);
                                if (libItem == null)
                                {
                                    libItem = GasLibraryItem.FromGasProperties(dlg.Gas);
                                    _editor.Scene.GasLibrary.Add(libItem);
                                }
                                src.GasRefId = libItem.Id;
                            }

                            _editor.Scene.DispersionScenario?.Sources.Remove(src);
                            if (!_editor.Scene.TopLevelSources.Contains(src))
                                _editor.Scene.TopLevelSources.Add(src);

                            _editor.RefreshViewport();
                            RefreshProjectTree();
                            ShowSourceProperties(src);
                            UpdateStatus("Source placed: " + src.Name);
                        }
                        else
                        {
                            _editor.Scene.DispersionScenario?.Sources.Remove(src);
                            _editor.Scene.TopLevelSources.Remove(src);
                            _editor.RefreshViewport();
                            RefreshProjectTree();
                            UpdateStatus("Source placement cancelled");
                        }
                    }
                    break;

                case EditMode.PlaceMonitorPoint:
                    var mon = (MonitorPoint3D)e.PlacedObject;
                    using (var dlg = new MonitorPointDialog(mon.Name, mon.Position.X, mon.Position.Y, mon.Position.Z))
                    {
                        dlg.Text = "Configure Monitor Point";
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            mon.Name = dlg.MonitorName;
                            mon.Position = new Point3D(dlg.PosX, dlg.PosY, dlg.PosZ);
                            _editor.RefreshViewport();
                            UpdateStatus("Monitor placed: " + mon.Name);
                        }
                        else
                        {
                            _editor.RemoveMonitorPoint(mon);
                            UpdateStatus("Monitor placement cancelled");
                        }
                    }
                    break;

                case EditMode.PlaceFireSource:
                    var fire = (FireSource)e.PlacedObject;
                    using (var dlg = new FireSourceDialog())
                    {
                        dlg.Text = "Configure Fire Source";
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            var r = dlg.Result;
                            fire.Name = r.Name;
                            fire.MassFlowRateKgS = r.MassFlowRateKgS;
                            fire.OrificeDiameterM = r.OrificeDiameterM;
                            fire.HeatOfCombustionJKg = r.HeatOfCombustionJKg;
                            fire.RadiativeFraction = r.RadiativeFraction;
                            fire.IsPoolFire = r.IsPoolFire;
                            fire.PoolDiameterM = r.PoolDiameterM;
                            fire.PoolBurnRateKgM2S = r.PoolBurnRateKgM2S;
                            fire.Direction = r.Direction;
                            _editor.RefreshViewport();
                            UpdateStatus("Fire source placed: " + fire.Name);
                        }
                        else
                        {
                            _editor.Scene.FireScenario.Sources.Remove(fire);
                            _editor.RefreshViewport();
                            UpdateStatus("Fire source placement cancelled");
                        }
                    }
                    break;

                case EditMode.PlaceGasDetector:
                    var det = (GasDetector3D)e.PlacedObject;
                    UpdateStatus("Detector placed: " + det.Name);
                    break;
            }
        }

        private void DoAddFireSource()
        {
            _editor.CurrentEditMode = EditMode.PlaceFireSource;
            UncheckAllModes();
            UpdateStatus("Click to place fire source");
        }

        private void DoAddDetector()
        {
            string name = "Detector" + (_editor.Scene.GasDetectors.Count + 1);
            _editor.PendingDetectorTemplate = new Models.GasDetector3D { Name = name };
            _editor.CurrentEditMode = EditMode.PlaceGasDetector;
            UncheckAllModes();
            UpdateStatus("Click to place detector: " + name);
        }

        private void DoShowDetectorResults()
        {
            var dets = _editor.Scene.GasDetectors;
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
            var scenario = _editor.Scene.DispersionScenario;
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
            var monitors = _editor.Scene.MonitorPoints;
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
            var current = AppSettings.Instance.CreateCfdConfig();
            using (var dlg = new CfdSettingsDialog(current, _editor.CfdEnvironment))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    AppSettings.Instance.UpdateFromConfig(dlg.Result);
                    UpdateStatus("Application CFD settings updated (" + dlg.Result.DetectedEnvironment + ")");
                }
            }
        }

        private void DoDwsimSettings()
        {
            using (var dlg = new Dialogs.DwsimSettingsDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    UpdateStatus("DWSIM settings updated (" +
                        AppSettings.Instance.DwsimPropertyPackage + ")");
                }
            }
        }

        private void DoGpuPerfSettings()
        {
            using (var dlg = new Dialogs.GpuPerformanceSettingsDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                    UpdateStatus("GPU / performance settings updated (device " +
                        AppSettings.Instance.PreferredComputeDeviceId + ")");
            }
        }

        private void DoAbout()
        {
            using (var dlg = new Dialogs.AboutDialog())
            {
                dlg.ShowDialog(this);
            }
        }

        /// <summary>Runs the embedded IOGP 434-01 table self-test and presents
        /// the result (pass/fail per row + diagnostic on any mismatch) in a
        /// MessageBox. Same logic as <c>DisperSim3D.App.exe --iogp-selftest</c>;
        /// surfaced here so end users can sanity-check the leak-frequency
        /// database without dropping to a terminal.</summary>
        private void DoRunIogpSelfTest()
        {
            string report;
            MessageBoxIcon icon = MessageBoxIcon.Information;
            try
            {
                report = DisperSim3D.Core.IogpTableTests.RunAll();
            }
            catch (Exception ex)
            {
                report = ex.Message;
                icon = MessageBoxIcon.Error;
            }
            // The report can be long — show it in a sizeable text dialog rather
            // than a fixed-width MessageBox.
            using (var dlg = new Form
            {
                Text = "IOGP 434-01 self-test",
                StartPosition = FormStartPosition.CenterParent,
                Size = new System.Drawing.Size(820, 560),
                MinimumSize = new System.Drawing.Size(520, 360),
                MinimizeBox = false,
                MaximizeBox = true
            })
            {
                var tb = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    WordWrap = false,
                    Font = new System.Drawing.Font(
                        System.Drawing.FontFamily.GenericMonospace, 9F),
                    Text = report
                };
                var btn = new Button
                {
                    Text = "Close", DialogResult = DialogResult.OK,
                    Dock = DockStyle.Bottom, Height = 32
                };
                dlg.AcceptButton = btn;
                dlg.CancelButton = btn;
                dlg.Controls.Add(tb);
                dlg.Controls.Add(btn);
                dlg.ShowDialog(this);
            }
            // Suppress unused-warning on icon for the rare case where the user
            // recompiles with a stricter analyser.
            _ = icon;
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't open URL:\n" + ex.Message, "DisperSim 3D",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DoSolverSettings()
        {
            var scenario = EnsureScenario();
            bool isGaussian = scenario.SolverType == CfdSolverType.GaussianPuff
                           || scenario.SolverType == CfdSolverType.GaussianPlume;

            if (isGaussian)
            {
                if (scenario.Meteo == null) scenario.Meteo = new MeteorologicalConditions();
                using (var dlg = new MeteorologicalDialog(scenario.Meteo))
                {
                    if (dlg.ShowDialog() == DialogResult.OK && dlg.Result != null)
                    {
                        scenario.Meteo = dlg.Result;
                        UpdateStatus("Gaussian meteorology updated");
                    }
                }
            }
            else
            {
                DoCfdSettings();
            }
        }

        private void DoShowSimulationManager()
        {
            if (_simManagerDock == null)
            {
                var panel = new SimulationManagerPanel(_editor.SimulationManager);
                panel.PlayResultRequested += (s, entry) =>
                {
                    if (_editor.LoadCfdSimulation(entry))
                    {
                        UpdatePlaybackButtons();
                    }
                };
                _simManagerDock = new SimulationManagerDockPanel(panel);
                _simManagerDock.Show(_dockPanel, DockState.DockBottom);
            }
            else
            {
                ShowDockPanel(_simManagerDock, DockState.DockBottom);
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

        private void DoShowEnvironmentSettings()
        {
            if (_editor?.Scene == null) return;
            if (_editor.Scene.Environment == null)
                _editor.Scene.Environment = new EnvironmentSettings();
            if (_propertiesDock != null) _propertiesDock.Text = "Properties - Environment";
            _propertyGrid.SelectedObject = _editor.Scene.Environment;
            // Live updates are driven by the global PropertyValueChanged hook wired
            // at construction (it branches on SelectedObject type). No need to
            // subscribe a per-show handler — and the previous LostFocus-based
            // subscription on the WinForms panel never fired in practice because
            // focus rarely leaves the panel during editing.
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
            if (_editor.Scene.DispersionScenario == null)
                _editor.Scene.DispersionScenario = new DispersionScenario();
            return _editor.Scene.DispersionScenario;
        }

        private bool _cfdSimPanelUserVisible;

        private void ToggleCfdSimPanel(bool visible)
        {
            // CFD Simulations dock panel was retired — simulations now live in the project tree.
            _cfdSimPanelUserVisible = false;
        }

        private void UpdatePlaybackButtons()
        {
            UpdatePlaybackBarState();

            var state = _editor.DispersionState;
            bool isStopped = state == DispersionSimulationState.Stopped;
            bool isRunning = state == DispersionSimulationState.Running;
            bool isPaused = state == DispersionSimulationState.Paused;
            bool isSolving = state == DispersionSimulationState.SolvingCfd;
            bool isSteadyComplete = state == DispersionSimulationState.SteadyStateComplete;

            // Toolbar buttons + timer + label may not be wired yet when this fires from a
            // tree-visibility event during early init (the Insert menu and viewport dock
            // are built before the legacy playback toolbar). Guard every reference.
            if (_btnRun != null)   _btnRun.Enabled = isStopped;
            if (_btnPlay != null)  _btnPlay.Enabled = isPaused || (isStopped && _editor.CfdResult != null && _editor.CfdResult.IsLoaded);
            if (_btnPause != null) _btnPause.Enabled = isRunning;
            if (_btnStop != null)  _btnStop.Enabled = !isStopped && !isSteadyComplete;

            if (isSolving)
            {
                if (_dispersionTimeLabel != null)
                    _dispersionTimeLabel.Text = "CFD solving...";
            }
            else if (isRunning || isPaused)
            {
                if (_dispersionStatusTimer == null)
                {
                    _dispersionStatusTimer = new Timer { Interval = 100 };
                    _dispersionStatusTimer.Tick += (s, e) =>
                    {
                        var ds = _editor.DispersionState;
                        if (ds == DispersionSimulationState.Running ||
                            ds == DispersionSimulationState.Paused)
                        {
                            double currentT = _editor.DispersionTimeS;
                            double totalT = _editor.SimulationTotalDurationS;
                            if (_dispersionTimeLabel != null)
                                _dispersionTimeLabel.Text = string.Format("T = {0:F1} s / {1:F0} s", currentT, totalT);
                            _cfdSimPanel?.UpdatePlaybackState(
                                ds == DispersionSimulationState.Running, currentT, totalT);

                            var bar = _viewportDock?.PlaybackBar;
                            if (bar != null && bar.Visible)
                            {
                                bar.SetTimeText(string.Format("T = {0:F1} s / {1:F0} s", currentT, totalT));
                                if (totalT > 0) bar.SetProgress(currentT / totalT);
                            }
                        }
                        else
                        {
                            _dispersionStatusTimer.Stop();
                        }
                    };
                }
                _dispersionStatusTimer.Start();
            }
            else if (isSteadyComplete)
            {
                if (_dispersionTimeLabel != null)
                    _dispersionTimeLabel.Text = "Steady-state";
            }
            else if (isStopped)
            {
                _cfdSimPanel?.HidePlaybackControls();
                try
                {
                    if (!_cfdSimPanelUserVisible && _cfdSimDock != null)
                        _cfdSimDock.DockState = DockState.Hidden;
                }
                catch { }
                if (_dispersionTimeLabel != null)
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
                var scenario = _editor.Scene.DispersionScenario;
                _addItemPanel.SetExistingSources(scenario?.Sources);
            }
            else
            {
                _addItemDock.DockState = DockState.Hidden;
            }
        }

        private void AddItemPanel_ItemAdded(object sender, AddItemEventArgs e)
        {
            var fs = _editor.Scene;
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

            var monitors = _editor.Scene.MonitorPoints;
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

        /// <summary>Drives the status-bar progress bar + status label from
        /// <see cref="Scene3DEditorControl.ProjectIoProgress"/> events fired
        /// during SaveToFile / LoadFromFile. Force-refreshes the strip so the
        /// repaint happens synchronously even though the save/load is running
        /// on the UI thread — without that, the user sees only the final
        /// "Saved/Loaded:" message after several seconds of frozen UI.</summary>
        private void Editor_ProjectIoProgress(object sender,
            Scene3DEditorControl.ProjectIoProgressEventArgs e)
        {
            if (e == null) return;
            try
            {
                string prefix = string.IsNullOrEmpty(e.Operation) ? "" : (e.Operation + ": ");
                _statusLabel.Text = prefix + (e.Step ?? "");

                if (_ioProgressBar != null)
                {
                    if (e.Done)
                    {
                        _ioProgressBar.Value = 100;
                        _ioProgressBar.Visible = false;
                        _ioProgressBar.Style = ProgressBarStyle.Continuous;
                    }
                    else if (double.IsNaN(e.Fraction))
                    {
                        _ioProgressBar.Style = ProgressBarStyle.Marquee;
                        _ioProgressBar.Visible = true;
                    }
                    else
                    {
                        _ioProgressBar.Style = ProgressBarStyle.Continuous;
                        int pct = (int)Math.Max(0, Math.Min(100, Math.Round(e.Fraction * 100)));
                        _ioProgressBar.Value = pct;
                        _ioProgressBar.Visible = true;
                    }
                }
                // Save/Load runs on the UI thread, so the strip would otherwise
                // only repaint after the whole operation finished. Force an
                // immediate repaint so the user sees the live progress.
                _statusStrip?.Refresh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Editor_ProjectIoProgress] " + ex.Message);
            }
        }

        #endregion
    }
}
