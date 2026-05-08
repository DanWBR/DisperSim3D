using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    public enum SimulationJobStatus
    {
        Queued,
        Running,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    public class SimulationJob
    {
        public string Id { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; }
        public DispersionScenario Scenario { get; set; }
        public CfdSolverType SolverType { get; set; }
        public CfdConfiguration CfdConfig { get; set; }
        public SimulationJobStatus Status { get; set; } = SimulationJobStatus.Queued;
        public double Progress { get; set; }
        public string StatusText { get; set; } = "Queued";
        public string LastLogLine { get; set; }
        public DateTime CreatedAt { get; } = DateTime.Now;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public CfdSimulationEntry ResultEntry { get; set; }
        public CancellationTokenSource Cts { get; } = new CancellationTokenSource();
        public ManualResetEventSlim PauseHandle { get; } = new ManualResetEventSlim(true);
        public OpenFoamRunner Runner { get; set; }

        // Context needed by the solver
        public Scene3D Scene { get; set; }
        public OpenFoamEnvironment Environment { get; set; }
        public List<BoundingBox> Obstacles { get; set; }
        public Dictionary<string, double[]> HpLeakProfiles { get; set; }
    }

    public class SimulationManager
    {
        private readonly ConcurrentQueue<SimulationJob> _pendingQueue = new ConcurrentQueue<SimulationJob>();
        private readonly List<SimulationJob> _allJobs = new List<SimulationJob>();
        private readonly object _lock = new object();
        private int _runningCount;
        private int _maxParallel;

        public int MaxParallelJobs
        {
            get => _maxParallel;
            set => _maxParallel = Math.Max(1, value);
        }

        public IReadOnlyList<SimulationJob> AllJobs
        {
            get { lock (_lock) return _allJobs.ToList(); }
        }

        public event EventHandler<SimulationJob> JobStatusChanged;
        public event EventHandler<(SimulationJob Job, OpenFoamProgress Progress)> JobProgressUpdated;
        public event EventHandler<SimulationJob> JobCompleted;

        public SimulationManager(int maxParallel = 2)
        {
            _maxParallel = Math.Max(1, maxParallel);
        }

        public SimulationJob Enqueue(DispersionScenario scenario, CfdSolverType solverType,
            CfdConfiguration config, Scene3D scene, OpenFoamEnvironment env,
            List<BoundingBox> obstacles = null, Dictionary<string, double[]> hpLeakProfiles = null)
        {
            string solverLabel;
            switch (solverType)
            {
                case CfdSolverType.GaussianPlume: solverLabel = "Gaussian Plume"; break;
                case CfdSolverType.GaussianPuff: solverLabel = "Gaussian Puff"; break;
                case CfdSolverType.ScalarTransportFoamSteady: solverLabel = "CFD Steady"; break;
                case CfdSolverType.ScalarSimpleFoam: solverLabel = "CFD SimpleFoam"; break;
                default: solverLabel = "CFD (OpenFOAM)"; break;
            }

            var job = new SimulationJob
            {
                Name = string.Format("{0} — {1}", solverLabel, scenario.Name ?? "Scenario"),
                Scenario = scenario,
                SolverType = solverType,
                CfdConfig = config,
                Scene = scene,
                Environment = env,
                Obstacles = obstacles,
                HpLeakProfiles = hpLeakProfiles
            };

            lock (_lock) _allJobs.Add(job);
            _pendingQueue.Enqueue(job);

            RaiseStatusChanged(job);
            TryStartNext();

            return job;
        }

        public void CancelJob(SimulationJob job)
        {
            if (job.Status == SimulationJobStatus.Queued)
            {
                job.Status = SimulationJobStatus.Cancelled;
                job.StatusText = "Cancelled";
                RaiseStatusChanged(job);
                return;
            }
            if (job.Status == SimulationJobStatus.Paused)
            {
                if (job.Runner?.CurrentProcess != null)
                {
                    try { ProcessSuspend.Resume(job.Runner.CurrentProcess); } catch { }
                }
                job.PauseHandle.Set();
            }
            if (job.Status == SimulationJobStatus.Running || job.Status == SimulationJobStatus.Paused)
            {
                job.Cts.Cancel();
            }
        }

        public void CancelAll()
        {
            lock (_lock)
            {
                foreach (var job in _allJobs)
                {
                    if (job.Status == SimulationJobStatus.Paused)
                    {
                        if (job.Runner?.CurrentProcess != null)
                        {
                            try { ProcessSuspend.Resume(job.Runner.CurrentProcess); } catch { }
                        }
                        job.PauseHandle.Set();
                    }
                    if (job.Status == SimulationJobStatus.Queued || job.Status == SimulationJobStatus.Running
                        || job.Status == SimulationJobStatus.Paused)
                    {
                        job.Cts.Cancel();
                        if (job.Status == SimulationJobStatus.Queued)
                        {
                            job.Status = SimulationJobStatus.Cancelled;
                            job.StatusText = "Cancelled";
                        }
                    }
                }
            }
        }

        public void PauseJob(SimulationJob job)
        {
            if (job.Status != SimulationJobStatus.Running) return;

            job.PauseHandle.Reset();
            job.Status = SimulationJobStatus.Paused;
            job.StatusText = "Paused";
            RaiseStatusChanged(job);

            if (job.Runner?.CurrentProcess != null)
            {
                try { ProcessSuspend.Suspend(job.Runner.CurrentProcess); } catch { }
            }
        }

        public void ResumeJob(SimulationJob job)
        {
            if (job.Status != SimulationJobStatus.Paused) return;

            if (job.Runner?.CurrentProcess != null)
            {
                try { ProcessSuspend.Resume(job.Runner.CurrentProcess); } catch { }
            }

            job.Status = SimulationJobStatus.Running;
            job.StatusText = "Running...";
            job.PauseHandle.Set();
            RaiseStatusChanged(job);
        }

        public void RemoveJob(SimulationJob job)
        {
            if (job.Status == SimulationJobStatus.Running || job.Status == SimulationJobStatus.Paused)
            {
                if (job.Status == SimulationJobStatus.Paused)
                    job.PauseHandle.Set();
                job.Cts.Cancel();
            }
            lock (_lock) _allJobs.Remove(job);
        }

        private void TryStartNext()
        {
            while (true)
            {
                if (Interlocked.CompareExchange(ref _runningCount, 0, 0) >= _maxParallel)
                    return;

                if (!_pendingQueue.TryDequeue(out var job))
                    return;

                if (job.Status != SimulationJobStatus.Queued)
                    continue;

                Interlocked.Increment(ref _runningCount);
                _ = RunJobAsync(job);
            }
        }

        private async Task RunJobAsync(SimulationJob job)
        {
            job.Status = SimulationJobStatus.Running;
            job.StartedAt = DateTime.Now;
            job.StatusText = "Starting...";
            RaiseStatusChanged(job);

            try
            {
                switch (job.SolverType)
                {
                    case CfdSolverType.GaussianPlume:
                        await RunGaussianPlumeAsync(job);
                        break;
                    case CfdSolverType.GaussianPuff:
                        await RunGaussianPuffAsync(job);
                        break;
                    case CfdSolverType.ScalarTransportFoam:
                    case CfdSolverType.ScalarTransportFoamSteady:
                    case CfdSolverType.ScalarSimpleFoam:
                        await RunCfdAsync(job);
                        break;
                }

                if (job.Status == SimulationJobStatus.Running)
                {
                    job.Status = SimulationJobStatus.Completed;
                    job.StatusText = "Completed";
                    job.Progress = 1.0;
                    job.CompletedAt = DateTime.Now;
                    RaiseStatusChanged(job);
                    JobCompleted?.Invoke(this, job);
                }
            }
            catch (OperationCanceledException)
            {
                job.Status = SimulationJobStatus.Cancelled;
                job.StatusText = "Cancelled";
                job.CompletedAt = DateTime.Now;
                RaiseStatusChanged(job);
            }
            catch (Exception ex)
            {
                job.Status = SimulationJobStatus.Failed;
                job.StatusText = "FAILED: " + ex.Message;
                job.LastLogLine = ex.ToString();
                job.CompletedAt = DateTime.Now;
                RaiseStatusChanged(job);
                JobCompleted?.Invoke(this, job);
            }
            finally
            {
                Interlocked.Decrement(ref _runningCount);
                TryStartNext();
            }
        }

        private Task RunGaussianPlumeAsync(SimulationJob job)
        {
            return Task.Run(() =>
            {
                job.Cts.Token.ThrowIfCancellationRequested();

                var scenario = job.Scenario;
                ReportProgress(job, 0.1, "Initializing plume engine...");

                var plume = new GaussianPlumeEngine();
                plume.Initialize(scenario);

                ReportProgress(job, 0.3, "Sampling concentration field...");
                job.PauseHandle.Wait(job.Cts.Token);

                var renderer = new DispersionRenderer();
                renderer.Initialize(scenario);
                renderer.ComputeOccupancyGrid(job.Scene);

                var thresholds = scenario.Thresholds;
                if (thresholds.Count == 0)
                {
                    renderer.ComputeIsosurfaces(plume, thresholds);
                    double maxC = renderer.GetMaxConcentration();
                    if (maxC > 1e-20)
                    {
                        thresholds = new List<DispersionThreshold>
                        {
                            new DispersionThreshold { Name = "High", ConcentrationValue = maxC * 0.1,
                                Color = System.Windows.Media.Colors.Red, Opacity = 0.6, Visible = true },
                            new DispersionThreshold { Name = "Medium", ConcentrationValue = maxC * 0.01,
                                Color = System.Windows.Media.Colors.Orange, Opacity = 0.35, Visible = true },
                            new DispersionThreshold { Name = "Low", ConcentrationValue = maxC * 0.001,
                                Color = System.Windows.Media.Colors.Yellow, Opacity = 0.12, Visible = true }
                        };
                    }
                }

                ReportProgress(job, 0.6, "Generating isosurfaces...");
                job.PauseHandle.Wait(job.Cts.Token);

                var isoGroup = renderer.ComputeIsosurfaces(plume, thresholds);
                var trajectories = plume.GetTrajectoryPaths();

                var contourGroups = new List<System.Windows.Media.Media3D.Model3DGroup>();
                if (scenario.ContourPlanes.Count > 0)
                {
                    double maxConc = renderer.GetMaxConcentration();
                    double dom = renderer.DomainSize;
                    foreach (var cp in scenario.ContourPlanes)
                    {
                        if (!cp.Visible) continue;
                        contourGroups.Add(renderer.ComputeContourPlane(plume, cp, -dom, dom, maxConc));
                    }
                }

                ReportProgress(job, 0.9, "Finalizing...");

                var entry = new CfdSimulationEntry
                {
                    Name = job.Name,
                    ScenarioName = scenario.Name,
                    SolverType = "Gaussian Plume",
                    DurationS = 0,
                    TimeStepCount = 1,
                    GridNx = scenario.GridResolution,
                    GridNy = scenario.GridResolution,
                    GridNz = Math.Max(1, scenario.GridResolution / 2),
                    DomainSizeM = scenario.DomainSizeM,
                    HasResults = true
                };

                job.ResultEntry = entry;
                job.ResultEntry.Tag = new SteadyStateResultData
                {
                    Engine = plume,
                    Renderer = renderer,
                    Thresholds = thresholds,
                    IsoGroup = isoGroup,
                    ContourGroups = contourGroups,
                    Trajectories = trajectories
                };

                ReportProgress(job, 1.0, "Complete");

            }, job.Cts.Token);
        }

        private Task RunGaussianPuffAsync(SimulationJob job)
        {
            return Task.Run(() =>
            {
                var scenario = job.Scenario;
                int nx = scenario.GridResolution;
                int ny = scenario.GridResolution;
                int nz = Math.Max(1, scenario.GridResolution / 2);
                double domain = scenario.DomainSizeM;
                double endTime = scenario.SimulationDurationS;
                double dt = scenario.TimeStepS;
                int totalSteps = (int)Math.Ceiling(endTime / dt);
                int writeEvery = Math.Max(1, totalSteps / 100);

                var engine = new GaussianPuffEngine();
                engine.Initialize(scenario);

                var config = job.CfdConfig ?? new CfdConfiguration();
                bool useWindField = config.UseWindField && job.Environment != null && job.Environment.IsAvailable;

                if (useWindField && job.Obstacles != null && job.Obstacles.Count > 0)
                {
                    try
                    {
                        ReportProgress(job, 0.0, "Computing wind field around obstacles...");
                        var env = job.Environment;
                        var windRunner = new OpenFoamRunner(env);

                        string windCaseDir = OpenFoamCaseGenerator.GenerateWindCase(
                            scenario, config, job.Obstacles);

                        var windField = windRunner.RunWindCase(windCaseDir,
                            nx, ny, nz, -domain, domain, -domain, domain, domain,
                            true,
                            config.NumberOfProcessors > 1 ? config.NumberOfProcessors : 1,
                            (frac, msg) => ReportProgress(job, frac * 0.2, msg));

                        if (windField != null)
                            engine.WindField = windField;
                    }
                    catch (Exception ex)
                    {
                        ReportProgress(job, 0.0, "Wind field failed, using uniform wind: " + ex.Message);
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
                    job.PauseHandle.Wait(job.Cts.Token);

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
                    double fraction = useWindField ? 0.2 + rawFraction * 0.8 : rawFraction;
                    string windLabel = engine.WindField != null ? " [wind field]" : "";
                    ReportProgress(job, fraction,
                        string.Format("Gaussian Puff (t={0:F1}/{1:F0}s) — {2} puffs{3}",
                            t, endTime, engine.ActivePuffs.Count, windLabel));
                }

                result.IsLoaded = result.TimeSteps.Count > 0;

                var entry = new CfdSimulationEntry
                {
                    Name = job.Name,
                    ScenarioName = scenario.Name,
                    SolverType = "Gaussian Puff",
                    CasePath = tempDir,
                    DurationS = endTime,
                    TimeStepCount = result.TimeSteps.Count,
                    GridNx = nx, GridNy = ny, GridNz = nz,
                    DomainSizeM = domain,
                    HasResults = result.IsLoaded
                };
                entry.Tag = result;
                job.ResultEntry = entry;

            }, job.Cts.Token);
        }

        private Task RunCfdAsync(SimulationJob job)
        {
            var tcs = new TaskCompletionSource<bool>();
            var scenario = job.Scenario;
            var config = job.CfdConfig ?? scenario.CfdConfig ?? new CfdConfiguration();
            var env = job.Environment;

            env.Configure(config.OpenFoamPath, config.DetectedEnvironment, config.WslDistroName);
            if (!env.IsAvailable)
            {
                job.Status = SimulationJobStatus.Failed;
                job.StatusText = "OpenFOAM not available: " + env.StatusMessage;
                RaiseStatusChanged(job);
                tcs.SetResult(false);
                return tcs.Task;
            }

            if (config.GridResolution > 0)
                scenario.GridResolution = config.GridResolution;

            var runner = new OpenFoamRunner(env);
            job.Runner = runner;
            runner.ProgressUpdated += (s, p) =>
            {
                job.Progress = p.Fraction;
                job.StatusText = p.Step;
                job.LastLogLine = p.LogLine;
                JobProgressUpdated?.Invoke(this, (job, p));
            };
            runner.Completed += (s, result) =>
            {
                bool isSteady = job.SolverType == CfdSolverType.ScalarTransportFoamSteady
                             || job.SolverType == CfdSolverType.ScalarSimpleFoam;
                string solverLabel = isSteady ? "CFD Steady" : "CFD (OpenFOAM)";

                var entry = new CfdSimulationEntry
                {
                    Name = job.Name,
                    ScenarioName = scenario.Name,
                    SolverType = solverLabel,
                    CasePath = runner.CasePath,
                    DurationS = isSteady ? 0 : scenario.SimulationDurationS,
                    TimeStepCount = result.TimeSteps.Count,
                    GridNx = result.GridNx, GridNy = result.GridNy, GridNz = result.GridNz,
                    DomainSizeM = result.DomainSizeM,
                    HasResults = result.IsLoaded
                };
                entry.Tag = result;
                job.ResultEntry = entry;
                tcs.TrySetResult(true);
            };
            runner.Failed += (s, msg) =>
            {
                job.Status = SimulationJobStatus.Failed;
                job.StatusText = "FAILED: " + msg;
                job.LastLogLine = msg;
                tcs.TrySetException(new Exception(msg));
            };

            job.Cts.Token.Register(() =>
            {
                runner.Cancel();
                tcs.TrySetCanceled();
            });

            if (job.SolverType == CfdSolverType.ScalarTransportFoamSteady ||
                job.SolverType == CfdSolverType.ScalarSimpleFoam)
                runner.RunSteadyAsync(scenario, config, job.SolverType);
            else
                runner.RunAsync(scenario, config);

            return tcs.Task;
        }

        private void ReportProgress(SimulationJob job, double fraction, string step, string logLine = null)
        {
            job.Progress = fraction;
            job.StatusText = step;
            if (logLine != null) job.LastLogLine = logLine;

            var progress = new OpenFoamProgress
            {
                Fraction = fraction,
                Step = step,
                LogLine = logLine ?? job.LastLogLine
            };
            JobProgressUpdated?.Invoke(this, (job, progress));
        }

        private void RaiseStatusChanged(SimulationJob job)
        {
            JobStatusChanged?.Invoke(this, job);
        }
    }

    public class SteadyStateResultData
    {
        public GaussianPlumeEngine Engine { get; set; }
        public DispersionRenderer Renderer { get; set; }
        public List<DispersionThreshold> Thresholds { get; set; }
        public System.Windows.Media.Media3D.Model3DGroup IsoGroup { get; set; }
        public List<System.Windows.Media.Media3D.Model3DGroup> ContourGroups { get; set; }
        public List<List<System.Windows.Media.Media3D.Point3D>> Trajectories { get; set; }
    }

    internal static class ProcessSuspend
    {
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtResumeProcess(IntPtr processHandle);

        public static void Suspend(Process process)
        {
            if (process == null || process.HasExited) return;
            NtSuspendProcess(process.Handle);
        }

        public static void Resume(Process process)
        {
            if (process == null || process.HasExited) return;
            NtResumeProcess(process.Handle);
        }
    }
}
