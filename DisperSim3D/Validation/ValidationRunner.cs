using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DisperSim3D.Geometry;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// Builds a self-contained <see cref="Scene3D"/> from a <see cref="BenchmarkSpec"/>,
    /// runs it via the appropriate engine, samples concentration at each declared sensor
    /// position, and returns a <see cref="ValidationReport"/>.
    /// </summary>
    public static class ValidationRunner
    {
        public static ValidationReport Run(BenchmarkSpec spec, CfdConfiguration envConfig = null,
            Action<string> log = null)
        {
            var report = new ValidationReport { Benchmark = spec };
            var sw = Stopwatch.StartNew();

            try
            {
                var solverType = spec.ResolveSolverType();
                var scene = BuildScene(spec);
                log?.Invoke("Benchmark '" + spec.Name + "' solver=" + solverType +
                            " sensors=" + spec.Sensors.Count);

                List<SensorPair> pairs;
                HeadlessResult cfdResult = null;
                if (solverType == CfdSolverType.GaussianPlume)
                {
                    pairs = SampleGaussianPlume(spec, scene);
                    report.Success = true;
                }
                else if (solverType == CfdSolverType.GaussianPuff)
                {
                    pairs = SampleGaussianPuff(spec, scene);
                    report.Success = true;
                }
                else
                {
                    var sim = scene.Simulations[0];
                    // Carry env settings (OpenFOAM path, distro, env type) into the snapshot.
                    if (envConfig != null && sim.SnapshotCfdConfig != null)
                    {
                        sim.SnapshotCfdConfig.OpenFoamPath = envConfig.OpenFoamPath;
                        sim.SnapshotCfdConfig.WslDistroName = envConfig.WslDistroName;
                        sim.SnapshotCfdConfig.DetectedEnvironment = envConfig.DetectedEnvironment;
                        if (envConfig.NumberOfProcessors > 0)
                            sim.SnapshotCfdConfig.NumberOfProcessors = envConfig.NumberOfProcessors;
                    }

                    // FluidX3D dispersion needs a pre-computed wind field on the scene.
                    if (solverType == CfdSolverType.FluidX3DDispersion
                        || solverType == CfdSolverType.FluidX3DDispersionSteady)
                    {
                        var wf = scene.WindFieldScenarios.Count > 0 ? scene.WindFieldScenarios[0] : null;
                        if (wf != null && wf.WindField == null)
                        {
                            log?.Invoke("Pre-running FluidX3D wind field (GPU LBM)...");
                            wf.UseFluidX3D = true;
                            var windRunner = new FluidX3DWindFieldRunner();
                            bool windOk = windRunner.Run(wf, new System.Collections.Generic.List<BoundingBox>(),
                                (frac, msg) => log?.Invoke("  [wind] " + msg));
                            if (!windOk)
                            {
                                report.Success = false;
                                report.ErrorMessage = "Wind field failed: " + wf.StatusMessage;
                                report.RunDuration = sw.Elapsed;
                                return report;
                            }
                            log?.Invoke("Wind field ready: " + wf.StatusMessage);
                        }
                    }

                    cfdResult = HeadlessRunner.RunSimulation(scene, sim, log);
                    // If the runner failed because its hard-coded species name (e.g. "CH4")
                    // was missing in the case (e.g. SF6 bench injected only SF6), but the
                    // case actually ran to completion, we can still sample the bench's
                    // declared field. Detect this by CasePath existing with timesteps.
                    bool caseHasTimesteps = !string.IsNullOrEmpty(cfdResult.CasePath)
                        && System.IO.Directory.Exists(cfdResult.CasePath)
                        && System.IO.Directory.EnumerateDirectories(cfdResult.CasePath)
                            .Any(d => double.TryParse(System.IO.Path.GetFileName(d),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var t) && t > 0);
                    if (!cfdResult.Success && !caseHasTimesteps)
                    {
                        report.Success = false;
                        report.ErrorMessage = cfdResult.Error;
                        report.RunDuration = sw.Elapsed;
                        return report;
                    }
                    report.CasePath = cfdResult.CasePath;
                    pairs = SampleCfd(spec, scene, cfdResult);
                    report.Success = true;
                }

                report.Spm = SpmCalculator.Compute(pairs);

                // Cloud volume validation (optional — only when the bench declares an expected volume).
                if (spec.ExpectedCloudVolumeM3.HasValue && spec.ExpectedCloudVolumeM3.Value > 0
                    && spec.Source?.Gas != null && spec.Source.Gas.Lfl > 0 && spec.Source.Gas.Ufl > spec.Source.Gas.Lfl)
                {
                    double[,,] field = null;
                    double cellX = 0, cellY = 0, cellZ = 0;

                    if (solverType == CfdSolverType.GaussianPlume || solverType == CfdSolverType.GaussianPuff)
                    {
                        field = RasteriseGaussian(spec, scene, solverType);
                        double half = spec.Domain.SizeM;
                        int gNx = field.GetLength(0);
                        cellX = cellY = 2.0 * half / gNx;
                        cellZ = half / field.GetLength(2);
                    }
                    else
                    {
                        field = ExtractConcentrationField(spec, cfdResult);
                        if (field != null)
                        {
                            double half = spec.Domain.SizeM;
                            int fNx = field.GetLength(0);
                            int fNz = field.GetLength(2);
                            cellX = cellY = 2.0 * half / fNx;
                            double height = half;
                            cellZ = height / fNz;
                        }
                    }

                    if (field != null)
                    {
                        var cloud = FlammableCloudCalculator.Compute(field, cellX, cellY, cellZ,
                            spec.Source.Gas.Lfl, spec.Source.Gas.Ufl);
                        report.Spm.PredictedCloudVolumeM3 = cloud.VolumeM3;
                        report.Spm.ExpectedCloudVolumeM3 = spec.ExpectedCloudVolumeM3.Value;
                        report.Spm.CloudVolumeRatio = spec.ExpectedCloudVolumeM3.Value > 0
                            ? cloud.VolumeM3 / spec.ExpectedCloudVolumeM3.Value
                            : double.NaN;
                        log?.Invoke(string.Format("  Cloud volume: predicted={0:G4} m³  expected={1:G4} m³  ratio={2:G3}",
                            cloud.VolumeM3, spec.ExpectedCloudVolumeM3.Value, report.Spm.CloudVolumeRatio));
                    }
                }

                if (log != null)
                    foreach (var p in pairs)
                        log("  " + p.Name + ": pred=" + p.Predicted.ToString("G4")
                            + " obs=" + p.Observed.ToString("G4")
                            + " ratio=" + (p.Observed != 0 ? (p.Predicted / p.Observed).ToString("G3") : "n/a"));
            }
            catch (Exception ex)
            {
                report.Success = false;
                report.ErrorMessage = ex.Message;
            }
            finally
            {
                report.RunDuration = sw.Elapsed;
            }
            return report;
        }

        public static Scene3D BuildScenePublic(BenchmarkSpec spec) => BuildScene(spec);

        // ── Scene construction ──

        private static Scene3D BuildScene(BenchmarkSpec spec)
        {
            var scene = new Scene3D { Name = spec.Name };

            // Gas
            var gasItem = new GasLibraryItem
            {
                Name = spec.Source.Gas?.Name ?? "Gas",
                Kind = GasLibraryItemKind.Pure,
                IsCryogenic = spec.Source.Gas?.IsCryogenic ?? false,
                PureGas = new GasProperties
                {
                    Name = spec.Source.Gas?.Name ?? "Gas",
                    MolarMass = spec.Source.Gas?.MolarMass ?? 0.029,
                    LFL = spec.Source.Gas?.Lfl ?? 0,
                    UFL = spec.Source.Gas?.Ufl ?? 0
                }
            };
            scene.GasLibrary.Add(gasItem);

            // Align release direction with wind so the Gaussian engine doesn't trigger
            // its bent-plume branch on a low-momentum source (engine quirk: any non-zero
            // angle between release and wind activates the bend, even at exitVel=0).
            // Wind direction in MeteorologicalConditions is "FROM" — release "TO" is
            // (windFrom + 180) mod 360, then mapped through Source.ReleaseAzimuthDeg
            // (azimuth = 0 means north / +y; azimuth = 90 means east / +x).
            double releaseAz = (spec.Meteo.WindDirectionDeg - 180.0) % 360.0;
            if (releaseAz < 0) releaseAz += 360.0;

            // Source
            var src = new ReleaseSource3D
            {
                Name = spec.Source.Name ?? "Source",
                GasRefId = gasItem.Id,
                Position = new Point3D(
                    spec.Source.Position[0], spec.Source.Position[1], spec.Source.Position[2]),
                ReleaseRateKgPerS = spec.Source.ReleaseRateKgPerS,
                ReleaseDurationS = spec.Source.ReleaseDurationS,
                StackDiameterM = spec.Source.StackDiameterM,
                ExitTemperatureK = spec.Source.ExitTemperatureK > 0
                    ? spec.Source.ExitTemperatureK : 293.15,
                ExitVelocityMPerS = spec.Source.ExitVelocityMPerS,
                ReleaseAzimuthDeg = releaseAz,
                ReleaseElevationDeg = 0,
                ReleaseHeightOffset = 0
            };
            // Mirror legacy Gas property so engines that don't go through GasRefId still see it.
            src.Gas = gasItem.PureGas;
            scene.TopLevelSources.Add(src);

            // Meteo
            var meteo = new MeteorologicalConditions
            {
                WindSpeed = spec.Meteo.WindSpeed,
                WindDirectionDeg = spec.Meteo.WindDirectionDeg,
                StabilityClass = ParseStability(spec.Meteo.Stability),
                AmbientTemperature = spec.Meteo.AmbientTemperature,
                AmbientPressure = spec.Meteo.AmbientPressure,
                WindMeasurementHeightM = spec.Meteo.WindMeasurementHeightM,
                RoughnessLengthM = spec.Meteo.RoughnessLengthM
            };

            // Wind field (acts as the meteo carrier; only used for CFD pairs)
            var wf = new WindFieldScenario
            {
                Name = "bench-wind",
                Meteo = meteo,
                DomainSizeM = spec.Domain.SizeM,
                DomainHeightM = spec.Domain.SizeM,
                GridResolution = spec.Domain.GridResolution
            };
            CfdConfigurationPresets.ApplyForSolver(wf.CfdConfig, CfdSolverType.ScalarSimpleFoam, gasItem, meteo);
            scene.WindFieldScenarios.Add(wf);

            // Simulation
            var sim = new Simulation
            {
                Name = "bench-sim",
                SourceId = src.Id,
                WindFieldId = wf.Id,
                SolverType = spec.ResolveSolverType(),
                SnapshotSource = src,
                SnapshotMeteo = meteo,
                SnapshotGas = gasItem,
                SnapshotDomainSizeM = spec.Domain.SizeM,
                SnapshotGridResolution = spec.Domain.GridResolution,
                SnapshotDurationS = spec.Domain.DurationS,
                SnapshotTimeStepS = spec.Domain.TimeStepS,
                SnapshotCfdConfig = new CfdConfiguration()
            };
            CfdConfigurationPresets.ApplyForSolver(sim.SnapshotCfdConfig, sim.SolverType, gasItem, meteo);
            // Bench grid resolution wins over the CfdConfiguration default — HeadlessRunner.RunCfd
            // copies cfdConfig.GridResolution onto scenario.GridResolution, so we have to set it here.
            sim.SnapshotCfdConfig.GridResolution = spec.Domain.GridResolution;
            scene.Simulations.Add(sim);

            // Monitor points (so the engine has them registered if needed downstream)
            foreach (var s in spec.Sensors)
            {
                scene.MonitorPoints.Add(new MonitorPoint3D
                {
                    Name = s.Name,
                    Position = new Point3D(s.Position[0], s.Position[1], s.Position[2])
                });
            }
            return scene;
        }

        private static PasquillStabilityClass ParseStability(string s)
        {
            if (string.IsNullOrEmpty(s)) return PasquillStabilityClass.D;
            PasquillStabilityClass c;
            return Enum.TryParse(s, true, out c) ? c : PasquillStabilityClass.D;
        }

        // ── Gaussian sampling ──

        private static List<SensorPair> SampleGaussianPlume(BenchmarkSpec spec, Scene3D scene)
        {
            var transient = ToScenario(spec, scene);
            var engine = new GaussianPlumeEngine();
            engine.Initialize(transient);
            return SamplePoints(spec, (x, y, z) => engine.EvaluateConcentration(x, y, z));
        }

        private static List<SensorPair> SampleGaussianPuff(BenchmarkSpec spec, Scene3D scene)
        {
            var transient = ToScenario(spec, scene);
            var engine = new GaussianPuffEngine();
            engine.Initialize(transient);

            bool peakKind = string.Equals(spec.ConcentrationKind, "PeakOverTime",
                StringComparison.OrdinalIgnoreCase);
            if (!peakKind)
            {
                engine.StepTo(spec.Domain.DurationS);
                return SamplePoints(spec, (x, y, z) => engine.EvaluateConcentration(x, y, z));
            }

            var maxByIdx = new double[spec.Sensors.Count];
            double dt = Math.Max(spec.Domain.TimeStepS, 0.1);
            int steps = Math.Max(1, (int)(spec.Domain.DurationS / dt));
            for (int s = 0; s <= steps; s++)
            {
                engine.StepTo(s * dt);
                for (int i = 0; i < spec.Sensors.Count; i++)
                {
                    var pos = spec.Sensors[i].Position;
                    double c = engine.EvaluateConcentration(pos[0], pos[1], pos[2]);
                    if (c > maxByIdx[i]) maxByIdx[i] = c;
                }
            }
            var pairs = new List<SensorPair>(spec.Sensors.Count);
            for (int i = 0; i < spec.Sensors.Count; i++)
            {
                pairs.Add(new SensorPair
                {
                    Name = spec.Sensors[i].Name,
                    Predicted = maxByIdx[i],
                    Observed = spec.Sensors[i].MeasuredKgM3
                });
            }
            return pairs;
        }

        // ── CFD sampling ──

        private static List<SensorPair> SampleCfd(BenchmarkSpec spec, Scene3D scene,
            HeadlessResult cfdResult)
        {
            int nx = spec.Domain.GridResolution;
            int ny = nx;
            int nz = Math.Max(1, nx / 2);
            double half = spec.Domain.SizeM;

            bool peakKind = string.Equals(spec.ConcentrationKind, "PeakOverTime",
                StringComparison.OrdinalIgnoreCase);

            // FluidX3D runners populate ConcentrationField in-memory (no OpenFOAM
            // case directory). Use it directly when available.
            if (cfdResult.ConcentrationField != null)
            {
                var fld = new OpenFoamConcentrationField(cfdResult.ConcentrationField, half,
                    cfdResult.ConcentrationField.GetLength(0));
                return SamplePoints(spec, (x, y, z) => fld.EvaluateConcentration(x, y, z));
            }

            if (!string.IsNullOrEmpty(cfdResult.CasePath)
                && System.IO.Directory.Exists(cfdResult.CasePath))
            {
                var fullResult = OpenFoamResultReader.ReadResults(
                    cfdResult.CasePath, nx, ny, nz, half,
                    scalarFieldName: spec.ResolveConcentrationField());
                if (fullResult != null && fullResult.IsLoaded && fullResult.TimeSteps.Count > 0)
                {
                    if (peakKind)
                    {
                        var maxByIdx = new double[spec.Sensors.Count];
                        foreach (var t in fullResult.TimeSteps)
                        {
                            var f = fullResult.GetField(t);
                            if (f == null) continue;
                            var fld = new OpenFoamConcentrationField(f, half, nx);
                            for (int i = 0; i < spec.Sensors.Count; i++)
                            {
                                var p = spec.Sensors[i].Position;
                                double c = fld.EvaluateConcentration(p[0], p[1], p[2]);
                                if (c > maxByIdx[i]) maxByIdx[i] = c;
                            }
                        }
                        return PairUp(spec, maxByIdx);
                    }
                    else // FinalSnapshot
                    {
                        var lastT = fullResult.TimeSteps[fullResult.TimeSteps.Count - 1];
                        var f = fullResult.GetField(lastT);
                        if (f != null)
                        {
                            var fld = new OpenFoamConcentrationField(f, half, nx);
                            return SamplePoints(spec, (x, y, z) => fld.EvaluateConcentration(x, y, z));
                        }
                    }
                }
            }

            // Fallback: zero out (SPM reflects total miss).
            return PairUp(spec, new double[spec.Sensors.Count]);
        }

        private static List<SensorPair> SamplePoints(BenchmarkSpec spec,
            Func<double, double, double, double> evalAt)
        {
            var pairs = new List<SensorPair>(spec.Sensors.Count);
            foreach (var s in spec.Sensors)
            {
                double cp = evalAt(s.Position[0], s.Position[1], s.Position[2]);
                pairs.Add(new SensorPair
                {
                    Name = s.Name,
                    Predicted = cp,
                    Observed = s.MeasuredKgM3
                });
            }
            return pairs;
        }

        private static List<SensorPair> PairUp(BenchmarkSpec spec, double[] predicted)
        {
            var pairs = new List<SensorPair>(spec.Sensors.Count);
            for (int i = 0; i < spec.Sensors.Count; i++)
            {
                pairs.Add(new SensorPair
                {
                    Name = spec.Sensors[i].Name,
                    Predicted = predicted[i],
                    Observed = spec.Sensors[i].MeasuredKgM3
                });
            }
            return pairs;
        }

        // ── cloud volume helpers ──

        private static double[,,] ExtractConcentrationField(BenchmarkSpec spec, HeadlessResult cfdResult)
        {
            if (cfdResult == null) return null;
            if (cfdResult.ConcentrationField != null) return cfdResult.ConcentrationField;

            if (string.IsNullOrEmpty(cfdResult.CasePath) || !System.IO.Directory.Exists(cfdResult.CasePath))
                return null;

            int nx = spec.Domain.GridResolution;
            int ny = nx;
            int nz = Math.Max(1, nx / 2);
            double half = spec.Domain.SizeM;
            var fullResult = OpenFoamResultReader.ReadResults(
                cfdResult.CasePath, nx, ny, nz, half,
                scalarFieldName: spec.ResolveConcentrationField());
            if (fullResult == null || !fullResult.IsLoaded || fullResult.TimeSteps.Count == 0)
                return null;
            return fullResult.GetField(fullResult.TimeSteps[fullResult.TimeSteps.Count - 1]);
        }

        private static double[,,] RasteriseGaussian(BenchmarkSpec spec, Scene3D scene, CfdSolverType solver)
        {
            var transient = ToScenario(spec, scene);
            Func<double, double, double, double> eval;

            if (solver == CfdSolverType.GaussianPuff)
            {
                var engine = new GaussianPuffEngine();
                engine.Initialize(transient);
                engine.StepTo(spec.Domain.DurationS);
                eval = (x, y, z) => engine.EvaluateConcentration(x, y, z);
            }
            else
            {
                var engine = new GaussianPlumeEngine();
                engine.Initialize(transient);
                eval = (x, y, z) => engine.EvaluateConcentration(x, y, z);
            }

            int nx = spec.Domain.GridResolution;
            int ny = nx;
            int nz = Math.Max(1, nx / 2);
            double half = spec.Domain.SizeM;
            double height = half;
            double dx = 2.0 * half / nx;
            double dy = 2.0 * half / ny;
            double dz = height / nz;
            var field = new double[nx, ny, nz];

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        double x = -half + (i + 0.5) * dx;
                        double y = -half + (j + 0.5) * dy;
                        double z = (k + 0.5) * dz;
                        field[i, j, k] = eval(x, y, z);
                    }
            return field;
        }

        // ── helpers ──

        private static DispersionScenario ToScenario(BenchmarkSpec spec, Scene3D scene)
        {
            var meteo = scene.WindFieldScenarios[0].Meteo;
            var src = scene.TopLevelSources[0];
            var sc = new DispersionScenario
            {
                Name = spec.Name,
                Meteo = meteo,
                SimulationDurationS = spec.Domain.DurationS,
                TimeStepS = spec.Domain.TimeStepS,
                DomainSizeM = spec.Domain.SizeM,
                GridResolution = spec.Domain.GridResolution,
                SolverType = spec.ResolveSolverType()
            };
            sc.Sources.Add(src);
            return sc;
        }
    }
}
