using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using DisperSim3D.Models;
using DisperSim3D.Core;

namespace DisperSim3D.Controls
{
    /// <summary>
    /// WinForms UserControl que hospeda o editor 3D de flowsheets
    /// </summary>
    public partial class FlowsheetEditor3DControl : UserControl
    {
        #region Fields

        private Scene3D _flowsheet;
        private readonly ModelLoader _modelLoader;

        private HelixViewport3D _viewport;
        private ElementHost _wpfHost;

        private EditMode _currentEditMode = EditMode.Select;
        private Models.CameraMode _currentCameraMode = Models.CameraMode.Isometric;
        private bool _snapToGrid = true;
        private double _gridSpacing = 5.0;
        private LinesVisual3D _selectionHighlight;

        private bool _isDragging;
        private Point3D _dragStartPosition;
        private Point3D _dragUnitOriginalPosition;

        private Decoration3D _selectedDecoration;
        private ReleaseSource3D _selectedSource;
        private bool _isDraggingDecoration;

        private GaussianPuffEngine _dispersionEngine;
        private IConcentrationField _steadyStateEngine;
        private DispersionRenderer _dispersionRenderer;
        private System.Windows.Threading.DispatcherTimer _animationTimer;
        private DispersionSimulationState _dispersionState = DispersionSimulationState.Stopped;
        private double _animationSpeedFactor = 1.0;
        private int _frameCount;
        private System.Windows.Media.Media3D.ModelVisual3D _isosurfaceVisual;
        private System.Windows.Media.Media3D.ModelVisual3D _particleVisual;
        private System.Windows.Media.Media3D.ModelVisual3D _windArrowVisual;

        private MonitorPoint3D _pendingMonitorTemplate;
        private bool _showVectorField;
        private FireSource _pendingFireTemplate;
        private GasDetector3D _pendingDetectorTemplate;
        private Dictionary<string, double[]> _hpLeakProfiles = new Dictionary<string, double[]>();

        private GridLinesVisual3D _gridVisual;
        private System.Windows.Media.Media3D.ModelVisual3D _groundPlaneVisual;
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

        #endregion

        #region Properties

