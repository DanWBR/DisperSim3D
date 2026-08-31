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
            CfdSolverType solverType = CfdSolverType.RhoReactingBuoyantFoam)
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

            // Only RhoReactingBuoyantFoam is supported now (all the older
            // OpenFOAM variants — scalarTransportFoam, pimpleFoam,
            // buoyantPimpleFoam, reactingFoam, rhoSimpleFoam, scalarSimpleFoam —
            // were removed as redundant). The patched WSL build replaces this
            // binary at dispatch time when UsePatchedSctSolver is set.
            string solverCommand = "rhoReactingBuoyantFoam";

            // Vu 2019 cryogenic LNG path: when the user (or the preset) asks for
            // the patched Sct-aware binary, the solver step is dispatched to a
            // WSL-built rhoReactingBuoyantFoamSct that reads Sct from
            // transportProperties. blockMesh / topoSet / decomposePar /
            // reconstructPar still run on the configured _env. Only applies to
            // rhoReactingBuoyantFoam — other solvers are unaffected.
            bool useWslPatchedSolver = solverType == CfdSolverType.RhoReactingBuoyantFoam
                && config != null
                && config.UsePatchedSctSolver
                && !string.IsNullOrEmpty(config.PatchedSctSolverBinary);

            _worker = new BackgroundWorker { WorkerSupportsCancellation = true };
            _worker.DoWork += (s, e) =>
            {
                try
                {
                    ReportProgress(0, "Generating case...", "");
                    _casePath = OpenFoamCaseGenerator.GenerateRhoReactingBuoyantFoam(scenario, config);

                    if (!string.IsNullOrEmpty(_casePath))
                        TempManager.RegisterActive(_casePath);

                    if (_worker.CancellationPending) { e.Cancel = true; return; }

                    ReportProgress(0.02, "Running blockMesh...", "");
                    RunStep("blockMesh");
                    if (_worker.CancellationPending) { e.Cancel = true; return; }

                    if (System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "refineMeshDict")))
                    {
                        for (int rl = 0; rl < 4; rl++)
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

                    // Default topoSetDict creates the `sourceZone_N` cellSets used
                    // by setFields and the scalarSemiImplicitSource fvOption. For
                    // the cryogenic-patch path these are skipped entirely (the
                    // gasInlet patch handles injection), so the file may not exist.
                    if (scenario.Sources.Count > 0 &&
                        System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "topoSetDict")))
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

                    // Cryogenic patch injection (Vu §5.3.1 method) — promote the
                    // ground faces inside each spill pool footprint to their own
                    // `gasInlet_N` patch via topoSet + createPatch. The 0/* files
                    // were written with matching boundaryField entries up-front,
                    // so the solver finds the new patches when it starts.
                    if (System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "createPatchDict")))
                    {
                        ReportProgress(0.065, "Creating gasInlet patches (topoSet)...", "");
                        RunStep("topoSet -dict system/topoSetDict_gasInlet");
                        if (_worker.CancellationPending) { e.Cancel = true; return; }
                        ReportProgress(0.07, "Creating gasInlet patches (createPatch)...", "");
                        RunStep("createPatch -overwrite");
                        if (_worker.CancellationPending) { e.Cancel = true; return; }
                    }

                    // ABL precursor — converge atmospheric BL on the ground field
                    // before the transient solver starts the gas release. Only
                    // runs when the case writer emitted controlDict.precursor
                    // (i.e. CfdConfiguration.UseAblPrecursor was true).
                    if (config != null && config.UseAblPrecursor &&
                        System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "controlDict.precursor")))
                    {
                        ReportProgress(0.07, "Running ABL precursor (buoyantSimpleFoam)...", "");
                        RunAblPrecursor();
                        if (_worker.CancellationPending) { e.Cancel = true; return; }
                    }

                    bool useParallel = _nProcs > 1 && _env.CanRunParallel;
                    if (useParallel)
                    {
                        ReportProgress(0.08, "Running decomposePar...", "");
                        RunStep("decomposePar");
                        if (_worker.CancellationPending) { e.Cancel = true; return; }

                        string solverArg = (useWslPatchedSolver
                            ? config.PatchedSctSolverBinary
                            : solverCommand) + " -parallel";
                        ReportProgress(0.1, string.Format("Running {0} ({1} CPUs){2}...",
                            useWslPatchedSolver ? config.PatchedSctSolverBinary : solverCommand,
                            _nProcs,
                            useWslPatchedSolver ? " [WSL]" : ""), "");
                        if (useWslPatchedSolver)
                            RunWslPatchedSolver(config, _nProcs, solverArg);
                        else
                            RunSolver(_env.BuildMpiCommand(_nProcs, solverArg));
                        if (_worker.CancellationPending) { e.Cancel = true; return; }

                        ReportProgress(0.93, "Running reconstructPar...", "");
                        RunStep("reconstructPar");
                        if (_worker.CancellationPending) { e.Cancel = true; return; }
                    }
                    else
                    {
                        string solverArg = useWslPatchedSolver
                            ? config.PatchedSctSolverBinary
                            : solverCommand;
                        ReportProgress(0.1, string.Format("Running {0}{1}...",
                            solverArg, useWslPatchedSolver ? " [WSL]" : ""), "");
                        if (useWslPatchedSolver)
                            RunWslPatchedSolver(config, 1, solverArg);
                        else
                            RunSolver(solverArg);
                        if (_worker.CancellationPending) { e.Cancel = true; return; }
                    }

                    if (System.IO.File.Exists(System.IO.Path.Combine(_casePath, "system", "refineMeshDict")))
                    {
                        ReportProgress(0.94, "Writing cell centres...", "");
                        try { RunStep("postProcess -func writeCellCentres"); } catch { }
                    }

                    // rhoReactingBuoyantFoam writes the species mass-fraction
                    // field. We use CH4 as the canonical concentration field —
                    // gas-specific overrides can be added back if needed when
                    // we re-enable multi-species cases.
                    string fieldName = "CH4";
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
        /// Legacy steady-state entry point retained only so older UI / CLI
        /// callers that still reference it compile. The steady-state solver
        /// set (scalarSimpleFoam, rhoSimpleFoam, scalarTransportFoamSteady)
        /// was retired — calls now forward to <see cref="RunAsync"/> with
        /// rhoReactingBuoyantFoam (transient run to controlDict.endTime).
        /// Remove once all callers migrate.
        /// </summary>
        [Obsolete("Steady-state OpenFOAM solvers were removed. Use RunAsync with RhoReactingBuoyantFoam instead.", error: false)]
        public void RunSteadyAsync(DispersionScenario scenario, CfdConfiguration config, CfdSolverType solverType)
        {
            RunAsync(scenario, config, CfdSolverType.RhoReactingBuoyantFoam);
        }

        // Steady-state body and RunSteadySolverAsync helper removed along with
        // the steady-state solver variants. The transient rhoReactingBuoyantFoam
        // can be configured to run to a residual-converged endTime for the same
        // effect.

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
                for (int rl = 0; rl < 4; rl++)
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

            // Mirror stdout/stderr to log.<solver> in the case dir — mpiexec eats rank
            // stderr, so this is our only diagnostic when a parallel solver crashes.
            string solverName = command.TrimStart().Split(' ')[0];
            if (solverName == "mpiexec" || solverName == "mpirun")
            {
                var parts = command.Split(' ');
                if (parts.Length > 3) solverName = parts[3];
            }
            string solverLogPath = System.IO.Path.Combine(_casePath, "log." + solverName);
            System.IO.StreamWriter logWriter = null;
            try { logWriter = new System.IO.StreamWriter(solverLogPath, false) { AutoFlush = true }; }
            catch { }

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

            try { logWriter?.Dispose(); } catch { }

            const int timeoutMs = 30 * 60 * 1000;
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
                    try
                    {
                        if (System.IO.File.Exists(solverLogPath))
                        {
                            var allLines = System.IO.File.ReadAllLines(solverLogPath);
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
        /// Runs a steady buoyantSimpleFoam ABL precursor on the case in-place:
        /// swaps the four main dicts (controlDict, fvSchemes, fvSolution,
        /// fvOptions) with their .precursor variants, runs the steady solver
        /// in serial, copies converged U / T / p / p_rgh / k / epsilon / nut /
        /// alphat / rho from the final time directory back into 0/ (so the
        /// transient picks them up as initial conditions), wipes the precursor
        /// time directories, and restores the main dicts. Per-step failures
        /// always restore the main dicts to keep the case runnable.
        /// </summary>
        private void RunAblPrecursor()
        {
            string sys = System.IO.Path.Combine(_casePath, "system");
            string[] dicts = { "controlDict", "fvSchemes", "fvSolution", "fvOptions" };

            // 1. Swap main dicts to .main, precursor dicts to active name.
            foreach (var d in dicts)
            {
                string main = System.IO.Path.Combine(sys, d);
                string mainBak = System.IO.Path.Combine(sys, d + ".main");
                string prec = System.IO.Path.Combine(sys, d + ".precursor");
                if (System.IO.File.Exists(main))
                {
                    if (System.IO.File.Exists(mainBak)) System.IO.File.Delete(mainBak);
                    System.IO.File.Move(main, mainBak);
                }
                if (System.IO.File.Exists(prec))
                    System.IO.File.Copy(prec, main, true);
            }

            try
            {
                // 2. Run the precursor solver in serial. buoyantSimpleFoam is
                //    cheap (a few hundred steady SIMPLE iters) and avoids the
                //    decomposePar / reconstructPar round trip for this step.
                RunSolver("buoyantSimpleFoam");

                // 3. Copy converged fields from the latest time dir into 0/.
                //    Skip the dict-side files (controlDict etc. live in system,
                //    not in time dirs) and any species fields a future solver
                //    might dump there.
                string latest = FindLatestNumericTimeDir(_casePath, excludeZero: true);
                if (latest != null)
                {
                    string zeroDir = System.IO.Path.Combine(_casePath, "0");
                    string[] copyFields = { "U", "T", "p", "p_rgh", "k", "epsilon", "nut", "alphat", "rho" };
                    foreach (var f in copyFields)
                    {
                        string src = System.IO.Path.Combine(latest, f);
                        if (!System.IO.File.Exists(src)) continue;
                        string dst = System.IO.Path.Combine(zeroDir, f);
                        System.IO.File.Copy(src, dst, true);
                    }
                    ReportProgress(-1, null, "ABL precursor: copied converged fields from " +
                        System.IO.Path.GetFileName(latest) + " back to 0/");
                }

                // 4. Remove all numeric time dirs except 0 so the transient
                //    starts clean from the (now-overwritten) 0/ fields.
                foreach (var dir in System.IO.Directory.GetDirectories(_casePath))
                {
                    string name = System.IO.Path.GetFileName(dir);
                    if (IsNumericTimeDirName(name) && name != "0")
                    {
                        try { System.IO.Directory.Delete(dir, true); } catch { }
                    }
                }
            }
            finally
            {
                // 5. Always restore main dicts, even if the precursor failed.
                foreach (var d in dicts)
                {
                    string main = System.IO.Path.Combine(sys, d);
                    string mainBak = System.IO.Path.Combine(sys, d + ".main");
                    if (System.IO.File.Exists(mainBak))
                    {
                        if (System.IO.File.Exists(main)) System.IO.File.Delete(main);
                        System.IO.File.Move(mainBak, main);
                    }
                }
            }
        }

        private static string FindLatestNumericTimeDir(string casePath, bool excludeZero)
        {
            string latest = null;
            double latestVal = double.MinValue;
            foreach (var dir in System.IO.Directory.GetDirectories(casePath))
            {
                string name = System.IO.Path.GetFileName(dir);
                if (!IsNumericTimeDirName(name)) continue;
                if (!double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) continue;
                if (excludeZero && v == 0) continue;
                if (v > latestVal) { latestVal = v; latest = dir; }
            }
            return latest;
        }

        private static bool IsNumericTimeDirName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        /// <summary>
        /// Runs the user-built patched solver (e.g. rhoReactingBuoyantFoamSct)
        /// inside WSL, bypassing the native OpenFOAM environment for this step
        /// only. blockMesh, topoSet, decomposePar, reconstructPar still run via
        /// the configured _env. The case directory is shared via /mnt/c.
        /// </summary>
        private void RunWslPatchedSolver(Models.CfdConfiguration config, int nProcs, string solverArg)
        {
            string linuxCase = OpenFoamEnvironment.WindowsToWslPath(_casePath);
            string distro = string.IsNullOrEmpty(config.PatchedSctSolverWslDistro)
                ? "Ubuntu" : config.PatchedSctSolverWslDistro;
            // Explicit setting wins; otherwise take the install the environment was
            // configured with, so both dispatch paths agree on which OpenFOAM runs.
            // The literal is the last resort, for a project saved before the
            // environment carried a path.
            string bashrc = config.PatchedSctSolverBashrc;
            if (string.IsNullOrEmpty(bashrc))
            {
                string envRoot = _env != null ? _env.WslOpenFoamRoot() : "";
                bashrc = string.IsNullOrEmpty(envRoot)
                    ? "/usr/lib/openfoam/openfoam2412/etc/bashrc"
                    : envRoot + "/etc/bashrc";
            }

            // mpirun lives under $WM_PROJECT_DIR/.../bin after sourcing bashrc.
            string runCmd = nProcs > 1
                ? "mpirun -np " + nProcs + " " + solverArg
                : solverArg;

            string bashCmd = ". '" + bashrc + "' && " +
                "cd '" + linuxCase + "' && " + runCmd;

            var psi = new ProcessStartInfo
            {
                FileName = "wsl",
                // -e for the same reason as StartWSL2Command: no outer login shell to
                // expand variables out of the script before bash sees it.
                Arguments = "-d " + distro + " -e bash -c \"" + bashCmd.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            _currentProcess = Process.Start(psi);

            // Reuse the same regex-driven progress parsing as the native solver path.
            var timeRegex = new Regex(@"^Time\s*=\s*([\d.eE+-]+)", RegexOptions.Compiled);
            var residualRegex = new Regex(@"Solving for (\w+).*Final residual\s*=\s*([\d.eE+-]+)", RegexOptions.Compiled);
            var courantRegex = new Regex(@"Courant Number mean:\s*([\d.eE+-]+)\s+max:\s*([\d.eE+-]+)", RegexOptions.Compiled);
            var deltaTRegex = new Regex(@"deltaT\s*=\s*([\d.eE+-]+)", RegexOptions.Compiled);

            string logPath = System.IO.Path.Combine(_casePath, "log." + config.PatchedSctSolverBinary);
            System.IO.StreamWriter logWriter = null;
            try { logWriter = new System.IO.StreamWriter(logPath, false) { AutoFlush = true }; }
            catch { }

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
            string lastCourant = "";
            string lastDeltaT = "";

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
                        double fraction = 0.1 + 0.85 * (t / _endTime);
                        string step = "Solving [WSL] (t=" + t.ToString("F2") + "/" +
                            _endTime.ToString("F0") + "s)";
                        if (!string.IsNullOrEmpty(lastDeltaT)) step += "  dt=" + lastDeltaT;
                        if (!string.IsNullOrEmpty(lastCourant)) step += "  Co=" + lastCourant;
                        if (!string.IsNullOrEmpty(lastResidual)) step += "  res=" + lastResidual;
                        ReportProgress(fraction, step, line);
                    }
                }
                else
                {
                    var m = residualRegex.Match(line);
                    if (m.Success) { lastResidual = m.Groups[2].Value; ReportProgress(-1, null, line); }
                    else
                    {
                        var c = courantRegex.Match(line);
                        if (c.Success) { lastCourant = c.Groups[2].Value; ReportProgress(-1, null, line); }
                        else
                        {
                            var d = deltaTRegex.Match(line);
                            if (d.Success) { lastDeltaT = d.Groups[1].Value; ReportProgress(-1, null, line); }
                        }
                    }
                }

                if (_worker.CancellationPending)
                {
                    try { _currentProcess.Kill(); } catch { }
                    return;
                }
            }
            try { logWriter?.Dispose(); } catch { }

            const int timeoutMs = 30 * 60 * 1000;
            if (!_currentProcess.WaitForExit(timeoutMs))
            {
                try { _currentProcess.Kill(); } catch { }
                throw new Exception("WSL patched solver timed out after " + (timeoutMs / 60000) + " min");
            }
            if (_currentProcess.ExitCode != 0)
            {
                string err = stderrBuilder.ToString().Trim();
                throw new Exception("WSL " + config.PatchedSctSolverBinary +
                    " failed (exit " + _currentProcess.ExitCode + "): " + err);
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
