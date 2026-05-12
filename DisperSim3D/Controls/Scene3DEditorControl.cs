using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using DisperSim3D.Models;
using DisperSim3D.Core;

namespace DisperSim3D.Controls
{
    /// <summary>
    /// Main 3D scene editor UserControl hosted via ElementHost.
    /// </summary>
    public partial class Scene3DEditorControl : UserControl
    {
        #region Fields

        private Scene3D _scene;
        private readonly ModelLoader _modelLoader;

        private HelixViewport3D _viewport;
        private ElementHost _wpfHost;

        private EditMode _currentEditMode = EditMode.Select;
        private Models.CameraMode _currentCameraMode = Models.CameraMode.Isometric;
        private bool _snapToGrid = true;
        private double _gridSpacing = 5.0;
        private LinesVisual3D _selectionHighlight;

        // Drag-to-reposition is disabled by design — moving a placed object would
        // invalidate the CFD snapshots cached for any simulations that already use
        // its current position. Edit positions through the properties panel only.

        private Decoration3D _selectedDecoration;
        private ReleaseSource3D _selectedSource;

        private GaussianPuffEngine _dispersionEngine;
        private IConcentrationField _steadyStateEngine;
        private DispersionRenderer _dispersionRenderer;
        private System.Windows.Threading.DispatcherTimer _animationTimer;
        private DispersionSimulationState _dispersionState = DispersionSimulationState.Stopped;
        private double _animationSpeedFactor = 1.0;
        private int _frameCount;
        private bool _computingFrame;
        private System.Windows.Media.Media3D.ModelVisual3D _isosurfaceVisual;
        private System.Windows.Media.Media3D.ModelVisual3D _particleVisual;
        private System.Windows.Controls.StackPanel _legendPanel;
        private System.Windows.Media.Media3D.ModelVisual3D _windArrowVisual;
        private System.Windows.Media.Media3D.ModelVisual3D _windFieldArrowsVisual;
        private AnimatedArrowField _windFieldArrowField;
        private AnimatedStreamlineField _windFieldStreamlineField;
        private System.Windows.Threading.DispatcherTimer _windFieldAnimTimer;
        private double _windFieldAnimTimeS;

        private MonitorPoint3D _pendingMonitorTemplate;
        private bool _showVectorField;
        private FireSource _pendingFireTemplate;
        private GasDetector3D _pendingDetectorTemplate;
        private Dictionary<string, double[]> _hpLeakProfiles = new Dictionary<string, double[]>();
        private Dictionary<string, ModelVisual3D> _viewVisuals = new Dictionary<string, ModelVisual3D>();
        private Dictionary<string, ModelVisual3D> _studyVisuals = new Dictionary<string, ModelVisual3D>();
        private Dictionary<string, ModelVisual3D> _allocationVisuals = new Dictionary<string, ModelVisual3D>();

        private GridLinesVisual3D _gridVisual;
        private System.Windows.Media.Media3D.ModelVisual3D _groundPlaneVisual;
        private System.Windows.Media.Media3D.Visual3D _defaultLightsVisual;
        private System.Windows.Media.Media3D.ModelVisual3D _envLightsVisual;
        private System.Windows.Media.Media3D.ModelVisual3D _skyDomeVisual;
        private readonly List<Visual3D> _compassVisuals = new List<Visual3D>();
        private bool _showGroundPlane = true;
        private double _groundSize = 200;

        // CFD (OpenFOAM) fields
        private OpenFoamEnvironment _cfdEnvironment;
        private OpenFoamRunner _cfdRunner;
        private BackgroundWorker _gaussianPuffWorker;
        private OpenFoamResult _cfdResult;
        private int _cfdPlaybackIndex;
        private bool _cfdPlaybackActive;
        private double _cfdPlaybackTimeS;
        private OpenFoamConcentrationField _cfdConcentrationField;

        private SimulationManager _simulationManager;

        #endregion

        #region Properties

        /// <summary>
        /// The current 3D scene.
        /// </summary>
        [Browsable(false)]
        public Scene3D Scene
        {
            get => _scene;
            set
            {
                _scene = value;
                UpdateViewport();
            }
        }

        /// <summary>
        /// Modo de edição atual
        /// </summary>
        [Category("Behavior")]
        [DefaultValue(EditMode.Select)]
        public EditMode CurrentEditMode
        {
            get => _currentEditMode;
            set
            {
                _currentEditMode = value;
                OnEditModeChanged();
            }
        }

        /// <summary>
        /// Modo de câmera atual
        /// </summary>
        [Category("Behavior")]
        [DefaultValue(Models.CameraMode.Isometric)]
        public Models.CameraMode CurrentCameraMode
        {
            get => _currentCameraMode;
            set
            {
                _currentCameraMode = value;
                UpdateCameraMode(value);
            }
        }

        /// <summary>
        /// Habilita snap-to-grid
        /// </summary>
        [Category("Grid")]
        [DefaultValue(true)]
        public bool SnapToGrid
        {
            get => _snapToGrid;
            set => _snapToGrid = value;
        }

        /// <summary>
        /// Espaçamento do grid em metros
        /// </summary>
        [Category("Grid")]
        [DefaultValue(5.0)]
        public double GridSpacing
        {
            get => _gridSpacing;
            set
            {
                _gridSpacing = value;
                if (_scene != null)
                {
                    _scene.GridSpacing = value;
                }
            }
        }

        /// <summary>
        /// Currently selected decoration (null if none or a unit is selected)
        /// </summary>
        [Browsable(false)]
        public Decoration3D SelectedDecoration
        {
            get => _selectedDecoration;
            set
            {
                _selectedDecoration = value;
                OnSelectedUnitChanged();
            }
        }

        [Browsable(false)]
        public ReleaseSource3D SelectedSource
        {
            get => _selectedSource;
            set
            {
                _selectedSource = value;
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Disparado quando o equipamento selecionado muda
        /// </summary>
        public event EventHandler SelectedUnitChanged;

        /// <summary>
        /// Disparado quando o modo de edição muda
        /// </summary>
        public event EventHandler EditModeChanged;

        public event EventHandler MonitorDataUpdated;
        public event EventHandler<Point3D> PointPicked;
        public event EventHandler<ObjectPlacedEventArgs> ObjectPlaced;

        #endregion

        #region Constructor

        public Scene3DEditorControl()
        {
            InitializeComponent();

            // Inicializar componentes
            _scene = new Scene3D();
            _modelLoader = new ModelLoader();

            // Criar viewport WPF
            InitializeWpfViewport();

            // Configurar eventos
            this.Resize += Scene3DEditorControl_Resize;
        }

        #endregion

        #region Initialization

        private void InitializeWpfViewport()
        {
            // Criar ElementHost para hospedar controle WPF
            _wpfHost = new ElementHost
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };

            // Criar viewport HelixToolkit
            _viewport = new HelixViewport3D
            {
                Background = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Color.FromRgb(220, 225, 230),
                    System.Windows.Media.Color.FromRgb(180, 185, 195),
                    new System.Windows.Point(0.5, 0),
                    new System.Windows.Point(0.5, 1)),
                ShowCoordinateSystem = true,
                ShowViewCube = true,
                CameraRotationMode = CameraRotationMode.Turntable,
                RotateAroundMouseDownPoint = true,
                ZoomAroundMouseDownPoint = true
            };

            // Lighting + sky dome are rebuilt by ApplyEnvironment() once _scene is set;
            // start with DefaultLights so the very first frame (before scene load) isn't
            // pitch black.
            _defaultLightsVisual = new DefaultLights();
            _viewport.Children.Add(_defaultLightsVisual);

            // Adicionar grid
            _gridVisual = new GridLinesVisual3D
            {
                Width = 100,
                Length = 100,
                MinorDistance = 1,
                MajorDistance = 5,
                Thickness = 0.01,
                Fill = System.Windows.Media.Brushes.LightGray
            };
            _viewport.Children.Add(_gridVisual);

            // Ground plane
            UpdateGroundPlane();

            // Configurar câmera inicial (isométrica)
            SetIsometricCamera();

            // Adicionar eventos de mouse
            _viewport.MouseDown += Viewport_MouseDown;
            _viewport.MouseMove += Viewport_MouseMove;
            _viewport.MouseUp += Viewport_MouseUp;
            _viewport.KeyDown += Viewport_KeyDown;
            _viewport.PreviewMouseWheel += Viewport_PreviewMouseWheel;

            // Legend overlay panel (top-right corner)
            _legendPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new System.Windows.Thickness(0, 60, 10, 0),
                Visibility = System.Windows.Visibility.Collapsed
            };

            var rootGrid = new System.Windows.Controls.Grid();
            rootGrid.Children.Add(_viewport);
            rootGrid.Children.Add(_legendPanel);

            // Hospedar no ElementHost
            _wpfHost.Child = rootGrid;

            // Adicionar ao UserControl
            this.Controls.Add(_wpfHost);
        }

        #endregion

        #region Public Methods




        /// <summary>
        /// Clears the entire scene.
        /// </summary>
        /// <summary>
        /// Starts the dispersion simulation animation
        /// </summary>
        public void StartDispersion()
        {
            var scenario = _scene.DispersionScenario;
            if (scenario == null || scenario.Sources.Count == 0) return;

            if (scenario.SolverType == CfdSolverType.GaussianPlume)
            {
                StartSteadyStateDispersion();
                return;
            }

            _cfdPlaybackActive = false;
            _dispersionEngine = new GaussianPuffEngine();
            _dispersionEngine.WindField = WindFieldResolver.ResolveWindField(_scene, scenario);
            _dispersionEngine.Initialize(scenario);

            _dispersionRenderer = new DispersionRenderer();
            _dispersionRenderer.Initialize(scenario);
            _dispersionRenderer.ComputeOccupancyGrid(_scene);

            _hpLeakProfiles.Clear();
            foreach (var src in scenario.Sources)
            {
                if (src.HighPressureLeak != null)
                {
                    var profile = HighPressureLeakModel.ComputeBlowdownProfile(
                        src.HighPressureLeak, scenario.SimulationDurationS, scenario.TimeStepS);
                    _hpLeakProfiles[src.Id] = profile;
                }
            }

            if (_scene.GasDetectors.Count > 0)
                DetectorEvaluator.Reset(_scene.GasDetectors);

            _frameCount = 0;
            _dispersionState = DispersionSimulationState.Running;

            if (scenario.Thresholds.Count > 0)
                ShowLegend(scenario.Thresholds);

            if (_animationTimer == null)
            {
                _animationTimer = new System.Windows.Threading.DispatcherTimer();
                _animationTimer.Interval = TimeSpan.FromMilliseconds(33);
                _animationTimer.Tick += AnimationTimer_Tick;
            }
            _animationTimer.Start();
        }