        /// <summary>
        /// O flowsheet 3D atual
        /// </summary>
        [Browsable(false)]
        public Scene3D Flowsheet
        {
            get => _flowsheet;
            set
            {
                _flowsheet = value;
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
                if (_flowsheet != null)
                {
                    _flowsheet.GridSpacing = value;
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

        #endregion

        #region Constructor

        public FlowsheetEditor3DControl()
        {
            InitializeComponent();

            // Inicializar componentes
            _flowsheet = new Scene3D();
            _modelLoader = new ModelLoader();

            // Criar viewport WPF
            InitializeWpfViewport();

            // Configurar eventos
            this.Resize += FlowsheetEditor3DControl_Resize;
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

            // Adicionar iluminação
            _viewport.Children.Add(new DefaultLights());

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

            // Hospedar no ElementHost
            _wpfHost.Child = _viewport;

            // Adicionar ao UserControl
            this.Controls.Add(_wpfHost);
        }

        #endregion

        #region Public Methods




        /// <summary>
        /// Limpa o flowsheet inteiro
        /// </summary>
        /// <summary>
        /// Starts the dispersion simulation animation
        /// </summary>
        public void StartDispersion()
        {
            var scenario = _flowsheet.DispersionScenario;
            if (scenario == null || scenario.Sources.Count == 0) return;

            if (scenario.SolverType == CfdSolverType.GaussianPlume)
            {
                StartSteadyStateDispersion();
                return;
            }

            _cfdPlaybackActive = false;
            _dispersionEngine = new GaussianPuffEngine();
            _dispersionEngine.Initialize(scenario);

            _dispersionRenderer = new DispersionRenderer();
            _dispersionRenderer.Initialize(scenario);
            _dispersionRenderer.ComputeOccupancyGrid(_flowsheet);

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

            if (_flowsheet.GasDetectors.Count > 0)
                DetectorEvaluator.Reset(_flowsheet.GasDetectors);

            _frameCount = 0;
            _dispersionState = DispersionSimulationState.Running;

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
        public void StopDispersion()
        {
            _dispersionState = DispersionSimulationState.Stopped;
            if (_animationTimer != null)
                _animationTimer.Stop();

            if (_dispersionEngine != null)
                _dispersionEngine.Reset();

            RemoveDispersionVisuals();
            _steadyStateEngine = null;
        }

        /// <summary>
        /// Computes and displays the steady-state Gaussian plume concentration field.
        /// Renders isosurfaces and contour planes in a single pass with no animation.
        /// </summary>
        public void StartSteadyStateDispersion()
        {
            var scenario = _flowsheet.DispersionScenario;
            if (scenario == null || scenario.Sources.Count == 0) return;

            StopDispersion();

            var plume = new GaussianPlumeEngine();
            plume.Initialize(scenario);
            _steadyStateEngine = plume;

            _dispersionRenderer = new DispersionRenderer();
            _dispersionRenderer.Initialize(scenario);
            _dispersionRenderer.ComputeOccupancyGrid(_flowsheet);

            RemoveDispersionVisuals();

            if (scenario.Thresholds.Count > 0)
            {
                _isosurfaceVisual = _dispersionRenderer.GenerateIsosurfaces(
                    _steadyStateEngine, scenario.Thresholds);
                _isosurfaceVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("DispersionIsosurface", "iso"));
                _viewport.Children.Add(_isosurfaceVisual);
            }

            if (scenario.ContourPlanes.Count > 0)
            {
                double maxC = _dispersionRenderer.GetMaxConcentration();
                double dom = _dispersionRenderer.DomainSize;
                foreach (var cp in scenario.ContourPlanes)
                {
                    if (!cp.Visible) continue;
                    var cpVisual = _dispersionRenderer.GenerateContourPlane(
                        _steadyStateEngine, cp, -dom, dom, maxC);
                    cpVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("ContourPlane", "contour"));
                    _viewport.Children.Add(cpVisual);
                }
            }

            if (_flowsheet.GasDetectors.Count > 0)
            {
                DetectorEvaluator.Reset(_flowsheet.GasDetectors);
                DetectorEvaluator.EvaluateStep(
                    _flowsheet.GasDetectors, _steadyStateEngine, 0);
            }

            foreach (var monitor in _flowsheet.MonitorPoints)
            {
                if (!monitor.Visible) continue;
                double c = _steadyStateEngine.EvaluateConcentration(
                    monitor.Position.X, monitor.Position.Y, monitor.Position.Z);
                monitor.TimeSeries.Add(new MonitorSample { TimeS = 0, Concentration = c });
            }

            _dispersionState = DispersionSimulationState.Running;
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
                var scenario = _flowsheet.DispersionScenario;
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
        public void SeekCfdPlayback(double fraction)
        {
            if (!_cfdPlaybackActive || _cfdResult == null || _cfdResult.TimeSteps.Count == 0) return;

            double firstTime = _cfdResult.TimeSteps[0];
            double lastTime = _cfdResult.TimeSteps[_cfdResult.TimeSteps.Count - 1];
            double targetTime = firstTime + fraction * (lastTime - firstTime);

            _cfdPlaybackTimeS = targetTime;
            _cfdPlaybackIndex = 0;
            while (_cfdPlaybackIndex < _cfdResult.TimeSteps.Count - 1 &&
                   _cfdResult.TimeSteps[_cfdPlaybackIndex + 1] <= targetTime)
                _cfdPlaybackIndex++;
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
                var sc = _flowsheet?.DispersionScenario;
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
                if (_cfdPlaybackActive && _cfdResult != null && _cfdPlaybackIndex < _cfdResult.TimeSteps.Count)
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

        public event EventHandler<OpenFoamProgress> CfdProgressUpdated;
        public event EventHandler<CfdSimulationEntry> CfdSolveCompleted;

        public void StartCfdSolve(CfdConfiguration config)
        {
            var scenario = _flowsheet.DispersionScenario;
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
                    string solverLabel = isSteady ? "CFD Steady" : "CFD (OpenFOAM)";
                    string baseName = string.IsNullOrEmpty(scenario.Name) ? solverLabel : scenario.Name;
                    var entry = new CfdSimulationEntry
                    {
                        Name = baseName + " #" + (_flowsheet.CfdSimulations.Count + 1),
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

                    _flowsheet.CfdSimulations.Add(entry);
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
                scenario.SolverType == CfdSolverType.ScalarSimpleFoam)
                _cfdRunner.RunSteadyAsync(scenario, config, scenario.SolverType);
            else
                _cfdRunner.RunAsync(scenario, config);
        }

        public void RunGaussianPuffAsync()
        {
            var scenario = _flowsheet.DispersionScenario;
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
                foreach (var deco in _flowsheet.Decorations)
                    if (deco.BoundingBox != null) obstacles.Add(deco.BoundingBox);
            }

            worker.DoWork += (s, e) =>
            {
                var engine = new GaussianPuffEngine();
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
                           + " #" + (_flowsheet.CfdSimulations.Count + 1),
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

                _flowsheet.CfdSimulations.Add(entry);
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

            var scenario = _flowsheet.DispersionScenario;
            _dispersionRenderer = new DispersionRenderer();
            _dispersionRenderer.Initialize(scenario);
            _dispersionRenderer.SetDomainBounds(
                _cfdResult.DomainXMin, _cfdResult.DomainXMax,
                _cfdResult.DomainYMin, _cfdResult.DomainYMax,
                _cfdResult.DomainZMax);

            if (scenario.Thresholds.Count == 0)
            {
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
                    _dispersionRenderer.SetScalarFieldDirect(lastField);
                double maxC = _dispersionRenderer.GetMaxConcentration();
                System.Diagnostics.Debug.WriteLine("maxC = " + maxC);
                if (maxC > 1e-20)
                {
                    scenario.Thresholds.Add(new DispersionThreshold
                    {
                        Name = "High (50%)",
                        ConcentrationValue = maxC * 0.5,
                        Color = System.Windows.Media.Colors.Red,
                        Opacity = 0.4,
                        Visible = true
                    });
                    scenario.Thresholds.Add(new DispersionThreshold
                    {
                        Name = "Medium (10%)",
                        ConcentrationValue = maxC * 0.1,
                        Color = System.Windows.Media.Colors.Yellow,
                        Opacity = 0.25,
                        Visible = true
                    });
                    scenario.Thresholds.Add(new DispersionThreshold
                    {
                        Name = "Low (1%)",
                        ConcentrationValue = maxC * 0.01,
                        Color = System.Windows.Media.Colors.LightBlue,
                        Opacity = 0.15,
                        Visible = true
                    });
                }
            }

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
            get => _flowsheet.CurrentWorkPlane?.Elevation ?? 0;
            set
            {
                if (_flowsheet.CurrentWorkPlane != null)
                {
                    _flowsheet.CurrentWorkPlane.Elevation = value;
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
            _flowsheet.CameraPresets.Add(preset);
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
            if (_flowsheet.DispersionScenario == null)
            {
                _flowsheet.DispersionScenario = new DispersionScenario();
            }

            var source = new ReleaseSource3D
            {
                Position = _snapToGrid ? position.SnapToGrid(_gridSpacing) : position,
                Gas = template?.Gas ?? GasProperties.CreateMethane(),
                Name = template?.Name ?? "Source",
                ReleaseRateKgPerS = template?.ReleaseRateKgPerS ?? 0.5,
                ReleaseDurationS = template?.ReleaseDurationS ?? 60,
                PuffIntervalS = template?.PuffIntervalS ?? 1.0,
                ReleaseHeightOffset = template?.ReleaseHeightOffset ?? 2.0
            };
            _flowsheet.DispersionScenario.Sources.Add(source);
            UpdateViewport();
            return source;
        }

        public MonitorPoint3D AddMonitorPoint(Point3D position, MonitorPoint3D template = null)
        {
            var monitor = new MonitorPoint3D
            {
                Position = _snapToGrid ? position.SnapToGrid(_gridSpacing) : position,
                Name = template?.Name ?? "Monitor" + (_flowsheet.MonitorPoints.Count + 1)
            };
            _flowsheet.MonitorPoints.Add(monitor);
            UpdateViewport();
            return monitor;
        }

        public void RemoveMonitorPoint(MonitorPoint3D monitor)
        {
            _flowsheet.MonitorPoints.Remove(monitor);
            UpdateViewport();
        }

        public void ExportMonitorDataToCsv(string filePath)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();

            var monitors = _flowsheet.MonitorPoints;
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

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            if (_dispersionState != DispersionSimulationState.Running)
                return;

            if (_cfdPlaybackActive)
            {
                AnimationTimer_CfdTick();
                return;
            }

            if (_dispersionEngine == null)
                return;

            var scenario = _flowsheet.DispersionScenario;
            double newTime = _dispersionEngine.CurrentTimeS + scenario.TimeStepS * _animationSpeedFactor;

            bool isLastStep = newTime >= scenario.SimulationDurationS;
            if (isLastStep)
                newTime = scenario.SimulationDurationS;

            if (scenario.TransientWind != null && scenario.TransientWind.Enabled && scenario.TransientWind.Entries.Count > 0)
            {
                var windEntry = scenario.TransientWind.GetEntryAtTime(newTime);
                if (windEntry != null)
                {
                    _dispersionEngine.UpdateWind(windEntry.WindVector, windEntry.StabilityClass);
                }
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

            _dispersionEngine.StepTo(newTime);
            _frameCount++;

            foreach (var monitor in _flowsheet.MonitorPoints)
            {
                if (!monitor.Visible) continue;
                double c = 0;

                switch (monitor.Type)
                {
                    case Models.MonitorType.Point:
                        c = _dispersionEngine.EvaluateConcentration(
                            monitor.Position.X, monitor.Position.Y, monitor.Position.Z);
                        break;

                    case Models.MonitorType.Line:
                        var linePts = monitor.GetLineSamplePoints();
                        double lineSum = 0, lineMin = double.MaxValue, lineMax = 0;
                        foreach (var pt in linePts)
                        {
                            double v = _dispersionEngine.EvaluateConcentration(pt.X, pt.Y, pt.Z);
                            lineSum += v;
                            if (v < lineMin) lineMin = v;
                            if (v > lineMax) lineMax = v;
                        }
                        c = linePts.Count > 0 ? lineSum / linePts.Count : 0;
                        monitor.LastMinConcentration = lineMin == double.MaxValue ? 0 : lineMin;
                        monitor.LastMaxConcentration = lineMax;
                        break;

                    case Models.MonitorType.Region:
                        var regPts = monitor.GetRegionSamplePoints();
                        double regSum = 0, regMin = double.MaxValue, regMax = 0;
                        int aboveThreshold = 0;
                        foreach (var pt in regPts)
                        {
                            double v = _dispersionEngine.EvaluateConcentration(pt.X, pt.Y, pt.Z);
                            regSum += v;
                            if (v < regMin) regMin = v;
                            if (v > regMax) regMax = v;
                            if (v > 1e-6) aboveThreshold++;
                        }
                        c = regPts.Count > 0 ? regSum / regPts.Count : 0;
                        monitor.LastMinConcentration = regMin == double.MaxValue ? 0 : regMin;
                        monitor.LastMaxConcentration = regMax;
                        double cellVol = monitor.RegionSize.X * monitor.RegionSize.Y * monitor.RegionSize.Z
                            / Math.Max(1, regPts.Count);
                        monitor.LastGasVolume = aboveThreshold * cellVol;
                        break;
                }

                monitor.TimeSeries.Add(new Models.MonitorSample
                {
                    TimeS = _dispersionEngine.CurrentTimeS,
                    Concentration = c
                });
            }

            MonitorDataUpdated?.Invoke(this, EventArgs.Empty);

            if (_flowsheet.GasDetectors.Count > 0)
            {
                DetectorEvaluator.EvaluateStep(
                    _flowsheet.GasDetectors, _dispersionEngine, _dispersionEngine.CurrentTimeS);
            }

            RemoveDispersionVisuals();

            // Isosurfaces every 2nd frame for performance
            if (_frameCount % 2 == 0 && scenario.Thresholds.Count > 0)
            {
                _isosurfaceVisual = _dispersionRenderer.GenerateIsosurfaces(
                    _dispersionEngine, scenario.Thresholds);
                _isosurfaceVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("DispersionIsosurface", "iso"));
                _viewport.Children.Add(_isosurfaceVisual);
            }
            else if (_isosurfaceVisual != null)
            {
                _viewport.Children.Add(_isosurfaceVisual);
            }

            // Particles every frame
            _particleVisual = _dispersionRenderer.GenerateParticleCloud(_dispersionEngine);
            _particleVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                new Visual3DTag("DispersionParticles", "particles"));
            _viewport.Children.Add(_particleVisual);

            // Contour planes every 4th frame for performance
            if (_frameCount % 4 == 0 && scenario.ContourPlanes.Count > 0)
            {
                double maxC = _dispersionRenderer.GetMaxConcentration();
                double dom = _dispersionRenderer.DomainSize;
                foreach (var cp in scenario.ContourPlanes)
                {
                    if (!cp.Visible) continue;
                    var cpVisual = _dispersionRenderer.GenerateContourPlane(
                        _dispersionEngine, cp, -dom, dom, maxC);
                    cpVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("ContourPlane", "contour"));
                    _viewport.Children.Add(cpVisual);
                }
            }

            // Vector field every 4th frame
            if (_frameCount % 4 == 0 && _showVectorField)
            {
                double maxC = _dispersionRenderer.GetMaxConcentration();
                var windVec = scenario.Meteo.WindVector;
                var vfVisual = _dispersionRenderer.GenerateVectorField(
                    _dispersionEngine, windVec, maxC);
                vfVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("VectorField", "vectors"));
                _viewport.Children.Add(vfVisual);
            }

            // Streamlines every 4th frame
            if (_frameCount % 4 == 0 && scenario.StreamlineSeedPoints.Count > 0)
            {
                double maxC = _dispersionRenderer.GetMaxConcentration();
                var windVec = scenario.Meteo.WindVector;
                var slVisual = _dispersionRenderer.GenerateStreamlines(
                    _dispersionEngine, windVec, scenario.StreamlineSeedPoints, maxC);
                slVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("Streamline", "streamlines"));
                _viewport.Children.Add(slVisual);
            }

            // Fire visuals every 4th frame
            if (_frameCount % 4 == 0 && _flowsheet.FireScenario != null && _flowsheet.FireScenario.Sources.Count > 0)
            {
                var windVec = scenario.Meteo.WindVector;
                foreach (var fire in _flowsheet.FireScenario.Sources)
                {
                    var flameVisual = FireRenderer.GenerateFlameVisual(fire, windVec);
                    flameVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("FireVisual", fire.Id));
                    _viewport.Children.Add(flameVisual);

                    var radVisual = FireRenderer.GenerateRadiationContours(
                        fire, _flowsheet.FireScenario.RadiationContourLevels);
                    radVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("FireVisual", "rad_" + fire.Id));
                    _viewport.Children.Add(radVisual);
                }
            }

