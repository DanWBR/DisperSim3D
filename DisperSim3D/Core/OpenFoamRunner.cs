using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Represents the progress state of an OpenFOAM simulation run.
    /// </summary>
    public class OpenFoamProgress
    {
        /// <summary>
        /// Gets or sets the progress fraction, ranging from 0.0 (not started) to 1.0 (complete).
        /// A value of -1 indicates a log-only update with no progress change.
        /// </summary>
        public double Fraction { get; set; }

        /// <summary>
        /// Gets or sets a human-readable description of the current simulation step.
        /// </summary>
        public string Step { get; set; }

        /// <summary>
        /// Gets or sets the raw solver log line associated with this progress update.
        /// </summary>
        public string LogLine { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this progress update represents an error condition.
        /// </summary>
        public bool IsError { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the simulation has completed.
        /// </summary>
        public bool IsComplete { get; set; }
    }

    /// <summary>
    /// Orchestrates the execution of OpenFOAM simulation pipelines, including mesh generation,
    /// field initialization, solver execution, domain decomposition, and result reconstruction.
    /// Supports both transient scalar transport and steady-state wind field simulations.
    /// </summary>
    public class OpenFoamRunner
    {
        private readonly OpenFoamEnvironment _env;
        private BackgroundWorker _worker;
        private Process _currentProcess;
        private string _casePath;
        private double _endTime;
        private int _nProcs;

        /// <summary>
        /// Raised when the simulation progress is updated, providing fraction complete, step description, and log output.
        /// </summary>
        public event EventHandler<OpenFoamProgress> ProgressUpdated;

        /// <summary>
        /// Raised when the simulation completes successfully, providing the parsed result data.
        /// </summary>
        public event EventHandler<OpenFoamResult> Completed;

        /// <summary>
        /// Raised when the simulation fails or is cancelled, providing an error message.
        /// </summary>
        public event EventHandler<string> Failed;

        /// <summary>
        /// Gets a value indicating whether a simulation is currently running.
        /// </summary>
        public bool IsRunning => _worker != null && _worker.IsBusy;

        /// <summary>
        /// Gets the absolute path to the current OpenFOAM case directory.
        /// </summary>
        public string CasePath => _casePath;

        public Process CurrentProcess => _currentProcess;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenFoamRunner"/> class.
        /// </summary>
        /// <param name="environment">The OpenFOAM environment used to locate binaries and start processes.</param>
        public OpenFoamRunner(OpenFoamEnvironment environment)
        {
            _env = environment;
        }

        /// <summary>
        /// Runs the full transient scalar transport simulation pipeline asynchronously on a background thread.
        /// Executes blockMesh, topoSet, setFields (if jets present), optional parallel decomposition,
        /// scalarTransportFoam, optional reconstruction, and result reading.
        /// </summary>
        /// <param name="scenario">The dispersion scenario defining sources, domain, and simulation parameters.</param>
        /// <param name="config">The CFD configuration specifying solver settings and parallelism.</param>
        public void RunAsync(DispersionScenario scenario, CfdConfiguration config,
            CfdSolverType solverType = CfdSolverType.ScalarTransportFoam)
        {
            if (IsRunning) return;
            if (!_env.IsAvailable)
            {
                Failed?.Invoke(this, _env.StatusMessage);
                return;
            }

            _endTime = scenario.SimulationDurationS;
            _nProcs = config.NumberOfProcessors > 1 ? config.NumberOfProcessors : 1;
            int nx = scenario.GridResolution;
            int ny = scenario.GridResolution;
            int nz = scenario.GridResolution / 2;
            if (nz < 1) nz = 1;
            double domain = scenario.DomainSizeM;

            string solverCommand;
            switch (solverType)
            {
                case CfdSolverType.PimpleFoam: solverCommand = "pimpleFoam"; break;
                case CfdSolverType.BuoyantPimpleFoam: solverCommand = "buoyantPimpleFoam"; break;
                case CfdSolverType.ReactingFoam: solverCommand = "reactingFoam"; break;
                case CfdSolverType.RhoReactingBuoyantFoam: solverCommand = "rhoReactingBuoyantFoam"; break;
                default: solverCommand = "scalarTransportFoam"; break;
            }

            _worker = new BackgroundWorker { WorkerSupportsCancellation = true };
            _worker.DoWork += (s, e) =>
            {
                try
                {
                    ReportProgress(0, "Generating case...", "");
                    switch (solverType)
                    {
                        case CfdSolverType.PimpleFoam:
                            _casePath = OpenFoamCaseGenerator.GeneratePimpleFoam(scenario, config);
                            break;
                        case CfdSolverType.BuoyantPimpleFoam:
                            _casePath = OpenFoamCaseGenerator.GenerateBuoyantPimpleFoam(scenario, config);
                            break;
                        case CfdSolverType.ReactingFoam:
                            _casePath = OpenFoamCaseGenerator.GenerateReactingFoam(scenario, config);
                            break;
                        case CfdSolverType.RhoReactingBuoyantFoam:
                            _casePath = OpenFoamCaseGenerator.GenerateRhoReactingBuoyantFoam(scenario, config);
                            break;
                        default:
                            _casePath = OpenFoamCaseGenerator.Generate(scenario, config);
                            break;
                    }

                    if (_worker.CancellationPending) { e.Cancel = true; return; }

                    ReportProgress(0.02, "Running blockMesh...", "");
                    RunStep("blockMesh");
                    if (_worker.CancellationPending) { e.Cancel = true; return; }

                    if (System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "refineMeshDict")))
                    {
                        for (int rl = 0; rl < 2; rl++)
                        {
                            string dictFile = "system/topoSetDict_refine" + rl;
                            if (!System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "topoSetDict_refine" + rl)))
                                break;
                            ReportProgress(0.03 + rl * 0.01, "Refining mesh (level " + (rl + 1) + ")...", "");
                            RunStep("topoSet -dict " + dictFile);
                            if (_worker.CancellationPending) { e.Cancel = true; return; }
                            RunStep("refineMesh -dict system/refineMeshDict -overwrite");
                            if (_worker.CancellationPending) { e.Cancel = true; return; }
                        }
                    }

                    if (scenario.Sources.Count > 0)
                    {
                        ReportProgress(0.05, "Running topoSet...", "");
                        RunStep("topoSet");
                        if (_worker.CancellationPending) { e.Cancel = true; return; }
                    }

                    if (System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "setFieldsDict")))
                    {
                        ReportProgress(0.06, "Running setFields (jet velocity)...", "");
                        RunStep("setFields");
                        if (_worker.CancellationPending) { e.Cancel = true; return; }
                    }

                    bool useParallel = _nProcs > 1 && _env.CanRunParallel;
                    if (useParallel)
                    {
                        ReportProgress(0.08, "Running decomposePar...", "");
                        RunStep("decomposePar");
                        if (_worker.CancellationPending) { e.Cancel = true; return; }

                        string mpiCmd = _env.BuildMpiCommand(_nProcs, solverCommand + " -parallel");
                        ReportProgress(0.1, string.Format("Running {0} ({1} CPUs)...", solverCommand, _nProcs), "");
                        RunSolver(mpiCmd);
                        if (_worker.CancellationPending) { e.Cancel = true; return; }

                        ReportProgress(0.93, "Running reconstructPar...", "");
                        RunStep("reconstructPar");
                        if (_worker.CancellationPending) { e.Cancel = true; return; }
                    }
                    else
                    {
                        ReportProgress(0.1, string.Format("Running {0}...", solverCommand), "");
                        RunSolver(solverCommand);
                        if (_worker.CancellationPending) { e.Cancel = true; return; }
                    }

                    if (System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "refineMeshDict")))
                    {
                        ReportProgress(0.94, "Writing cell centres...", "");
                        try { RunStep("postProcess -func writeCellCentres"); } catch { }
                    }

                    string fieldName;
                    switch (solverType)
                    {
                        case CfdSolverType.ReactingFoam: fieldName = "CH4"; break;
                        case CfdSolverType.RhoReactingBuoyantFoam: fieldName = "CH4"; break;
                        case CfdSolverType.BuoyantPimpleFoam: fieldName = "s"; break;
                        default: fieldName = "T"; break;
                    }
                    ReportProgress(0.95, "Reading results...", "");
                    var result = OpenFoamResultReader.ReadResults(_casePath, nx, ny, nz, domain,
                        (frac, msg) => ReportProgress(0.95 + frac * 0.04, msg, ""),
                        fieldName);

                    e.Result = result;
                }
                catch (Exception ex)
                {
                    e.Result = ex;
                }
            };

            _worker.RunWorkerCompleted += (s, e) =>
            {
                if (e.Cancelled)
                {
                    Failed?.Invoke(this, "Cancelled by user");
                }
                else if (e.Result is Exception ex)
                {
                    Failed?.Invoke(this, ex.Message);
                }
                else if (e.Result is OpenFoamResult result)
                {
                    ReportProgress(1.0, "Complete", "", isComplete: true);
                    Completed?.Invoke(this, result);
                }
                else
                {
                    Failed?.Invoke(this, "Unknown error");
                }
            };

            _worker.RunWorkerAsync();
        }

        /// <summary>
        /// Runs a steady-state scalar transport simulation asynchronously.
        /// Supports both scalarTransportFoam (with steadyState ddtSchemes) and simpleFoam (with scalar).
        /// </summary>
        public void RunSteadyAsync(DispersionScenario scenario, CfdConfiguration config, CfdSolverType solverType)
        {
            if (IsRunning) return;
            if (!_env.IsAvailable)
            {
                Failed?.Invoke(this, _env.StatusMessage);
                return;
            }

            _nProcs = config.NumberOfProcessors > 1 ? config.NumberOfProcessors : 1;
            int nx = scenario.GridResolution;
            int ny = scenario.GridResolution;
            int nz = scenario.GridResolution / 2;
            if (nz < 1) nz = 1;
            double domain = scenario.DomainSizeM;

            string solverName;
            switch (solverType)
            {
                case CfdSolverType.ScalarSimpleFoam: solverName = "simpleFoam"; break;
                case CfdSolverType.RhoSimpleFoam: solverName = "rhoSimpleFoam"; break;
                default: solverName = "scalarTransportFoam"; break;
            }

            _worker = new BackgroundWorker { WorkerSupportsCancellation = true };
            _worker.DoWork += (s, e) =>
            {
                try
                {
                    ReportProgress(0, "Generating steady-state case...", "");
                    switch (solverType)
                    {
                        case CfdSolverType.ScalarSimpleFoam:
                            _casePath = OpenFoamCaseGenerator.GenerateSteadyStateSIMPLE(scenario, config);
                            break;
                        case CfdSolverType.RhoSimpleFoam:
                            _casePath = OpenFoamCaseGenerator.GenerateRhoSimpleFoam(scenario, config);
                            break;
                        default:
                            _casePath = OpenFoamCaseGenerator.GenerateSteadyState(scenario, config);
                            break;
                    }

                    if (_worker.CancellationPending) { e.Cancel = true; return; }

                    ReportProgress(0.02, "Running blockMesh...", "");
                    RunStep("blockMesh");
                    if (_worker.CancellationPending) { e.Cancel = true; return; }

                    if (System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "refineMeshDict")))
                    {
                        for (int rl = 0; rl < 2; rl++)
                        {
                            string dictFile = "system/topoSetDict_refine" + rl;
                            if (!System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "topoSetDict_refine" + rl)))
                                break;
                            ReportProgress(0.03 + rl * 0.01, "Refining mesh (level " + (rl + 1) + ")...", "");
                            RunStep("topoSet -dict " + dictFile);
                            if (_worker.CancellationPending) { e.Cancel = true; return; }
                            RunStep("refineMesh -dict system/refineMeshDict -overwrite");
                            if (_worker.CancellationPending) { e.Cancel = true; return; }
                        }
                    }

                    if (scenario.Sources.Count > 0)
                    {
                        ReportProgress(0.05, "Running topoSet...", "");
                        RunStep("topoSet");
                        if (_worker.CancellationPending) { e.Cancel = true; return; }

                        if ((solverType == CfdSolverType.ScalarSimpleFoam || solverType == CfdSolverType.RhoSimpleFoam)
                            && System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "setFieldsDict")))
                        {
                            bool hasJet = false;
                            foreach (var src in scenario.Sources)
                                if (src.ComputedExitVelocity > 0) { hasJet = true; break; }
                            if (hasJet)
                            {
                                ReportProgress(0.07, "Running setFields (jet velocity)...", "");
                                RunStep("setFields");
                                if (_worker.CancellationPending) { e.Cancel = true; return; }
                            }
                        }
                    }

                    if (solverType != CfdSolverType.ScalarSimpleFoam
                        && solverType != CfdSolverType.RhoSimpleFoam
                        && System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "setFieldsDict")))
                    {
                        ReportProgress(0.06, "Running setFields (jet velocity)...", "");
                        RunStep("setFields");
                        if (_worker.CancellationPending) { e.Cancel = true; return; }
                    }

                    bool useParallel = _nProcs > 1 && _env.CanRunParallel;
                    if (useParallel)
                    {
                        ReportProgress(0.08, "Running decomposePar...", "");
                        RunStep("decomposePar");
                        if (_worker.CancellationPending) { e.Cancel = true; return; }

                        string mpiCmd = _env.BuildMpiCommand(_nProcs, solverName + " -parallel");
                        ReportProgress(0.1, string.Format("Running {0} steady-state ({1} CPUs)...", solverName, _nProcs), "");
                        RunSteadySolverAsync(mpiCmd);
                        if (_worker.CancellationPending) { e.Cancel = true; return; }

                        ReportProgress(0.93, "Running reconstructPar...", "");
                        RunStep("reconstructPar");
                        if (_worker.CancellationPending) { e.Cancel = true; return; }
                    }
                    else
                    {
                        ReportProgress(0.1, string.Format("Running {0} steady-state...", solverName), "");
                        RunSteadySolverAsync(solverName);
                        if (_worker.CancellationPending) { e.Cancel = true; return; }
                    }

                    if (System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "refineMeshDict")))
                    {
                        ReportProgress(0.94, "Writing cell centres...", "");
                        try { RunStep("postProcess -func writeCellCentres"); } catch { }
                    }

                    string ssFieldName = solverType == CfdSolverType.RhoSimpleFoam ? "s" : "T";
                    ReportProgress(0.95, "Reading results...", "");
                    var result = OpenFoamResultReader.ReadResults(_casePath, nx, ny, nz, domain,
                        (frac, msg) => ReportProgress(0.95 + frac * 0.04, msg, ""),
                        ssFieldName);

                    e.Result = result;
                }
                catch (Exception ex)
                {
                    e.Result = ex;
                }
            };

            _worker.RunWorkerCompleted += (s, e) =>
            {
                if (e.Cancelled)
                {
                    Failed?.Invoke(this, "Cancelled by user");
                }
                else if (e.Result is Exception ex)
                {
                    Failed?.Invoke(this, ex.Message);
                }
                else if (e.Result is OpenFoamResult result)
                {
                    ReportProgress(1.0, "Complete", "", isComplete: true);
                    Completed?.Invoke(this, result);
                }
                else
                {
                    Failed?.Invoke(this, "Unknown error");
                }
            };

            _worker.RunWorkerAsync();
        }

        private void RunSteadySolverAsync(string command)
        {
            _currentProcess = _env.StartCommand(_casePath, command);
            var timeRegex = new Regex(@"^Time\s*=\s*([\d.eE+-]+)", RegexOptions.Compiled);
            var residualRegex = new Regex(@"Solving for (\w+).*Final residual\s*=\s*([\d.eE+-]+)", RegexOptions.Compiled);
            var convergenceRegex = new Regex(@"(SIMPLE|PIMPLE).*solution converged", RegexOptions.Compiled);

            var stderrBuilder = new System.Text.StringBuilder();
            _currentProcess.ErrorDataReceived += (s2, ev) =>
            {
                if (ev.Data != null) stderrBuilder.AppendLine(ev.Data);
            };
            _currentProcess.BeginErrorReadLine();

            int maxIter = 1000;
            string lastResidual = "";

            while (!_currentProcess.StandardOutput.EndOfStream)
            {
                string line = _currentProcess.StandardOutput.ReadLine();
                if (line == null) continue;

                var timeMatch = timeRegex.Match(line);
                if (timeMatch.Success)
                {
                    double t;
                    if (double.TryParse(timeMatch.Groups[1].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out t))
                    {
                        double frac = 0.1 + 0.85 * Math.Min(1.0, t / maxIter);
                        string step = string.Format("Steady-state iteration {0}", (int)t);
                        if (!string.IsNullOrEmpty(lastResidual))
                            step += "  res=" + lastResidual;
                        ReportProgress(frac, step, line);
                    }
                }
                else
                {
                    var resMatch = residualRegex.Match(line);
                    if (resMatch.Success)
                    {
                        lastResidual = resMatch.Groups[2].Value;
                        ReportProgress(-1, null, line);
                    }

                    if (convergenceRegex.IsMatch(line))
                        ReportProgress(-1, null, "Converged! " + line);
                }

                if (_worker.CancellationPending)
                {
                    try { _currentProcess.Kill(); } catch { }
                    return;
                }
            }

            const int timeoutMs = 30 * 60 * 1000; // 30 min — large meshes / many ranks can be slow
            if (!_currentProcess.WaitForExit(timeoutMs))
            {
                try { _currentProcess.Kill(); } catch { }
                throw new Exception(command + " timed out after " + (timeoutMs / 60000) + " min");
            }
            if (_currentProcess.ExitCode != 0)
            {
                string err = stderrBuilder.ToString().Trim();
                if (string.IsNullOrEmpty(err))
                {
                    // mpiexec swallows rank stderr — try to surface the OpenFOAM log if present.
                    try
                    {
                        string solver = command.TrimStart().Split(' ')[0];
                        if (solver == "mpiexec" || solver == "mpirun")
                        {
                            // mpiexec -np N solver -parallel → solver is at index 3
                            var parts = command.Split(' ');
                            if (parts.Length > 3) solver = parts[3];
                        }
                        var logPath = System.IO.Path.Combine(_casePath, "log." + solver);
                        if (System.IO.File.Exists(logPath))
                        {
                            var allLines = System.IO.File.ReadAllLines(logPath);
                            int n = Math.Min(40, allLines.Length);
                            err = string.Join("\n", allLines, allLines.Length - n, n);
                        }
                    }
                    catch { }
                }
                throw new Exception(command + " failed (exit " + _currentProcess.ExitCode + "): " + err);
            }
            _currentProcess = null;
        }

        /// <summary>
        /// Runs a steady-state wind field simulation (simpleFoam) synchronously and returns the computed wind field.
        /// Executes blockMesh, optional topoSet/setFields for obstacles, optional parallel decomposition,
        /// simpleFoam, optional reconstruction, and velocity field reading.
        /// </summary>
        /// <param name="caseDir">The absolute path to the wind case directory.</param>
        /// <param name="nx">Number of grid cells in the X direction.</param>
        /// <param name="ny">Number of grid cells in the Y direction.</param>
        /// <param name="nz">Number of grid cells in the Z direction.</param>
        /// <param name="xMin">Domain minimum X coordinate in meters.</param>
        /// <param name="xMax">Domain maximum X coordinate in meters.</param>
        /// <param name="yMin">Domain minimum Y coordinate in meters.</param>
        /// <param name="yMax">Domain maximum Y coordinate in meters.</param>
        /// <param name="zMax">Domain maximum Z coordinate in meters.</param>
        /// <param name="hasObstacles">Whether the case includes obstacle definitions.</param>
        /// <param name="nProcs">Number of processors for parallel execution.</param>
        /// <param name="progress">Optional callback invoked with progress fraction and status message.</param>
        /// <returns>The computed 3D wind field, or <c>null</c> if the velocity field could not be read.</returns>
        public WindField3D RunWindCase(string caseDir, int nx, int ny, int nz,
            double xMin, double xMax, double yMin, double yMax, double zMax,
            bool hasObstacles, int nProcs, Action<double, string> progress)
        {
            _casePath = caseDir;

            progress?.Invoke(0.0, "Wind field: blockMesh...");
            RunStep("blockMesh");

            if (System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "refineMeshDict")))
            {
                for (int rl = 0; rl < 2; rl++)
                {
                    string dictFile = "system/topoSetDict_refine" + rl;
                    if (!System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "topoSetDict_refine" + rl)))
                        break;
                    progress?.Invoke(0.03 + rl * 0.02, "Wind field: refining mesh (level " + (rl + 1) + ")...");
                    RunStep("topoSet -dict " + dictFile);
                    RunStep("refineMesh -dict system/refineMeshDict -overwrite");
                }
            }

            if (hasObstacles)
            {
                progress?.Invoke(0.1, "Wind field: topoSet...");
                RunStep("topoSet");

                progress?.Invoke(0.15, "Wind field: setFields...");
                RunStep("setFields");
            }

            bool useParallel = nProcs > 1 && _env.CanRunParallel;
            if (useParallel)
            {
                progress?.Invoke(0.2, "Wind field: decomposePar...");
                RunStep("decomposePar");

                string mpiCmd = _env.BuildMpiCommand(nProcs, "simpleFoam -parallel");
                progress?.Invoke(0.25, "Wind field: simpleFoam (" + nProcs + " CPUs)...");
                RunSteadySolver(mpiCmd, progress);

                progress?.Invoke(0.9, "Wind field: reconstructPar...");
                RunStep("reconstructPar");
            }
            else
            {
                progress?.Invoke(0.25, "Wind field: simpleFoam...");
                RunSteadySolver("simpleFoam", progress);
            }

            progress?.Invoke(0.95, "Wind field: reading U...");
            var windField = OpenFoamResultReader.ReadWindField(caseDir, nx, ny, nz,
                xMin, xMax, yMin, yMax, zMax);

            return windField;
        }

        private void RunSteadySolver(string command, Action<double, string> progress)
        {
            _currentProcess = _env.StartCommand(_casePath, command);
            var timeRegex = new Regex(@"^Time\s*=\s*([\d.eE+-]+)", RegexOptions.Compiled);
            var residualRegex = new Regex(@"Solving for (\w+).*Final residual\s*=\s*([\d.eE+-]+)", RegexOptions.Compiled);

            // Mirror everything to log.<solver> so we can diagnose failures even when
            // mpiexec swallows rank output. Solver name = first non-mpi token.
            string solverName = command.TrimStart().Split(' ')[0];
            if (solverName == "mpiexec" || solverName == "mpirun")
            {
                var parts = command.Split(' ');
                if (parts.Length > 3) solverName = parts[3];
            }
            string solverLogPath = System.IO.Path.Combine(_casePath, "log." + solverName);
            System.IO.StreamWriter logWriter = null;
            try { logWriter = new System.IO.StreamWriter(solverLogPath, false) { AutoFlush = true }; }
            catch { /* non-fatal: keep running without on-disk log */ }

            var stderrBuilder = new System.Text.StringBuilder();
            _currentProcess.ErrorDataReceived += (s, ev) =>
            {
                if (ev.Data != null)
                {
                    stderrBuilder.AppendLine(ev.Data);
                    try { logWriter?.WriteLine("[stderr] " + ev.Data); } catch { }
                }
            };
            _currentProcess.BeginErrorReadLine();

            string lastResidual = "";
            while (!_currentProcess.StandardOutput.EndOfStream)
            {
                string line = _currentProcess.StandardOutput.ReadLine();
                if (line == null) continue;
                try { logWriter?.WriteLine(line); } catch { }

                var timeMatch = timeRegex.Match(line);
                if (timeMatch.Success)
                {
                    double t;
                    if (double.TryParse(timeMatch.Groups[1].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out t))
                    {
                        double frac = 0.25 + 0.65 * Math.Min(1.0, t / 500.0);
                        string msg = string.Format("Wind field: iter {0}/500", (int)t);
                        if (!string.IsNullOrEmpty(lastResidual))
                            msg += "  res=" + lastResidual;
                        progress?.Invoke(frac, msg);
                    }
                }
                else
                {
                    var resMatch = residualRegex.Match(line);
                    if (resMatch.Success)
                        lastResidual = resMatch.Groups[2].Value;
                }
            }
            try { logWriter?.Dispose(); } catch { }

            const int timeoutMs = 30 * 60 * 1000; // 30 min — large meshes / many ranks can be slow
            if (!_currentProcess.WaitForExit(timeoutMs))
            {
                try { _currentProcess.Kill(); } catch { }
                throw new Exception(command + " timed out after " + (timeoutMs / 60000) + " min");
            }
            if (_currentProcess.ExitCode != 0)
            {
                string err = stderrBuilder.ToString().Trim();
                if (string.IsNullOrEmpty(err))
                {
                    // mpiexec swallows rank stderr — try to surface the OpenFOAM log if present.
                    try
                    {
                        string solver = command.TrimStart().Split(' ')[0];
                        if (solver == "mpiexec" || solver == "mpirun")
                        {
                            // mpiexec -np N solver -parallel → solver is at index 3
                            var parts = command.Split(' ');
                            if (parts.Length > 3) solver = parts[3];
                        }
                        var logPath = System.IO.Path.Combine(_casePath, "log." + solver);
                        if (System.IO.File.Exists(logPath))
                        {
                            var allLines = System.IO.File.ReadAllLines(logPath);
                            int n = Math.Min(40, allLines.Length);
                            err = string.Join("\n", allLines, allLines.Length - n, n);
                        }
                    }
                    catch { }
                }
                throw new Exception(command + " failed (exit " + _currentProcess.ExitCode + "): " + err);
            }
            _currentProcess = null;
        }

        /// <summary>
        /// Requests cancellation of the currently running simulation and kills the active OpenFOAM process.
        /// </summary>
        public void Cancel()
        {
            if (_worker != null) _worker.CancelAsync();
            try { _currentProcess?.Kill(); } catch { }
        }

        private void RunStep(string command)
        {
            _currentProcess = _env.StartCommand(_casePath, command);
            string stdout = _currentProcess.StandardOutput.ReadToEnd();
            string stderr = _currentProcess.StandardError.ReadToEnd();
            _currentProcess.WaitForExit(300000);

            if (_currentProcess.ExitCode != 0)
            {
                string msg = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                throw new Exception(command + " failed: " + msg);
            }
            _currentProcess = null;
        }

        private void RunSolver(string command)
        {
            _currentProcess = _env.StartCommand(_casePath, command);
            var timeRegex = new Regex(@"^Time\s*=\s*([\d.eE+-]+)", RegexOptions.Compiled);
            var residualRegex = new Regex(@"Solving for (\w+).*Final residual\s*=\s*([\d.eE+-]+)", RegexOptions.Compiled);
            var courantRegex = new Regex(@"Courant Number mean:\s*([\d.eE+-]+)\s+max:\s*([\d.eE+-]+)", RegexOptions.Compiled);
            var deltaTRegex = new Regex(@"deltaT\s*=\s*([\d.eE+-]+)", RegexOptions.Compiled);

            double currentTime = 0;
            string lastResidual = "";
            string lastCourant = "";
            string lastDeltaT = "";

            var stderrBuilder = new System.Text.StringBuilder();
            _currentProcess.ErrorDataReceived += (s, ev) =>
            {
                if (ev.Data != null) stderrBuilder.AppendLine(ev.Data);
            };
            _currentProcess.BeginErrorReadLine();

            while (!_currentProcess.StandardOutput.EndOfStream)
            {
                string line = _currentProcess.StandardOutput.ReadLine();
                if (line == null) continue;

                var timeMatch = timeRegex.Match(line);
                if (timeMatch.Success)
                {
                    double t;
                    if (double.TryParse(timeMatch.Groups[1].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out t))
                    {
                        currentTime = t;
                        double fraction = 0.1 + 0.85 * (t / _endTime);
                        string step = "Solving (t=" + t.ToString("F2") + "/" +
                            _endTime.ToString("F0") + "s)";
                        if (!string.IsNullOrEmpty(lastDeltaT))
                            step += "  dt=" + lastDeltaT;
                        if (!string.IsNullOrEmpty(lastCourant))
                            step += "  Co=" + lastCourant;
                        if (!string.IsNullOrEmpty(lastResidual))
                            step += "  res=" + lastResidual;
                        ReportProgress(fraction, step, line);
                    }
                }
                else
                {
                    var resMatch = residualRegex.Match(line);
                    if (resMatch.Success)
                    {
                        lastResidual = resMatch.Groups[2].Value;
                        ReportProgress(-1, null, line);
                    }
                    else
                    {
                        var coMatch = courantRegex.Match(line);
                        if (coMatch.Success)
                        {
                            lastCourant = coMatch.Groups[2].Value;
                            ReportProgress(-1, null, line);
                        }
                        else
                        {
                            var dtMatch = deltaTRegex.Match(line);
                            if (dtMatch.Success)
                            {
                                lastDeltaT = dtMatch.Groups[1].Value;
                                ReportProgress(-1, null, line);
                            }
                        }
                    }
                }

                if (_worker.CancellationPending)
                {
                    try { _currentProcess.Kill(); } catch { }
                    return;
                }
            }

            _currentProcess.WaitForExit(60000);
            if (_currentProcess.ExitCode != 0)
            {
                string err = stderrBuilder.ToString().Trim();
                throw new Exception(command + " failed (exit " + _currentProcess.ExitCode + "): " + err);
            }
            _currentProcess = null;
        }

        private void ReportProgress(double fraction, string step, string logLine,
            bool isError = false, bool isComplete = false)
        {
            ProgressUpdated?.Invoke(this, new OpenFoamProgress
            {
                Fraction = fraction,
                Step = step,
                LogLine = logLine,
                IsError = isError,
                IsComplete = isComplete
            });
        }
    }
}