        /// <summary>
        /// Stops the dispersion simulation and removes visuals
        /// </summary>
        /// <summary>
        /// Shows or hides the animated wind field arrows in the 3D viewport.
        /// Reads the wind field from the scenario's associated WindFieldScenario.
        /// </summary>
        public void ToggleWindFieldArrows(bool show)
        {
            if (!show)
            {
                HideWindFieldArrows();
                return;
            }
            var scenario = _scene.DispersionScenario;
            if (scenario == null) return;
            var wfScenario = WindFieldResolver.FindWindFieldScenario(_scene, scenario);
            if (wfScenario == null)
            {
                System.Windows.MessageBox.Show(
                    "No ready wind field is associated with the active dispersion scenario.\n" +
                    "Open Dispersion → Manage Wind Fields..., create and run one, then assign it via the Scenario Manager.",
                    "Wind Field", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            ShowWindFieldArrows(wfScenario, silent: false);
        }

        public void ShowWindFieldArrows(WindFieldScenario wfScenario, bool silent = false)
        {
            if (wfScenario == null) { HideWindFieldArrows(); return; }

            WindField3D field = wfScenario.WindField;
            if (field == null && wfScenario.Status == WindFieldStatus.Ready)
            {
                // Try FluidX3D's windfield.bin first (cheap, returns null if not present);
                // fall back to OpenFOAM case reader for the legacy path.
                field = FluidX3DWindFieldRunner.LoadFromCase(wfScenario);
                if (field == null)
                    field = WindFieldRunner.LoadFromCase(wfScenario);
            }

            if (field == null)
            {
                if (!silent)
                {
                    System.Windows.MessageBox.Show(
                        "Wind field '" + wfScenario.Name + "' is not Ready (status: " + wfScenario.Status + ").\n" +
                        "Run it from the project tree (right-click → Run) before visualizing.",
                        "Wind Field", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                HideWindFieldArrows();
                return;
            }

            double cfdDomain = wfScenario.DomainSizeM;
            // Clip the visualisation to match the editor's ground plane exactly when the
            // user leaves DisplayExtentM = 0 (the CFD domain may be 1 km+ but the visible
            // scene is typically tens of metres). User can override per-scenario via the
            // property grid.
            double extent = wfScenario.DisplayExtentM > 0
                ? wfScenario.DisplayExtentM
                : _groundSize * 0.5;
            double domain = Math.Min(extent, cfdDomain);
            double height = wfScenario.DomainHeightM > 0
                ? Math.Min(wfScenario.DomainHeightM, domain)
                : domain;

            int nx = Math.Max(2, wfScenario.ArrowsPerAxis);
            int nz = Math.Max(1, wfScenario.ArrowVerticalLayers);
            System.Windows.Media.Color arrowColor = System.Windows.Media.Colors.Black;
            if (!string.IsNullOrEmpty(wfScenario.ArrowColorHex))
            {
                try
                {
                    var hex = wfScenario.ArrowColorHex;
                    if (hex.Length == 8)
                        arrowColor = System.Windows.Media.Color.FromArgb(
                            Convert.ToByte(hex.Substring(0, 2), 16),
                            Convert.ToByte(hex.Substring(2, 2), 16),
                            Convert.ToByte(hex.Substring(4, 2), 16),
                            Convert.ToByte(hex.Substring(6, 2), 16));
                    else if (hex.Length == 6)
                        arrowColor = System.Windows.Media.Color.FromRgb(
                            Convert.ToByte(hex.Substring(0, 2), 16),
                            Convert.ToByte(hex.Substring(2, 2), 16),
                            Convert.ToByte(hex.Substring(4, 2), 16));
                }
                catch { }
            }

            // Dispatch by display mode. Streamlines path uses Helix LinesVisual3D —
            // built ONCE, then animation modulates only Color (no per-frame mesh
            // rebuild). Arrows path kept for back-compat.
            bool useStreamlines = wfScenario.DisplayMode == WindFieldDisplayMode.Streamlines;

            // Tear down any previous streamline visuals before re-build (the timer below
            // expects a stable visual list).
            if (_windFieldStreamlineField != null)
            {
                _windFieldStreamlineField.RemoveFrom(_viewport);
                _windFieldStreamlineField = null;
            }

            if (useStreamlines)
            {
                _windFieldStreamlineField = WindFieldStreamlineVisual.Build(field,
                    -domain, domain, -domain, domain, height,
                    wfScenario.StreamlineCount > 0 ? wfScenario.StreamlineCount : 256,
                    wfScenario.StreamlineVerticalLayers > 0 ? wfScenario.StreamlineVerticalLayers : 1,
                    wfScenario.StreamlineThicknessFactor > 0 ? wfScenario.StreamlineThicknessFactor : 0.025,
                    wfScenario.StreamlineAnimated);
                _windFieldStreamlineField.AddTo(_viewport);
                _windFieldArrowField = null;
                if (_windFieldArrowsVisual != null)
                {
                    _viewport.Children.Remove(_windFieldArrowsVisual);
                    _windFieldArrowsVisual = null;
                }
            }
            else
            {
                _windFieldArrowField = WindFieldVisual.Build(field,
                    -domain, domain, -domain, domain, height,
                    nx, nx, nz,
                    arrowColor,
                    wfScenario.ArrowLengthFactor > 0 ? wfScenario.ArrowLengthFactor : 0.30,
                    wfScenario.ArrowThicknessFactor > 0 ? wfScenario.ArrowThicknessFactor : 0.025,
                    wfScenario.ArrowOpacity > 0 ? wfScenario.ArrowOpacity : 0.55,
                    wfScenario.ArrowAnimated);
                if (_windFieldArrowsVisual == null)
                {
                    _windFieldArrowsVisual = new System.Windows.Media.Media3D.ModelVisual3D();
                    _viewport.Children.Add(_windFieldArrowsVisual);
                }
                _windFieldArrowsVisual.Content = _windFieldArrowField.BuildVisual(0);
            }
            _windFieldAnimTimeS = 0;

            if (_windFieldAnimTimer == null)
            {
                _windFieldAnimTimer = new System.Windows.Threading.DispatcherTimer();
                _windFieldAnimTimer.Interval = TimeSpan.FromMilliseconds(80);
                _windFieldAnimTimer.Tick += (s, e) =>
                {
                    _windFieldAnimTimeS += 0.08;
                    if (_windFieldStreamlineField != null)
                        _windFieldStreamlineField.Animate(_windFieldAnimTimeS); // O(N) colour-only
                    else if (_windFieldArrowField != null && _windFieldArrowsVisual != null)
                        _windFieldArrowsVisual.Content = _windFieldArrowField.BuildVisual(_windFieldAnimTimeS);
                };
            }
            _windFieldAnimTimer.Start();
        }

        public void HideWindFieldArrows()
        {
            if (_windFieldArrowsVisual != null)
            {
                _viewport.Children.Remove(_windFieldArrowsVisual);
                _windFieldArrowsVisual = null;
            }
            if (_windFieldStreamlineField != null)
            {
                _windFieldStreamlineField.RemoveFrom(_viewport);
                _windFieldStreamlineField = null;
            }
            _windFieldArrowField = null;
            if (_windFieldAnimTimer != null)
            {
                _windFieldAnimTimer.Stop();
                _windFieldAnimTimer = null;
            }
        }

        public bool IsWindFieldArrowsVisible =>
            _windFieldArrowsVisual != null || _windFieldStreamlineField != null;

        public void StopDispersion()
        {
            _dispersionState = DispersionSimulationState.Stopped;
            if (_animationTimer != null)
                _animationTimer.Stop();

            if (_dispersionEngine != null)
                _dispersionEngine.Reset();

            RemoveDispersionVisuals();
            HideLegend();
            _steadyStateEngine = null;
        }

        /// <summary>
        /// Computes and displays the steady-state Gaussian plume concentration field.
        /// Renders isosurfaces and contour planes in a single pass with no animation.
        /// </summary>
        public async void StartSteadyStateDispersion()
        {
            var scenario = _scene.DispersionScenario;
            if (scenario == null || scenario.Sources.Count == 0) return;

            StopDispersion();

            var plume = new GaussianPlumeEngine();
            plume.WindField = WindFieldResolver.ResolveWindField(_scene, scenario);
            plume.Initialize(scenario);
            _steadyStateEngine = plume;

            var renderer = new DispersionRenderer();
            renderer.Initialize(scenario);
            renderer.ComputeOccupancyGrid(_scene);
            _dispersionRenderer = renderer;

            RemoveDispersionVisuals();

            var thresholds = scenario.Thresholds;
            var engine = _steadyStateEngine;
            var monitors = _scene.MonitorPoints.ToList();
            var detectors = _scene.GasDetectors;
            bool hasContours = scenario.ContourPlanes.Count > 0;
            var contourConfigs = hasContours ? scenario.ContourPlanes.Where(cp => cp.Visible).ToList() : null;

            var result = await Task.Run(() =>
            {
                if (thresholds.Count == 0)
                {
                    renderer.ComputeIsosurfaces(engine, thresholds);
                    double maxC = renderer.GetMaxConcentration();
                    if (maxC > 1e-20)
                    {
                        var autoThresholds = new List<DispersionThreshold>
                        {
                            new DispersionThreshold { Name = "High", ConcentrationValue = maxC * 0.1,
                                Color = System.Windows.Media.Colors.Red, Opacity = 0.6, Visible = true },
                            new DispersionThreshold { Name = "Medium", ConcentrationValue = maxC * 0.01,
                                Color = System.Windows.Media.Colors.Orange, Opacity = 0.35, Visible = true },
                            new DispersionThreshold { Name = "Low", ConcentrationValue = maxC * 0.001,
                                Color = System.Windows.Media.Colors.Yellow, Opacity = 0.12, Visible = true }
                        };
                        return ComputeSteadyState(renderer, engine, autoThresholds, monitors, contourConfigs, autoThresholds);
                    }
                }
                return ComputeSteadyState(renderer, engine, thresholds, monitors, contourConfigs, thresholds);
            });

            thresholds = result.Thresholds;

            RemoveDispersionVisuals();

            if (result.IsoGroup != null && result.IsoGroup.Children.Count > 0)
            {
                _isosurfaceVisual = new ModelVisual3D { Content = result.IsoGroup };
                _isosurfaceVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("DispersionIsosurface", "iso"));
                _viewport.Children.Add(_isosurfaceVisual);
            }

            if (_steadyStateEngine is GaussianPlumeEngine plumeSS)
            {
                foreach (var path in result.Trajectories)
                {
                    if (path.Count < 2) continue;
                    var lines = new LinesVisual3D { Color = System.Windows.Media.Colors.Cyan, Thickness = 3 };
                    for (int ti = 0; ti < path.Count - 1; ti++)
                    {
                        lines.Points.Add(path[ti]);
                        lines.Points.Add(path[ti + 1]);
                    }
                    lines.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("TrajectoryLine", "trajectory"));
                    _viewport.Children.Add(lines);
                }
            }

            if (thresholds.Count > 0)
                ShowLegend(thresholds);

            foreach (var cg in result.ContourGroups)
            {
                var cpVisual = new ModelVisual3D { Content = cg };
                cpVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("ContourPlane", "contour"));
                _viewport.Children.Add(cpVisual);
            }

            if (detectors.Count > 0)
            {
                DetectorEvaluator.Reset(detectors);
                DetectorEvaluator.EvaluateStep(detectors, _steadyStateEngine, 0);
            }

            foreach (var (mon, c) in result.MonitorData)
                mon.TimeSeries.Add(new MonitorSample { TimeS = 0, Concentration = ApplyMonitorTransform(mon, c) });

            _dispersionState = DispersionSimulationState.SteadyStateComplete;
        }

        private struct SteadyStateResult
        {
            public Model3DGroup IsoGroup;
            public List<List<Point3D>> Trajectories;
            public List<Model3DGroup> ContourGroups;
            public List<(MonitorPoint3D mon, double c)> MonitorData;
            public List<DispersionThreshold> Thresholds;
        }

        private SteadyStateResult ComputeSteadyState(
            DispersionRenderer renderer, IConcentrationField engine,
            List<DispersionThreshold> thresholds,
            List<MonitorPoint3D> monitors,
            List<ContourPlaneConfig> contourConfigs,
            List<DispersionThreshold> resultThresholds)
        {
            var isoGroup = renderer.ComputeIsosurfaces(engine, thresholds);

            var trajectories = new List<List<Point3D>>();
            if (engine is GaussianPlumeEngine plumeEngine)
                trajectories = plumeEngine.GetTrajectoryPaths();

            var contourGroups = new List<Model3DGroup>();
            if (contourConfigs != null && contourConfigs.Count > 0)
            {
                double maxConc = renderer.GetMaxConcentration();
                double dom = renderer.DomainSize;
                foreach (var cp in contourConfigs)
                    contourGroups.Add(renderer.ComputeContourPlane(engine, cp, -dom, dom, maxConc));
            }

            var monitorData = new List<(MonitorPoint3D mon, double c)>();
            foreach (var mon in monitors)
            {
                if (!mon.Visible) continue;
                double c = engine.EvaluateConcentration(mon.Position.X, mon.Position.Y, mon.Position.Z);
                monitorData.Add((mon, c));
            }

            return new SteadyStateResult
            {
                IsoGroup = isoGroup,
                Trajectories = trajectories,
                ContourGroups = contourGroups,
                MonitorData = monitorData,
                Thresholds = resultThresholds
            };
        }

        private void OnSimManagerProgress(object sender, (SimulationJob Job, OpenFoamProgress Progress) e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<object, (SimulationJob, OpenFoamProgress)>(OnSimManagerProgress), sender, e);
                return;
            }
            CfdProgressUpdated?.Invoke(this, e.Progress);
        }

        private void OnSimManagerStatusChanged(object sender, SimulationJob job)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<object, SimulationJob>(OnSimManagerStatusChanged), sender, job);
                return;
            }
            SimulationJobStatusChanged?.Invoke(this, job);
        }

        private void OnSimManagerJobCompleted(object sender, SimulationJob job)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<object, SimulationJob>(OnSimManagerJobCompleted), sender, job);
                return;
            }

            if (job.Status != SimulationJobStatus.Completed || job.ResultEntry == null)
                return;

            var entry = job.ResultEntry;

            // Remove any prior entry that targets the same Simulation — without this,
            // re-running a Simulation leaves the stale entry from the previous session
            // (with Tag=null after reload) in the list, and the visibility-checkbox
            // lookup picks it instead of the fresh result.
            _scene.CfdSimulations.RemoveAll(en =>
                (!string.IsNullOrEmpty(en.Id) && en.Id == entry.Id) ||
                (!string.IsNullOrEmpty(en.Name) && en.Name == entry.Name) ||
                (!string.IsNullOrEmpty(en.ScenarioName) && en.ScenarioName == entry.ScenarioName
                 && en.SolverType == entry.SolverType));

            _scene.CfdSimulations.Add(entry);
            CfdSolveCompleted?.Invoke(this, entry);

            if (job.SolverType == CfdSolverType.GaussianPlume && entry.Tag is SteadyStateResultData ssData)
            {
                _steadyStateEngine = ssData.Engine;
                _dispersionRenderer = ssData.Renderer;
                DisplaySteadyStateResult(ssData);
            }
            else if (entry.Tag is OpenFoamResult ofResult)
            {
                _cfdResult = ofResult;
            }
        }

        private void DisplaySteadyStateResult(SteadyStateResultData data)
        {
            RemoveDispersionVisuals();

            if (data.IsoGroup != null && data.IsoGroup.Children.Count > 0)
            {
                _isosurfaceVisual = new ModelVisual3D { Content = data.IsoGroup };
                _isosurfaceVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("DispersionIsosurface", "iso"));
                _viewport.Children.Add(_isosurfaceVisual);
            }

            if (data.Trajectories != null)
            {
                foreach (var path in data.Trajectories)
                {
                    if (path.Count < 2) continue;
                    var lines = new LinesVisual3D { Color = System.Windows.Media.Colors.Cyan, Thickness = 3 };
                    for (int ti = 0; ti < path.Count - 1; ti++)
                    {
                        lines.Points.Add(path[ti]);
                        lines.Points.Add(path[ti + 1]);
                    }
                    lines.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("TrajectoryLine", "trajectory"));
                    _viewport.Children.Add(lines);
                }
            }

            if (data.Thresholds != null && data.Thresholds.Count > 0)
                ShowLegend(data.Thresholds);

            if (data.ContourGroups != null)
            {
                foreach (var cg in data.ContourGroups)
                {
                    var cpVisual = new ModelVisual3D { Content = cg };
                    cpVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("ContourPlane", "contour"));
                    _viewport.Children.Add(cpVisual);
                }
            }

            if (_scene.GasDetectors.Count > 0)
            {
                DetectorEvaluator.Reset(_scene.GasDetectors);
                DetectorEvaluator.EvaluateStep(_scene.GasDetectors, data.Engine, 0);
            }

            foreach (var monitor in _scene.MonitorPoints)
            {
                if (!monitor.Visible) continue;
                double c = data.Engine.EvaluateConcentration(
                    monitor.Position.X, monitor.Position.Y, monitor.Position.Z);
                monitor.TimeSeries.Add(new MonitorSample { TimeS = 0, Concentration = ApplyMonitorTransform(monitor, c) });
            }

            _dispersionState = DispersionSimulationState.SteadyStateComplete;
        }

        public SimulationJob EnqueueSimulation(CfdSolverType solverType, CfdConfiguration config = null)
        {
            var scenario = _scene.DispersionScenario;
            if (scenario == null || scenario.Sources.Count == 0) return null;

            if (config == null)
                config = scenario.CfdConfig ?? new CfdConfiguration();

            // Per-triangle voxelization (UI-thread only — Model3DGroup is a WPF
            // DependencyObject). Matches the wind-field path so the CPU dispersion
            // engine sees the actual obstacle geometry instead of one giant AABB
            // wrapping the whole imported model.
            var obstacles = new List<BoundingBox>();
            foreach (var deco in _scene.Decorations)
            {
                var aabbs = FluidX3DObstacleVoxelizer.ExtractWorldAabbs(deco);
                if (aabbs != null) obstacles.AddRange(aabbs);
            }

            var hpProfiles = new Dictionary<string, double[]>();
            foreach (var src in scenario.Sources)
            {
                if (src.HighPressureLeak != null)
                {
                    var profile = HighPressureLeakModel.ComputeBlowdownProfile(
                        src.HighPressureLeak, scenario.SimulationDurationS, scenario.TimeStepS);
                    hpProfiles[src.Id] = profile;
                }
            }

            _dispersionState = DispersionSimulationState.SolvingCfd;

            return SimulationManager.Enqueue(
                scenario, solverType, config, _scene, CfdEnvironment,
                obstacles, hpProfiles);
        }

        /// <summary>
        /// Pauses the dispersion simulation
        /// </summary>
        public void PauseDispersion()
        {
            if (_dispersionState == DispersionSimulationState.Running)
            {
                _dispersionState = DispersionSimulationState.Paused;
                if (_animationTimer != null)
                    _animationTimer.Stop();
            }
        }

        /// <summary>
        /// Resumes the dispersion simulation
        /// </summary>
        public void ResumeDispersion()
        {
            if (_dispersionState == DispersionSimulationState.Paused)
            {
                _dispersionState = DispersionSimulationState.Running;
                if (_animationTimer != null)
                    _animationTimer.Start();
            }
        }

        /// <summary>
        /// Rewinds the dispersion to the beginning and restarts playback.
        /// </summary>
        public void RewindDispersion()
        {
            if (_cfdPlaybackActive && _cfdResult != null && _cfdResult.TimeSteps.Count > 0)
            {
                _cfdPlaybackIndex = 0;
                _cfdPlaybackTimeS = _cfdResult.TimeSteps[0];
                _dispersionState = DispersionSimulationState.Running;
                _frameCount = 0;
                if (_animationTimer != null)
                    _animationTimer.Start();
                return;
            }

            if (_dispersionEngine != null)
            {
                var scenario = _scene.DispersionScenario;
                _dispersionEngine.Reset();
                _dispersionEngine.Initialize(scenario);
                _frameCount = 0;
                _dispersionState = DispersionSimulationState.Running;
                if (_animationTimer != null)
                    _animationTimer.Start();
            }
        }

        /// <summary>
        /// Seeks CFD playback to a fractional position (0.0 to 1.0).
        /// </summary>
        public async void SeekCfdPlayback(double fraction)
        {
            // Allow scrubbing whenever a result is loaded — even if playback isn't
            // actively running. Previously the _cfdPlaybackActive check made the slider
            // a no-op once auto-play finished or the user pressed Stop.
            if (_cfdResult == null || _cfdResult.TimeSteps.Count == 0) return;
            if (_computingFrame) return;

            double firstTime = _cfdResult.TimeSteps[0];
            double lastTime = _cfdResult.TimeSteps[_cfdResult.TimeSteps.Count - 1];
            double targetTime = firstTime + fraction * (lastTime - firstTime);

            _cfdPlaybackTimeS = targetTime;
            _cfdPlaybackIndex = 0;
            while (_cfdPlaybackIndex < _cfdResult.TimeSteps.Count - 1 &&
                   _cfdResult.TimeSteps[_cfdPlaybackIndex + 1] <= targetTime)
                _cfdPlaybackIndex++;

            if (_dispersionState != DispersionSimulationState.Running)
                await RenderCfdFrameAsync(_cfdPlaybackIndex);
        }

        private async Task RenderCfdFrameAsync(int frameIndex)
        {
            if (_cfdResult == null || !_cfdResult.IsLoaded || _computingFrame) return;
            if (frameIndex < 0 || frameIndex >= _cfdResult.TimeSteps.Count) return;

            _computingFrame = true;
            try
            {
                var scenario = _scene.DispersionScenario;
                double t = _cfdResult.TimeSteps[frameIndex];
                var cfdResult = _cfdResult;
                var renderer = _dispersionRenderer;
                var thresholds = scenario?.Thresholds;
                bool doContours = scenario != null && scenario.ContourPlanes.Count > 0;
                bool doVectors = _showVectorField && scenario != null;
                var contourConfigs = doContours ? scenario.ContourPlanes.Where(cp => cp.Visible).ToList() : null;
                var windVec = scenario?.Meteo.WindVector ?? new Vector3D();

                var result = await Task.Run(() =>
                {
                    var field = cfdResult.GetField(t);
                    if (field == null) return (object)null;

                    var concField = new OpenFoamConcentrationField(
                        field, cfdResult.DomainXMin, cfdResult.DomainXMax,
                        cfdResult.DomainYMin, cfdResult.DomainYMax, cfdResult.DomainZMax);

                    renderer.SetScalarFieldDirect(field);

                    Model3DGroup isoGroup = renderer.ComputeCloudVisual(thresholds);

                    double maxC = renderer.GetMaxConcentration();

                    var contourGroups = new List<Model3DGroup>();
                    Model3DGroup vectorGroup = null;
                    if (doContours || doVectors)
                    {
                        double dom = renderer.DomainSize;
                        if (doContours)
                        {
                            foreach (var cp in contourConfigs)
                                contourGroups.Add(renderer.ComputeContourPlane(concField, cp, -dom, dom, maxC));
                        }
                        if (doVectors)
                            vectorGroup = renderer.ComputeVectorField(concField, windVec, maxC);
                    }

                    return new { concField, isoGroup, contourGroups, vectorGroup };
                });

                if (result == null) return;
                dynamic r = result;

                _cfdConcentrationField = r.concField;

                RemoveDispersionVisuals();

                if (r.isoGroup != null)
                {
                    _isosurfaceVisual = new ModelVisual3D { Content = r.isoGroup };
                    _isosurfaceVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("DispersionIsosurface", "iso"));
                    _viewport.Children.Add(_isosurfaceVisual);
                }

                foreach (var cg in (List<Model3DGroup>)r.contourGroups)
                {
                    var cpVisual = new ModelVisual3D { Content = cg };
                    cpVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("ContourPlane", "contour"));
                    _viewport.Children.Add(cpVisual);
                }

                if (r.vectorGroup != null)
                {
                    var vfVisual = new ModelVisual3D { Content = r.vectorGroup };
                    vfVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("VectorField", "vectors"));
                    _viewport.Children.Add(vfVisual);
                }
            }
            finally
            {
                _computingFrame = false;
            }
        }

        /// <summary>
        /// Gets the total simulation duration in seconds.
        /// </summary>
        public double SimulationTotalDurationS
        {
            get
            {
                if (_cfdPlaybackActive && _cfdResult != null && _cfdResult.TimeSteps.Count > 0)
                    return _cfdResult.TimeSteps[_cfdResult.TimeSteps.Count - 1];
                var sc = _scene?.DispersionScenario;
                return sc != null ? sc.SimulationDurationS : 0;
            }
        }

        /// <summary>
        /// Gets the current dispersion simulation state
        /// </summary>
        public DispersionSimulationState DispersionState => _dispersionState;

        /// <summary>
        /// Gets the current simulation time in seconds
        /// </summary>
        public double DispersionTimeS
        {
            get
            {
                // Return the current CFD playback time whenever a result is loaded —
                // not just while playback is actively running. After Stop or auto-end,
                // the slider's seek position should still be readable.
                if (_cfdResult != null && _cfdResult.IsLoaded && _cfdResult.TimeSteps.Count > 0
                    && _cfdPlaybackIndex >= 0 && _cfdPlaybackIndex < _cfdResult.TimeSteps.Count)
                    return _cfdResult.TimeSteps[_cfdPlaybackIndex];
                return _dispersionEngine != null ? _dispersionEngine.CurrentTimeS : 0;
            }
        }

        public OpenFoamEnvironment CfdEnvironment
        {
            get
            {
                if (_cfdEnvironment == null)
                {
                    _cfdEnvironment = new OpenFoamEnvironment();
                }
                return _cfdEnvironment;
            }
        }

        public OpenFoamRunner CfdRunner => _cfdRunner;
        public OpenFoamResult CfdResult => _cfdResult;
        public bool IsCfdPlaybackActive => _cfdPlaybackActive;

        public SimulationManager SimulationManager
        {
            get
            {
                if (_simulationManager == null)
                {
                    _simulationManager = new SimulationManager(2);
                    _simulationManager.JobProgressUpdated += OnSimManagerProgress;
                    _simulationManager.JobCompleted += OnSimManagerJobCompleted;
                    _simulationManager.JobStatusChanged += OnSimManagerStatusChanged;
                }
                return _simulationManager;
            }
        }

        public event EventHandler<OpenFoamProgress> CfdProgressUpdated;
        public event EventHandler<CfdSimulationEntry> CfdSolveCompleted;
        public event EventHandler<SimulationJob> SimulationJobStatusChanged;

        public void StartCfdSolve(CfdConfiguration config)
        {
            var scenario = _scene.DispersionScenario;
            if (scenario == null) return;

            if (config == null)
                config = scenario.CfdConfig ?? new CfdConfiguration();

            var env = CfdEnvironment;
            env.Configure(config.OpenFoamPath, config.DetectedEnvironment, config.WslDistroName);
            if (!env.IsAvailable)
            {
                _dispersionState = DispersionSimulationState.SolvingCfd;
                CfdProgressUpdated?.Invoke(this, new OpenFoamProgress
                {
                    IsError = true,
                    Step = "OpenFOAM not available",
                    LogLine = env.StatusMessage + "\n\nPlease configure the OpenFOAM path in CFD Settings."
                });
                _dispersionState = DispersionSimulationState.Stopped;
                return;
            }

            if (config.GridResolution > 0)
                scenario.GridResolution = config.GridResolution;

            _dispersionState = DispersionSimulationState.SolvingCfd;

            _cfdRunner = new OpenFoamRunner(env);
            _cfdRunner.ProgressUpdated += (s, p) =>
            {
                BeginInvoke(new Action(() => CfdProgressUpdated?.Invoke(this, p)));
            };
            _cfdRunner.Completed += (s, result) =>
            {
                _cfdResult = result;
                BeginInvoke(new Action(() =>
                {
                    _dispersionState = DispersionSimulationState.Stopped;

                    bool isSteady = scenario.SolverType == CfdSolverType.ScalarTransportFoamSteady
                                 || scenario.SolverType == CfdSolverType.ScalarSimpleFoam;
                    string solverLabel = "[" + Core.SolverCode.Of(scenario.SolverType) + "] " + Core.SolverCode.DisplayName(scenario.SolverType);
                    string baseName = string.IsNullOrEmpty(scenario.Name) ? solverLabel : scenario.Name;
                    var entry = new CfdSimulationEntry
                    {
                        Name = baseName + " #" + (_scene.CfdSimulations.Count + 1),
                        ScenarioName = scenario.Name,
                        SolverType = solverLabel,
                        CasePath = _cfdRunner.CasePath,
                        DurationS = isSteady ? 0 : scenario.SimulationDurationS,
                        TimeStepCount = result.TimeSteps.Count,
                        GridNx = result.GridNx,
                        GridNy = result.GridNy,
                        GridNz = result.GridNz,
                        DomainSizeM = result.DomainSizeM,
                        HasResults = result.IsLoaded
                    };

                    CfdProgressUpdated?.Invoke(this, new OpenFoamProgress
                    {
                        IsComplete = true,
                        IsError = !result.IsLoaded,
                        Step = result.IsLoaded
                            ? string.Format("Complete — {0} time steps loaded", result.TimeSteps.Count)
                            : "WARNING: Solver finished but no results could be read",
                        LogLine = !result.IsLoaded
                            ? "Check that grid resolution matches the case, and that the T field is non-uniform."
                            : null,
                        Fraction = 1.0
                    });

                    _scene.CfdSimulations.Add(entry);
                    CfdSolveCompleted?.Invoke(this, entry);
                }));
            };
            _cfdRunner.Failed += (s, msg) =>
            {
                BeginInvoke(new Action(() =>
                {
                    _dispersionState = DispersionSimulationState.Stopped;
                    CfdProgressUpdated?.Invoke(this, new OpenFoamProgress
                        { IsError = true, Step = "FAILED: " + msg, LogLine = msg, Fraction = 0 });
                }));
            };

            if (scenario.SolverType == CfdSolverType.ScalarTransportFoamSteady ||
                scenario.SolverType == CfdSolverType.ScalarSimpleFoam ||
                scenario.SolverType == CfdSolverType.RhoSimpleFoam)
                _cfdRunner.RunSteadyAsync(scenario, config, scenario.SolverType);
            else
                _cfdRunner.RunAsync(scenario, config, scenario.SolverType);
        }

        public void RunGaussianPuffAsync()
        {
            var scenario = _scene.DispersionScenario;
            if (scenario == null || scenario.Sources.Count == 0) return;

            _dispersionState = DispersionSimulationState.SolvingCfd;

            int nx = scenario.GridResolution;
            int ny = scenario.GridResolution;
            int nz = Math.Max(1, scenario.GridResolution / 2);
            double domain = scenario.DomainSizeM;
            double endTime = scenario.SimulationDurationS;
            double dt = scenario.TimeStepS;
            int totalSteps = (int)Math.Ceiling(endTime / dt);
            int writeEvery = Math.Max(1, totalSteps / 100);

            var worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            _gaussianPuffWorker = worker;

            var config = scenario.CfdConfig ?? new CfdConfiguration();
            bool useWindField = config.UseWindField && CfdEnvironment.IsAvailable;

            var obstacles = new List<Models.BoundingBox>();
            if (useWindField)
            {
                foreach (var deco in _scene.Decorations)
                    if (deco.BoundingBox != null) obstacles.Add(deco.BoundingBox);
            }

            var preResolvedWindField = WindFieldResolver.ResolveWindField(_scene, scenario);
            worker.DoWork += (s, e) =>
            {
                var engine = new GaussianPuffEngine();
                if (preResolvedWindField != null) engine.WindField = preResolvedWindField;
                engine.Initialize(scenario);

                if (useWindField)
                {
                    try
                    {
                        worker.ReportProgress(0, new OpenFoamProgress
                        {
                            Fraction = 0.0,
                            Step = "Computing wind field around obstacles..."
                        });

                        var env = CfdEnvironment;
                        env.Configure(config.OpenFoamPath, config.DetectedEnvironment, config.WslDistroName);
                        var windRunner = new OpenFoamRunner(env);

                        string windCaseDir = OpenFoamCaseGenerator.GenerateWindCase(
                            scenario, config, obstacles.Count > 0 ? obstacles : null);

                        var windField = windRunner.RunWindCase(windCaseDir,
                            nx, ny, nz, -domain, domain, -domain, domain, domain,
                            obstacles.Count > 0,
                            config.NumberOfProcessors > 1 ? config.NumberOfProcessors : 1,
                            (frac, msg) => worker.ReportProgress(0, new OpenFoamProgress
                            {
                                Fraction = frac * 0.2,
                                Step = msg
                            }));

                        if (windField != null)
                            engine.WindField = windField;
                    }
                    catch (Exception ex)
                    {
                        worker.ReportProgress(0, new OpenFoamProgress
                        {
                            Fraction = 0.0,
                            Step = "Wind field failed, using uniform wind: " + ex.Message,
                            LogLine = ex.ToString()
                        });
                    }
                }

                string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "DisperSim_GP_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                System.IO.Directory.CreateDirectory(tempDir);

                var result = new OpenFoamResult
                {
                    GridNx = nx, GridNy = ny, GridNz = nz,
                    DomainSizeM = domain,
                    DomainXMin = -domain, DomainXMax = domain,
                    DomainYMin = -domain, DomainYMax = domain,
                    DomainZMax = domain,
                    CaseDir = tempDir
                };

                double cellSizeX = (domain * 2.0) / nx;
                double cellSizeY = (domain * 2.0) / ny;
                double cellSizeZ = domain / nz;

                for (int step = 1; step <= totalSteps; step++)
                {
                    if (worker.CancellationPending) { e.Cancel = true; return; }

                    double t = step * dt;
                    if (t > endTime) t = endTime;
                    engine.StepTo(t);

                    if (step % writeEvery == 0 || step == totalSteps)
                    {
                        var field = new double[nx, ny, nz];
                        for (int i = 0; i < nx; i++)
                            for (int j = 0; j < ny; j++)
                                for (int k = 0; k < nz; k++)
                                {
                                    double x = -domain + (i + 0.5) * cellSizeX;
                                    double y = -domain + (j + 0.5) * cellSizeY;
                                    double z = (k + 0.5) * cellSizeZ;
                                    field[i, j, k] = engine.EvaluateConcentration(x, y, z);
                                }

                        string binPath = System.IO.Path.Combine(tempDir, t.ToString("F4") + ".bin");
                        OpenFoamResult.SaveBinaryField(binPath, field);
                        result.TimeSteps.Add(t);
                        result.TimeStepPaths[t] = binPath;
                    }

                    double rawFraction = (double)step / totalSteps;
                    double fraction = useWindField
                        ? 0.2 + rawFraction * 0.8
                        : rawFraction;
                    string windLabel = engine.WindField != null ? " [wind field]" : "";
                    worker.ReportProgress((int)(fraction * 100), new OpenFoamProgress
                    {
                        Fraction = fraction,
                        Step = string.Format("Gaussian Puff (t={0:F1}/{1:F0}s) — {2} puffs{3}",
                            t, endTime, engine.ActivePuffs.Count, windLabel)
                    });
                }

                result.IsLoaded = result.TimeSteps.Count > 0;
                e.Result = result;
            };

            worker.ProgressChanged += (s, e) =>
            {
                var p = e.UserState as OpenFoamProgress;
                if (p != null)
                    CfdProgressUpdated?.Invoke(this, p);
            };

            worker.RunWorkerCompleted += (s, e) =>
            {
                _gaussianPuffWorker = null;
                _dispersionState = DispersionSimulationState.Stopped;

                if (e.Cancelled)
                {
                    CfdProgressUpdated?.Invoke(this, new OpenFoamProgress
                        { IsError = true, Step = "Cancelled by user", Fraction = 0 });
                    return;
                }
                if (e.Error != null)
                {
                    CfdProgressUpdated?.Invoke(this, new OpenFoamProgress
                        { IsError = true, Step = "FAILED: " + e.Error.Message, LogLine = e.Error.ToString(), Fraction = 0 });
                    return;
                }

                var result = e.Result as OpenFoamResult;
                if (result == null || !result.IsLoaded)
                {
                    CfdProgressUpdated?.Invoke(this, new OpenFoamProgress
                        { IsError = true, Step = "No results generated", Fraction = 0 });
                    return;
                }

                _cfdResult = result;

                var entry = new CfdSimulationEntry
                {
                    Name = (string.IsNullOrEmpty(scenario.Name) ? "Gaussian Puff" : scenario.Name)
                           + " #" + (_scene.CfdSimulations.Count + 1),
                    ScenarioName = scenario.Name,
                    SolverType = "Gaussian Puff",
                    CasePath = result.CaseDir,
                    DurationS = scenario.SimulationDurationS,
                    TimeStepCount = result.TimeSteps.Count,
                    GridNx = nx, GridNy = ny, GridNz = nz,
                    DomainSizeM = domain,
                    HasResults = true
                };

                CfdProgressUpdated?.Invoke(this, new OpenFoamProgress
                {
                    IsComplete = true,
                    Step = string.Format("Complete — {0} time steps", result.TimeSteps.Count),
                    Fraction = 1.0
                });

                _scene.CfdSimulations.Add(entry);
                CfdSolveCompleted?.Invoke(this, entry);
            };

            worker.RunWorkerAsync();
        }

        public void CancelGaussianPuff()
        {
            _gaussianPuffWorker?.CancelAsync();
        }

        public void StartCfdPlayback()
        {
            if (_cfdResult == null || !_cfdResult.IsLoaded || _cfdResult.TimeSteps.Count == 0) return;

            var scenario = _scene.DispersionScenario;
            _dispersionRenderer = new DispersionRenderer();
            _dispersionRenderer.Initialize(scenario);
            _dispersionRenderer.SetDomainBounds(
                _cfdResult.DomainXMin, _cfdResult.DomainXMax,
                _cfdResult.DomainYMin, _cfdResult.DomainYMax,
                _cfdResult.DomainZMax);

            System.Diagnostics.Debug.WriteLine("StartCfdPlayback: existing thresholds=" + scenario.Thresholds.Count);
            scenario.Thresholds.Clear();

            double lastTime = _cfdResult.TimeSteps[_cfdResult.TimeSteps.Count - 1];
                var lastField = _cfdResult.GetField(lastTime);
                System.Diagnostics.Debug.WriteLine(string.Format(
                    "StartCfdPlayback: lastTime={0}, lastField={1}, timeSteps={2}, paths={3}",
                    lastTime, lastField != null ? "OK" : "NULL",
                    _cfdResult.TimeSteps.Count, _cfdResult.TimeStepPaths.Count));
                if (lastField == null)
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: Cannot load last field, checking first timestep...");
                    if (_cfdResult.TimeSteps.Count > 0)
                    {
                        lastField = _cfdResult.GetField(_cfdResult.TimeSteps[0]);
                        System.Diagnostics.Debug.WriteLine("First field: " + (lastField != null ? "OK" : "NULL"));
                    }
                }
                if (lastField != null)
                {
                    double fieldMax = 0;
                    for (int i = 0; i < lastField.GetLength(0); i++)
                        for (int j = 0; j < lastField.GetLength(1); j++)
                            for (int k = 0; k < lastField.GetLength(2); k++)
                                if (lastField[i, j, k] > fieldMax) fieldMax = lastField[i, j, k];
                    System.Diagnostics.Debug.WriteLine(string.Format(
                        "StartCfdPlayback: field dims=[{0},{1},{2}], fieldMax={3}, path={4}",
                        lastField.GetLength(0), lastField.GetLength(1), lastField.GetLength(2),
                        fieldMax, _cfdResult.TimeStepPaths.ContainsKey(_cfdResult.TimeSteps[_cfdResult.TimeSteps.Count - 1])
                            ? _cfdResult.TimeStepPaths[_cfdResult.TimeSteps[_cfdResult.TimeSteps.Count - 1]] : "N/A"));
                    _dispersionRenderer.SetScalarFieldDirect(lastField);
                }
                double maxC = _dispersionRenderer.GetMaxConcentration();
                System.Diagnostics.Debug.WriteLine("maxC from renderer = " + maxC);
                if (maxC > 1e-20)
                {
                    scenario.Thresholds.Add(new DispersionThreshold
                    {
                        Name = "High (10%)",
                        ConcentrationValue = maxC * 0.1,
                        Color = System.Windows.Media.Colors.Red,
                        Opacity = 0.6,
                        Visible = true
                    });
                    scenario.Thresholds.Add(new DispersionThreshold
                    {
                        Name = "Medium (1%)",
                        ConcentrationValue = maxC * 0.01,
                        Color = System.Windows.Media.Colors.Orange,
                        Opacity = 0.35,
                        Visible = true
                    });
                    scenario.Thresholds.Add(new DispersionThreshold
                    {
                        Name = "Low (0.1%)",
                        ConcentrationValue = maxC * 0.001,
                        Color = System.Windows.Media.Colors.Yellow,
                        Opacity = 0.12,
                        Visible = true
                    });
                }

            if (scenario.Thresholds.Count > 0)
                ShowLegend(scenario.Thresholds);

            _cfdPlaybackIndex = 0;
            _cfdPlaybackTimeS = _cfdResult.TimeSteps[0];
            _cfdPlaybackActive = true;
            _dispersionState = DispersionSimulationState.Running;
            _frameCount = 0;

            if (_animationTimer == null)
            {
                _animationTimer = new System.Windows.Threading.DispatcherTimer();
                _animationTimer.Interval = TimeSpan.FromMilliseconds(33);
                _animationTimer.Tick += AnimationTimer_Tick;
            }
            _animationTimer.Start();
        }

        public void StopCfdPlayback()
        {
            _cfdPlaybackActive = false;
            _cfdPlaybackIndex = 0;
            _cfdPlaybackTimeS = 0;
            _cfdConcentrationField = null;
            StopDispersion();
        }

        public bool LoadCfdSimulation(CfdSimulationEntry entry)
        {
            if (entry == null) return false;

            // FluidX3D (and any in-memory result producer) attaches the full OpenFoamResult
            // — with snapshots already PreloadField'd — to entry.Tag. Use it directly
            // instead of trying to read from a non-existent case directory.
            if (entry.Tag is OpenFoamResult cached && cached.IsLoaded && cached.TimeSteps.Count > 0)
            {
                _cfdResult = cached;
                StartCfdPlayback();
                return true;
            }

            // Generic on-disk path for any solver that writes a flat directory of
            // <time>.bin files (FluidX3D dispersion, Gaussian Puff with wind subgrid).
            // Detected by the ABSENCE of an OpenFOAM controlDict + PRESENCE of .bin files.
            if (!string.IsNullOrEmpty(entry.CasePath) && System.IO.Directory.Exists(entry.CasePath))
            {
                string controlDict = System.IO.Path.Combine(entry.CasePath, "system", "controlDict");
                var rootBins = System.IO.Directory.GetFiles(entry.CasePath, "*.bin",
                    System.IO.SearchOption.TopDirectoryOnly);
                if (!System.IO.File.Exists(controlDict) && rootBins.Length > 0)
                {
                    // FluidX3D Steady writes a single converged snapshot — detect that
                    // either by the solver-type tag in the entry name OR by the on-disk
                    // file count (1 bin file = steady).
                    bool isSteady = (entry.SolverType?.IndexOf("FX3DDS", StringComparison.Ordinal) >= 0)
                                 || (entry.Name?.IndexOf("FX3DDS", StringComparison.Ordinal) >= 0)
                                 || rootBins.Length == 1;
                    var rebuilt = new OpenFoamResult
                    {
                        GridNx = entry.GridNx, GridNy = entry.GridNy, GridNz = entry.GridNz,
                        DomainSizeM = entry.DomainSizeM,
                        DomainXMin = -entry.DomainSizeM, DomainXMax = entry.DomainSizeM,
                        DomainYMin = -entry.DomainSizeM, DomainYMax = entry.DomainSizeM,
                        DomainZMax = entry.DomainSizeM,
                        IsSteadyState = isSteady,
                        CaseDir = entry.CasePath
                    };
                    foreach (var binFile in rootBins)
                    {
                        string name = System.IO.Path.GetFileNameWithoutExtension(binFile);
                        double t;
                        if (double.TryParse(name, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out t))
                        {
                            rebuilt.TimeSteps.Add(t);
                            rebuilt.TimeStepPaths[t] = binFile;
                        }
                    }
                    rebuilt.TimeSteps.Sort();
                    rebuilt.IsLoaded = rebuilt.TimeSteps.Count > 0;
                    if (rebuilt.IsLoaded)
                    {
                        _cfdResult = rebuilt;
                        StartCfdPlayback();
                        return true;
                    }
                }
            }

            if (entry.SolverType == "Gaussian Puff")
            {
                if (_cfdResult != null && _cfdResult.IsLoaded && _cfdResult.TimeSteps.Count > 0)
                {
                    StartCfdPlayback();
                    return true;
                }

                if (!string.IsNullOrEmpty(entry.CasePath) && System.IO.Directory.Exists(entry.CasePath))
                {
                    var result = new OpenFoamResult
                    {
                        GridNx = entry.GridNx, GridNy = entry.GridNy, GridNz = entry.GridNz,
                        DomainSizeM = entry.DomainSizeM,
                        DomainXMin = -entry.DomainSizeM, DomainXMax = entry.DomainSizeM,
                        DomainYMin = -entry.DomainSizeM, DomainYMax = entry.DomainSizeM,
                        DomainZMax = entry.DomainSizeM,
                        CaseDir = entry.CasePath
                    };

                    var binFiles = System.IO.Directory.GetFiles(entry.CasePath, "*.bin");
                    foreach (var binFile in binFiles)
                    {
                        string name = System.IO.Path.GetFileNameWithoutExtension(binFile);
                        double t;
                        if (double.TryParse(name, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out t))
                        {
                            result.TimeSteps.Add(t);
                            result.TimeStepPaths[t] = binFile;
                        }
                    }
                    result.TimeSteps.Sort();
                    result.IsLoaded = result.TimeSteps.Count > 0;

                    if (result.IsLoaded)
                    {
                        _cfdResult = result;
                        StartCfdPlayback();
                        return true;
                    }
                }
                return false;
            }

            if (string.IsNullOrEmpty(entry.CasePath)) return false;
            if (!System.IO.Directory.Exists(entry.CasePath)) return false;

            try
            {
                var result = OpenFoamResultReader.ReadResults(
                    entry.CasePath, entry.GridNx, entry.GridNy, entry.GridNz, entry.DomainSizeM);

                if (!result.IsLoaded || result.TimeSteps.Count == 0) return false;

                _cfdResult = result;
                StartCfdPlayback();
                return true;
            }
            catch (OutOfMemoryException)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Not enough memory to load all timesteps.\n" +
                    "Try reducing Grid Resolution or using Purge Write to limit stored timesteps.",
                    "Out of Memory", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return false;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Error reading results: " + ex.Message,
                    "Load Error", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// Sets the animation speed factor (1.0 = real-time)
        /// </summary>
        public double AnimationSpeedFactor
        {
            get => _animationSpeedFactor;
            set => _animationSpeedFactor = Math.Max(0.1, Math.Min(20.0, value));
        }

        public ReleaseSource3D PendingSourceTemplate { get; set; }

        public MonitorPoint3D PendingMonitorTemplate
        {
            get => _pendingMonitorTemplate;
            set => _pendingMonitorTemplate = value;
        }

        public bool ShowVectorField
        {
            get => _showVectorField;
            set => _showVectorField = value;
        }

        public bool ShowGroundPlane
        {
            get => _showGroundPlane;
            set { _showGroundPlane = value; UpdateGroundPlane(); }
        }

        public double GroundSize
        {
            get => _groundSize;
            set { _groundSize = value; UpdateGroundPlane(); }
        }

        public double GroundLevel
        {
            get => _scene.CurrentWorkPlane?.Elevation ?? 0;
            set
            {
                if (_scene.CurrentWorkPlane != null)
                {
                    _scene.CurrentWorkPlane.Elevation = value;
                    UpdateGroundPlane();
                    UpdateGridElevation();
                }
            }
        }

        public FireSource PendingFireTemplate
        {
            get => _pendingFireTemplate;
            set => _pendingFireTemplate = value;
        }

        public GasDetector3D PendingDetectorTemplate
        {
            get => _pendingDetectorTemplate;
            set => _pendingDetectorTemplate = value;
        }

        public CameraPreset SaveCurrentCameraPreset(string name)
        {
            var cam = _viewport.Camera as System.Windows.Media.Media3D.PerspectiveCamera;
            if (cam == null) return null;
            var preset = new CameraPreset
            {
                Name = name,
                Position = cam.Position,
                LookDirection = cam.LookDirection,
                UpDirection = cam.UpDirection
            };
            _scene.CameraPresets.Add(preset);
            return preset;
        }

        public void ApplyCameraPreset(CameraPreset preset)
        {
            _viewport.Camera.Position = preset.Position;
            _viewport.Camera.LookDirection = preset.LookDirection;
            _viewport.Camera.UpDirection = preset.UpDirection;
        }

        public void ExportViewportImage(string filePath, int width, int height)
        {
            var bitmap = HelixToolkit.Wpf.Viewport3DHelper.RenderBitmap(
                _viewport.Viewport, width, height,
                System.Windows.Media.Brushes.White);
            using (var stream = System.IO.File.Create(filePath))
            {
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }
        }

        /// <summary>
        /// Adds a release source at the specified position
        /// </summary>
        public ReleaseSource3D AddReleaseSource(Point3D position, ReleaseSource3D template = null)
        {
            if (_scene.DispersionScenario == null)
            {
                _scene.DispersionScenario = new DispersionScenario();
            }

            var source = new ReleaseSource3D
            {
                Position = _snapToGrid ? position.SnapToGrid(_gridSpacing) : position,
                Gas = template?.Gas ?? GasProperties.CreateMethane(),
                Name = template?.Name ?? "Source",
                ReleaseRateKgPerS = template?.ReleaseRateKgPerS ?? 0.5,
                PuffIntervalS = template?.PuffIntervalS ?? 1.0,
                ReleaseHeightOffset = template?.ReleaseHeightOffset ?? 2.0
            };
            _scene.DispersionScenario.Sources.Add(source);
            UpdateViewport();
            return source;
        }

        public MonitorPoint3D AddMonitorPoint(Point3D position, MonitorPoint3D template = null)
        {
            var monitor = new MonitorPoint3D
            {
                Position = _snapToGrid ? position.SnapToGrid(_gridSpacing) : position,
                Name = template?.Name ?? "Monitor" + (_scene.MonitorPoints.Count + 1)
            };
            _scene.MonitorPoints.Add(monitor);
            UpdateViewport();
            return monitor;
        }

        public void RemoveMonitorPoint(MonitorPoint3D monitor)
        {
            _scene.MonitorPoints.Remove(monitor);
            UpdateViewport();
        }

        public void ExportMonitorDataToCsv(string filePath)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();

            var monitors = _scene.MonitorPoints;
            if (monitors.Count == 0) return;

            sb.Append("Time(s)");
            foreach (var m in monitors)
                sb.Append("," + m.Name + " (kg/m3)");
            sb.AppendLine();

            int maxLen = 0;
            foreach (var m in monitors)
                if (m.TimeSeries.Count > maxLen) maxLen = m.TimeSeries.Count;

            for (int i = 0; i < maxLen; i++)
            {
                double t = i < monitors[0].TimeSeries.Count ? monitors[0].TimeSeries[i].TimeS : 0;
                sb.Append(t.ToString(inv));
                foreach (var m in monitors)
                {
                    double c = i < m.TimeSeries.Count ? m.TimeSeries[i].Concentration : 0;
                    sb.Append("," + c.ToString("G6", inv));
                }
                sb.AppendLine();
            }

            System.IO.File.WriteAllText(filePath, sb.ToString());
        }

        private async void AnimationTimer_Tick(object sender, EventArgs e)
        {
            if (_dispersionState != DispersionSimulationState.Running)
                return;

            if (_cfdPlaybackActive)
            {
                await AnimationTimer_CfdTickAsync();
                return;
            }

            if (_dispersionEngine == null || _computingFrame)
                return;

            _computingFrame = true;

            try
            {
                var scenario = _scene.DispersionScenario;
                double newTime = _dispersionEngine.CurrentTimeS + scenario.TimeStepS * _animationSpeedFactor;

                bool isLastStep = newTime >= scenario.SimulationDurationS;
                if (isLastStep)
                    newTime = scenario.SimulationDurationS;

                if (scenario.TransientWind != null && scenario.TransientWind.Enabled && scenario.TransientWind.Entries.Count > 0)
                {
                    var windEntry = scenario.TransientWind.GetEntryAtTime(newTime);
                    if (windEntry != null)
                        _dispersionEngine.UpdateWind(windEntry.WindVector, windEntry.StabilityClass);
                }

                if (scenario.TransientWind != null && scenario.TransientWind.ESDTimeS >= 0 && newTime >= scenario.TransientWind.ESDTimeS)
                {
                    foreach (var src in scenario.Sources)
                        src.ReleaseRateKgPerS = 0;
                }
                else
                {
                    foreach (var src in scenario.Sources)
                    {
                        if (src.HighPressureLeak != null && _hpLeakProfiles.ContainsKey(src.Id))
                        {
                            var profile = _hpLeakProfiles[src.Id];
                            int step = (int)(newTime / scenario.TimeStepS);
                            if (step >= 0 && step < profile.Length)
                                src.ReleaseRateKgPerS = profile[step];
                            else
                                src.ReleaseRateKgPerS = 0;
                        }
                    }
                }

                var engine = _dispersionEngine;
                var renderer = _dispersionRenderer;
                var thresholds = scenario.Thresholds;
                int frameCount = ++_frameCount;
                bool doIso = frameCount % 2 == 0 && thresholds.Count > 0;
                bool doExtras = frameCount % 4 == 0;
                bool doContours = doExtras && scenario.ContourPlanes.Count > 0;
                bool doVectors = doExtras && _showVectorField;
                var contourConfigs = doContours ? scenario.ContourPlanes.Where(cp => cp.Visible).ToList() : null;
                var windVec = scenario.Meteo.WindVector;
                var monitors = _scene.MonitorPoints.ToList();
                var detectors = _scene.GasDetectors;
                double finalNewTime = newTime;

                var result = await Task.Run(() =>
                {
                    engine.StepTo(finalNewTime);

                    var monitorData = new List<(MonitorPoint3D mon, double c, double minC, double maxC, double gasVol)>();
                    foreach (var monitor in monitors)
                    {
                        if (!monitor.Visible) continue;
                        double c = 0;
                        double minC = 0, maxC2 = 0, gasVol = 0;

                        switch (monitor.Type)
                        {
                            case Models.MonitorType.Point:
                                c = engine.EvaluateConcentration(
                                    monitor.Position.X, monitor.Position.Y, monitor.Position.Z);
                                break;

                            case Models.MonitorType.Line:
                                var linePts = monitor.GetLineSamplePoints();
                                double lineSum = 0, lineMin = double.MaxValue, lineMax = 0;
                                foreach (var pt in linePts)
                                {
                                    double v = engine.EvaluateConcentration(pt.X, pt.Y, pt.Z);
                                    lineSum += v;
                                    if (v < lineMin) lineMin = v;
                                    if (v > lineMax) lineMax = v;
                                }
                                c = linePts.Count > 0 ? lineSum / linePts.Count : 0;
                                minC = lineMin == double.MaxValue ? 0 : lineMin;
                                maxC2 = lineMax;
                                break;

                            case Models.MonitorType.Region:
                                var regPts = monitor.GetRegionSamplePoints();
                                double regSum = 0, regMin = double.MaxValue, regMax = 0;
                                int aboveThreshold = 0;
                                foreach (var pt in regPts)
                                {
                                    double v = engine.EvaluateConcentration(pt.X, pt.Y, pt.Z);
                                    regSum += v;
                                    if (v < regMin) regMin = v;
                                    if (v > regMax) regMax = v;
                                    if (v > 1e-6) aboveThreshold++;
                                }
                                c = regPts.Count > 0 ? regSum / regPts.Count : 0;
                                minC = regMin == double.MaxValue ? 0 : regMin;
                                maxC2 = regMax;
                                double cellVol = monitor.RegionSize.X * monitor.RegionSize.Y * monitor.RegionSize.Z
                                    / Math.Max(1, regPts.Count);
                                gasVol = aboveThreshold * cellVol;
                                break;
                        }
                        monitorData.Add((monitor, c, minC, maxC2, gasVol));
                    }

                    Model3DGroup isoGroup = doIso ? renderer.ComputeIsosurfaces(engine, thresholds) : null;
                    Model3DGroup particleGroup = renderer.ComputeParticleCloud(engine);

                    var contourGroups = new List<Model3DGroup>();
                    Model3DGroup vectorGroup = null;
                    if (doContours || doVectors)
                    {
                        double maxConc = renderer.GetMaxConcentration();
                        double dom = renderer.DomainSize;
                        if (doContours)
                        {
                            foreach (var cp in contourConfigs)
                                contourGroups.Add(renderer.ComputeContourPlane(engine, cp, -dom, dom, maxConc));
                        }
                        if (doVectors)
                            vectorGroup = renderer.ComputeVectorField(engine, windVec, maxConc);
                    }

                    return new
                    {
                        monitorData,
                        isoGroup,
                        particleGroup,
                        contourGroups,
                        vectorGroup,
                        currentTime = engine.CurrentTimeS
                    };
                });

                foreach (var (mon, c, minC, maxC2, gasVol) in result.monitorData)
                {
                    if (mon.Type == Models.MonitorType.Line || mon.Type == Models.MonitorType.Region)
                    {
                        mon.LastMinConcentration = minC;
                        mon.LastMaxConcentration = maxC2;
                    }
                    if (mon.Type == Models.MonitorType.Region)
                        mon.LastGasVolume = gasVol;
                    mon.TimeSeries.Add(new Models.MonitorSample { TimeS = result.currentTime, Concentration = c });
                }
                MonitorDataUpdated?.Invoke(this, EventArgs.Empty);

                if (detectors.Count > 0)
                    DetectorEvaluator.EvaluateStep(detectors, engine, result.currentTime);

                RemoveDispersionVisuals();

                if (result.isoGroup != null)
                {
                    _isosurfaceVisual = new ModelVisual3D { Content = result.isoGroup };
                    _isosurfaceVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("DispersionIsosurface", "iso"));
                    _viewport.Children.Add(_isosurfaceVisual);
                }
                else if (_isosurfaceVisual != null)
                {
                    _viewport.Children.Add(_isosurfaceVisual);
                }

                _particleVisual = new ModelVisual3D { Content = result.particleGroup };
                _particleVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("DispersionParticles", "particles"));
                _viewport.Children.Add(_particleVisual);

                foreach (var cg in result.contourGroups)
                {
                    var cpVisual = new ModelVisual3D { Content = cg };
                    cpVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("ContourPlane", "contour"));
                    _viewport.Children.Add(cpVisual);
                }

                if (result.vectorGroup != null)
                {
                    var vfVisual = new ModelVisual3D { Content = result.vectorGroup };
                    vfVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("VectorField", "vectors"));
                    _viewport.Children.Add(vfVisual);
                }

                // Streamlines every 4th frame (uses LinesVisual3D, must stay on UI thread)
                if (frameCount % 4 == 0 && scenario.StreamlineSeedPoints.Count > 0)
                {
                    double maxC = _dispersionRenderer.GetMaxConcentration();
                    var slVisual = _dispersionRenderer.GenerateStreamlines(
                        engine, windVec, scenario.StreamlineSeedPoints, maxC);
                    slVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("Streamline", "streamlines"));
                    _viewport.Children.Add(slVisual);
                }

                // Fire visuals every 4th frame
                if (frameCount % 4 == 0 && _scene.FireScenario != null && _scene.FireScenario.Sources.Count > 0)
                {
                    foreach (var fire in _scene.FireScenario.Sources)
                    {
                        var flameVisual = FireRenderer.GenerateFlameVisual(fire, windVec);
                        flameVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                            new Visual3DTag("FireVisual", fire.Id));
                        _viewport.Children.Add(flameVisual);

                        var radVisual = FireRenderer.GenerateRadiationContours(
                            fire, _scene.FireScenario.RadiationContourLevels);
                        radVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                            new Visual3DTag("FireVisual", "rad_" + fire.Id));
                        _viewport.Children.Add(radVisual);
                    }
                }

                if (isLastStep)
                {
                    _dispersionState = DispersionSimulationState.Stopped;
                    if (_animationTimer != null)
                        _animationTimer.Stop();
                }
            }
            finally
            {
                _computingFrame = false;
            }
        }

        private async Task AnimationTimer_CfdTickAsync()
        {
            if (_cfdResult == null || !_cfdResult.IsLoaded || _computingFrame) return;

            _computingFrame = true;
            try
            {
                var scenario = _scene.DispersionScenario;
                int frameCount = ++_frameCount;

                double totalDuration = _cfdResult.TimeSteps[_cfdResult.TimeSteps.Count - 1]
                                     - _cfdResult.TimeSteps[0];
                double dtReal = 0.033 * _animationSpeedFactor * totalDuration / 10.0;
                _cfdPlaybackTimeS += dtReal;

                double lastTime = _cfdResult.TimeSteps[_cfdResult.TimeSteps.Count - 1];
                if (_cfdPlaybackTimeS >= lastTime)
                {
                    _cfdPlaybackIndex = _cfdResult.TimeSteps.Count - 1;
                    _cfdPlaybackTimeS = lastTime;
                    _dispersionState = DispersionSimulationState.Stopped;
                    if (_animationTimer != null)
                        _animationTimer.Stop();
                    return;
                }

                while (_cfdPlaybackIndex < _cfdResult.TimeSteps.Count - 1 &&
                       _cfdResult.TimeSteps[_cfdPlaybackIndex + 1] <= _cfdPlaybackTimeS)
                    _cfdPlaybackIndex++;

                double t = _cfdResult.TimeSteps[_cfdPlaybackIndex];
                var cfdResult = _cfdResult;
                var renderer = _dispersionRenderer;
                var thresholds = scenario?.Thresholds;
                bool doIso = frameCount % 2 == 0;
                bool doExtras = frameCount % 4 == 0;
                bool doContours = doExtras && scenario != null && scenario.ContourPlanes.Count > 0;
                bool doVectors = doExtras && _showVectorField && scenario != null;
                var contourConfigs = doContours ? scenario.ContourPlanes.Where(cp => cp.Visible).ToList() : null;
                var windVec = scenario?.Meteo.WindVector ?? new Vector3D();
                var monitors = _scene.MonitorPoints.ToList();

                var result = await Task.Run(() =>
                {
                    var field = cfdResult.GetField(t);
                    if (field == null) return (object)null;

                    var concField = new OpenFoamConcentrationField(
                        field, cfdResult.DomainXMin, cfdResult.DomainXMax,
                        cfdResult.DomainYMin, cfdResult.DomainYMax, cfdResult.DomainZMax);

                    var monitorData = new List<(MonitorPoint3D mon, double c)>();
                    foreach (var mon in monitors)
                    {
                        double c = concField.EvaluateConcentration(
                            mon.Position.X, mon.Position.Y, mon.Position.Z);
                        monitorData.Add((mon, c));
                    }

                    renderer.SetScalarFieldDirect(field);

                    Model3DGroup isoGroup = doIso ? renderer.ComputeCloudVisual(thresholds) : null;

                    double maxC = renderer.GetMaxConcentration();

                    var contourGroups = new List<Model3DGroup>();
                    Model3DGroup vectorGroup = null;
                    if (doContours || doVectors)
                    {
                        double dom = renderer.DomainSize;
                        if (doContours)
                        {
                            foreach (var cp in contourConfigs)
                                contourGroups.Add(renderer.ComputeContourPlane(concField, cp, -dom, dom, maxC));
                        }
                        if (doVectors)
                            vectorGroup = renderer.ComputeVectorField(concField, windVec, maxC);
                    }

                    return new { concField, monitorData, isoGroup, contourGroups, vectorGroup };
                });

                if (result == null) return;
                dynamic r = result;

                _cfdConcentrationField = r.concField;
                foreach (var (mon, c) in (List<(MonitorPoint3D, double)>)r.monitorData)
                {
                    // c is in kg/m³ from the engine. Convert to the monitor's chosen
                    // unit before storing in the time series.
                    double measured = ApplyMonitorTransform(mon, c);
                    mon.TimeSeries.Add(new MonitorSample { TimeS = t, Concentration = measured });
                }
                MonitorDataUpdated?.Invoke(this, EventArgs.Empty);

                RemoveDispersionVisuals();

                if (r.isoGroup != null)
                {
                    _isosurfaceVisual = new ModelVisual3D { Content = r.isoGroup };
                    _isosurfaceVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("DispersionIsosurface", "iso"));
                    _viewport.Children.Add(_isosurfaceVisual);
                }
                else if (_isosurfaceVisual != null)
                {
                    _viewport.Children.Add(_isosurfaceVisual);
                }

                foreach (var cg in (List<Model3DGroup>)r.contourGroups)
                {
                    var cpVisual = new ModelVisual3D { Content = cg };
                    cpVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("ContourPlane", "contour"));
                    _viewport.Children.Add(cpVisual);
                }

                if (r.vectorGroup != null)
                {
                    var vfVisual = new ModelVisual3D { Content = r.vectorGroup };
                    vfVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("VectorField", "vectors"));
                    _viewport.Children.Add(vfVisual);
                }
            }
            finally
            {
                _computingFrame = false;
            }
        }

        private void RemoveDispersionVisuals()
        {
            if (_isosurfaceVisual != null)
            {
                _viewport.Children.Remove(_isosurfaceVisual);
            }
            if (_particleVisual != null)
            {
                _viewport.Children.Remove(_particleVisual);
                _particleVisual = null;
            }
            if (_windArrowVisual != null)
            {
                _viewport.Children.Remove(_windArrowVisual);
                _windArrowVisual = null;
            }

            var dynamicVisuals = _viewport.Children
                .OfType<System.Windows.Media.Media3D.Visual3D>()
                .Where(m => m.GetValue(System.Windows.FrameworkElement.TagProperty) is Visual3DTag tag &&
                           (tag.Category == "ContourPlane" || tag.Category == "VectorField" || tag.Category == "Streamline" || tag.Category == "FireVisual" || tag.Category == "GasDetectorVis" || tag.Category == "TrajectoryLine"))
                .ToList();
            foreach (var dv in dynamicVisuals)
                _viewport.Children.Remove(dv);
        }

        private void ShowLegend(List<DispersionThreshold> thresholds)
        {
            _legendPanel.Children.Clear();

            var border = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(210, 255, 255, 255)),
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new System.Windows.Thickness(1),
                CornerRadius = new System.Windows.CornerRadius(4),
                Padding = new System.Windows.Thickness(8, 6, 8, 6)
            };

            var stack = new System.Windows.Controls.StackPanel();

            var title = new System.Windows.Controls.TextBlock
            {
                Text = "Concentration (kg/m³)",
                FontWeight = System.Windows.FontWeights.Bold,
                FontSize = 11,
                Margin = new System.Windows.Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(title);

            foreach (var t in thresholds)
            {
                if (!t.Visible) continue;
                var row = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Margin = new System.Windows.Thickness(0, 2, 0, 2)
                };

                var swatch = new System.Windows.Shapes.Rectangle
                {
                    Width = 14, Height = 14,
                    Fill = new System.Windows.Media.SolidColorBrush(t.Color),
                    Opacity = Math.Max(t.Opacity, 0.6),
                    Stroke = System.Windows.Media.Brushes.DarkGray,
                    StrokeThickness = 0.5,
                    Margin = new System.Windows.Thickness(0, 0, 6, 0),
                    RadiusX = 2, RadiusY = 2
                };

                string valueStr;
                if (t.ConcentrationValue >= 0.01)
                    valueStr = t.ConcentrationValue.ToString("F3");
                else if (t.ConcentrationValue >= 1e-6)
                    valueStr = t.ConcentrationValue.ToString("E2");
                else
                    valueStr = t.ConcentrationValue.ToString("E1");

                var label = new System.Windows.Controls.TextBlock
                {
                    Text = t.Name + ": " + valueStr,
                    FontSize = 11,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };

                row.Children.Add(swatch);
                row.Children.Add(label);
                stack.Children.Add(row);
            }

            border.Child = stack;
            _legendPanel.Children.Add(border);
            _legendPanel.Visibility = System.Windows.Visibility.Visible;
        }

        private void HideLegend()
        {
            if (_legendPanel != null)
            {
                _legendPanel.Children.Clear();
                _legendPanel.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void FlowAnimTimer_Tick(object sender, EventArgs e)
        {
            // Flow animation removed (unit operations/streams no longer supported)
        }

        private bool GetPointAlongPath(List<Point3D> path, double distance, out Point3D result)
        {
            result = path[0];
            double walked = 0;

            for (int i = 0; i < path.Count - 1; i++)
            {
                double segLen = (path[i + 1] - path[i]).Length;
                if (segLen < 0.001) continue;

                if (walked + segLen >= distance)
                {
                    double t = (distance - walked) / segLen;
                    result = new Point3D(
                        path[i].X + (path[i + 1].X - path[i].X) * t,
                        path[i].Y + (path[i + 1].Y - path[i].Y) * t,
                        path[i].Z + (path[i + 1].Z - path[i].Z) * t);
                    return true;
                }
                walked += segLen;
            }

            result = path[path.Count - 1];
            return true;
        }

        private Model3DGroup RecenterModel(Model3DGroup model, double cx, double cy, double cz)
        {
            var group = new Model3DGroup();
            var offsetTransform = new TranslateTransform3D(-cx, -cy, -cz);

            foreach (var child in model.Children)
            {
                var clone = child.Clone();
                if (clone.Transform != null && clone.Transform != Transform3D.Identity)
                {
                    var tg = new Transform3DGroup();
                    tg.Children.Add(offsetTransform);
                    tg.Children.Add(clone.Transform);
                    clone.Transform = tg;
                }
                else
                {
                    clone.Transform = offsetTransform;
                }
                group.Children.Add(clone);
            }

            return group;
        }

        private static MeshGeometry3D CreateFlowMarkerMesh(double radius, int detail)
        {
            var mesh = new MeshGeometry3D();
            int slices = detail * 2;
            int stacks = detail;

            mesh.Positions.Add(new Point3D(0, 0, radius));
            for (int s = 1; s < stacks; s++)
            {
                double phi = Math.PI * s / stacks;
                double sinP = Math.Sin(phi);
                double cosP = Math.Cos(phi);
                for (int sl = 0; sl < slices; sl++)
                {
                    double theta = 2 * Math.PI * sl / slices;
                    mesh.Positions.Add(new Point3D(
                        radius * sinP * Math.Cos(theta),
                        radius * sinP * Math.Sin(theta),
                        radius * cosP));
                }
            }
            mesh.Positions.Add(new Point3D(0, 0, -radius));
            int bottom = mesh.Positions.Count - 1;

            for (int sl = 0; sl < slices; sl++)
            {
                int next = (sl + 1) % slices;
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(1 + sl);
                mesh.TriangleIndices.Add(1 + next);
            }
            for (int s = 0; s < stacks - 2; s++)
            {
                int row = 1 + s * slices;
                int nextRow = 1 + (s + 1) * slices;
                for (int sl = 0; sl < slices; sl++)
                {
                    int next = (sl + 1) % slices;
                    mesh.TriangleIndices.Add(row + sl);
                    mesh.TriangleIndices.Add(nextRow + sl);
                    mesh.TriangleIndices.Add(nextRow + next);
                    mesh.TriangleIndices.Add(row + sl);
                    mesh.TriangleIndices.Add(nextRow + next);
                    mesh.TriangleIndices.Add(row + next);
                }
            }
            int lastRow = 1 + (stacks - 2) * slices;
            for (int sl = 0; sl < slices; sl++)
            {
                int next = (sl + 1) % slices;
                mesh.TriangleIndices.Add(bottom);
                mesh.TriangleIndices.Add(lastRow + next);
                mesh.TriangleIndices.Add(lastRow + sl);
            }

            mesh.Freeze();
            return mesh;
        }

        public void ClearScene()
        {
            StopDispersion();
            _scene.Clear();
            UpdateViewport();
        }


        /// <summary>
        /// Imports a 3D model from file as a standalone decoration at the given position.
        /// If position is null, places at origin or camera target.
        /// </summary>
        public Decoration3D ImportDecoration(string filePath, Point3D? position = null)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            var model = _modelLoader.LoadModelFromFile(filePath);
            if (model == null)
                return null;

            var pos = position ?? new Point3D(0, 0, 0);
            if (_snapToGrid)
            {
                pos = pos.SnapToGrid(_gridSpacing);
            }

            var bounds = model.Bounds;
            double maxExtent = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
            double autoScale = 1.0;
            if (maxExtent > 0.001)
            {
                double targetSize = 5.0;
                autoScale = targetSize / maxExtent;
            }

            var centerX = bounds.X + bounds.SizeX * 0.5;
            var centerY = bounds.Y + bounds.SizeY * 0.5;
            var centerZ = bounds.Z;

            System.Diagnostics.Debug.WriteLine(string.Format(
                "Import decoration: maxExtent={0:F2}, autoScale={1:F4}, center=({2:F2},{3:F2},{4:F2})",
                maxExtent, autoScale, centerX, centerY, centerZ));

            var recentered = RecenterModel(model, centerX, centerY, centerZ);

            var decoration = new Decoration3D
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                Position = pos,
                Scale = autoScale,
                OriginalModel3D = recentered,
                Model3D = recentered
            };
            decoration.UpdateBoundingBox();

            _scene.Decorations.Add(decoration);
            UpdateViewport();
            SelectedDecoration = decoration;
            return decoration;
        }

        public Decoration3D ImportDecorationWithTransform(string filePath, Model3DGroup model,
            Point3D position, Vector3D rotation, double scale)
        {
            if (model == null) return null;

            var bounds = model.Bounds;
            var cx = bounds.X + bounds.SizeX * 0.5;
            var cy = bounds.Y + bounds.SizeY * 0.5;
            var cz = bounds.Z;

            var recentered = RecenterModel(model, cx, cy, cz);

            var decoration = new Decoration3D
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                Position = position,
                Rotation = rotation,
                Scale = scale,
                OriginalModel3D = recentered,
                Model3D = recentered
            };
            decoration.UpdateBoundingBox();

            _scene.Decorations.Add(decoration);
            UpdateViewport();
            SelectedDecoration = decoration;
            return decoration;
        }

        public Model3DGroup LoadModelFile(string filePath)
        {
            return _modelLoader.LoadModelFromFile(filePath);
        }

        /// <summary>
        /// Deletes the selected decoration
        /// </summary>
        public void DeleteSelectedDecoration()
        {
            if (_selectedDecoration == null) return;

            _scene.Decorations.Remove(_selectedDecoration);
            SelectedDecoration = null;
            UpdateViewport();
        }

        public void DeleteSelectedSource()
        {
            if (_selectedSource == null) return;

            _scene.DispersionScenario?.Sources.Remove(_selectedSource);
            SelectedSource = null;
            UpdateViewport();
        }

        public void DeleteSelected()
        {
            if (_selectedSource != null)
                DeleteSelectedSource();
            else if (_selectedDecoration != null)
                DeleteSelectedDecoration();
        }

        /// <summary>
        /// Scales the selected decoration by a factor
        /// </summary>
        public void ScaleSelectedDecoration(double factor)
        {
            if (_selectedDecoration == null || factor <= 0) return;

            _selectedDecoration.Scale *= factor;
            _selectedDecoration.UpdateBoundingBox();
            UpdateViewport();
            OnSelectedUnitChanged();
        }


        /// <summary>
        /// Saves the scene to a file. If the path ends in .dsproj it's written as a self-contained
        /// ZIP bundle (project.xml + assets + embedded CFD cases). Otherwise it's a bare XML file
        /// with external references (legacy format).
        /// </summary>
        public void SaveToFile(string filePath)
        {
            var doc = BuildSceneXDocument(filePath);

            if (ProjectBundle.IsBundleFile(filePath))
            {
                ProjectBundle.Save(filePath, _scene, doc);
            }
            else
            {
                doc.Save(filePath);
            }
        }

        private System.Xml.Linq.XDocument BuildSceneXDocument(string filePath)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            return new System.Xml.Linq.XDocument(
                new System.Xml.Linq.XElement("Scene3D",
                    new System.Xml.Linq.XAttribute("Version", "1"),
                    new System.Xml.Linq.XAttribute("Name", _scene.Name ?? ""),
                    new System.Xml.Linq.XAttribute("Description", _scene.Description ?? ""),

                    new System.Xml.Linq.XElement("GridSettings",
                        new System.Xml.Linq.XAttribute("Spacing", _scene.GridSpacing.ToString(inv)),
                        new System.Xml.Linq.XAttribute("SnapToGrid", _scene.SnapToGrid)),

                    new System.Xml.Linq.XElement("WorkPlanes",
                        _scene.WorkPlanes.Select(wp =>
                            new System.Xml.Linq.XElement("WorkPlane",
                                new System.Xml.Linq.XAttribute("Name", wp.Name ?? ""),
                                new System.Xml.Linq.XAttribute("Elevation", wp.Elevation.ToString(inv)),
                                new System.Xml.Linq.XAttribute("Visible", wp.Visible),
                                new System.Xml.Linq.XAttribute("GridColor", wp.GridColor.ToString()),
                                new System.Xml.Linq.XAttribute("GridSpacing", wp.GridSpacing.ToString(inv))))),

                    new System.Xml.Linq.XElement("CurrentWorkPlane",
                        new System.Xml.Linq.XAttribute("Name", _scene.CurrentWorkPlane != null
                            ? _scene.CurrentWorkPlane.Name ?? "" : "")),

                    new System.Xml.Linq.XElement("Decorations",
                        _scene.Decorations.Select(d =>
                            new System.Xml.Linq.XElement("Decoration",
                                new System.Xml.Linq.XAttribute("Id", d.Id),
                                new System.Xml.Linq.XAttribute("Name", d.Name ?? ""),
                                new System.Xml.Linq.XAttribute("FilePath", d.FilePath ?? ""),
                                new System.Xml.Linq.XAttribute("PosX", d.Position.X.ToString(inv)),
                                new System.Xml.Linq.XAttribute("PosY", d.Position.Y.ToString(inv)),
                                new System.Xml.Linq.XAttribute("PosZ", d.Position.Z.ToString(inv)),
                                new System.Xml.Linq.XAttribute("RotX", d.Rotation.X.ToString(inv)),
                                new System.Xml.Linq.XAttribute("RotY", d.Rotation.Y.ToString(inv)),
                                new System.Xml.Linq.XAttribute("RotZ", d.Rotation.Z.ToString(inv)),
                                new System.Xml.Linq.XAttribute("Scale", d.Scale.ToString(inv)),
                                new System.Xml.Linq.XAttribute("ClipEnabled", d.ClipEnabled.ToString()),
                                new System.Xml.Linq.XAttribute("ClipAxis", d.ClipAxis.ToString()),
                                new System.Xml.Linq.XAttribute("ClipValue", d.ClipValue.ToString(inv)),
                                new System.Xml.Linq.XAttribute("ClipAbove", d.ClipAbove.ToString()),
                                new System.Xml.Linq.XAttribute("UseCustomMaterial", d.UseCustomMaterial.ToString()),
                                new System.Xml.Linq.XAttribute("MaterialType", d.MaterialType.ToString()),
                                new System.Xml.Linq.XAttribute("MaterialColor", d.MaterialColor.ToString()),
                                new System.Xml.Linq.XAttribute("SpecularPower", d.SpecularPower.ToString(inv)),
                                new System.Xml.Linq.XAttribute("Opacity", d.Opacity.ToString(inv))))),

                    SerializeGeneralSettings(inv),
                    SerializeEnvironment(inv),
                    SerializeGasLibrary(inv),
                    SerializeTopLevelSources(inv),
                    SerializeWindFieldScenarios(inv),
                    SerializeSimulations(inv),
                    SerializeViews(inv),
                    SerializeDispersionStudies(inv),
                    SerializeDetectorAllocations(inv),
                    SerializeDispersionScenarios(inv),
                    SerializeMonitorPoints(inv),
                    SerializeWindRose(inv),
                    SerializeFireScenario(inv),
                    SerializeGasDetectors(inv),
                    SerializeCfdSimulations(inv, filePath)
                ));
        }

        private System.Xml.Linq.XElement SerializeEnvironment(System.Globalization.CultureInfo inv)
        {
            var e = _scene.Environment;
            if (e == null) return null;
            return new System.Xml.Linq.XElement("Environment",
                new System.Xml.Linq.XAttribute("UseSunLighting", e.UseSunLighting.ToString()),
                new System.Xml.Linq.XAttribute("SunAzimuthDeg", e.SunAzimuthDeg.ToString(inv)),
                new System.Xml.Linq.XAttribute("SunElevationDeg", e.SunElevationDeg.ToString(inv)),
                new System.Xml.Linq.XAttribute("SunIntensity", e.SunIntensity.ToString(inv)),
                new System.Xml.Linq.XAttribute("AmbientIntensity", e.AmbientIntensity.ToString(inv)),
                new System.Xml.Linq.XAttribute("SkydomeEnabled", e.SkydomeEnabled.ToString()),
                new System.Xml.Linq.XAttribute("SkyZenith", e.SkyZenithColor.ToString()),
                new System.Xml.Linq.XAttribute("SkyHorizon", e.SkyHorizonColor.ToString()),
                new System.Xml.Linq.XAttribute("Ground", e.Ground.ToString()),
                new System.Xml.Linq.XAttribute("ShowGridOverlay", e.ShowGridOverlay.ToString()));
        }

        private void DeserializeEnvironment(System.Xml.Linq.XElement root, System.Globalization.CultureInfo inv, Scene3D fs)
        {
            var el = root.Element("Environment");
            if (el == null) { fs.Environment = new EnvironmentSettings(); return; }
            var e = new EnvironmentSettings();
            bool b; double d;
            if (bool.TryParse((string)el.Attribute("UseSunLighting"), out b)) e.UseSunLighting = b;
            if (double.TryParse((string)el.Attribute("SunAzimuthDeg"), System.Globalization.NumberStyles.Float, inv, out d)) e.SunAzimuthDeg = d;
            if (double.TryParse((string)el.Attribute("SunElevationDeg"), System.Globalization.NumberStyles.Float, inv, out d)) e.SunElevationDeg = d;
            if (double.TryParse((string)el.Attribute("SunIntensity"), System.Globalization.NumberStyles.Float, inv, out d)) e.SunIntensity = d;
            if (double.TryParse((string)el.Attribute("AmbientIntensity"), System.Globalization.NumberStyles.Float, inv, out d)) e.AmbientIntensity = d;
            if (bool.TryParse((string)el.Attribute("SkydomeEnabled"), out b)) e.SkydomeEnabled = b;
            try { e.SkyZenithColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString((string)el.Attribute("SkyZenith")); } catch { }
            try { e.SkyHorizonColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString((string)el.Attribute("SkyHorizon")); } catch { }
            GroundMaterial gm;
            if (Enum.TryParse((string)el.Attribute("Ground") ?? "Grass", out gm)) e.Ground = gm;
            if (bool.TryParse((string)el.Attribute("ShowGridOverlay"), out b)) e.ShowGridOverlay = b;
            fs.Environment = e;
        }

        private System.Xml.Linq.XElement SerializeGeneralSettings(System.Globalization.CultureInfo inv)
        {
            var s = _scene.GeneralSettings;
            if (s == null) return null;
            return new System.Xml.Linq.XElement("GeneralSettings",
                new System.Xml.Linq.XAttribute("Name", s.Name ?? ""),
                new System.Xml.Linq.XAttribute("Description", s.Description ?? ""),
                new System.Xml.Linq.XAttribute("Author", s.Author ?? ""),
                new System.Xml.Linq.XAttribute("CreatedAt", s.CreatedAt.ToString("o", inv)),
                new System.Xml.Linq.XAttribute("DefaultDomainSize", s.DefaultDomainSizeM.ToString(inv)),
                new System.Xml.Linq.XAttribute("DefaultGridRes", s.DefaultGridResolution.ToString(inv)),
                s.DefaultMeteo != null ? new System.Xml.Linq.XElement("DefaultMeteo",
                    new System.Xml.Linq.XAttribute("WindSpeed", s.DefaultMeteo.WindSpeed.ToString(inv)),
                    new System.Xml.Linq.XAttribute("WindDir", s.DefaultMeteo.WindDirectionDeg.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Stability", s.DefaultMeteo.StabilityClass.ToString()),
                    new System.Xml.Linq.XAttribute("Temp", s.DefaultMeteo.AmbientTemperature.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Pressure", s.DefaultMeteo.AmbientPressure.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Roughness", s.DefaultMeteo.RoughnessLengthM.ToString(inv))) : null);
        }

        private System.Xml.Linq.XElement SerializeGasLibrary(System.Globalization.CultureInfo inv)
        {
            if (_scene.GasLibrary == null || _scene.GasLibrary.Count == 0) return null;
            return new System.Xml.Linq.XElement("GasLibrary",
                _scene.GasLibrary.Select(g =>
                {
                    if (g.Kind == GasLibraryItemKind.Mixture && g.Mixture != null)
                    {
                        return new System.Xml.Linq.XElement("Gas",
                            new System.Xml.Linq.XAttribute("Id", g.Id ?? ""),
                            new System.Xml.Linq.XAttribute("Name", g.Name ?? ""),
                            new System.Xml.Linq.XAttribute("Kind", "Mixture"),
                            new System.Xml.Linq.XAttribute("Cryogenic", g.IsCryogenic ? "1" : "0"),
                            new System.Xml.Linq.XElement("Mixture",
                                g.Mixture.Components.Select(c =>
                                    new System.Xml.Linq.XElement("Component",
                                        new System.Xml.Linq.XAttribute("Name", c.Name ?? ""),
                                        new System.Xml.Linq.XAttribute("MolarMass", c.MolarMass.ToString(inv)),
                                        new System.Xml.Linq.XAttribute("MoleFrac", c.MoleFraction.ToString(inv)),
                                        new System.Xml.Linq.XAttribute("LFL", c.LFL.ToString(inv)),
                                        new System.Xml.Linq.XAttribute("UFL", c.UFL.ToString(inv)),
                                        new System.Xml.Linq.XAttribute("IDLH", c.IDLH.ToString(inv))))));
                    }
                    var gp = g.PureGas ?? new GasProperties();
                    return new System.Xml.Linq.XElement("Gas",
                        new System.Xml.Linq.XAttribute("Id", g.Id ?? ""),
                        new System.Xml.Linq.XAttribute("Name", g.Name ?? ""),
                        new System.Xml.Linq.XAttribute("Kind", "Pure"),
                        new System.Xml.Linq.XAttribute("Cryogenic", g.IsCryogenic ? "1" : "0"),
                        new System.Xml.Linq.XAttribute("MolarMass", gp.MolarMass.ToString(inv)),
                        new System.Xml.Linq.XAttribute("LFL", gp.LFL.ToString(inv)),
                        new System.Xml.Linq.XAttribute("IDLH", gp.IDLH.ToString(inv)),
                        new System.Xml.Linq.XAttribute("ERPG1", gp.ERPG1.ToString(inv)),
                        new System.Xml.Linq.XAttribute("ERPG2", gp.ERPG2.ToString(inv)),
                        new System.Xml.Linq.XAttribute("ERPG3", gp.ERPG3.ToString(inv)));
                }));
        }

        private System.Xml.Linq.XElement SerializeTopLevelSources(System.Globalization.CultureInfo inv)
        {
            if (_scene.TopLevelSources == null || _scene.TopLevelSources.Count == 0) return null;
            return new System.Xml.Linq.XElement("TopLevelSources",
                _scene.TopLevelSources.Select(src => SerializeSourceCommon(src, inv)));
        }

        private System.Xml.Linq.XElement SerializeSourceCommon(ReleaseSource3D src, System.Globalization.CultureInfo inv)
        {
            return new System.Xml.Linq.XElement("Source",
                new System.Xml.Linq.XAttribute("Id", src.Id ?? ""),
                new System.Xml.Linq.XAttribute("Name", src.Name ?? ""),
                new System.Xml.Linq.XAttribute("AttachedUnitId", src.AttachedUnitId ?? ""),
                new System.Xml.Linq.XAttribute("GasRefId", src.GasRefId ?? ""),
                new System.Xml.Linq.XAttribute("PosX", src.Position.X.ToString(inv)),
                new System.Xml.Linq.XAttribute("PosY", src.Position.Y.ToString(inv)),
                new System.Xml.Linq.XAttribute("PosZ", src.Position.Z.ToString(inv)),
                new System.Xml.Linq.XAttribute("ReleaseRate", src.ReleaseRateKgPerS.ToString(inv)),
                new System.Xml.Linq.XAttribute("PuffInterval", src.PuffIntervalS.ToString(inv)),
                new System.Xml.Linq.XAttribute("HeightOffset", src.ReleaseHeightOffset.ToString(inv)),
                new System.Xml.Linq.XAttribute("Azimuth", src.ReleaseAzimuthDeg.ToString(inv)),
                new System.Xml.Linq.XAttribute("Elevation", src.ReleaseElevationDeg.ToString(inv)),
                src.HighPressureLeak != null ? new System.Xml.Linq.XElement("HPLeak",
                    new System.Xml.Linq.XAttribute("VesselP", src.HighPressureLeak.VesselPressurePa.ToString(inv)),
                    new System.Xml.Linq.XAttribute("VesselT", src.HighPressureLeak.VesselTemperatureK.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Orifice", src.HighPressureLeak.OrificeDiameterM.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Volume", src.HighPressureLeak.VesselVolumeM3.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Gamma", src.HighPressureLeak.GasGamma.ToString(inv)),
                    new System.Xml.Linq.XAttribute("MolarMass", src.HighPressureLeak.GasMolarMassKgMol.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Cd", src.HighPressureLeak.DischargeCoefficient.ToString(inv))) : null,
                src.Gas != null ? new System.Xml.Linq.XElement("Gas",
                    new System.Xml.Linq.XAttribute("Name", src.Gas.Name ?? ""),
                    new System.Xml.Linq.XAttribute("MolarMass", src.Gas.MolarMass.ToString(inv)),
                    new System.Xml.Linq.XAttribute("LFL", src.Gas.LFL.ToString(inv)),
                    new System.Xml.Linq.XAttribute("IDLH", src.Gas.IDLH.ToString(inv))) : null);
        }

        private System.Xml.Linq.XElement SerializeSimulations(System.Globalization.CultureInfo inv)
        {
            if (_scene.Simulations == null || _scene.Simulations.Count == 0) return null;
            return new System.Xml.Linq.XElement("Simulations",
                _scene.Simulations.Select(s => new System.Xml.Linq.XElement("Simulation",
                    new System.Xml.Linq.XAttribute("Id", s.Id ?? ""),
                    new System.Xml.Linq.XAttribute("Name", s.Name ?? ""),
                    new System.Xml.Linq.XAttribute("CreatedAt", s.CreatedAt.ToString("o", inv)),
                    s.CompletedAt.HasValue ? new System.Xml.Linq.XAttribute("CompletedAt", s.CompletedAt.Value.ToString("o", inv)) : null,
                    new System.Xml.Linq.XAttribute("SourceId", s.SourceId ?? ""),
                    new System.Xml.Linq.XAttribute("WindFieldId", s.WindFieldId ?? ""),
                    new System.Xml.Linq.XAttribute("SolverType", s.SolverType.ToString()),
                    new System.Xml.Linq.XAttribute("Status", s.Status.ToString()),
                    new System.Xml.Linq.XAttribute("StatusMessage", s.StatusMessage ?? ""),
                    new System.Xml.Linq.XAttribute("DomainSize", s.SnapshotDomainSizeM.ToString(inv)),
                    new System.Xml.Linq.XAttribute("GridRes", s.SnapshotGridResolution.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Duration", s.SnapshotDurationS.ToString(inv)),
                    new System.Xml.Linq.XAttribute("TimeStep", s.SnapshotTimeStepS.ToString(inv)),
                    new System.Xml.Linq.XAttribute("SnapshotCount", s.SnapshotCount.ToString(inv)),
                    new System.Xml.Linq.XAttribute("CasePath", s.CasePath ?? ""),
                    new System.Xml.Linq.XAttribute("EmbedMode", s.EmbedMode.ToString()),
                    new System.Xml.Linq.XAttribute("MaxC", s.MaxConcentration.ToString(inv)),
                    s.SnapshotSource != null ? new System.Xml.Linq.XElement("SnapshotSource", SerializeSourceCommon(s.SnapshotSource, inv).Attributes(), SerializeSourceCommon(s.SnapshotSource, inv).Elements()) : null,
                    s.SnapshotMeteo != null ? new System.Xml.Linq.XElement("SnapshotMeteo",
                        new System.Xml.Linq.XAttribute("WindSpeed", s.SnapshotMeteo.WindSpeed.ToString(inv)),
                        new System.Xml.Linq.XAttribute("WindDir", s.SnapshotMeteo.WindDirectionDeg.ToString(inv)),
                        new System.Xml.Linq.XAttribute("Stability", s.SnapshotMeteo.StabilityClass.ToString()),
                        new System.Xml.Linq.XAttribute("Temp", s.SnapshotMeteo.AmbientTemperature.ToString(inv)),
                        new System.Xml.Linq.XAttribute("Pressure", s.SnapshotMeteo.AmbientPressure.ToString(inv)),
                        new System.Xml.Linq.XAttribute("Roughness", s.SnapshotMeteo.RoughnessLengthM.ToString(inv))) : null,
                    SerializeAtmosphericCfd(s.SnapshotCfdConfig, inv))));
        }

        private System.Xml.Linq.XElement SerializeViews(System.Globalization.CultureInfo inv)
        {
            if (_scene.Views == null || _scene.Views.Count == 0) return null;
            return new System.Xml.Linq.XElement("Views",
                _scene.Views.Select(v => new System.Xml.Linq.XElement("View",
                    new System.Xml.Linq.XAttribute("Id", v.Id ?? ""),
                    new System.Xml.Linq.XAttribute("Name", v.Name ?? ""),
                    new System.Xml.Linq.XAttribute("Kind", v.Kind.ToString()),
                    new System.Xml.Linq.XAttribute("SimulationId", v.SimulationId ?? ""),
                    new System.Xml.Linq.XAttribute("FieldProperty", v.FieldProperty.ToString()),
                    new System.Xml.Linq.XAttribute("TimeMode", v.TimeMode.ToString()),
                    new System.Xml.Linq.XAttribute("SpecificTime", v.SpecificTimeS.ToString(inv)),
                    new System.Xml.Linq.XAttribute("IsVisible", v.IsVisible.ToString()),
                    new System.Xml.Linq.XAttribute("Opacity", v.Opacity.ToString(inv)),
                    new System.Xml.Linq.XAttribute("IsoValue", v.IsoValue.ToString(inv)),
                    new System.Xml.Linq.XAttribute("IsoColor", v.IsoColor.ToString()),
                    new System.Xml.Linq.XAttribute("PlanePosition", v.PlanePosition.ToString(inv)),
                    new System.Xml.Linq.XAttribute("ColorMap", v.ColorMap.ToString()),
                    new System.Xml.Linq.XAttribute("MinValue", v.MinValue.ToString(inv)),
                    new System.Xml.Linq.XAttribute("MaxValue", v.MaxValue.ToString(inv)),
                    new System.Xml.Linq.XAttribute("SampleResolution", v.SampleResolution.ToString(inv)))));
        }

        private void DeserializeViews(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D scene)
        {
            scene.Views.Clear();
            var el = root.Element("Views");
            if (el == null) return;
            foreach (var ve in el.Elements("View"))
            {
                var v = new DisperSim3D.Models.View
                {
                    Id = (string)ve.Attribute("Id") ?? Guid.NewGuid().ToString(),
                    Name = (string)ve.Attribute("Name") ?? "View",
                    SimulationId = (string)ve.Attribute("SimulationId") ?? ""
                };
                if (Enum.TryParse((string)ve.Attribute("Kind") ?? "Isosurface", out ViewKind k)) v.Kind = k;
                if (Enum.TryParse((string)ve.Attribute("FieldProperty") ?? "Concentration", out ViewFieldProperty fp)) v.FieldProperty = fp;
                if (Enum.TryParse((string)ve.Attribute("TimeMode") ?? "PeakOverTime", out ViewTimeMode tm)) v.TimeMode = tm;
                v.SpecificTimeS = double.Parse((string)ve.Attribute("SpecificTime") ?? "0", inv);
                v.IsVisible = bool.Parse((string)ve.Attribute("IsVisible") ?? "True");
                v.Opacity = double.Parse((string)ve.Attribute("Opacity") ?? "0.5", inv);
                v.IsoValue = double.Parse((string)ve.Attribute("IsoValue") ?? "0.05", inv);
                try { v.IsoColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString((string)ve.Attribute("IsoColor") ?? "#FF00FFFF"); }
                catch { v.IsoColor = System.Windows.Media.Colors.Cyan; }
                v.PlanePosition = double.Parse((string)ve.Attribute("PlanePosition") ?? "1", inv);
                if (Enum.TryParse((string)ve.Attribute("ColorMap") ?? "Jet", out ColorMapName cm)) v.ColorMap = cm;
                v.MinValue = double.Parse((string)ve.Attribute("MinValue") ?? "0", inv);
                v.MaxValue = double.Parse((string)ve.Attribute("MaxValue") ?? "0", inv);
                v.SampleResolution = int.Parse((string)ve.Attribute("SampleResolution") ?? "80", inv);
                scene.Views.Add(v);
            }
        }

        private System.Xml.Linq.XElement SerializeDispersionStudies(System.Globalization.CultureInfo inv)
        {
            if (_scene.DispersionStudies == null || _scene.DispersionStudies.Count == 0) return null;
            return new System.Xml.Linq.XElement("DispersionStudies",
                _scene.DispersionStudies.Select(st => new System.Xml.Linq.XElement("Study",
                    new System.Xml.Linq.XAttribute("Id", st.Id ?? ""),
                    new System.Xml.Linq.XAttribute("Name", st.Name ?? ""),
                    new System.Xml.Linq.XAttribute("Description", st.Description ?? ""),
                    new System.Xml.Linq.XAttribute("DetectionQuantity", st.DetectionQuantity.ToString()),
                    new System.Xml.Linq.XAttribute("DetectionThreshold", st.DetectionThreshold.ToString(inv)),
                    new System.Xml.Linq.XAttribute("CreatedAt", st.CreatedAt.ToString("o")),
                    new System.Xml.Linq.XAttribute("IsVisible", st.IsVisible.ToString()),
                    new System.Xml.Linq.XElement("Simulations",
                        (st.SimulationIds ?? new List<string>()).Select(sid =>
                            new System.Xml.Linq.XElement("Simulation", new System.Xml.Linq.XAttribute("Id", sid)))))));
        }

        private void DeserializeDispersionStudies(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D scene)
        {
            scene.DispersionStudies.Clear();
            var el = root.Element("DispersionStudies");
            if (el == null) return;
            foreach (var se in el.Elements("Study"))
            {
                var st = new DispersionStudy
                {
                    Id = (string)se.Attribute("Id") ?? Guid.NewGuid().ToString(),
                    Name = (string)se.Attribute("Name") ?? "Study",
                    Description = (string)se.Attribute("Description") ?? "",
                    DetectionThreshold = double.Parse((string)se.Attribute("DetectionThreshold") ?? "50", inv),
                    IsVisible = bool.Parse((string)se.Attribute("IsVisible") ?? "True")
                };
                if (Enum.TryParse((string)se.Attribute("DetectionQuantity") ?? "PercentLFL", out ViewFieldProperty dq))
                    st.DetectionQuantity = dq;
                if (DateTime.TryParse((string)se.Attribute("CreatedAt") ?? "", null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTime ca))
                    st.CreatedAt = ca;
                var sims = se.Element("Simulations");
                if (sims != null)
                    foreach (var sx in sims.Elements("Simulation"))
                        st.SimulationIds.Add((string)sx.Attribute("Id") ?? "");
                scene.DispersionStudies.Add(st);
            }
        }

        private System.Xml.Linq.XElement SerializeDetectorAllocations(System.Globalization.CultureInfo inv)
        {
            if (_scene.DetectorAllocations == null || _scene.DetectorAllocations.Count == 0) return null;
            return new System.Xml.Linq.XElement("DetectorAllocations",
                _scene.DetectorAllocations.Select(a => new System.Xml.Linq.XElement("Allocation",
                    new System.Xml.Linq.XAttribute("Id", a.Id ?? ""),
                    new System.Xml.Linq.XAttribute("Name", a.Name ?? ""),
                    new System.Xml.Linq.XAttribute("DispersionStudyId", a.DispersionStudyId ?? ""),
                    new System.Xml.Linq.XAttribute("Objective", a.Objective.ToString()),
                    new System.Xml.Linq.XAttribute("TargetCoveragePercent", a.TargetCoveragePercent.ToString(inv)),
                    new System.Xml.Linq.XAttribute("MaxDetectors", a.MaxDetectors.ToString(inv)),
                    new System.Xml.Linq.XAttribute("DetectionRadiusM", a.DetectionRadiusM.ToString(inv)),
                    new System.Xml.Linq.XAttribute("MinZ", a.MinZ.ToString(inv)),
                    new System.Xml.Linq.XAttribute("MaxZ", a.MaxZ.ToString(inv)),
                    new System.Xml.Linq.XAttribute("CandidateNx", a.CandidateNx.ToString(inv)),
                    new System.Xml.Linq.XAttribute("CandidateNy", a.CandidateNy.ToString(inv)),
                    new System.Xml.Linq.XAttribute("CandidateNz", a.CandidateNz.ToString(inv)),
                    new System.Xml.Linq.XAttribute("UseExistingDetectors", a.UseExistingDetectors.ToString()),
                    new System.Xml.Linq.XAttribute("Strategy", a.Strategy.ToString()),
                    new System.Xml.Linq.XAttribute("AchievedCoveragePercent", a.AchievedCoveragePercent.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Status", a.Status.ToString()),
                    new System.Xml.Linq.XAttribute("StatusMessage", a.StatusMessage ?? ""),
                    new System.Xml.Linq.XAttribute("RunAt", a.RunAt.ToString("o")),
                    new System.Xml.Linq.XAttribute("IsVisible", a.IsVisible.ToString()),
                    new System.Xml.Linq.XElement("Positions",
                        (a.AllocatedPositions ?? new List<System.Windows.Media.Media3D.Point3D>()).Select(p =>
                            new System.Xml.Linq.XElement("P",
                                new System.Xml.Linq.XAttribute("X", p.X.ToString(inv)),
                                new System.Xml.Linq.XAttribute("Y", p.Y.ToString(inv)),
                                new System.Xml.Linq.XAttribute("Z", p.Z.ToString(inv))))),
                    new System.Xml.Linq.XElement("Coverage",
                        (a.PerCloudCovered ?? new Dictionary<string, bool>()).Select(kv =>
                            new System.Xml.Linq.XElement("C",
                                new System.Xml.Linq.XAttribute("SimId", kv.Key),
                                new System.Xml.Linq.XAttribute("Covered", kv.Value.ToString())))))));
        }

        private void DeserializeDetectorAllocations(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D scene)
        {
            scene.DetectorAllocations.Clear();
            var el = root.Element("DetectorAllocations");
            if (el == null) return;
            foreach (var ae in el.Elements("Allocation"))
            {
                var a = new DetectorAllocation
                {
                    Id = (string)ae.Attribute("Id") ?? Guid.NewGuid().ToString(),
                    Name = (string)ae.Attribute("Name") ?? "Allocation",
                    DispersionStudyId = (string)ae.Attribute("DispersionStudyId") ?? "",
                    TargetCoveragePercent = double.Parse((string)ae.Attribute("TargetCoveragePercent") ?? "100", inv),
                    MaxDetectors = int.Parse((string)ae.Attribute("MaxDetectors") ?? "0", inv),
                    DetectionRadiusM = double.Parse((string)ae.Attribute("DetectionRadiusM") ?? "5", inv),
                    MinZ = double.Parse((string)ae.Attribute("MinZ") ?? "1.5", inv),
                    MaxZ = double.Parse((string)ae.Attribute("MaxZ") ?? "3.0", inv),
                    CandidateNx = int.Parse((string)ae.Attribute("CandidateNx") ?? "60", inv),
                    CandidateNy = int.Parse((string)ae.Attribute("CandidateNy") ?? "60", inv),
                    CandidateNz = int.Parse((string)ae.Attribute("CandidateNz") ?? "3", inv),
                    UseExistingDetectors = bool.Parse((string)ae.Attribute("UseExistingDetectors") ?? "False"),
                    AchievedCoveragePercent = double.Parse((string)ae.Attribute("AchievedCoveragePercent") ?? "0", inv),
                    StatusMessage = (string)ae.Attribute("StatusMessage") ?? "",
                    IsVisible = bool.Parse((string)ae.Attribute("IsVisible") ?? "True")
                };
                if (Enum.TryParse((string)ae.Attribute("Objective") ?? "CoverAll", out AllocationObjective ob)) a.Objective = ob;
                if (Enum.TryParse((string)ae.Attribute("Strategy") ?? "GreedyMaxCoverage", out AllocationStrategy str)) a.Strategy = str;
                if (Enum.TryParse((string)ae.Attribute("Status") ?? "Configured", out AllocationStatus stt)) a.Status = stt;
                if (DateTime.TryParse((string)ae.Attribute("RunAt") ?? "", null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTime ra))
                    a.RunAt = ra;
                var pos = ae.Element("Positions");
                if (pos != null)
                    foreach (var pe in pos.Elements("P"))
                        a.AllocatedPositions.Add(new System.Windows.Media.Media3D.Point3D(
                            double.Parse((string)pe.Attribute("X") ?? "0", inv),
                            double.Parse((string)pe.Attribute("Y") ?? "0", inv),
                            double.Parse((string)pe.Attribute("Z") ?? "0", inv)));
                var cov = ae.Element("Coverage");
                if (cov != null)
                    foreach (var ce in cov.Elements("C"))
                    {
                        string sid = (string)ce.Attribute("SimId") ?? "";
                        if (!string.IsNullOrEmpty(sid))
                            a.PerCloudCovered[sid] = bool.Parse((string)ce.Attribute("Covered") ?? "False");
                    }
                scene.DetectorAllocations.Add(a);
            }
        }

        private System.Xml.Linq.XElement SerializeWindFieldScenarios(System.Globalization.CultureInfo inv)
        {
            if (_scene.WindFieldScenarios == null || _scene.WindFieldScenarios.Count == 0) return null;
            return new System.Xml.Linq.XElement("WindFieldScenarios",
                _scene.WindFieldScenarios.Select(wf =>
                    new System.Xml.Linq.XElement("WindFieldScenario",
                        new System.Xml.Linq.XAttribute("Id", wf.Id ?? ""),
                        new System.Xml.Linq.XAttribute("Name", wf.Name ?? ""),
                        new System.Xml.Linq.XAttribute("DomainSize", wf.DomainSizeM.ToString(inv)),
                        new System.Xml.Linq.XAttribute("DomainHeight", wf.DomainHeightM.ToString(inv)),
                        new System.Xml.Linq.XAttribute("GridRes", wf.GridResolution.ToString(inv)),
                        new System.Xml.Linq.XAttribute("Status", wf.Status.ToString()),
                        new System.Xml.Linq.XAttribute("CasePath", wf.CasePath ?? ""),
                        new System.Xml.Linq.XAttribute("EmbedMode", wf.EmbedMode.ToString()),
                        new System.Xml.Linq.XAttribute("UseFluidX3D", wf.UseFluidX3D.ToString()),
                        new System.Xml.Linq.XAttribute("FluidX3DQuality", wf.FluidX3DQuality.ToString()),
                        new System.Xml.Linq.XElement("Meteo",
                            new System.Xml.Linq.XAttribute("WindSpeed", wf.Meteo.WindSpeed.ToString(inv)),
                            new System.Xml.Linq.XAttribute("WindDir", wf.Meteo.WindDirectionDeg.ToString(inv)),
                            new System.Xml.Linq.XAttribute("Stability", wf.Meteo.StabilityClass.ToString()),
                            new System.Xml.Linq.XAttribute("Temp", wf.Meteo.AmbientTemperature.ToString(inv)),
                            new System.Xml.Linq.XAttribute("Pressure", wf.Meteo.AmbientPressure.ToString(inv)),
                            new System.Xml.Linq.XAttribute("Roughness", wf.Meteo.RoughnessLengthM.ToString(inv))),
                        SerializeAtmosphericCfd(wf.CfdConfig, inv))));
        }

        /// <summary>
        /// Emits the atmospheric CFD block (UseAtmosphericBL, Sct, Prt, Ceps3, sigmaEps,
        /// GroundThermalBC + values). Returns null when cfd is null so the caller can pass
        /// it directly to XElement constructor without conditionals.
        /// </summary>
        private System.Xml.Linq.XElement SerializeAtmosphericCfd(
            CfdConfiguration cfd, System.Globalization.CultureInfo inv)
        {
            if (cfd == null) return null;
            var el = new System.Xml.Linq.XElement("Cfd",
                new System.Xml.Linq.XAttribute("AtmBL", cfd.UseAtmosphericBL ? "1" : "0"),
                new System.Xml.Linq.XAttribute("Sct", cfd.TurbulentSchmidtNumber.ToString(inv)),
                new System.Xml.Linq.XAttribute("Prt", cfd.TurbulentPrandtlNumber.ToString(inv)),
                new System.Xml.Linq.XAttribute("SigmaEps", cfd.KEpsilonSigmaEpsilon.ToString(inv)),
                new System.Xml.Linq.XAttribute("GroundBC", cfd.GroundThermalBC.ToString()),
                new System.Xml.Linq.XAttribute("GroundT", cfd.GroundTemperatureK.ToString(inv)),
                new System.Xml.Linq.XAttribute("GroundQ", cfd.GroundHeatFluxWPerM2.ToString(inv)));
            if (cfd.BuoyancyEpsCoefficient.HasValue)
                el.Add(new System.Xml.Linq.XAttribute("Ceps3", cfd.BuoyancyEpsCoefficient.Value.ToString(inv)));
            return el;
        }

        /// <summary>Reads the &lt;Cfd&gt; child of <paramref name="parent"/> into <paramref name="cfd"/>.</summary>
        private void DeserializeAtmosphericCfd(System.Xml.Linq.XElement parent,
            CfdConfiguration cfd, System.Globalization.CultureInfo inv)
        {
            if (parent == null || cfd == null) return;
            var el = parent.Element("Cfd");
            if (el == null) return;
            cfd.UseAtmosphericBL = ((string)el.Attribute("AtmBL") ?? "0") == "1";
            cfd.TurbulentSchmidtNumber = double.Parse((string)el.Attribute("Sct") ?? "0.7", inv);
            cfd.TurbulentPrandtlNumber = double.Parse((string)el.Attribute("Prt") ?? "0.85", inv);
            cfd.KEpsilonSigmaEpsilon = double.Parse((string)el.Attribute("SigmaEps") ?? "1.3", inv);
            GroundThermalBoundary gbc;
            if (Enum.TryParse((string)el.Attribute("GroundBC") ?? "Adiabatic", out gbc))
                cfd.GroundThermalBC = gbc;
            cfd.GroundTemperatureK = double.Parse((string)el.Attribute("GroundT") ?? "293.15", inv);
            cfd.GroundHeatFluxWPerM2 = double.Parse((string)el.Attribute("GroundQ") ?? "0", inv);
            var ceps3Attr = (string)el.Attribute("Ceps3");
            cfd.BuoyancyEpsCoefficient = string.IsNullOrEmpty(ceps3Attr) ? (double?)null
                : double.Parse(ceps3Attr, inv);
        }

        private System.Xml.Linq.XElement SerializeDispersionScenarios(System.Globalization.CultureInfo inv)
        {
            if (_scene.DispersionScenarios.Count == 0) return null;

            return new System.Xml.Linq.XElement("DispersionScenarios",
                new System.Xml.Linq.XAttribute("ActiveIndex", _scene.ActiveScenarioIndex.ToString(inv)),
                _scene.DispersionScenarios.Select(sc => SerializeSingleScenario(sc, inv)));
        }

        private System.Xml.Linq.XElement SerializeSingleScenario(DispersionScenario sc, System.Globalization.CultureInfo inv)
        {
            return new System.Xml.Linq.XElement("DispersionScenario",
                new System.Xml.Linq.XAttribute("Name", sc.Name ?? ""),
                new System.Xml.Linq.XAttribute("Duration", sc.SimulationDurationS.ToString(inv)),
                new System.Xml.Linq.XAttribute("TimeStep", sc.TimeStepS.ToString(inv)),
                new System.Xml.Linq.XAttribute("DomainSize", sc.DomainSizeM.ToString(inv)),
                new System.Xml.Linq.XAttribute("GridRes", sc.GridResolution.ToString(inv)),
                new System.Xml.Linq.XAttribute("SolverType", sc.SolverType.ToString()),
                new System.Xml.Linq.XAttribute("WindFieldId", sc.WindFieldScenarioId ?? ""),

                new System.Xml.Linq.XElement("Meteo",
                    new System.Xml.Linq.XAttribute("WindSpeed", sc.Meteo.WindSpeed.ToString(inv)),
                    new System.Xml.Linq.XAttribute("WindDir", sc.Meteo.WindDirectionDeg.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Stability", sc.Meteo.StabilityClass.ToString()),
                    new System.Xml.Linq.XAttribute("Temp", sc.Meteo.AmbientTemperature.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Pressure", sc.Meteo.AmbientPressure.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Roughness", sc.Meteo.RoughnessLengthM.ToString(inv))),

                new System.Xml.Linq.XElement("Sources",
                    sc.Sources.Select(src =>
                        new System.Xml.Linq.XElement("Source",
                            new System.Xml.Linq.XAttribute("Id", src.Id),
                            new System.Xml.Linq.XAttribute("Name", src.Name ?? ""),
                            new System.Xml.Linq.XAttribute("AttachedUnitId", src.AttachedUnitId ?? ""),
                            new System.Xml.Linq.XAttribute("PosX", src.Position.X.ToString(inv)),
                            new System.Xml.Linq.XAttribute("PosY", src.Position.Y.ToString(inv)),
                            new System.Xml.Linq.XAttribute("PosZ", src.Position.Z.ToString(inv)),
                            new System.Xml.Linq.XAttribute("ReleaseRate", src.ReleaseRateKgPerS.ToString(inv)),
                            new System.Xml.Linq.XAttribute("PuffInterval", src.PuffIntervalS.ToString(inv)),
                            new System.Xml.Linq.XAttribute("HeightOffset", src.ReleaseHeightOffset.ToString(inv)),
                            new System.Xml.Linq.XAttribute("Azimuth", src.ReleaseAzimuthDeg.ToString(inv)),
                            new System.Xml.Linq.XAttribute("Elevation", src.ReleaseElevationDeg.ToString(inv)),
                            src.HighPressureLeak != null ? new System.Xml.Linq.XElement("HPLeak",
                                new System.Xml.Linq.XAttribute("VesselP", src.HighPressureLeak.VesselPressurePa.ToString(inv)),
                                new System.Xml.Linq.XAttribute("VesselT", src.HighPressureLeak.VesselTemperatureK.ToString(inv)),
                                new System.Xml.Linq.XAttribute("Orifice", src.HighPressureLeak.OrificeDiameterM.ToString(inv)),
                                new System.Xml.Linq.XAttribute("Volume", src.HighPressureLeak.VesselVolumeM3.ToString(inv)),
                                new System.Xml.Linq.XAttribute("Gamma", src.HighPressureLeak.GasGamma.ToString(inv)),
                                new System.Xml.Linq.XAttribute("MolarMass", src.HighPressureLeak.GasMolarMassKgMol.ToString(inv)),
                                new System.Xml.Linq.XAttribute("Cd", src.HighPressureLeak.DischargeCoefficient.ToString(inv)),
                                new System.Xml.Linq.XAttribute("SpecifyMdot", src.HighPressureLeak.SpecifyMassFlow ? "1" : "0"),
                                new System.Xml.Linq.XAttribute("Mdot", src.HighPressureLeak.SpecifiedMassFlowKgPerS.ToString(inv))) : null,
                            new System.Xml.Linq.XElement("Gas",
                                new System.Xml.Linq.XAttribute("Name", src.Gas.Name ?? ""),
                                new System.Xml.Linq.XAttribute("MolarMass", src.Gas.MolarMass.ToString(inv)),
                                new System.Xml.Linq.XAttribute("LFL", src.Gas.LFL.ToString(inv)),
                                new System.Xml.Linq.XAttribute("IDLH", src.Gas.IDLH.ToString(inv)),
                                new System.Xml.Linq.XAttribute("ERPG1", src.Gas.ERPG1.ToString(inv)),
                                new System.Xml.Linq.XAttribute("ERPG2", src.Gas.ERPG2.ToString(inv)),
                                new System.Xml.Linq.XAttribute("ERPG3", src.Gas.ERPG3.ToString(inv)))))),

                new System.Xml.Linq.XElement("Thresholds",
                    sc.Thresholds.Select(t =>
                        new System.Xml.Linq.XElement("Threshold",
                            new System.Xml.Linq.XAttribute("Name", t.Name ?? ""),
                            new System.Xml.Linq.XAttribute("Type", t.Type.ToString()),
                            new System.Xml.Linq.XAttribute("Value", t.ConcentrationValue.ToString(inv)),
                            new System.Xml.Linq.XAttribute("Color", t.Color.ToString()),
                            new System.Xml.Linq.XAttribute("Opacity", t.Opacity.ToString(inv)),
                            new System.Xml.Linq.XAttribute("Visible", t.Visible)))),

                new System.Xml.Linq.XElement("ContourPlanes",
                    sc.ContourPlanes.Select(cp =>
                        new System.Xml.Linq.XElement("ContourPlane",
                            new System.Xml.Linq.XAttribute("Axis", cp.Axis.ToString()),
                            new System.Xml.Linq.XAttribute("Position", cp.Position.ToString(inv)),
                            new System.Xml.Linq.XAttribute("Visible", cp.Visible),
                            new System.Xml.Linq.XAttribute("Opacity", cp.Opacity.ToString(inv)),
                            new System.Xml.Linq.XAttribute("ColorMap", cp.ColorMap.ToString())))),

                sc.TransientWind != null && sc.TransientWind.Entries.Count > 0
                    ? new System.Xml.Linq.XElement("TransientWind",
                        new System.Xml.Linq.XAttribute("Enabled", sc.TransientWind.Enabled),
                        new System.Xml.Linq.XAttribute("ESD", sc.TransientWind.ESDTimeS.ToString(inv)),
                        sc.TransientWind.Entries.Select(we =>
                            new System.Xml.Linq.XElement("Entry",
                                new System.Xml.Linq.XAttribute("Time", we.TimeS.ToString(inv)),
                                new System.Xml.Linq.XAttribute("Speed", we.WindSpeed.ToString(inv)),
                                new System.Xml.Linq.XAttribute("Dir", we.WindDirectionDeg.ToString(inv)),
                                new System.Xml.Linq.XAttribute("Stability", we.StabilityClass.ToString()))))
                    : null,

                sc.GasMixture != null && sc.GasMixture.Components.Count > 0
                    ? new System.Xml.Linq.XElement("GasMixture",
                        sc.GasMixture.Components.Select(gc =>
                            new System.Xml.Linq.XElement("Component",
                                new System.Xml.Linq.XAttribute("Name", gc.Name ?? ""),
                                new System.Xml.Linq.XAttribute("MolarMass", gc.MolarMass.ToString(inv)),
                                new System.Xml.Linq.XAttribute("MoleFrac", gc.MoleFraction.ToString(inv)),
                                new System.Xml.Linq.XAttribute("LFL", gc.LFL.ToString(inv)),
                                new System.Xml.Linq.XAttribute("IDLH", gc.IDLH.ToString(inv)))))
                    : null);
        }

        private void DeserializeGeneralSettings(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D fs)
        {
            var el = root.Element("GeneralSettings");
            if (el == null) return;
            var s = new ProjectSettings();
            s.Name = (string)el.Attribute("Name") ?? "";
            s.Description = (string)el.Attribute("Description") ?? "";
            s.Author = (string)el.Attribute("Author") ?? "";
            DateTime ca;
            if (DateTime.TryParse((string)el.Attribute("CreatedAt") ?? "", null, System.Globalization.DateTimeStyles.RoundtripKind, out ca))
                s.CreatedAt = ca;
            s.DefaultDomainSizeM = double.Parse((string)el.Attribute("DefaultDomainSize") ?? "200", inv);
            s.DefaultGridResolution = int.Parse((string)el.Attribute("DefaultGridRes") ?? "40", inv);
            var mEl = el.Element("DefaultMeteo");
            if (mEl != null)
            {
                s.DefaultMeteo = new MeteorologicalConditions
                {
                    WindSpeed = double.Parse((string)mEl.Attribute("WindSpeed") ?? "5", inv),
                    WindDirectionDeg = double.Parse((string)mEl.Attribute("WindDir") ?? "270", inv),
                    StabilityClass = (PasquillStabilityClass)Enum.Parse(typeof(PasquillStabilityClass),
                        (string)mEl.Attribute("Stability") ?? "D"),
                    AmbientTemperature = double.Parse((string)mEl.Attribute("Temp") ?? "293.15", inv),
                    AmbientPressure = double.Parse((string)mEl.Attribute("Pressure") ?? "101325", inv),
                    RoughnessLengthM = double.Parse((string)mEl.Attribute("Roughness") ?? "0.03", inv)
                };
            }
            fs.GeneralSettings = s;
        }

        private void DeserializeGasLibrary(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D fs)
        {
            var el = root.Element("GasLibrary");
            if (el == null) return;
            foreach (var ge in el.Elements("Gas"))
            {
                var item = new GasLibraryItem
                {
                    Id = (string)ge.Attribute("Id") ?? Guid.NewGuid().ToString(),
                    Name = (string)ge.Attribute("Name") ?? "Gas",
                    IsCryogenic = ((string)ge.Attribute("Cryogenic") ?? "0") == "1"
                };
                string kind = (string)ge.Attribute("Kind") ?? "Pure";
                if (kind == "Mixture")
                {
                    item.Kind = GasLibraryItemKind.Mixture;
                    item.Mixture = new GasMixture();
                    var mxEl = ge.Element("Mixture");
                    if (mxEl != null)
                    {
                        foreach (var ce in mxEl.Elements("Component"))
                        {
                            item.Mixture.Components.Add(new GasComponent
                            {
                                Name = (string)ce.Attribute("Name") ?? "",
                                MolarMass = double.Parse((string)ce.Attribute("MolarMass") ?? "0.016", inv),
                                MoleFraction = double.Parse((string)ce.Attribute("MoleFrac") ?? "1", inv),
                                LFL = double.Parse((string)ce.Attribute("LFL") ?? "0", inv),
                                UFL = double.Parse((string)ce.Attribute("UFL") ?? "0", inv),
                                IDLH = double.Parse((string)ce.Attribute("IDLH") ?? "0", inv)
                            });
                        }
                    }
                }
                else
                {
                    item.Kind = GasLibraryItemKind.Pure;
                    item.PureGas = new GasProperties
                    {
                        Name = item.Name,
                        MolarMass = double.Parse((string)ge.Attribute("MolarMass") ?? "0.016", inv),
                        LFL = double.Parse((string)ge.Attribute("LFL") ?? "0", inv),
                        IDLH = double.Parse((string)ge.Attribute("IDLH") ?? "0", inv),
                        ERPG1 = double.Parse((string)ge.Attribute("ERPG1") ?? "0", inv),
                        ERPG2 = double.Parse((string)ge.Attribute("ERPG2") ?? "0", inv),
                        ERPG3 = double.Parse((string)ge.Attribute("ERPG3") ?? "0", inv)
                    };
                }
                fs.GasLibrary.Add(item);
            }
        }

        private ReleaseSource3D DeserializeSourceCommon(System.Xml.Linq.XElement se, System.Globalization.CultureInfo inv)
        {
            var src = new ReleaseSource3D();
            src.Id = (string)se.Attribute("Id") ?? Guid.NewGuid().ToString();
            src.Name = (string)se.Attribute("Name") ?? "";
            src.AttachedUnitId = (string)se.Attribute("AttachedUnitId");
            if (string.IsNullOrEmpty(src.AttachedUnitId)) src.AttachedUnitId = null;
            src.GasRefId = (string)se.Attribute("GasRefId");
            if (string.IsNullOrEmpty(src.GasRefId)) src.GasRefId = null;
            src.Position = new System.Windows.Media.Media3D.Point3D(
                double.Parse((string)se.Attribute("PosX") ?? "0", inv),
                double.Parse((string)se.Attribute("PosY") ?? "0", inv),
                double.Parse((string)se.Attribute("PosZ") ?? "0", inv));
            src.ReleaseRateKgPerS = double.Parse((string)se.Attribute("ReleaseRate") ?? "0.5", inv);
            src.PuffIntervalS = double.Parse((string)se.Attribute("PuffInterval") ?? "1", inv);
            src.ReleaseHeightOffset = double.Parse((string)se.Attribute("HeightOffset") ?? "2", inv);
            src.ReleaseAzimuthDeg = double.Parse((string)se.Attribute("Azimuth") ?? "0", inv);
            src.ReleaseElevationDeg = double.Parse((string)se.Attribute("Elevation") ?? "0", inv);

            var hpEl = se.Element("HPLeak");
            if (hpEl != null)
            {
                src.HighPressureLeak = new HighPressureLeakParams
                {
                    VesselPressurePa = double.Parse((string)hpEl.Attribute("VesselP") ?? "1000000", inv),
                    VesselTemperatureK = double.Parse((string)hpEl.Attribute("VesselT") ?? "293.15", inv),
                    OrificeDiameterM = double.Parse((string)hpEl.Attribute("Orifice") ?? "0.01", inv),
                    VesselVolumeM3 = double.Parse((string)hpEl.Attribute("Volume") ?? "10", inv),
                    GasGamma = double.Parse((string)hpEl.Attribute("Gamma") ?? "1.4", inv),
                    GasMolarMassKgMol = double.Parse((string)hpEl.Attribute("MolarMass") ?? "0.016", inv),
                    DischargeCoefficient = double.Parse((string)hpEl.Attribute("Cd") ?? "0.65", inv)
                };
            }
            var gasEl = se.Element("Gas");
            if (gasEl != null)
            {
                src.Gas = new GasProperties
                {
                    Name = (string)gasEl.Attribute("Name") ?? "",
                    MolarMass = double.Parse((string)gasEl.Attribute("MolarMass") ?? "0.016", inv),
                    LFL = double.Parse((string)gasEl.Attribute("LFL") ?? "0", inv),
                    IDLH = double.Parse((string)gasEl.Attribute("IDLH") ?? "0", inv)
                };
            }
            return src;
        }

        private void DeserializeTopLevelSources(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D fs)
        {
            var el = root.Element("TopLevelSources");
            if (el == null) return;
            foreach (var se in el.Elements("Source"))
                fs.TopLevelSources.Add(DeserializeSourceCommon(se, inv));
        }

        private void DeserializeSimulations(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D fs)
        {
            var el = root.Element("Simulations");
            if (el == null) return;
            foreach (var se in el.Elements("Simulation"))
            {
                var sim = new Simulation
                {
                    Id = (string)se.Attribute("Id") ?? Guid.NewGuid().ToString(),
                    Name = (string)se.Attribute("Name") ?? "Simulation",
                    SourceId = (string)se.Attribute("SourceId") ?? "",
                    WindFieldId = (string)se.Attribute("WindFieldId") ?? "",
                    StatusMessage = (string)se.Attribute("StatusMessage") ?? "",
                    SnapshotDomainSizeM = double.Parse((string)se.Attribute("DomainSize") ?? "200", inv),
                    SnapshotGridResolution = int.Parse((string)se.Attribute("GridRes") ?? "40", inv),
                    SnapshotDurationS = double.Parse((string)se.Attribute("Duration") ?? "300", inv),
                    SnapshotTimeStepS = double.Parse((string)se.Attribute("TimeStep") ?? "0.5", inv),
                    SnapshotCount = int.Parse((string)se.Attribute("SnapshotCount") ?? "20", inv),
                    CasePath = (string)se.Attribute("CasePath") ?? "",
                    MaxConcentration = double.Parse((string)se.Attribute("MaxC") ?? "0", inv)
                };
                DateTime ca;
                if (DateTime.TryParse((string)se.Attribute("CreatedAt") ?? "", null, System.Globalization.DateTimeStyles.RoundtripKind, out ca))
                    sim.CreatedAt = ca;
                DateTime cmp;
                if (DateTime.TryParse((string)se.Attribute("CompletedAt") ?? "", null, System.Globalization.DateTimeStyles.RoundtripKind, out cmp))
                    sim.CompletedAt = cmp;
                CfdSolverType solverType;
                if (Enum.TryParse((string)se.Attribute("SolverType") ?? "GaussianPuff", out solverType))
                    sim.SolverType = solverType;
                SimulationStatus statusVal;
                if (Enum.TryParse((string)se.Attribute("Status") ?? "Configured", out statusVal))
                    sim.Status = statusVal;
                BundleEmbedMode embedMode;
                if (Enum.TryParse((string)se.Attribute("EmbedMode") ?? "ResultsOnly", out embedMode))
                    sim.EmbedMode = embedMode;

                var snapSrcEl = se.Element("SnapshotSource");
                if (snapSrcEl != null)
                    sim.SnapshotSource = DeserializeSourceCommon(snapSrcEl, inv);
                var snapMeteoEl = se.Element("SnapshotMeteo");
                if (snapMeteoEl != null)
                {
                    sim.SnapshotMeteo = new MeteorologicalConditions
                    {
                        WindSpeed = double.Parse((string)snapMeteoEl.Attribute("WindSpeed") ?? "5", inv),
                        WindDirectionDeg = double.Parse((string)snapMeteoEl.Attribute("WindDir") ?? "270", inv),
                        StabilityClass = (PasquillStabilityClass)Enum.Parse(typeof(PasquillStabilityClass),
                            (string)snapMeteoEl.Attribute("Stability") ?? "D"),
                        AmbientTemperature = double.Parse((string)snapMeteoEl.Attribute("Temp") ?? "293.15", inv),
                        AmbientPressure = double.Parse((string)snapMeteoEl.Attribute("Pressure") ?? "101325", inv),
                        RoughnessLengthM = double.Parse((string)snapMeteoEl.Attribute("Roughness") ?? "0.03", inv)
                    };
                }
                if (sim.SnapshotCfdConfig == null) sim.SnapshotCfdConfig = new CfdConfiguration();
                DeserializeAtmosphericCfd(se, sim.SnapshotCfdConfig, inv);
                fs.Simulations.Add(sim);
            }
        }

        private void DeserializeWindFieldScenarios(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D fs)
        {
            var listEl = root.Element("WindFieldScenarios");
            if (listEl == null) return;
            foreach (var wfEl in listEl.Elements("WindFieldScenario"))
            {
                var wf = new WindFieldScenario
                {
                    Id = (string)wfEl.Attribute("Id") ?? Guid.NewGuid().ToString(),
                    Name = (string)wfEl.Attribute("Name") ?? "Wind Field",
                    DomainSizeM = double.Parse((string)wfEl.Attribute("DomainSize") ?? "200", inv),
                    DomainHeightM = double.Parse((string)wfEl.Attribute("DomainHeight") ?? "100", inv),
                    GridResolution = int.Parse((string)wfEl.Attribute("GridRes") ?? "40", inv),
                    CasePath = (string)wfEl.Attribute("CasePath") ?? null
                };
                var statusStr = (string)wfEl.Attribute("Status");
                if (!string.IsNullOrEmpty(statusStr))
                {
                    WindFieldStatus parsedStatus;
                    if (Enum.TryParse(statusStr, out parsedStatus))
                        wf.Status = parsedStatus;
                }
                BundleEmbedMode wfEmbed;
                if (Enum.TryParse((string)wfEl.Attribute("EmbedMode") ?? "ResultsOnly", out wfEmbed))
                    wf.EmbedMode = wfEmbed;
                bool useFx;
                if (bool.TryParse((string)wfEl.Attribute("UseFluidX3D") ?? "False", out useFx))
                    wf.UseFluidX3D = useFx;
                FluidX3DQuality fxQual;
                if (Enum.TryParse((string)wfEl.Attribute("FluidX3DQuality") ?? "Fast", out fxQual))
                    wf.FluidX3DQuality = fxQual;
                var mEl = wfEl.Element("Meteo");
                if (mEl != null)
                {
                    wf.Meteo = new MeteorologicalConditions
                    {
                        WindSpeed = double.Parse((string)mEl.Attribute("WindSpeed") ?? "5", inv),
                        WindDirectionDeg = double.Parse((string)mEl.Attribute("WindDir") ?? "270", inv),
                        StabilityClass = (PasquillStabilityClass)Enum.Parse(typeof(PasquillStabilityClass),
                            (string)mEl.Attribute("Stability") ?? "D"),
                        AmbientTemperature = double.Parse((string)mEl.Attribute("Temp") ?? "293.15", inv),
                        AmbientPressure = double.Parse((string)mEl.Attribute("Pressure") ?? "101325", inv),
                        RoughnessLengthM = double.Parse((string)mEl.Attribute("Roughness") ?? "0.03", inv)
                    };
                }
                if (wf.CfdConfig == null) wf.CfdConfig = new CfdConfiguration();
                DeserializeAtmosphericCfd(wfEl, wf.CfdConfig, inv);
                fs.WindFieldScenarios.Add(wf);
            }
        }

        private void DeserializeDispersionScenario(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D fs)
        {
            var multiEl = root.Element("DispersionScenarios");
            if (multiEl != null)
            {
                fs.ActiveScenarioIndex = int.Parse((string)multiEl.Attribute("ActiveIndex") ?? "0", inv);
                foreach (var dEl in multiEl.Elements("DispersionScenario"))
                {
                    var sc = DeserializeSingleScenario(dEl, inv);
                    fs.DispersionScenarios.Add(sc);
                }
                return;
            }

            var singleEl = root.Element("DispersionScenario");
            if (singleEl == null) return;
            fs.DispersionScenarios.Add(DeserializeSingleScenario(singleEl, inv));
            fs.ActiveScenarioIndex = 0;
        }

        private DispersionScenario DeserializeSingleScenario(System.Xml.Linq.XElement dEl,
            System.Globalization.CultureInfo inv)
        {
            var sc = new DispersionScenario();
            sc.Name = (string)dEl.Attribute("Name") ?? "";
            sc.SimulationDurationS = double.Parse((string)dEl.Attribute("Duration") ?? "300", inv);
            sc.TimeStepS = double.Parse((string)dEl.Attribute("TimeStep") ?? "0.5", inv);
            sc.DomainSizeM = double.Parse((string)dEl.Attribute("DomainSize") ?? "200", inv);
            sc.GridResolution = int.Parse((string)dEl.Attribute("GridRes") ?? "80", inv);

            var solverTypeStr = (string)dEl.Attribute("SolverType");
            if (!string.IsNullOrEmpty(solverTypeStr))
            {
                CfdSolverType parsed;
                if (Enum.TryParse(solverTypeStr, out parsed))
                    sc.SolverType = parsed;
            }

            var wfId = (string)dEl.Attribute("WindFieldId");
            sc.WindFieldScenarioId = string.IsNullOrEmpty(wfId) ? null : wfId;

            var meteoEl = dEl.Element("Meteo");
            if (meteoEl != null)
            {
                sc.Meteo = new MeteorologicalConditions
                {
                    WindSpeed = double.Parse((string)meteoEl.Attribute("WindSpeed") ?? "5", inv),
                    WindDirectionDeg = double.Parse((string)meteoEl.Attribute("WindDir") ?? "270", inv),
                    StabilityClass = (PasquillStabilityClass)Enum.Parse(typeof(PasquillStabilityClass),
                        (string)meteoEl.Attribute("Stability") ?? "D"),
                    AmbientTemperature = double.Parse((string)meteoEl.Attribute("Temp") ?? "293.15", inv),
                    AmbientPressure = double.Parse((string)meteoEl.Attribute("Pressure") ?? "101325", inv),
                    RoughnessLengthM = double.Parse((string)meteoEl.Attribute("Roughness") ?? "0.03", inv)
                };
            }

            var srcEl = dEl.Element("Sources");
            if (srcEl != null)
            {
                foreach (var se in srcEl.Elements("Source"))
                {
                    var source = new ReleaseSource3D();
                    source.Id = (string)se.Attribute("Id") ?? Guid.NewGuid().ToString();
                    source.Name = (string)se.Attribute("Name") ?? "";
                    source.AttachedUnitId = (string)se.Attribute("AttachedUnitId");
                    if (string.IsNullOrEmpty(source.AttachedUnitId)) source.AttachedUnitId = null;
                    source.Position = new Point3D(
                        double.Parse((string)se.Attribute("PosX") ?? "0", inv),
                        double.Parse((string)se.Attribute("PosY") ?? "0", inv),
                        double.Parse((string)se.Attribute("PosZ") ?? "0", inv));
                    source.ReleaseRateKgPerS = double.Parse((string)se.Attribute("ReleaseRate") ?? "0.5", inv);
                    source.PuffIntervalS = double.Parse((string)se.Attribute("PuffInterval") ?? "1", inv);
                    source.ReleaseHeightOffset = double.Parse((string)se.Attribute("HeightOffset") ?? "2", inv);
                    source.ReleaseAzimuthDeg = double.Parse((string)se.Attribute("Azimuth") ?? "0", inv);
                    source.ReleaseElevationDeg = double.Parse((string)se.Attribute("Elevation") ?? "0", inv);

                    var hpEl = se.Element("HPLeak");
                    if (hpEl != null)
                    {
                        source.HighPressureLeak = new HighPressureLeakParams
                        {
                            VesselPressurePa = double.Parse((string)hpEl.Attribute("VesselP") ?? "1000000", inv),
                            VesselTemperatureK = double.Parse((string)hpEl.Attribute("VesselT") ?? "293.15", inv),
                            OrificeDiameterM = double.Parse((string)hpEl.Attribute("Orifice") ?? "0.01", inv),
                            VesselVolumeM3 = double.Parse((string)hpEl.Attribute("Volume") ?? "10", inv),
                            GasGamma = double.Parse((string)hpEl.Attribute("Gamma") ?? "1.4", inv),
                            GasMolarMassKgMol = double.Parse((string)hpEl.Attribute("MolarMass") ?? "0.016", inv),
                            DischargeCoefficient = double.Parse((string)hpEl.Attribute("Cd") ?? "0.65", inv),
                            SpecifyMassFlow = ((string)hpEl.Attribute("SpecifyMdot") ?? "0") == "1",
                            SpecifiedMassFlowKgPerS = double.Parse((string)hpEl.Attribute("Mdot") ?? "1", inv)
                        };
                    }

                    var gasEl = se.Element("Gas");
                    if (gasEl != null)
                    {
                        source.Gas = new GasProperties
                        {
                            Name = (string)gasEl.Attribute("Name") ?? "",
                            MolarMass = double.Parse((string)gasEl.Attribute("MolarMass") ?? "0.016", inv),
                            LFL = double.Parse((string)gasEl.Attribute("LFL") ?? "0", inv),
                            IDLH = double.Parse((string)gasEl.Attribute("IDLH") ?? "0", inv),
                            ERPG1 = double.Parse((string)gasEl.Attribute("ERPG1") ?? "0", inv),
                            ERPG2 = double.Parse((string)gasEl.Attribute("ERPG2") ?? "0", inv),
                            ERPG3 = double.Parse((string)gasEl.Attribute("ERPG3") ?? "0", inv)
                        };
                    }

                    sc.Sources.Add(source);
                }
            }

            var thrEl = dEl.Element("Thresholds");
            if (thrEl != null)
            {
                sc.Thresholds.Clear();
                foreach (var te in thrEl.Elements("Threshold"))
                {
                    var threshold = new DispersionThreshold();
                    threshold.Name = (string)te.Attribute("Name") ?? "";
                    threshold.Type = (DispersionThresholdType)Enum.Parse(typeof(DispersionThresholdType),
                        (string)te.Attribute("Type") ?? "Custom");
                    threshold.ConcentrationValue = double.Parse((string)te.Attribute("Value") ?? "0.01", inv);
                    threshold.Opacity = double.Parse((string)te.Attribute("Opacity") ?? "0.3", inv);
                    threshold.Visible = bool.Parse((string)te.Attribute("Visible") ?? "True");

                    var colorStr = (string)te.Attribute("Color");
                    if (!string.IsNullOrEmpty(colorStr))
                    {
                        try
                        {
                            threshold.Color = (System.Windows.Media.Color)
                                System.Windows.Media.ColorConverter.ConvertFromString(colorStr);
                        }
                        catch { }
                    }

                    sc.Thresholds.Add(threshold);
                }
            }

            var cpEl = dEl.Element("ContourPlanes");
            if (cpEl != null)
            {
                foreach (var ce in cpEl.Elements("ContourPlane"))
                {
                    var cp = new ContourPlaneConfig();
                    cp.Axis = (ContourAxis)Enum.Parse(typeof(ContourAxis),
                        (string)ce.Attribute("Axis") ?? "XY");
                    cp.Position = double.Parse((string)ce.Attribute("Position") ?? "0", inv);
                    cp.Visible = bool.Parse((string)ce.Attribute("Visible") ?? "True");
                    cp.Opacity = double.Parse((string)ce.Attribute("Opacity") ?? "0.8", inv);
                    cp.ColorMap = (Core.ColorMapName)Enum.Parse(typeof(Core.ColorMapName),
                        (string)ce.Attribute("ColorMap") ?? "Jet");
                    sc.ContourPlanes.Add(cp);
                }
            }

            var twEl = dEl.Element("TransientWind");
            if (twEl != null)
            {
                sc.TransientWind = new TransientWindProfile
                {
                    Enabled = bool.Parse((string)twEl.Attribute("Enabled") ?? "False"),
                    ESDTimeS = double.Parse((string)twEl.Attribute("ESD") ?? "-1", inv)
                };
                foreach (var we in twEl.Elements("Entry"))
                {
                    sc.TransientWind.Entries.Add(new WindProfileEntry
                    {
                        TimeS = double.Parse((string)we.Attribute("Time") ?? "0", inv),
                        WindSpeed = double.Parse((string)we.Attribute("Speed") ?? "5", inv),
                        WindDirectionDeg = double.Parse((string)we.Attribute("Dir") ?? "270", inv),
                        StabilityClass = (PasquillStabilityClass)Enum.Parse(typeof(PasquillStabilityClass),
                            (string)we.Attribute("Stability") ?? "D")
                    });
                }
            }

            var gmEl = dEl.Element("GasMixture");
            if (gmEl != null)
            {
                sc.GasMixture = new GasMixture();
                foreach (var ce in gmEl.Elements("Component"))
                {
                    sc.GasMixture.Components.Add(new GasComponent
                    {
                        Name = (string)ce.Attribute("Name") ?? "",
                        MolarMass = double.Parse((string)ce.Attribute("MolarMass") ?? "0.016", inv),
                        MoleFraction = double.Parse((string)ce.Attribute("MoleFrac") ?? "1", inv),
                        LFL = double.Parse((string)ce.Attribute("LFL") ?? "0", inv),
                        IDLH = double.Parse((string)ce.Attribute("IDLH") ?? "0", inv)
                    });
                }
            }

            return sc;
        }

        private System.Xml.Linq.XElement SerializeMonitorPoints(System.Globalization.CultureInfo inv)
        {
            if (_scene.MonitorPoints.Count == 0) return null;

            return new System.Xml.Linq.XElement("MonitorPoints",
                _scene.MonitorPoints.Select(m =>
                    new System.Xml.Linq.XElement("Monitor",
                        new System.Xml.Linq.XAttribute("Id", m.Id),
                        new System.Xml.Linq.XAttribute("Name", m.Name ?? ""),
                        new System.Xml.Linq.XAttribute("PosX", m.Position.X.ToString(inv)),
                        new System.Xml.Linq.XAttribute("PosY", m.Position.Y.ToString(inv)),
                        new System.Xml.Linq.XAttribute("PosZ", m.Position.Z.ToString(inv)),
                        new System.Xml.Linq.XAttribute("MeasuredQuantity", m.MeasuredQuantity.ToString()),
                        new System.Xml.Linq.XAttribute("Visible", m.Visible))));
        }

        private void DeserializeMonitorPoints(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D fs)
        {
            var mpEl = root.Element("MonitorPoints");
            if (mpEl == null) return;

            foreach (var me in mpEl.Elements("Monitor"))
            {
                var monitor = new MonitorPoint3D();
                monitor.Id = (string)me.Attribute("Id") ?? Guid.NewGuid().ToString();
                monitor.Name = (string)me.Attribute("Name") ?? "";
                monitor.Position = new Point3D(
                    double.Parse((string)me.Attribute("PosX") ?? "0", inv),
                    double.Parse((string)me.Attribute("PosY") ?? "0", inv),
                    double.Parse((string)me.Attribute("PosZ") ?? "0", inv));
                monitor.Visible = bool.Parse((string)me.Attribute("Visible") ?? "True");
                ViewFieldProperty mqM;
                if (Enum.TryParse((string)me.Attribute("MeasuredQuantity") ?? "ConcentrationKgM3", out mqM))
                    monitor.MeasuredQuantity = mqM;
                fs.MonitorPoints.Add(monitor);
            }
        }

        private System.Xml.Linq.XElement SerializeWindRose(System.Globalization.CultureInfo inv)
        {
            var wr = _scene.WindRose;
            if (wr == null || wr.Bins.Count == 0) return null;

            return new System.Xml.Linq.XElement("WindRose",
                new System.Xml.Linq.XAttribute("ShowIn3D", wr.ShowIn3D),
                wr.Bins.Select(b =>
                    new System.Xml.Linq.XElement("Bin",
                        new System.Xml.Linq.XAttribute("Dir", b.DirectionDeg.ToString(inv)),
                        new System.Xml.Linq.XAttribute("Freq", b.Frequency.ToString(inv)),
                        new System.Xml.Linq.XAttribute("Speed", b.WindSpeed.ToString(inv)),
                        new System.Xml.Linq.XAttribute("Stability", b.StabilityClass.ToString()))));
        }

        private void DeserializeWindRose(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D fs)
        {
            var wrEl = root.Element("WindRose");
            if (wrEl == null) return;

            var wr = new WindRoseData();
            wr.ShowIn3D = bool.Parse((string)wrEl.Attribute("ShowIn3D") ?? "True");

            foreach (var be in wrEl.Elements("Bin"))
            {
                wr.Bins.Add(new WindRoseBin
                {
                    DirectionDeg = double.Parse((string)be.Attribute("Dir") ?? "0", inv),
                    Frequency = double.Parse((string)be.Attribute("Freq") ?? "0", inv),
                    WindSpeed = double.Parse((string)be.Attribute("Speed") ?? "5", inv),
                    StabilityClass = (PasquillStabilityClass)Enum.Parse(typeof(PasquillStabilityClass),
                        (string)be.Attribute("Stability") ?? "D")
                });
            }

            fs.WindRose = wr;
        }

        private System.Xml.Linq.XElement SerializeFireScenario(System.Globalization.CultureInfo inv)
        {
            var fs = _scene.FireScenario;
            if (fs == null || fs.Sources.Count == 0) return null;

            return new System.Xml.Linq.XElement("FireScenario",
                new System.Xml.Linq.XAttribute("Name", fs.Name ?? ""),
                new System.Xml.Linq.XElement("RadLevels",
                    string.Join(",", fs.RadiationContourLevels.Select(l => l.ToString(inv)))),
                new System.Xml.Linq.XElement("FireSources",
                    fs.Sources.Select(f =>
                        new System.Xml.Linq.XElement("Fire",
                            new System.Xml.Linq.XAttribute("Id", f.Id),
                            new System.Xml.Linq.XAttribute("Name", f.Name ?? ""),
                            new System.Xml.Linq.XAttribute("PosX", f.Position.X.ToString(inv)),
                            new System.Xml.Linq.XAttribute("PosY", f.Position.Y.ToString(inv)),
                            new System.Xml.Linq.XAttribute("PosZ", f.Position.Z.ToString(inv)),
                            new System.Xml.Linq.XAttribute("DirX", f.Direction.X.ToString(inv)),
                            new System.Xml.Linq.XAttribute("DirY", f.Direction.Y.ToString(inv)),
                            new System.Xml.Linq.XAttribute("DirZ", f.Direction.Z.ToString(inv)),
                            new System.Xml.Linq.XAttribute("MassFlow", f.MassFlowRateKgS.ToString(inv)),
                            new System.Xml.Linq.XAttribute("Orifice", f.OrificeDiameterM.ToString(inv)),
                            new System.Xml.Linq.XAttribute("HeatComb", f.HeatOfCombustionJKg.ToString(inv)),
                            new System.Xml.Linq.XAttribute("RadFrac", f.RadiativeFraction.ToString(inv)),
                            new System.Xml.Linq.XAttribute("IsPool", f.IsPoolFire),
                            new System.Xml.Linq.XAttribute("PoolDia", f.PoolDiameterM.ToString(inv)),
                            new System.Xml.Linq.XAttribute("BurnRate", f.PoolBurnRateKgM2S.ToString(inv))))));
        }

        private void DeserializeFireScenario(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D fs)
        {
            var el = root.Element("FireScenario");
            if (el == null) return;

            var scenario = new FireScenario();
            scenario.Name = (string)el.Attribute("Name") ?? "";

            var levelsEl = el.Element("RadLevels");
            if (levelsEl != null && !string.IsNullOrWhiteSpace(levelsEl.Value))
            {
                scenario.RadiationContourLevels.Clear();
                foreach (var s in levelsEl.Value.Split(','))
                    scenario.RadiationContourLevels.Add(double.Parse(s.Trim(), inv));
            }

            var srcEl = el.Element("FireSources");
            if (srcEl != null)
            {
                foreach (var fe in srcEl.Elements("Fire"))
                {
                    var fire = new FireSource();
                    fire.Id = (string)fe.Attribute("Id") ?? Guid.NewGuid().ToString();
                    fire.Name = (string)fe.Attribute("Name") ?? "";
                    fire.Position = new Point3D(
                        double.Parse((string)fe.Attribute("PosX") ?? "0", inv),
                        double.Parse((string)fe.Attribute("PosY") ?? "0", inv),
                        double.Parse((string)fe.Attribute("PosZ") ?? "0", inv));
                    fire.Direction = new Vector3D(
                        double.Parse((string)fe.Attribute("DirX") ?? "0", inv),
                        double.Parse((string)fe.Attribute("DirY") ?? "0", inv),
                        double.Parse((string)fe.Attribute("DirZ") ?? "1", inv));
                    fire.MassFlowRateKgS = double.Parse((string)fe.Attribute("MassFlow") ?? "1", inv);
                    fire.OrificeDiameterM = double.Parse((string)fe.Attribute("Orifice") ?? "0.02", inv);
                    fire.HeatOfCombustionJKg = double.Parse((string)fe.Attribute("HeatComb") ?? "50000000", inv);
                    fire.RadiativeFraction = double.Parse((string)fe.Attribute("RadFrac") ?? "0.2", inv);
                    fire.IsPoolFire = bool.Parse((string)fe.Attribute("IsPool") ?? "False");
                    fire.PoolDiameterM = double.Parse((string)fe.Attribute("PoolDia") ?? "5", inv);
                    fire.PoolBurnRateKgM2S = double.Parse((string)fe.Attribute("BurnRate") ?? "0.05", inv);
                    scenario.Sources.Add(fire);
                }
            }

            fs.FireScenario = scenario;
        }

        private System.Xml.Linq.XElement SerializeGasDetectors(System.Globalization.CultureInfo inv)
        {
            if (_scene.GasDetectors.Count == 0) return null;

            return new System.Xml.Linq.XElement("GasDetectors",
                _scene.GasDetectors.Select(d =>
                    new System.Xml.Linq.XElement("Detector",
                        new System.Xml.Linq.XAttribute("Id", d.Id),
                        new System.Xml.Linq.XAttribute("Name", d.Name ?? ""),
                        new System.Xml.Linq.XAttribute("PosX", d.Position.X.ToString(inv)),
                        new System.Xml.Linq.XAttribute("PosY", d.Position.Y.ToString(inv)),
                        new System.Xml.Linq.XAttribute("PosZ", d.Position.Z.ToString(inv)),
                        new System.Xml.Linq.XAttribute("Threshold", d.ThresholdKgM3.ToString(inv)),
                        new System.Xml.Linq.XAttribute("MeasuredQuantity", d.MeasuredQuantity.ToString()),
                        new System.Xml.Linq.XAttribute("MeasuredThreshold", d.Threshold.ToString(inv)),
                        new System.Xml.Linq.XAttribute("Visible", d.Visible))));
        }

        private void DeserializeGasDetectors(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D fs)
        {
            var el = root.Element("GasDetectors");
            if (el == null) return;

            foreach (var de in el.Elements("Detector"))
            {
                var det = new GasDetector3D();
                det.Id = (string)de.Attribute("Id") ?? Guid.NewGuid().ToString();
                det.Name = (string)de.Attribute("Name") ?? "";
                det.Position = new Point3D(
                    double.Parse((string)de.Attribute("PosX") ?? "0", inv),
                    double.Parse((string)de.Attribute("PosY") ?? "0", inv),
                    double.Parse((string)de.Attribute("PosZ") ?? "0", inv));
                det.ThresholdKgM3 = double.Parse((string)de.Attribute("Threshold") ?? "0.01", inv);
                ViewFieldProperty mq;
                if (Enum.TryParse((string)de.Attribute("MeasuredQuantity") ?? "ConcentrationKgM3", out mq))
                    det.MeasuredQuantity = mq;
                det.Threshold = double.Parse((string)de.Attribute("MeasuredThreshold") ?? "25", inv);
                det.Visible = bool.Parse((string)de.Attribute("Visible") ?? "True");
                fs.GasDetectors.Add(det);
            }
        }

        private System.Xml.Linq.XElement SerializeCfdSimulations(
            System.Globalization.CultureInfo inv, string projectFilePath)
        {
            if (_scene.CfdSimulations.Count == 0) return null;

            string projectDir = System.IO.Path.GetDirectoryName(projectFilePath);
            string projectName = System.IO.Path.GetFileNameWithoutExtension(projectFilePath);
            string resultsDir = System.IO.Path.Combine(projectDir, projectName + "_results");

            foreach (var entry in _scene.CfdSimulations)
            {
                if (!entry.HasResults || string.IsNullOrEmpty(entry.CasePath)) continue;
                if (!System.IO.Directory.Exists(entry.CasePath)) continue;

                string destDir = System.IO.Path.Combine(resultsDir, entry.Id);
                if (entry.CasePath == destDir) continue;

                try
                {
                    if (!System.IO.Directory.Exists(destDir))
                        System.IO.Directory.CreateDirectory(destDir);

                    // Gaussian Puff + FluidX3D dispersion both produce a flat directory of
                    // <time>.bin concentration snapshots. Detect by absence of OpenFOAM
                    // controlDict + presence of .bin files at root.
                    bool hasFoamCtrl = System.IO.File.Exists(System.IO.Path.Combine(
                        entry.CasePath, "system", "controlDict"));
                    bool hasRootBins = System.IO.Directory.GetFiles(entry.CasePath, "*.bin",
                        System.IO.SearchOption.TopDirectoryOnly).Length > 0;
                    if (!hasFoamCtrl && hasRootBins)
                    {
                        foreach (var f in System.IO.Directory.GetFiles(entry.CasePath, "*.bin"))
                            System.IO.File.Copy(f, System.IO.Path.Combine(destDir,
                                System.IO.Path.GetFileName(f)), true);
                    }
                    else
                    {
                        CopyEssentialCfdResults(entry.CasePath, destDir);
                    }

                    entry.CasePath = destDir;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to copy results for " + entry.Id + ": " + ex.Message);
                }
            }

            return new System.Xml.Linq.XElement("CfdSimulations",
                _scene.CfdSimulations.Select(e =>
                    new System.Xml.Linq.XElement("Simulation",
                        new System.Xml.Linq.XAttribute("Id", e.Id ?? ""),
                        new System.Xml.Linq.XAttribute("Name", e.Name ?? ""),
                        new System.Xml.Linq.XAttribute("ScenarioName", e.ScenarioName ?? ""),
                        new System.Xml.Linq.XAttribute("CasePath", e.CasePath ?? ""),
                        new System.Xml.Linq.XAttribute("CreatedAt", e.CreatedAt.ToString("o", inv)),
                        new System.Xml.Linq.XAttribute("DurationS", e.DurationS.ToString(inv)),
                        new System.Xml.Linq.XAttribute("TimeStepCount", e.TimeStepCount.ToString(inv)),
                        new System.Xml.Linq.XAttribute("GridNx", e.GridNx.ToString(inv)),
                        new System.Xml.Linq.XAttribute("GridNy", e.GridNy.ToString(inv)),
                        new System.Xml.Linq.XAttribute("GridNz", e.GridNz.ToString(inv)),
                        new System.Xml.Linq.XAttribute("DomainSizeM", e.DomainSizeM.ToString(inv)),
                        new System.Xml.Linq.XAttribute("HasResults", e.HasResults),
                        new System.Xml.Linq.XAttribute("SolverType", e.SolverType ?? ""))));
        }

        private static void CopyEssentialCfdResults(string srcCase, string destCase)
        {
            string sysDir = System.IO.Path.Combine(destCase, "system");
            if (!System.IO.Directory.Exists(sysDir))
                System.IO.Directory.CreateDirectory(sysDir);

            string srcBlockMesh = System.IO.Path.Combine(srcCase, "system", "blockMeshDict");
            if (System.IO.File.Exists(srcBlockMesh))
                System.IO.File.Copy(srcBlockMesh, System.IO.Path.Combine(sysDir, "blockMeshDict"), true);

            foreach (var dir in System.IO.Directory.GetDirectories(srcCase))
            {
                string name = System.IO.Path.GetFileName(dir);
                double t;
                if (!double.TryParse(name, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out t)) continue;
                if (t <= 0) continue;

                string tFile = System.IO.Path.Combine(dir, "T");
                if (!System.IO.File.Exists(tFile)) continue;

                string destTimeDir = System.IO.Path.Combine(destCase, name);
                if (!System.IO.Directory.Exists(destTimeDir))
                    System.IO.Directory.CreateDirectory(destTimeDir);

                System.IO.File.Copy(tFile, System.IO.Path.Combine(destTimeDir, "T"), true);

                foreach (var extra in new[] { "C", "Cx", "Cy", "Cz" })
                {
                    string src = System.IO.Path.Combine(dir, extra);
                    if (System.IO.File.Exists(src))
                        System.IO.File.Copy(src, System.IO.Path.Combine(destTimeDir, extra), true);
                }
            }
        }

        private void DeserializeCfdSimulations(System.Xml.Linq.XElement root,
            System.Globalization.CultureInfo inv, Scene3D fs)
        {
            var el = root.Element("CfdSimulations");
            if (el == null) return;

            foreach (var se in el.Elements("Simulation"))
            {
                var entry = new CfdSimulationEntry();
                entry.Id = (string)se.Attribute("Id") ?? entry.Id;
                entry.Name = (string)se.Attribute("Name") ?? "";
                entry.ScenarioName = (string)se.Attribute("ScenarioName") ?? "";
                entry.CasePath = (string)se.Attribute("CasePath") ?? "";
                entry.DurationS = double.Parse((string)se.Attribute("DurationS") ?? "0", inv);
                entry.TimeStepCount = int.Parse((string)se.Attribute("TimeStepCount") ?? "0", inv);
                entry.GridNx = int.Parse((string)se.Attribute("GridNx") ?? "40", inv);
                entry.GridNy = int.Parse((string)se.Attribute("GridNy") ?? "40", inv);
                entry.GridNz = int.Parse((string)se.Attribute("GridNz") ?? "20", inv);
                entry.DomainSizeM = double.Parse((string)se.Attribute("DomainSizeM") ?? "200", inv);
                entry.HasResults = bool.Parse((string)se.Attribute("HasResults") ?? "False");
                entry.SolverType = (string)se.Attribute("SolverType") ?? "OpenFOAM";

                var createdStr = (string)se.Attribute("CreatedAt");
                if (createdStr != null)
                {
                    DateTime dt;
                    if (DateTime.TryParse(createdStr, inv, System.Globalization.DateTimeStyles.RoundtripKind, out dt))
                        entry.CreatedAt = dt;
                }

                if (entry.HasResults && !string.IsNullOrEmpty(entry.CasePath)
                    && System.IO.Directory.Exists(entry.CasePath))
                {
                    fs.CfdSimulations.Add(entry);
                }
            }
        }

        /// <summary>Path to the extracted bundle for the currently loaded .dsproj, if any.</summary>
        private string _currentBundleRoot;

        /// <summary>
        /// Loads a scene from a file. Accepts both .dsproj (self-contained ZIP bundle) and bare
        /// .xml (legacy with external file references).
        /// </summary>
        public void LoadFromFile(string filePath)
        {
            if (!System.IO.File.Exists(filePath)) return;

            var inv = System.Globalization.CultureInfo.InvariantCulture;

            // Drop any previous bundle's temp dir.
            if (!string.IsNullOrEmpty(_currentBundleRoot))
            {
                try { if (System.IO.Directory.Exists(_currentBundleRoot))
                    System.IO.Directory.Delete(_currentBundleRoot, recursive: true); } catch { }
                _currentBundleRoot = null;
            }

            try
            {
                System.Xml.Linq.XDocument doc;
                if (ProjectBundle.IsBundleFile(filePath))
                {
                    var bundle = ProjectBundle.Open(filePath);
                    _currentBundleRoot = bundle.BundleRoot;
                    doc = bundle.ProjectXml;
                }
                else
                {
                    doc = System.Xml.Linq.XDocument.Load(filePath);
                }
                var root = doc.Root;
                if (root == null || (root.Name.LocalName != "Scene3D" && root.Name.LocalName != "Flowsheet3D")) return;

                var fs = new Scene3D();
                fs.Name = (string)root.Attribute("Name") ?? "New Scene";
                fs.Description = (string)root.Attribute("Description") ?? "";

                var gridEl = root.Element("GridSettings");
                if (gridEl != null)
                {
                    fs.GridSpacing = double.Parse((string)gridEl.Attribute("Spacing") ?? "5", inv);
                    fs.SnapToGrid = bool.Parse((string)gridEl.Attribute("SnapToGrid") ?? "True");
                }

                var wpEl = root.Element("WorkPlanes");
                if (wpEl != null)
                {
                    fs.WorkPlanes.Clear();
                    foreach (var wp in wpEl.Elements("WorkPlane"))
                    {
                        var plane = new WorkPlane(
                            double.Parse((string)wp.Attribute("Elevation") ?? "0", inv),
                            (string)wp.Attribute("Name") ?? "");
                        plane.Visible = bool.Parse((string)wp.Attribute("Visible") ?? "True");
                        plane.GridSpacing = double.Parse((string)wp.Attribute("GridSpacing") ?? "5", inv);

                        var colorStr = (string)wp.Attribute("GridColor");
                        if (!string.IsNullOrEmpty(colorStr))
                        {
                            try
                            {
                                plane.GridColor = (System.Windows.Media.Color)
                                    System.Windows.Media.ColorConverter.ConvertFromString(colorStr);
                            }
                            catch { }
                        }
                        fs.WorkPlanes.Add(plane);
                    }
                }

                var cwpEl = root.Element("CurrentWorkPlane");
                var cwpName = cwpEl != null ? (string)cwpEl.Attribute("Name") ?? "" : "";
                fs.CurrentWorkPlane = fs.WorkPlanes.FirstOrDefault(w => w.Name == cwpName)
                                      ?? fs.WorkPlanes.FirstOrDefault()
                                      ?? WorkPlane.CreateGroundLevel();

                var decosEl = root.Element("Decorations");
                if (decosEl != null)
                {
                    foreach (var de in decosEl.Elements("Decoration"))
                    {
                        var deco = new Decoration3D();
                        deco.Id = (string)de.Attribute("Id") ?? Guid.NewGuid().ToString();
                        deco.Name = (string)de.Attribute("Name") ?? "";
                        deco.FilePath = (string)de.Attribute("FilePath") ?? "";
                        deco.Position = new Point3D(
                            double.Parse((string)de.Attribute("PosX") ?? "0", inv),
                            double.Parse((string)de.Attribute("PosY") ?? "0", inv),
                            double.Parse((string)de.Attribute("PosZ") ?? "0", inv));
                        deco.Rotation = new Vector3D(
                            double.Parse((string)de.Attribute("RotX") ?? "0", inv),
                            double.Parse((string)de.Attribute("RotY") ?? "0", inv),
                            double.Parse((string)de.Attribute("RotZ") ?? "0", inv));
                        deco.Scale = double.Parse((string)de.Attribute("Scale") ?? "1", inv);

                        if (!string.IsNullOrEmpty(deco.FilePath) && System.IO.File.Exists(deco.FilePath))
                        {
                            deco.OriginalModel3D = _modelLoader.LoadModelFromFile(deco.FilePath);
                            deco.Model3D = deco.OriginalModel3D;
                        }

                        var clipEnabledAttr = (string)de.Attribute("ClipEnabled");
                        if (clipEnabledAttr != null)
                        {
                            deco.ClipEnabled = bool.Parse(clipEnabledAttr);
                            deco.ClipAxis = (Core.ClipAxis)Enum.Parse(typeof(Core.ClipAxis), (string)de.Attribute("ClipAxis") ?? "Y");
                            deco.ClipValue = double.Parse((string)de.Attribute("ClipValue") ?? "0", inv);
                            deco.ClipAbove = bool.Parse((string)de.Attribute("ClipAbove") ?? "True");
                            deco.ApplyClip();
                        }

                        var useCustomAttr = (string)de.Attribute("UseCustomMaterial");
                        if (useCustomAttr != null)
                        {
                            deco.UseCustomMaterial = bool.Parse(useCustomAttr);
                            if (Enum.TryParse((string)de.Attribute("MaterialType") ?? "Matte", out MaterialType3D mt))
                                deco.MaterialType = mt;
                            try { deco.MaterialColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString((string)de.Attribute("MaterialColor") ?? "#FFD3D3D3"); }
                            catch { }
                            deco.SpecularPower = double.Parse((string)de.Attribute("SpecularPower") ?? "40", inv);
                            deco.Opacity = double.Parse((string)de.Attribute("Opacity") ?? "1", inv);
                        }

                        deco.UpdateBoundingBox();
                        fs.Decorations.Add(deco);
                    }
                }

                DeserializeGeneralSettings(root, inv, fs);
                DeserializeEnvironment(root, inv, fs);
                DeserializeGasLibrary(root, inv, fs);
                DeserializeTopLevelSources(root, inv, fs);
                DeserializeWindFieldScenarios(root, inv, fs);
                DeserializeSimulations(root, inv, fs);
                DeserializeViews(root, inv, fs);
                DeserializeDispersionStudies(root, inv, fs);
                DeserializeDetectorAllocations(root, inv, fs);
                DeserializeDispersionScenario(root, inv, fs);

                LegacyProjectMigrator.MigrateInPlace(fs);

                DeserializeMonitorPoints(root, inv, fs);
                DeserializeWindRose(root, inv, fs);
                DeserializeFireScenario(root, inv, fs);
                DeserializeGasDetectors(root, inv, fs);
                DeserializeCfdSimulations(root, inv, fs);

                _scene = fs;
                _snapToGrid = fs.SnapToGrid;
                _gridSpacing = fs.GridSpacing;
                SelectedDecoration = null;
                if (_scene.Environment == null) _scene.Environment = new EnvironmentSettings();
                ApplyEnvironment();
                UpdateViewport();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to load scene: " + ex.Message);
            }
        }

        #endregion

        #region Camera Methods

        private void UpdateCameraMode(Models.CameraMode mode)
        {
            switch (mode)
            {
                case Models.CameraMode.TopDown:
                    SetTopDownCamera();
                    break;
                case Models.CameraMode.Isometric:
                    SetIsometricCamera();
                    break;
                case Models.CameraMode.Front:
                    SetFrontCamera();
                    break;
                case Models.CameraMode.Side:
                    SetSideCamera();
                    break;
            }
        }

        private void SetIsometricCamera()
        {
            _viewport.Camera.Position = new Point3D(50, 50, 50);
            _viewport.Camera.LookDirection = new Vector3D(-1, -1, -1);
            _viewport.Camera.UpDirection = new Vector3D(0, 0, 1);
        }

        private void SetTopDownCamera()
        {
            _viewport.Camera.Position = new Point3D(0, 0, 100);
            _viewport.Camera.LookDirection = new Vector3D(0, 0, -1);
            _viewport.Camera.UpDirection = new Vector3D(0, 1, 0);
        }

        private void SetFrontCamera()
        {
            _viewport.Camera.Position = new Point3D(0, -100, 0);
            _viewport.Camera.LookDirection = new Vector3D(0, 1, 0);
            _viewport.Camera.UpDirection = new Vector3D(0, 0, 1);
        }

        private void SetSideCamera()
        {
            _viewport.Camera.Position = new Point3D(100, 0, 0);
            _viewport.Camera.LookDirection = new Vector3D(-1, 0, 0);
            _viewport.Camera.UpDirection = new Vector3D(0, 0, 1);
        }

        #endregion

        #region Event Handlers

        private void Viewport_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var position = e.GetPosition(_viewport);

            if (PointPicked != null && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                var pickHit = GetHitPoint(position);
                if (pickHit != null)
                {
                    PointPicked.Invoke(this, pickHit.Value);
                }
            }

            switch (_currentEditMode)
            {
                case EditMode.Select:
                    var hitTag = PerformTagHitTest(position);

                    if (hitTag != null && hitTag.Category == "Decoration")
                    {
                        var hitDeco = _scene.FindDecoration(hitTag.Id);
                        SelectedDecoration = hitDeco;
                        SelectedSource = null;

                        // Drag-to-reposition is intentionally disabled: any geometry move
                        // would invalidate the CFD snapshots cached for existing
                        // simulations. Positions can only be edited through the
                        // properties panel (which performs the proper invalidation
                        // bookkeeping).
                    }
                    else if (hitTag != null && hitTag.Category == "ReleaseSource")
                    {
                        SelectedDecoration = null;
                        SelectedSource = FindSourceById(hitTag.Id);
                        SelectedUnitChanged?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        SelectedDecoration = null;
                        SelectedSource = null;
                    }
                    break;

                case EditMode.PlaceReleaseSource:
                    var releasePoint = GetHitPoint(position);
                    if (releasePoint != null)
                    {
                        var src = AddReleaseSource(releasePoint.Value, PendingSourceTemplate);
                        PendingSourceTemplate = null;
                        CurrentEditMode = EditMode.Select;
                        ObjectPlaced?.Invoke(this, new ObjectPlacedEventArgs
                            { PlacementType = EditMode.PlaceReleaseSource, PlacedObject = src });
                    }
                    break;

                case EditMode.PlaceMonitorPoint:
                    var monitorPoint = GetHitPoint(position);
                    if (monitorPoint != null)
                    {
                        var mon = AddMonitorPoint(monitorPoint.Value, PendingMonitorTemplate);
                        PendingMonitorTemplate = null;
                        CurrentEditMode = EditMode.Select;
                        ObjectPlaced?.Invoke(this, new ObjectPlacedEventArgs
                            { PlacementType = EditMode.PlaceMonitorPoint, PlacedObject = mon });
                    }
                    break;

                case EditMode.PlaceFireSource:
                    var firePoint = GetHitPoint(position);
                    if (firePoint != null)
                    {
                        var fire = PendingFireTemplate ?? new Models.FireSource();
                        fire.Position = _snapToGrid ? firePoint.Value.SnapToGrid(_gridSpacing) : firePoint.Value;
                        _scene.FireScenario.Sources.Add(fire);
                        PendingFireTemplate = null;
                        CurrentEditMode = EditMode.Select;
                        UpdateViewport();
                        ObjectPlaced?.Invoke(this, new ObjectPlacedEventArgs
                            { PlacementType = EditMode.PlaceFireSource, PlacedObject = fire });
                    }
                    break;

                case EditMode.PlaceGasDetector:
                    var detPoint = GetHitPoint(position);
                    if (detPoint != null)
                    {
                        var det = PendingDetectorTemplate ?? new Models.GasDetector3D
                            { Name = "Detector" + (_scene.GasDetectors.Count + 1) };
                        det.Position = _snapToGrid ? detPoint.Value.SnapToGrid(_gridSpacing) : detPoint.Value;
                        _scene.GasDetectors.Add(det);
                        PendingDetectorTemplate = null;
                        CurrentEditMode = EditMode.Select;
                        UpdateViewport();
                        ObjectPlaced?.Invoke(this, new ObjectPlacedEventArgs
                            { PlacementType = EditMode.PlaceGasDetector, PlacedObject = det });
                    }
                    break;

            }
        }

        private void Viewport_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Drag-to-reposition has been removed — see field block at the top of
            // the class. Mouse-move handling for orbit/pan is owned by HelixToolkit.
        }

        private void Viewport_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // No drag state to release — see field block at the top of the class.
        }

        private void Viewport_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.Delete:
                    DeleteSelected();
                    break;

                case System.Windows.Input.Key.Escape:
                    CurrentEditMode = EditMode.Select;
                    break;
            }
        }

        private void Viewport_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            bool ctrl = (System.Windows.Input.Keyboard.Modifiers &
                System.Windows.Input.ModifierKeys.Control) != 0;

            if (ctrl)
            {
                var camera = _viewport.Camera as System.Windows.Media.Media3D.PerspectiveCamera;
                if (camera != null)
                {
                    var lookDir = camera.LookDirection;
                    lookDir.Normalize();
                    double zoomFactor = e.Delta > 0 ? 2.0 : -2.0;
                    camera.Position = new Point3D(
                        camera.Position.X + lookDir.X * zoomFactor,
                        camera.Position.Y + lookDir.Y * zoomFactor,
                        camera.Position.Z + lookDir.Z * zoomFactor);
                    e.Handled = true;
                }
            }
        }

        private void Scene3DEditorControl_Resize(object sender, EventArgs e)
        {
            _wpfHost?.Invalidate();
        }

        private void OnSelectedUnitChanged()
        {
            if (_selectionHighlight != null)
            {
                _viewport.Children.Remove(_selectionHighlight);
                _selectionHighlight = null;
            }

            BoundingBox selBB = null;

            if (_selectedDecoration != null && _selectedDecoration.BoundingBox != null)
            {
                selBB = _selectedDecoration.BoundingBox;
            }

            if (selBB != null)
            {
                var min = selBB.Min;
                var max = selBB.Max;

                _selectionHighlight = new LinesVisual3D
                {
                    Color = System.Windows.Media.Colors.Orange,
                    Thickness = 2
                };
                var pts = _selectionHighlight.Points;

                // Bottom face
                pts.Add(new Point3D(min.X, min.Y, min.Z)); pts.Add(new Point3D(max.X, min.Y, min.Z));
                pts.Add(new Point3D(max.X, min.Y, min.Z)); pts.Add(new Point3D(max.X, max.Y, min.Z));
                pts.Add(new Point3D(max.X, max.Y, min.Z)); pts.Add(new Point3D(min.X, max.Y, min.Z));
                pts.Add(new Point3D(min.X, max.Y, min.Z)); pts.Add(new Point3D(min.X, min.Y, min.Z));

                // Top face
                pts.Add(new Point3D(min.X, min.Y, max.Z)); pts.Add(new Point3D(max.X, min.Y, max.Z));
                pts.Add(new Point3D(max.X, min.Y, max.Z)); pts.Add(new Point3D(max.X, max.Y, max.Z));
                pts.Add(new Point3D(max.X, max.Y, max.Z)); pts.Add(new Point3D(min.X, max.Y, max.Z));
                pts.Add(new Point3D(min.X, max.Y, max.Z)); pts.Add(new Point3D(min.X, min.Y, max.Z));

                // Vertical edges
                pts.Add(new Point3D(min.X, min.Y, min.Z)); pts.Add(new Point3D(min.X, min.Y, max.Z));
                pts.Add(new Point3D(max.X, min.Y, min.Z)); pts.Add(new Point3D(max.X, min.Y, max.Z));
                pts.Add(new Point3D(max.X, max.Y, min.Z)); pts.Add(new Point3D(max.X, max.Y, max.Z));
                pts.Add(new Point3D(min.X, max.Y, min.Z)); pts.Add(new Point3D(min.X, max.Y, max.Z));

                _viewport.Children.Add(_selectionHighlight);
            }

            SelectedUnitChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnEditModeChanged()
        {
            EditModeChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Helper Methods

        private Decoration3D PerformDecorationHitTest(System.Windows.Point screenPosition)
        {
            var tag = PerformTagHitTest(screenPosition);
            if (tag != null && tag.Category == "Decoration")
                return _scene.FindDecoration(tag.Id);
            return null;
        }

        private Visual3DTag PerformTagHitTest(System.Windows.Point screenPosition)
        {
            Visual3DTag hitTag = null;

            System.Windows.Media.VisualTreeHelper.HitTest(
                _viewport.Viewport,
                null,
                result =>
                {
                    var meshResult = result as System.Windows.Media.Media3D.RayMeshGeometry3DHitTestResult;
                    if (meshResult == null)
                        return System.Windows.Media.HitTestResultBehavior.Continue;

                    var dep = meshResult.VisualHit as System.Windows.DependencyObject;
                    while (dep != null)
                    {
                        if (dep is System.Windows.Media.Media3D.ModelVisual3D mv)
                        {
                            var tag = mv.GetValue(System.Windows.FrameworkElement.TagProperty) as Visual3DTag;
                            if (tag != null && tag.Category != null)
                            {
                                hitTag = tag;
                                return System.Windows.Media.HitTestResultBehavior.Stop;
                            }
                        }
                        dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
                    }

                    return System.Windows.Media.HitTestResultBehavior.Continue;
                },
                new System.Windows.Media.PointHitTestParameters(screenPosition));

            return hitTag;
        }

        private Point3D? GetHitPoint(System.Windows.Point screenPosition)
        {
            var ray = _viewport.Viewport.Point2DtoRay3D(screenPosition);

            var plane = _scene.CurrentWorkPlane;
            if (plane == null) return null;

            var planeOrigin = new Point3D(0, 0, plane.Elevation);
            var planeNormal = new Vector3D(0, 0, 1);

            var hitPoint = GetPlaneIntersection(ray, planeOrigin, planeNormal);

            if (hitPoint != null && _snapToGrid)
            {
                hitPoint = hitPoint.Value.SnapToGrid(_gridSpacing);
            }

            return hitPoint;
        }

        private Point3D? GetPlaneIntersection(Ray3D ray, Point3D planePoint, Vector3D planeNormal)
        {
            var denom = Vector3D.DotProduct(planeNormal, ray.Direction);

            if (Math.Abs(denom) < 0.0001)
                return null;

            var t = Vector3D.DotProduct(planeNormal, planePoint - ray.Origin) / denom;

            if (t < 0)
                return null;

            return ray.Origin + ray.Direction * t;
        }

        private MeshGeometry3D CreateArrowConeMesh(double radius, double height, int segments)
        {
            var mesh = new MeshGeometry3D();

            // Apex at +Z
            mesh.Positions.Add(new Point3D(0, 0, height));

            // Base circle
            for (int i = 0; i <= segments; i++)
            {
                var angle = 2 * Math.PI * i / segments;
                mesh.Positions.Add(new Point3D(
                    radius * Math.Cos(angle),
                    radius * Math.Sin(angle),
                    0));
            }

            // Side faces
            for (int i = 0; i < segments; i++)
            {
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(i + 1);
                mesh.TriangleIndices.Add(i + 2);
            }

            // Base cap
            var center = mesh.Positions.Count;
            mesh.Positions.Add(new Point3D(0, 0, 0));
            for (int i = 0; i < segments; i++)
            {
                mesh.TriangleIndices.Add(center);
                mesh.TriangleIndices.Add(i + 2);
                mesh.TriangleIndices.Add(i + 1);
            }

            return mesh;
        }

        private Transform3D CreateArrowAlignment(Point3D position, Vector3D direction)
        {
            var group = new Transform3DGroup();
            var zAxis = new Vector3D(0, 0, 1);
            var axis = Vector3D.CrossProduct(zAxis, direction);

            if (axis.Length > 0.001)
            {
                axis.Normalize();
                var dot = Vector3D.DotProduct(zAxis, direction);
                if (dot > 1.0) dot = 1.0;
                if (dot < -1.0) dot = -1.0;
                var angle = Math.Acos(dot) * 180 / Math.PI;
                group.Children.Add(new RotateTransform3D(
                    new AxisAngleRotation3D(axis, angle)));
            }
            else if (Vector3D.DotProduct(zAxis, direction) < 0)
            {
                group.Children.Add(new RotateTransform3D(
                    new AxisAngleRotation3D(new Vector3D(1, 0, 0), 180)));
            }

            group.Children.Add(new TranslateTransform3D(position.X, position.Y, position.Z));
            return group;
        }

        public void RefreshViewport()
        {
            UpdateViewport();
            RefreshViews();
            RefreshStudiesAndAllocations();
        }

        /// <summary>
        /// Rebuilds visuals for all visible <see cref="View"/>s in the scene. Called whenever
        /// the Views collection or any View property changes. Removed/hidden views have their
        /// visual stripped from the viewport; new/edited views get a fresh visual.
        /// </summary>
        /// <summary>Convert a kg/m³ concentration sample to the monitor's chosen unit
        /// (%LFL, ppm, mole fraction, K, kW/m², etc.). Pulls the gas via the scenario's
        /// first source. Returns the input unchanged for ConcentrationKgM3.</summary>
        private double ApplyMonitorTransform(MonitorPoint3D mon, double concKgM3)
        {
            var q = mon.MeasuredQuantity;
            if (q == DisperSim3D.Models.ViewFieldProperty.ThermalRadiationKwM2)
                return Core.FieldTransform.RadiationAtPoint(_scene, mon.Position.X, mon.Position.Y, mon.Position.Z);
            if (q == DisperSim3D.Models.ViewFieldProperty.ConcentrationKgM3) return concKgM3;
            if (q == DisperSim3D.Models.ViewFieldProperty.Concentration
                || q == DisperSim3D.Models.ViewFieldProperty.MassFraction)
                return concKgM3 / 1.205;
            var gas = _scene?.DispersionScenario?.Sources?.Count > 0
                ? _scene.DispersionScenario.Sources[0].Gas
                : (_scene?.TopLevelSources?.Count > 0 ? _scene.TopLevelSources[0].Gas : null);
            double y = concKgM3 / 1.205;
            return Core.FieldTransform.ScalarFromMassFraction(y, q, gas);
        }

        public void RefreshViews()
        {
            if (_viewport == null || _scene == null) return;

            var keep = new HashSet<string>();
            foreach (var view in _scene.Views)
            {
                if (!view.IsVisible) continue;
                var sim = _scene.Simulations.FirstOrDefault(s => s.Id == view.SimulationId);
                if (sim == null || sim.Status != SimulationStatus.Completed) continue;

                ModelVisual3D vis = null;
                try { vis = ViewRenderer.BuildVisual(view, sim, _scene); }
                catch { vis = null; }
                if (vis == null) continue;

                if (_viewVisuals.TryGetValue(view.Id, out var oldVis))
                    _viewport.Children.Remove(oldVis);
                _viewport.Children.Add(vis);
                _viewVisuals[view.Id] = vis;
                keep.Add(view.Id);
            }

            // Remove visuals for views that are no longer visible / present.
            var toRemove = _viewVisuals.Keys.Where(k => !keep.Contains(k)).ToList();
            foreach (var k in toRemove)
            {
                _viewport.Children.Remove(_viewVisuals[k]);
                _viewVisuals.Remove(k);
            }
        }

        /// <summary>Strips every View visual from the viewport (used on project unload).</summary>
        public void RemoveAllViews()
        {
            if (_viewport == null) return;
            foreach (var v in _viewVisuals.Values)
                _viewport.Children.Remove(v);
            _viewVisuals.Clear();
            foreach (var v in _studyVisuals.Values)
                _viewport.Children.Remove(v);
            _studyVisuals.Clear();
            foreach (var v in _allocationVisuals.Values)
                _viewport.Children.Remove(v);
            _allocationVisuals.Clear();
        }

        /// <summary>Rebuilds the cloud isosurfaces for every visible
        /// <see cref="DispersionStudy"/> + the marker geometry for every visible
        /// <see cref="DetectorAllocation"/>. Caches by Id — only re-renders entries
        /// that aren't already present in the cache. Call after any edit to a study
        /// or allocation, or when their visibility toggles.</summary>
        public void RefreshStudiesAndAllocations()
        {
            if (_viewport == null || _scene == null) return;

            // Studies.
            var keepStudies = new HashSet<string>();
            if (_scene.DispersionStudies != null)
            {
                foreach (var st in _scene.DispersionStudies)
                {
                    if (!st.IsVisible) continue;
                    if (_studyVisuals.ContainsKey(st.Id))
                    { keepStudies.Add(st.Id); continue; }
                    ModelVisual3D vis = null;
                    try { vis = Core.StudyAllocationRenderer.BuildStudyVisual(st, _scene); }
                    catch { vis = null; }
                    if (vis == null) continue;
                    _viewport.Children.Add(vis);
                    _studyVisuals[st.Id] = vis;
                    keepStudies.Add(st.Id);
                }
            }
            foreach (var k in _studyVisuals.Keys.Where(k => !keepStudies.Contains(k)).ToList())
            {
                _viewport.Children.Remove(_studyVisuals[k]);
                _studyVisuals.Remove(k);
            }

            // Allocations.
            var keepAllocs = new HashSet<string>();
            if (_scene.DetectorAllocations != null)
            {
                foreach (var a in _scene.DetectorAllocations)
                {
                    if (!a.IsVisible) continue;
                    if (_allocationVisuals.ContainsKey(a.Id))
                    { keepAllocs.Add(a.Id); continue; }
                    ModelVisual3D vis = null;
                    try { vis = Core.StudyAllocationRenderer.BuildAllocationVisual(a, _scene); }
                    catch { vis = null; }
                    if (vis == null) continue;
                    _viewport.Children.Add(vis);
                    _allocationVisuals[a.Id] = vis;
                    keepAllocs.Add(a.Id);
                }
            }
            foreach (var k in _allocationVisuals.Keys.Where(k => !keepAllocs.Contains(k)).ToList())
            {
                _viewport.Children.Remove(_allocationVisuals[k]);
                _allocationVisuals.Remove(k);
            }
        }

        /// <summary>Drops the cached visual for a single study (called after the user
        /// edits it — composition / threshold may have changed).</summary>
        public void InvalidateStudyVisual(string studyId)
        {
            if (string.IsNullOrEmpty(studyId)) return;
            if (_studyVisuals.TryGetValue(studyId, out var vis))
            {
                _viewport?.Children.Remove(vis);
                _studyVisuals.Remove(studyId);
            }
        }

        /// <summary>Drops the cached visual for a single allocation.</summary>
        public void InvalidateAllocationVisual(string allocId)
        {
            if (string.IsNullOrEmpty(allocId)) return;
            if (_allocationVisuals.TryGetValue(allocId, out var vis))
            {
                _viewport?.Children.Remove(vis);
                _allocationVisuals.Remove(allocId);
            }
        }

        /// <summary>Rebuilds Sun + sky-dome from <see cref="Scene3D.Environment"/> and
        /// refreshes the ground plane. Called after project load and whenever the user
        /// edits an EnvironmentSettings property.</summary>
        public void ApplyEnvironment()
        {
            if (_viewport == null) return;
            var env = _scene?.Environment;

            // Lights — remove every prior lighting visual.
            if (_defaultLightsVisual != null)
            { _viewport.Children.Remove(_defaultLightsVisual); _defaultLightsVisual = null; }
            if (_envLightsVisual != null)
            { _viewport.Children.Remove(_envLightsVisual); _envLightsVisual = null; }

            if (env != null && env.UseSunLighting)
            {
                _envLightsVisual = Core.EnvironmentRenderer.BuildLighting(env);
                if (_envLightsVisual != null) _viewport.Children.Add(_envLightsVisual);
            }
            else
            {
                _defaultLightsVisual = new DefaultLights();
                _viewport.Children.Add(_defaultLightsVisual);
            }

            // Sky dome.
            if (_skyDomeVisual != null)
            { _viewport.Children.Remove(_skyDomeVisual); _skyDomeVisual = null; }
            if (env != null)
            {
                _skyDomeVisual = Core.EnvironmentRenderer.BuildSkyDome(env, _groundSize);
                if (_skyDomeVisual != null) _viewport.Children.Add(_skyDomeVisual);
            }

            // Ground (texture may have changed).
            UpdateGroundPlane();
        }

        private void UpdateGroundPlane()
        {
            if (_viewport == null) return;

            if (_groundPlaneVisual != null)
            {
                _viewport.Children.Remove(_groundPlaneVisual);
                _groundPlaneVisual = null;
            }

            if (!_showGroundPlane) return;

            double elev = _scene?.CurrentWorkPlane?.Elevation ?? 0;
            double half = _groundSize * 0.5;

            var mesh = new System.Windows.Media.Media3D.MeshGeometry3D();
            mesh.Positions.Add(new Point3D(-half, -half, elev));
            mesh.Positions.Add(new Point3D(half, -half, elev));
            mesh.Positions.Add(new Point3D(half, half, elev));
            mesh.Positions.Add(new Point3D(-half, half, elev));
            mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(1); mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(2); mesh.TriangleIndices.Add(3);

            mesh.TextureCoordinates.Add(new System.Windows.Point(0, 0));
            mesh.TextureCoordinates.Add(new System.Windows.Point(1, 0));
            mesh.TextureCoordinates.Add(new System.Windows.Point(1, 1));
            mesh.TextureCoordinates.Add(new System.Windows.Point(0, 1));

            var env = _scene?.Environment;
            System.Windows.Media.Brush brush;
            if (env != null)
                brush = Core.EnvironmentRenderer.BuildGroundBrush(env.Ground, _groundSize, env.ShowGridOverlay);
            else
                brush = Core.EnvironmentRenderer.BuildGroundBrush(Models.GroundMaterial.Grid, _groundSize, false);

            var material = new System.Windows.Media.Media3D.DiffuseMaterial(brush);

            var geom = new System.Windows.Media.Media3D.GeometryModel3D
            {
                Geometry = mesh,
                Material = material,
                BackMaterial = material
            };

            _groundPlaneVisual = new System.Windows.Media.Media3D.ModelVisual3D { Content = geom };
            _groundPlaneVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                new Visual3DTag("GroundPlane", "ground"));
            _viewport.Children.Add(_groundPlaneVisual);

            UpdateCompassLabels(elev, half);
        }

        private void UpdateCompassLabels(double elev, double half)
        {
            foreach (var v in _compassVisuals)
                _viewport.Children.Remove(v);
            _compassVisuals.Clear();

            if (!_showGroundPlane) return;

            double arrowLen = half * 0.08;
            double arrowDiam = arrowLen * 0.15;
            double offset = half + arrowLen * 0.5;
            double textOffset = half + arrowLen * 1.8;

            var directions = new[]
            {
                ("N", new Vector3D(0, 1, 0), new Point3D(0, offset, elev), new Point3D(0, textOffset, elev),
                    System.Windows.Media.Color.FromRgb(220, 60, 60)),
                ("S", new Vector3D(0, -1, 0), new Point3D(0, -offset, elev), new Point3D(0, -textOffset, elev),
                    System.Windows.Media.Color.FromRgb(180, 180, 180)),
                ("E", new Vector3D(1, 0, 0), new Point3D(offset, 0, elev), new Point3D(textOffset, 0, elev),
                    System.Windows.Media.Color.FromRgb(180, 180, 180)),
                ("W", new Vector3D(-1, 0, 0), new Point3D(-offset, 0, elev), new Point3D(-textOffset, 0, elev),
                    System.Windows.Media.Color.FromRgb(180, 180, 180))
            };

            foreach (var (label, dir, arrowPos, textPos, color) in directions)
            {
                var arrow = new ArrowVisual3D
                {
                    Point1 = arrowPos,
                    Point2 = arrowPos + dir * arrowLen,
                    Diameter = arrowDiam,
                    HeadLength = arrowDiam * 3,
                    Fill = new System.Windows.Media.SolidColorBrush(color)
                };
                arrow.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("CompassArrow", "compass"));
                _compassVisuals.Add(arrow);
                _viewport.Children.Add(arrow);

                var text = new BillboardTextVisual3D
                {
                    Text = label,
                    Position = textPos,
                    FontSize = 14,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Foreground = new System.Windows.Media.SolidColorBrush(color),
                    Background = System.Windows.Media.Brushes.Transparent
                };
                text.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("CompassLabel", "compass"));
                _compassVisuals.Add(text);
                _viewport.Children.Add(text);
            }
        }

        private void UpdateGridElevation()
        {
            if (_gridVisual == null || _scene?.CurrentWorkPlane == null) return;
            double elev = _scene.CurrentWorkPlane.Elevation;
            _gridVisual.Transform = new System.Windows.Media.Media3D.TranslateTransform3D(0, 0, elev);
        }

        private void UpdateViewport()
        {
            // Decorations
            var existingDecos = _viewport.Children.OfType<System.Windows.Media.Media3D.ModelVisual3D>()
                .Where(m => m.GetValue(System.Windows.FrameworkElement.TagProperty) is Visual3DTag tag &&
                           tag.Category == "Decoration")
                .ToList();

            foreach (var model in existingDecos)
            {
                _viewport.Children.Remove(model);
            }

            foreach (var deco in _scene.Decorations)
            {
                if (deco.Model3D != null)
                {
                    if (deco.UseCustomMaterial)
                    {
                        var mat = Core.MaterialHelper.CreateMaterial(
                            deco.MaterialType, deco.MaterialColor, deco.SpecularPower, deco.Opacity);
                        Core.MaterialHelper.ApplyToModel(deco.Model3D, mat, deco.MaterialType);
                    }

                    var visual = new System.Windows.Media.Media3D.ModelVisual3D
                    {
                        Content = deco.Model3D,
                        Transform = deco.GetWorldTransform()
                    };
                    visual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("Decoration", deco.Id));
                    _viewport.Children.Add(visual);
                }
            }

            // Release sources
            var existingSources = _viewport.Children.OfType<System.Windows.Media.Media3D.ModelVisual3D>()
                .Where(m => m.GetValue(System.Windows.FrameworkElement.TagProperty) is Visual3DTag tag &&
                           tag.Category == "ReleaseSource")
                .ToList();

            foreach (var model in existingSources)
            {
                _viewport.Children.Remove(model);
            }

            // Always prefer TopLevelSources for UI rendering — those are the authoritative
            // user-edited sources. Running a simulation pushes a transient DispersionScenario
            // onto the scene with a CLONE of the source; if we picked that up here, the
            // marker would point at the snapshot taken at sim configuration time, not the
            // current source position. Also: clones don't carry IsVisible / other UI-only
            // flags, so showing them caused the marker to vanish after a run.
            var sourcesToRender = new List<ReleaseSource3D>();
            if (_scene.TopLevelSources != null && _scene.TopLevelSources.Count > 0)
            {
                sourcesToRender.AddRange(_scene.TopLevelSources);
            }
            else if (_scene.DispersionScenario != null)
            {
                // Legacy scenes with no TopLevelSources still keep sources on the scenario.
                sourcesToRender.AddRange(_scene.DispersionScenario.Sources);
            }

            System.Diagnostics.Debug.WriteLine($"[UpdateViewport] sourcesToRender.Count={sourcesToRender.Count} TopLevel={_scene.TopLevelSources?.Count ?? -1} DispScenarioSrcs={_scene.DispersionScenario?.Sources?.Count ?? -1}");
            if (sourcesToRender.Count > 0)
            {
                foreach (var source in sourcesToRender)
                {
                    if (!source.IsVisible) continue;
                    try
                    {
                    var pos = source.EffectivePosition;
                    var dir = source.ReleaseDirection;

                    var group = new System.Windows.Media.Media3D.Model3DGroup();

                    var sphereMesh = new System.Windows.Media.Media3D.MeshGeometry3D();
                    int slices = 8, stacks = 6;
                    double r = 1.5;
                    sphereMesh.Positions.Add(new Point3D(0, 0, r));
                    for (int s = 1; s < stacks; s++)
                    {
                        double phi = Math.PI * s / stacks;
                        double sinP = Math.Sin(phi);
                        double cosP = Math.Cos(phi);
                        for (int sl = 0; sl < slices; sl++)
                        {
                            double theta = 2 * Math.PI * sl / slices;
                            sphereMesh.Positions.Add(new Point3D(
                                r * sinP * Math.Cos(theta),
                                r * sinP * Math.Sin(theta),
                                r * cosP));
                        }
                    }
                    sphereMesh.Positions.Add(new Point3D(0, 0, -r));
                    int bottom = sphereMesh.Positions.Count - 1;
                    for (int sl = 0; sl < slices; sl++)
                    {
                        int next = (sl + 1) % slices;
                        sphereMesh.TriangleIndices.Add(0);
                        sphereMesh.TriangleIndices.Add(1 + sl);
                        sphereMesh.TriangleIndices.Add(1 + next);
                    }
                    for (int s = 0; s < stacks - 2; s++)
                    {
                        int row2 = 1 + s * slices;
                        int nextRow = 1 + (s + 1) * slices;
                        for (int sl = 0; sl < slices; sl++)
                        {
                            int next = (sl + 1) % slices;
                            sphereMesh.TriangleIndices.Add(row2 + sl);
                            sphereMesh.TriangleIndices.Add(nextRow + sl);
                            sphereMesh.TriangleIndices.Add(nextRow + next);
                            sphereMesh.TriangleIndices.Add(row2 + sl);
                            sphereMesh.TriangleIndices.Add(nextRow + next);
                            sphereMesh.TriangleIndices.Add(row2 + next);
                        }
                    }
                    int lastRow2 = 1 + (stacks - 2) * slices;
                    for (int sl = 0; sl < slices; sl++)
                    {
                        int next = (sl + 1) % slices;
                        sphereMesh.TriangleIndices.Add(bottom);
                        sphereMesh.TriangleIndices.Add(lastRow2 + next);
                        sphereMesh.TriangleIndices.Add(lastRow2 + sl);
                    }

                    var orangeBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(255, 255, 80, 0));
                    orangeBrush.Freeze();
                    // MaterialGroup with both diffuse and emissive so the sphere is bright
                    // even when partially occluded or unlit (e.g. inside an imported mesh).
                    var orangeMat = new System.Windows.Media.Media3D.MaterialGroup();
                    orangeMat.Children.Add(new System.Windows.Media.Media3D.DiffuseMaterial(orangeBrush));
                    orangeMat.Children.Add(new System.Windows.Media.Media3D.EmissiveMaterial(orangeBrush));

                    group.Children.Add(new System.Windows.Media.Media3D.GeometryModel3D
                    {
                        Geometry = sphereMesh,
                        Material = orangeMat,
                        BackMaterial = orangeMat,
                        Transform = new System.Windows.Media.Media3D.TranslateTransform3D(pos.X, pos.Y, pos.Z)
                    });

                    double arrowLen = 6.0;
                    double shaftRadius = 0.3;
                    double headRadius = 0.8;
                    double headLen = 1.5;
                    double shaftLen = arrowLen - headLen;

                    var arrowMesh = BuildDirectionArrow(pos, dir, arrowLen, shaftRadius, headRadius, headLen, shaftLen);
                    var redBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(255, 255, 30, 30));
                    redBrush.Freeze();
                    var redMat = new System.Windows.Media.Media3D.MaterialGroup();
                    redMat.Children.Add(new System.Windows.Media.Media3D.DiffuseMaterial(redBrush));
                    redMat.Children.Add(new System.Windows.Media.Media3D.EmissiveMaterial(redBrush));

                    group.Children.Add(new System.Windows.Media.Media3D.GeometryModel3D
                    {
                        Geometry = arrowMesh,
                        Material = redMat,
                        BackMaterial = redMat
                    });

                    // Vertical locator pole — a thin tall cylinder + marker sphere above the
                    // tallest decoration. Without it, sources placed inside equipment AABBs
                    // (very common — the leak IS at the equipment) are occluded by the mesh
                    // and become invisible. The pole is always visible above the scene.
                    double sceneTopZ = 0;
                    foreach (var d in _scene.Decorations)
                    {
                        if (d.BoundingBox != null && d.BoundingBox.Max.Z > sceneTopZ)
                            sceneTopZ = d.BoundingBox.Max.Z;
                    }
                    double poleTopZ = Math.Max(pos.Z + 15, sceneTopZ + 8);
                    var poleMesh = BuildCylinder(
                        new Point3D(pos.X, pos.Y, pos.Z),
                        new Point3D(pos.X, pos.Y, poleTopZ),
                        0.15, 12);
                    group.Children.Add(new System.Windows.Media.Media3D.GeometryModel3D
                    {
                        Geometry = poleMesh,
                        Material = orangeMat,
                        BackMaterial = orangeMat
                    });
                    // Marker sphere at top of pole.
                    var topSphereMesh = BuildSphere(2.0, 12, 8);
                    group.Children.Add(new System.Windows.Media.Media3D.GeometryModel3D
                    {
                        Geometry = topSphereMesh,
                        Material = orangeMat,
                        BackMaterial = orangeMat,
                        Transform = new System.Windows.Media.Media3D.TranslateTransform3D(pos.X, pos.Y, poleTopZ)
                    });

                    var visual = new System.Windows.Media.Media3D.ModelVisual3D { Content = group };
                    visual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("ReleaseSource", source.Id));
                    _viewport.Children.Add(visual);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UpdateViewport] EXCEPTION rendering src {source.Id}: {ex}");
                    }
                }
            }

            // Wind rose (below)
            var existingWindRose = _viewport.Children.OfType<System.Windows.Media.Media3D.ModelVisual3D>()
                .Where(m => m.GetValue(System.Windows.FrameworkElement.TagProperty) is Visual3DTag tag &&
                           tag.Category == "WindRose")
                .ToList();
            foreach (var wr in existingWindRose)
                _viewport.Children.Remove(wr);

            if (_scene.WindRose != null && _scene.WindRose.ShowIn3D && _scene.WindRose.Bins.Count > 0)
            {
                var wrVisual = Core.WindRoseRenderer.Generate(_scene.WindRose);
                wrVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("WindRose", "windrose"));
                _viewport.Children.Add(wrVisual);
            }

            // Monitor points
            var existingMonitors = _viewport.Children.OfType<System.Windows.Media.Media3D.ModelVisual3D>()
                .Where(m => m.GetValue(System.Windows.FrameworkElement.TagProperty) is Visual3DTag tag &&
                           tag.Category == "MonitorPoint")
                .ToList();

            foreach (var model in existingMonitors)
            {
                _viewport.Children.Remove(model);
            }

            foreach (var monitor in _scene.MonitorPoints)
            {
                if (!monitor.Visible) continue;

                var diamondMesh = new System.Windows.Media.Media3D.MeshGeometry3D();
                double h = 1.2;
                double w = 0.6;
                diamondMesh.Positions.Add(new Point3D(0, 0, h));
                diamondMesh.Positions.Add(new Point3D(w, 0, 0));
                diamondMesh.Positions.Add(new Point3D(0, w, 0));
                diamondMesh.Positions.Add(new Point3D(-w, 0, 0));
                diamondMesh.Positions.Add(new Point3D(0, -w, 0));
                diamondMesh.Positions.Add(new Point3D(0, 0, -h));

                int[] tris = { 0,1,2, 0,2,3, 0,3,4, 0,4,1, 5,2,1, 5,3,2, 5,4,3, 5,1,4 };
                foreach (int idx in tris)
                    diamondMesh.TriangleIndices.Add(idx);

                var brush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(220, 0, 180, 255));
                brush.Freeze();
                var material = new System.Windows.Media.Media3D.DiffuseMaterial(brush);

                var pos = monitor.Position;
                var transform = new System.Windows.Media.Media3D.TranslateTransform3D(pos.X, pos.Y, pos.Z);

                var geom = new System.Windows.Media.Media3D.GeometryModel3D
                {
                    Geometry = diamondMesh,
                    Material = material,
                    BackMaterial = material,
                    Transform = transform
                };

                var mVisual = new System.Windows.Media.Media3D.ModelVisual3D { Content = geom };
                mVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("MonitorPoint", monitor.Id));
                _viewport.Children.Add(mVisual);
            }

            // Fire sources (static markers)
            var existingFires = _viewport.Children.OfType<System.Windows.Media.Media3D.ModelVisual3D>()
                .Where(m => m.GetValue(System.Windows.FrameworkElement.TagProperty) is Visual3DTag tag &&
                           tag.Category == "FireSource")
                .ToList();
            foreach (var f in existingFires) _viewport.Children.Remove(f);

            if (_scene.FireScenario != null)
            {
                foreach (var fire in _scene.FireScenario.Sources)
                {
                    var coneMesh = new System.Windows.Media.Media3D.MeshGeometry3D();
                    int segs = 8;
                    double coneR = 0.8, coneH = 2.0;
                    for (int i = 0; i < segs; i++)
                    {
                        double a = 2 * Math.PI * i / segs;
                        coneMesh.Positions.Add(new Point3D(coneR * Math.Cos(a), coneR * Math.Sin(a), 0));
                    }
                    coneMesh.Positions.Add(new Point3D(0, 0, coneH));
                    int tipI = coneMesh.Positions.Count - 1;
                    for (int i = 0; i < segs; i++)
                    {
                        coneMesh.TriangleIndices.Add(i);
                        coneMesh.TriangleIndices.Add((i + 1) % segs);
                        coneMesh.TriangleIndices.Add(tipI);
                    }

                    var fireBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(220, 255, 100, 0));
                    fireBrush.Freeze();
                    var fireMat = new System.Windows.Media.Media3D.DiffuseMaterial(fireBrush);

                    var fireGeom = new System.Windows.Media.Media3D.GeometryModel3D
                    {
                        Geometry = coneMesh, Material = fireMat, BackMaterial = fireMat,
                        Transform = new System.Windows.Media.Media3D.TranslateTransform3D(
                            fire.Position.X, fire.Position.Y, fire.Position.Z)
                    };

                    var fVisual = new System.Windows.Media.Media3D.ModelVisual3D { Content = fireGeom };
                    fVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("FireSource", fire.Id));
                    _viewport.Children.Add(fVisual);
                }
            }

            // Gas detectors
            var existingDetectors = _viewport.Children.OfType<System.Windows.Media.Media3D.ModelVisual3D>()
                .Where(m => m.GetValue(System.Windows.FrameworkElement.TagProperty) is Visual3DTag tag &&
                           tag.Category == "GasDetector")
                .ToList();
            foreach (var d in existingDetectors) _viewport.Children.Remove(d);

            foreach (var det in _scene.GasDetectors)
            {
                if (!det.Visible) continue;

                var cubeMesh = new System.Windows.Media.Media3D.MeshGeometry3D();
                double s = 0.5;
                Point3D[] cubeVerts =
                {
                    new Point3D(-s,-s,-s), new Point3D(s,-s,-s), new Point3D(s,s,-s), new Point3D(-s,s,-s),
                    new Point3D(-s,-s,s), new Point3D(s,-s,s), new Point3D(s,s,s), new Point3D(-s,s,s)
                };
                foreach (var v in cubeVerts) cubeMesh.Positions.Add(v);
                int[] cubeIdx = { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 2,3,7, 2,7,6, 0,4,7, 0,7,3, 1,2,6, 1,6,5 };
                foreach (int ci in cubeIdx) cubeMesh.TriangleIndices.Add(ci);

                var detColor = det.Detected
                    ? System.Windows.Media.Color.FromArgb(220, 255, 0, 0)
                    : System.Windows.Media.Color.FromArgb(220, 0, 200, 0);
                var detBrush = new System.Windows.Media.SolidColorBrush(detColor);
                detBrush.Freeze();
                var detMat = new System.Windows.Media.Media3D.DiffuseMaterial(detBrush);

                var detGeom = new System.Windows.Media.Media3D.GeometryModel3D
                {
                    Geometry = cubeMesh, Material = detMat, BackMaterial = detMat,
                    Transform = new System.Windows.Media.Media3D.TranslateTransform3D(
                        det.Position.X, det.Position.Y, det.Position.Z)
                };

                var dVisual = new System.Windows.Media.Media3D.ModelVisual3D { Content = detGeom };
                dVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("GasDetector", det.Id));
                _viewport.Children.Add(dVisual);
            }
        }

        private ReleaseSource3D FindSourceById(string id)
        {
            if (_scene.DispersionScenario == null || id == null) return null;
            foreach (var src in _scene.DispersionScenario.Sources)
                if (src.Id == id) return src;
            return null;
        }

        private static MeshGeometry3D BuildCylinder(Point3D a, Point3D b, double radius, int segments)
        {
            var mesh = new MeshGeometry3D();
            var axis = b - a;
            if (axis.LengthSquared < 1e-10) return mesh;
            axis.Normalize();
            var up = Math.Abs(axis.Z) < 0.95 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            var right = Vector3D.CrossProduct(axis, up); right.Normalize();
            up = Vector3D.CrossProduct(right, axis);
            for (int i = 0; i < segments; i++)
            {
                double t1 = 2 * Math.PI * i / segments;
                double t2 = 2 * Math.PI * ((i + 1) % segments) / segments;
                var r1 = right * Math.Cos(t1) + up * Math.Sin(t1);
                var r2 = right * Math.Cos(t2) + up * Math.Sin(t2);
                var p0 = a + r1 * radius;
                var p1 = a + r2 * radius;
                var p2 = b + r1 * radius;
                var p3 = b + r2 * radius;
                int bIdx = mesh.Positions.Count;
                mesh.Positions.Add(p0); mesh.Positions.Add(p1);
                mesh.Positions.Add(p2); mesh.Positions.Add(p3);
                mesh.TriangleIndices.Add(bIdx); mesh.TriangleIndices.Add(bIdx + 1); mesh.TriangleIndices.Add(bIdx + 2);
                mesh.TriangleIndices.Add(bIdx + 1); mesh.TriangleIndices.Add(bIdx + 3); mesh.TriangleIndices.Add(bIdx + 2);
            }
            return mesh;
        }

        private static MeshGeometry3D BuildSphere(double r, int slices, int stacks)
        {
            var mesh = new MeshGeometry3D();
            mesh.Positions.Add(new Point3D(0, 0, r));
            for (int s = 1; s < stacks; s++)
            {
                double phi = Math.PI * s / stacks;
                double sinP = Math.Sin(phi);
                double cosP = Math.Cos(phi);
                for (int sl = 0; sl < slices; sl++)
                {
                    double theta = 2 * Math.PI * sl / slices;
                    mesh.Positions.Add(new Point3D(r * sinP * Math.Cos(theta), r * sinP * Math.Sin(theta), r * cosP));
                }
            }
            mesh.Positions.Add(new Point3D(0, 0, -r));
            int bottom = mesh.Positions.Count - 1;
            for (int sl = 0; sl < slices; sl++)
            {
                int next = (sl + 1) % slices;
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(1 + sl);
                mesh.TriangleIndices.Add(1 + next);
            }
            for (int s = 0; s < stacks - 2; s++)
            {
                int row2 = 1 + s * slices;
                int nextRow = 1 + (s + 1) * slices;
                for (int sl = 0; sl < slices; sl++)
                {
                    int next = (sl + 1) % slices;
                    mesh.TriangleIndices.Add(row2 + sl);
                    mesh.TriangleIndices.Add(nextRow + sl);
                    mesh.TriangleIndices.Add(nextRow + next);
                    mesh.TriangleIndices.Add(row2 + sl);
                    mesh.TriangleIndices.Add(nextRow + next);
                    mesh.TriangleIndices.Add(row2 + next);
                }
            }
            int lastRow2 = 1 + (stacks - 2) * slices;
            for (int sl = 0; sl < slices; sl++)
            {
                int next = (sl + 1) % slices;
                mesh.TriangleIndices.Add(bottom);
                mesh.TriangleIndices.Add(lastRow2 + next);
                mesh.TriangleIndices.Add(lastRow2 + sl);
            }
            return mesh;
        }

        private static MeshGeometry3D BuildDirectionArrow(
            Point3D origin, Vector3D direction, double length,
            double shaftRadius, double headRadius, double headLength, double shaftLength)
        {
            var mesh = new MeshGeometry3D();
            var dir = direction;
            if (dir.LengthSquared < 1e-10) dir = new Vector3D(0, 0, 1);
            dir.Normalize();

            var up = Math.Abs(dir.Z) < 0.95 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            var right = Vector3D.CrossProduct(dir, up);
            right.Normalize();
            up = Vector3D.CrossProduct(right, dir);

            int segs = 8;
            var shaftEnd = origin + dir * shaftLength;
            var tip = origin + dir * length;

            for (int i = 0; i < segs; i++)
            {
                double a1 = 2 * Math.PI * i / segs;
                double a2 = 2 * Math.PI * ((i + 1) % segs) / segs;

                var r1 = right * Math.Cos(a1) + up * Math.Sin(a1);
                var r2 = right * Math.Cos(a2) + up * Math.Sin(a2);

                var s0 = origin + r1 * shaftRadius;
                var s1 = origin + r2 * shaftRadius;
                var s2 = shaftEnd + r1 * shaftRadius;
                var s3 = shaftEnd + r2 * shaftRadius;

                int b = mesh.Positions.Count;
                mesh.Positions.Add(s0); mesh.Positions.Add(s1);
                mesh.Positions.Add(s2); mesh.Positions.Add(s3);
                mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b + 2);
                mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b + 3); mesh.TriangleIndices.Add(b + 2);

                var h0 = shaftEnd + r1 * headRadius;
                var h1 = shaftEnd + r2 * headRadius;

                b = mesh.Positions.Count;
                mesh.Positions.Add(h0); mesh.Positions.Add(h1); mesh.Positions.Add(tip);
                mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b + 2);
            }

            return mesh;
        }

        #endregion
    }
}