            if (isLastStep)
                StopDispersion();
        }

        private void AnimationTimer_CfdTick()
        {
            if (_cfdResult == null || !_cfdResult.IsLoaded) return;

            var scenario = _flowsheet.DispersionScenario;
            _frameCount++;

            double totalDuration = _cfdResult.TimeSteps[_cfdResult.TimeSteps.Count - 1]
                                 - _cfdResult.TimeSteps[0];
            double dtReal = 0.033 * _animationSpeedFactor * totalDuration / 10.0;
            _cfdPlaybackTimeS += dtReal;

            double lastTime = _cfdResult.TimeSteps[_cfdResult.TimeSteps.Count - 1];
            if (_cfdPlaybackTimeS >= lastTime)
            {
                _cfdPlaybackIndex = _cfdResult.TimeSteps.Count - 1;
                StopCfdPlayback();
                return;
            }

            while (_cfdPlaybackIndex < _cfdResult.TimeSteps.Count - 1 &&
                   _cfdResult.TimeSteps[_cfdPlaybackIndex + 1] <= _cfdPlaybackTimeS)
                _cfdPlaybackIndex++;

            double t = _cfdResult.TimeSteps[_cfdPlaybackIndex];
            var field = _cfdResult.GetField(t);
            if (field == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("CfdTick: GetField returned null for t={0}, index={1}/{2}, paths={3}, caseDir={4}",
                    t, _cfdPlaybackIndex, _cfdResult.TimeSteps.Count,
                    _cfdResult.TimeStepPaths.Count, _cfdResult.CaseDir ?? "null"));
                return;
            }

            _cfdConcentrationField = new OpenFoamConcentrationField(
                field, _cfdResult.DomainXMin, _cfdResult.DomainXMax,
                _cfdResult.DomainYMin, _cfdResult.DomainYMax, _cfdResult.DomainZMax);

            // Sample monitors
            foreach (var mon in _flowsheet.MonitorPoints)
            {
                double c = _cfdConcentrationField.EvaluateConcentration(
                    mon.Position.X, mon.Position.Y, mon.Position.Z);
                mon.TimeSeries.Add(new MonitorSample { TimeS = t, Concentration = c });
            }
            MonitorDataUpdated?.Invoke(this, EventArgs.Empty);

            RemoveDispersionVisuals();

            _dispersionRenderer.SetScalarFieldDirect(field);

            if (_frameCount % 2 == 0)
            {
                _isosurfaceVisual = _dispersionRenderer.GenerateCloudVisual(scenario?.Thresholds);
                _isosurfaceVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("DispersionIsosurface", "iso"));
                _viewport.Children.Add(_isosurfaceVisual);
            }
            else if (_isosurfaceVisual != null)
            {
                _viewport.Children.Add(_isosurfaceVisual);
            }

            // Particles
            {
                double maxC = _dispersionRenderer.GetMaxConcentration();
                if (maxC > 1e-20)
                {
                    _particleVisual = _dispersionRenderer.GenerateCfdParticleCloud(maxC);
                    _particleVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("DispersionParticles", "particles"));
                    _viewport.Children.Add(_particleVisual);
                }
            }

            // Contour planes
            if (_frameCount % 4 == 0 && scenario != null && scenario.ContourPlanes.Count > 0)
            {
                double maxC = _dispersionRenderer.GetMaxConcentration();
                double dom = _dispersionRenderer.DomainSize;
                foreach (var cp in scenario.ContourPlanes)
                {
                    if (!cp.Visible) continue;
                    var cpVisual = _dispersionRenderer.GenerateContourPlane(
                        _cfdConcentrationField, cp, -dom, dom, maxC);
                    cpVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("ContourPlane", "contour"));
                    _viewport.Children.Add(cpVisual);
                }
            }

            // Vector field
            if (_frameCount % 4 == 0 && _showVectorField && scenario != null)
            {
                double maxC = _dispersionRenderer.GetMaxConcentration();
                var windVec = scenario.Meteo.WindVector;
                var vfVisual = _dispersionRenderer.GenerateVectorField(
                    _cfdConcentrationField, windVec, maxC);
                vfVisual.SetValue(System.Windows.FrameworkElement.TagProperty,
                    new Visual3DTag("VectorField", "vectors"));
                _viewport.Children.Add(vfVisual);
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
                .OfType<System.Windows.Media.Media3D.ModelVisual3D>()
                .Where(m => m.GetValue(System.Windows.FrameworkElement.TagProperty) is Visual3DTag tag &&
                           (tag.Category == "ContourPlane" || tag.Category == "VectorField" || tag.Category == "Streamline" || tag.Category == "FireVisual" || tag.Category == "GasDetectorVis"))
                .ToList();
            foreach (var dv in dynamicVisuals)
                _viewport.Children.Remove(dv);
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

        public void ClearFlowsheet()
        {
            StopDispersion();
            _flowsheet.Clear();
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

            _flowsheet.Decorations.Add(decoration);
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

            _flowsheet.Decorations.Add(decoration);
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

            _flowsheet.Decorations.Remove(_selectedDecoration);
            SelectedDecoration = null;
            UpdateViewport();
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
        /// Saves the flowsheet to an XML file
        /// </summary>
        public void SaveToFile(string filePath)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            var doc = new System.Xml.Linq.XDocument(
                new System.Xml.Linq.XElement("Scene3D",
                    new System.Xml.Linq.XAttribute("Version", "1"),
                    new System.Xml.Linq.XAttribute("Name", _flowsheet.Name ?? ""),
                    new System.Xml.Linq.XAttribute("Description", _flowsheet.Description ?? ""),

                    new System.Xml.Linq.XElement("GridSettings",
                        new System.Xml.Linq.XAttribute("Spacing", _flowsheet.GridSpacing.ToString(inv)),
                        new System.Xml.Linq.XAttribute("SnapToGrid", _flowsheet.SnapToGrid)),

                    new System.Xml.Linq.XElement("WorkPlanes",
                        _flowsheet.WorkPlanes.Select(wp =>
                            new System.Xml.Linq.XElement("WorkPlane",
                                new System.Xml.Linq.XAttribute("Name", wp.Name ?? ""),
                                new System.Xml.Linq.XAttribute("Elevation", wp.Elevation.ToString(inv)),
                                new System.Xml.Linq.XAttribute("Visible", wp.Visible),
                                new System.Xml.Linq.XAttribute("GridColor", wp.GridColor.ToString()),
                                new System.Xml.Linq.XAttribute("GridSpacing", wp.GridSpacing.ToString(inv))))),

                    new System.Xml.Linq.XElement("CurrentWorkPlane",
                        new System.Xml.Linq.XAttribute("Name", _flowsheet.CurrentWorkPlane != null
                            ? _flowsheet.CurrentWorkPlane.Name ?? "" : "")),

                    new System.Xml.Linq.XElement("Decorations",
                        _flowsheet.Decorations.Select(d =>
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
                                new System.Xml.Linq.XAttribute("ClipAbove", d.ClipAbove.ToString())))),

                    SerializeDispersionScenarios(inv),
                    SerializeMonitorPoints(inv),
                    SerializeWindRose(inv),
                    SerializeFireScenario(inv),
                    SerializeGasDetectors(inv)
                ));

            doc.Save(filePath);
        }

        private System.Xml.Linq.XElement SerializeDispersionScenarios(System.Globalization.CultureInfo inv)
        {
            if (_flowsheet.DispersionScenarios.Count == 0) return null;

            return new System.Xml.Linq.XElement("DispersionScenarios",
                new System.Xml.Linq.XAttribute("ActiveIndex", _flowsheet.ActiveScenarioIndex.ToString(inv)),
                _flowsheet.DispersionScenarios.Select(sc => SerializeSingleScenario(sc, inv)));
        }

        private System.Xml.Linq.XElement SerializeSingleScenario(DispersionScenario sc, System.Globalization.CultureInfo inv)
        {
            return new System.Xml.Linq.XElement("DispersionScenario",
                new System.Xml.Linq.XAttribute("Name", sc.Name ?? ""),
                new System.Xml.Linq.XAttribute("Duration", sc.SimulationDurationS.ToString(inv)),
                new System.Xml.Linq.XAttribute("TimeStep", sc.TimeStepS.ToString(inv)),
                new System.Xml.Linq.XAttribute("DomainSize", sc.DomainSizeM.ToString(inv)),
                new System.Xml.Linq.XAttribute("GridRes", sc.GridResolution.ToString(inv)),

                new System.Xml.Linq.XElement("Meteo",
                    new System.Xml.Linq.XAttribute("WindSpeed", sc.Meteo.WindSpeed.ToString(inv)),
                    new System.Xml.Linq.XAttribute("WindDir", sc.Meteo.WindDirectionDeg.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Stability", sc.Meteo.StabilityClass.ToString()),
                    new System.Xml.Linq.XAttribute("Temp", sc.Meteo.AmbientTemperature.ToString(inv)),
                    new System.Xml.Linq.XAttribute("Pressure", sc.Meteo.AmbientPressure.ToString(inv))),

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
                            new System.Xml.Linq.XAttribute("Duration", src.ReleaseDurationS.ToString(inv)),
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
                                new System.Xml.Linq.XAttribute("MolarMass", src.HighPressureLeak.GasMolarMassKgMol.ToString(inv))) : null,
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
                    AmbientPressure = double.Parse((string)meteoEl.Attribute("Pressure") ?? "101325", inv)
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
                    source.ReleaseDurationS = double.Parse((string)se.Attribute("Duration") ?? "60", inv);
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
                            GasMolarMassKgMol = double.Parse((string)hpEl.Attribute("MolarMass") ?? "0.016", inv)
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
            if (_flowsheet.MonitorPoints.Count == 0) return null;

            return new System.Xml.Linq.XElement("MonitorPoints",
                _flowsheet.MonitorPoints.Select(m =>
                    new System.Xml.Linq.XElement("Monitor",
                        new System.Xml.Linq.XAttribute("Id", m.Id),
                        new System.Xml.Linq.XAttribute("Name", m.Name ?? ""),
                        new System.Xml.Linq.XAttribute("PosX", m.Position.X.ToString(inv)),
                        new System.Xml.Linq.XAttribute("PosY", m.Position.Y.ToString(inv)),
                        new System.Xml.Linq.XAttribute("PosZ", m.Position.Z.ToString(inv)),
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
                fs.MonitorPoints.Add(monitor);
            }
        }

        private System.Xml.Linq.XElement SerializeWindRose(System.Globalization.CultureInfo inv)
        {
            var wr = _flowsheet.WindRose;
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
            var fs = _flowsheet.FireScenario;
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
            if (_flowsheet.GasDetectors.Count == 0) return null;

            return new System.Xml.Linq.XElement("GasDetectors",
                _flowsheet.GasDetectors.Select(d =>
                    new System.Xml.Linq.XElement("Detector",
                        new System.Xml.Linq.XAttribute("Id", d.Id),
                        new System.Xml.Linq.XAttribute("Name", d.Name ?? ""),
                        new System.Xml.Linq.XAttribute("PosX", d.Position.X.ToString(inv)),
                        new System.Xml.Linq.XAttribute("PosY", d.Position.Y.ToString(inv)),
                        new System.Xml.Linq.XAttribute("PosZ", d.Position.Z.ToString(inv)),
                        new System.Xml.Linq.XAttribute("Threshold", d.ThresholdKgM3.ToString(inv)),
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
                det.Visible = bool.Parse((string)de.Attribute("Visible") ?? "True");
                fs.GasDetectors.Add(det);
            }
        }

        /// <summary>
        /// Loads the flowsheet from an XML file
        /// </summary>
        public void LoadFromFile(string filePath)
        {
            if (!System.IO.File.Exists(filePath)) return;

            var inv = System.Globalization.CultureInfo.InvariantCulture;

            try
            {
                var doc = System.Xml.Linq.XDocument.Load(filePath);
                var root = doc.Root;
                if (root == null || root.Name.LocalName != "Scene3D") return;

                var fs = new Scene3D();
                fs.Name = (string)root.Attribute("Name") ?? "New Flowsheet";
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

                        deco.UpdateBoundingBox();
                        fs.Decorations.Add(deco);
                    }
                }

                DeserializeDispersionScenario(root, inv, fs);
                DeserializeMonitorPoints(root, inv, fs);
                DeserializeWindRose(root, inv, fs);
                DeserializeFireScenario(root, inv, fs);
                DeserializeGasDetectors(root, inv, fs);

                _flowsheet = fs;
                _snapToGrid = fs.SnapToGrid;
                _gridSpacing = fs.GridSpacing;
                SelectedDecoration = null;
                UpdateViewport();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to load flowsheet: " + ex.Message);
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
                        var hitDeco = _flowsheet.FindDecoration(hitTag.Id);
                        SelectedDecoration = hitDeco;
                        SelectedSource = null;

                        if (hitDeco != null && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                        {
                            var dragPoint = GetHitPoint(position);
                            if (dragPoint != null)
                            {
                                _isDraggingDecoration = true;
                                _dragStartPosition = dragPoint.Value;
                                _dragUnitOriginalPosition = hitDeco.Position;
                                _viewport.CaptureMouse();
                            }
                        }
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
                        AddReleaseSource(releasePoint.Value, PendingSourceTemplate);
                        PendingSourceTemplate = null;
                    }
                    break;

                case EditMode.PlaceMonitorPoint:
                    var monitorPoint = GetHitPoint(position);
                    if (monitorPoint != null)
                    {
                        AddMonitorPoint(monitorPoint.Value, PendingMonitorTemplate);
                        PendingMonitorTemplate = null;
                    }
                    break;

                case EditMode.PlaceFireSource:
                    var firePoint = GetHitPoint(position);
                    if (firePoint != null && PendingFireTemplate != null)
                    {
                        PendingFireTemplate.Position = firePoint.Value;
                        _flowsheet.FireScenario.Sources.Add(PendingFireTemplate);
                        PendingFireTemplate = null;
                        CurrentEditMode = EditMode.Select;
                        UpdateViewport();
                    }
                    break;

                case EditMode.PlaceGasDetector:
                    var detPoint = GetHitPoint(position);
                    if (detPoint != null && PendingDetectorTemplate != null)
                    {
                        PendingDetectorTemplate.Position = detPoint.Value;
                        _flowsheet.GasDetectors.Add(PendingDetectorTemplate);
                        PendingDetectorTemplate = null;
                        CurrentEditMode = EditMode.Select;
                        UpdateViewport();
                    }
                    break;

            }
        }

        private void Viewport_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var position = e.GetPosition(_viewport);

            if (_isDraggingDecoration && _selectedDecoration != null)
            {
                var currentPoint = GetHitPoint(position);
                if (currentPoint != null)
                {
                    var delta = currentPoint.Value - _dragStartPosition;
                    var newPos = new Point3D(
                        _dragUnitOriginalPosition.X + delta.X,
                        _dragUnitOriginalPosition.Y + delta.Y,
                        _dragUnitOriginalPosition.Z);

                    if (_snapToGrid)
                    {
                        newPos = newPos.SnapToGrid(_gridSpacing);
                    }

                    _selectedDecoration.Position = newPos;
                    _selectedDecoration.UpdateBoundingBox();
                    UpdateViewport();
                    OnSelectedUnitChanged();
                }
                return;
            }

        }

        private void Viewport_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                _viewport.ReleaseMouseCapture();
            }

            if (_isDraggingDecoration)
            {
                _isDraggingDecoration = false;
                _viewport.ReleaseMouseCapture();
            }
        }

        private void Viewport_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.Delete:
                    if (_selectedDecoration != null)
                        DeleteSelectedDecoration();
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

        private void FlowsheetEditor3DControl_Resize(object sender, EventArgs e)
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
                return _flowsheet.FindDecoration(tag.Id);
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

            var plane = _flowsheet.CurrentWorkPlane;
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

            double elev = _flowsheet?.CurrentWorkPlane?.Elevation ?? 0;
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

            int tiles = (int)(_groundSize / 5.0);
            var drawingGroup = new System.Windows.Media.DrawingGroup();
            var bgColor = System.Windows.Media.Color.FromRgb(180, 195, 170);
            var lineColor = System.Windows.Media.Color.FromArgb(60, 100, 120, 90);
            drawingGroup.Children.Add(new System.Windows.Media.GeometryDrawing(
                new System.Windows.Media.SolidColorBrush(bgColor), null,
                new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(0, 0, tiles, tiles))));
            var pen = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(lineColor), 0.02);
            for (int i = 0; i <= tiles; i++)
            {
                drawingGroup.Children.Add(new System.Windows.Media.GeometryDrawing(null, pen,
                    new System.Windows.Media.LineGeometry(new System.Windows.Point(i, 0), new System.Windows.Point(i, tiles))));
                drawingGroup.Children.Add(new System.Windows.Media.GeometryDrawing(null, pen,
                    new System.Windows.Media.LineGeometry(new System.Windows.Point(0, i), new System.Windows.Point(tiles, i))));
            }

            var brush = new System.Windows.Media.DrawingBrush(drawingGroup);
            brush.TileMode = System.Windows.Media.TileMode.None;
            brush.Freeze();

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
        }

        private void UpdateGridElevation()
        {
            if (_gridVisual == null || _flowsheet?.CurrentWorkPlane == null) return;
            double elev = _flowsheet.CurrentWorkPlane.Elevation;
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

            foreach (var deco in _flowsheet.Decorations)
            {
                if (deco.Model3D != null)
                {
                    if (deco.UseCustomMaterial)
                    {
                        var mat = Core.MaterialHelper.CreateMaterial(
                            deco.MaterialType, deco.MaterialColor, deco.SpecularPower, deco.Opacity);
                        Core.MaterialHelper.ApplyToModel(deco.Model3D, mat);
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

            if (_flowsheet.DispersionScenario != null)
            {
                foreach (var source in _flowsheet.DispersionScenario.Sources)
                {
                    var pos = source.EffectivePosition;
                    var dir = source.ReleaseDirection;

                    var group = new System.Windows.Media.Media3D.Model3DGroup();

                    var sphereMesh = new System.Windows.Media.Media3D.MeshGeometry3D();
                    int slices = 8, stacks = 6;
                    double r = 1.0;
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
                        System.Windows.Media.Color.FromArgb(200, 255, 80, 0));
                    orangeBrush.Freeze();
                    var orangeMat = new System.Windows.Media.Media3D.DiffuseMaterial(orangeBrush);

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
                        System.Windows.Media.Color.FromArgb(220, 255, 30, 30));
                    redBrush.Freeze();
                    var redMat = new System.Windows.Media.Media3D.DiffuseMaterial(redBrush);

                    group.Children.Add(new System.Windows.Media.Media3D.GeometryModel3D
                    {
                        Geometry = arrowMesh,
                        Material = redMat,
                        BackMaterial = redMat
                    });

                    var visual = new System.Windows.Media.Media3D.ModelVisual3D { Content = group };
                    visual.SetValue(System.Windows.FrameworkElement.TagProperty,
                        new Visual3DTag("ReleaseSource", source.Id));
                    _viewport.Children.Add(visual);
                }
            }

            // Wind rose (below)
            var existingWindRose = _viewport.Children.OfType<System.Windows.Media.Media3D.ModelVisual3D>()
                .Where(m => m.GetValue(System.Windows.FrameworkElement.TagProperty) is Visual3DTag tag &&
                           tag.Category == "WindRose")
                .ToList();
            foreach (var wr in existingWindRose)
                _viewport.Children.Remove(wr);

            if (_flowsheet.WindRose != null && _flowsheet.WindRose.ShowIn3D && _flowsheet.WindRose.Bins.Count > 0)
            {
                var wrVisual = Core.WindRoseRenderer.Generate(_flowsheet.WindRose);
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

            foreach (var monitor in _flowsheet.MonitorPoints)
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

            if (_flowsheet.FireScenario != null)
            {
                foreach (var fire in _flowsheet.FireScenario.Sources)
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

            foreach (var det in _flowsheet.GasDetectors)
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
            if (_flowsheet.DispersionScenario == null || id == null) return null;
            foreach (var src in _flowsheet.DispersionScenario.Sources)
                if (src.Id == id) return src;
            return null;
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
