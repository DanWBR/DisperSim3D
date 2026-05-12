using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DisperSim3D.Core;
using DisperSim3D.Models;
using DisperSim3D.Validation;

namespace DisperSim3D.CLI
{
    /// <summary>
    /// Command-line driver for DisperSim 3D. Modes:
    /// <list type="bullet">
    ///   <item><c>--list</c> — dump every section of a project (gases, sources +
    ///   IOGP inventory + leak frequency, wind fields, simulations, dispersion
    ///   studies + risk weights, detector allocations + risk results, wind rose).</item>
    ///   <item><c>--list-gpus</c> — enumerate OpenCL devices visible to FluidX3D.</item>
    ///   <item><c>--list-iogp [type]</c> — dump the embedded IOGP 434-01 leak
    ///   frequency table for one equipment type or all 24.</item>
    ///   <item><c>--iogp-selftest</c> — verify the IOGP table against published values.</item>
    ///   <item><c>--memory-estimate &lt;solver&gt; &lt;N&gt;</c> — print VRAM / RAM /
    ///   disk estimate for a solver at an N³/2 grid before committing.</item>
    ///   <item><c>--simulation &lt;name|id&gt;</c> — run a project Simulation
    ///   end-to-end. FluidX3D solvers are dispatched in-process via the GPU
    ///   bridge; OpenFOAM solvers via the external environment.</item>
    ///   <item><c>-s &lt;solver&gt;</c> — bypass Simulations and run the active
    ///   <see cref="DispersionScenario"/> with a chosen solver name. Includes
    ///   the FluidX3D family.</item>
    ///   <item><c>--allocation &lt;name|id&gt;</c> — re-run a saved detector
    ///   allocation (max-coverage or min-residual-risk) and write the
    ///   results back into the project file.</item>
    ///   <item><c>--validate &lt;path&gt;</c> — Hanna SPM benchmark harness.</item>
    /// </list>
    /// </summary>
    class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.WriteLine("DisperSim 3D - Command Line Interface");
            Console.WriteLine();

            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                PrintUsage();
                return 0;
            }

            string filePath = null;
            string solver = null;
            string openFoamPath = null;
            string wslDistro = "Ubuntu";
            int scenarioIndex = -1;
            int gridRes = 0;
            int nProcs = 0;
            string envType = null;
            string simulationSelector = null;
            bool listOnly = false;
            string validatePath = null;
            bool listGpus = false;
            bool iogpSelftest = false;
            bool geometrySelftest = false;
            string listIogpType = null;        // null = "no --list-iogp"; "" = all 24; otherwise the type name
            bool memoryEstimate = false;
            string memSolverArg = null;
            int memNxArg = 0;
            int gpuDeviceId = int.MinValue;     // sentinel: unspecified
            string allocationSelector = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--file":
                    case "-f":
                        if (i + 1 < args.Length) filePath = args[++i];
                        break;
                    case "--solver":
                    case "-s":
                        if (i + 1 < args.Length) solver = args[++i];
                        break;
                    case "--openfoam-path":
                        if (i + 1 < args.Length) openFoamPath = args[++i];
                        break;
                    case "--wsl-distro":
                        if (i + 1 < args.Length) wslDistro = args[++i];
                        break;
                    case "--env":
                        if (i + 1 < args.Length) envType = args[++i];
                        break;
                    case "--scenario":
                        if (i + 1 < args.Length) scenarioIndex = int.Parse(args[++i]);
                        break;
                    case "--simulation":
                        if (i + 1 < args.Length) simulationSelector = args[++i];
                        break;
                    case "--list":
                    case "-l":
                        listOnly = true;
                        break;
                    case "--validate":
                        if (i + 1 < args.Length) validatePath = args[++i];
                        break;
                    case "--grid":
                        if (i + 1 < args.Length) gridRes = int.Parse(args[++i]);
                        break;
                    case "--nprocs":
                        if (i + 1 < args.Length) nProcs = int.Parse(args[++i]);
                        break;
                    case "--list-gpus":
                        listGpus = true;
                        break;
                    case "--iogp-selftest":
                        iogpSelftest = true;
                        break;
                    case "--geometry-selftest":
                        geometrySelftest = true;
                        break;
                    case "--list-iogp":
                        // Optional positional: equipment type name. We peek the
                        // next arg; if it starts with '-' it's the next flag.
                        listIogpType = "";
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                            listIogpType = args[++i];
                        break;
                    case "--memory-estimate":
                        memoryEstimate = true;
                        if (i + 1 < args.Length) memSolverArg = args[++i];
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedN))
                            { memNxArg = parsedN; i++; }
                        break;
                    case "--gpu-device":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int devId))
                            gpuDeviceId = devId;
                        break;
                    case "--allocation":
                        if (i + 1 < args.Length) allocationSelector = args[++i];
                        break;
                }
            }

            // ── Standalone modes (don't need a project file) ──────────────
            if (listGpus) return RunListGpus();
            if (iogpSelftest) return RunIogpSelfTest();
            if (geometrySelftest) return RunGeometrySelfTest();
            if (listIogpType != null) return RunListIogp(listIogpType);
            if (memoryEstimate) return RunMemoryEstimate(memSolverArg, memNxArg);

            if (gpuDeviceId != int.MinValue)
            {
                AppSettings.Instance.PreferredComputeDeviceId = gpuDeviceId;
                Console.WriteLine("FluidX3D compute device set to id " + gpuDeviceId);
            }

            // --validate is a stand-alone mode (doesn't need a project file).
            if (!string.IsNullOrEmpty(validatePath))
            {
                return RunValidate(validatePath, BuildEnvConfig(envType, openFoamPath, wslDistro, nProcs));
            }

            if (filePath == null)
            {
                if (args.Length > 0 && !args[0].StartsWith("-") && (File.Exists(args[0]) || Directory.Exists(args[0])))
                    filePath = args[0];
                else
                {
                    Console.Error.WriteLine("Error: no project file specified.");
                    PrintUsage();
                    return 1;
                }
            }

            if (!File.Exists(filePath) && !Directory.Exists(filePath))
            {
                Console.Error.WriteLine("Error: file not found: " + filePath);
                return 1;
            }

            // --list: open project, dump structure, exit.
            if (listOnly)
            {
                return ListProject(filePath);
            }

            // --allocation: rerun a saved DetectorAllocation against the project's clouds.
            if (!string.IsNullOrEmpty(allocationSelector))
            {
                return RunAllocation(filePath, allocationSelector);
            }

            // FluidX3D solvers and analytical solvers don't need an OpenFOAM
            // environment; only OpenFOAM solver names trigger the env probe.
            CfdConfiguration cfdConfig = null;
            bool needsCfd = solver != null &&
                solver != "plume" && solver != "steadystate" && solver != "gaussian-plume" &&
                solver != "puff" && solver != "gaussian-puff" &&
                !solver.StartsWith("fluidx3d", StringComparison.OrdinalIgnoreCase);

            if (needsCfd || openFoamPath != null || envType != null || simulationSelector != null)
            {
                cfdConfig = new CfdConfiguration();
                if (!string.IsNullOrEmpty(openFoamPath))
                    cfdConfig.OpenFoamPath = openFoamPath;
                if (!string.IsNullOrEmpty(wslDistro))
                    cfdConfig.WslDistroName = wslDistro;
                if (gridRes > 0)
                    cfdConfig.GridResolution = gridRes;
                if (nProcs > 0)
                    cfdConfig.NumberOfProcessors = nProcs;

                if (!string.IsNullOrEmpty(envType))
                {
                    switch (envType.ToLowerInvariant())
                    {
                        case "wsl": case "wsl2":
                            cfdConfig.DetectedEnvironment = OpenFoamEnvironmentType.WSL2;
                            break;
                        case "docker":
                            cfdConfig.DetectedEnvironment = OpenFoamEnvironmentType.Docker;
                            break;
                        case "native":
                            cfdConfig.DetectedEnvironment = OpenFoamEnvironmentType.NativeWindows;
                            break;
                        case "bluecfd":
                            cfdConfig.DetectedEnvironment = OpenFoamEnvironmentType.BlueCFD;
                            break;
                    }
                }
            }

            // read-case mode (existing OpenFOAM case folder, not a project)
            if (solver == "read-case" && filePath != null && Directory.Exists(filePath))
            {
                Console.WriteLine("Reading existing case: " + filePath);
                int nx = gridRes > 0 ? gridRes : 40;
                int ny = nx;
                int nz = nx / 2;
                var dirs = Directory.GetDirectories(filePath);
                foreach (var dir in dirs)
                {
                    string name = Path.GetFileName(dir);
                    double timeVal;
                    if (double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out timeVal) && timeVal > 0)
                    {
                        string sPath = Path.Combine(dir, "s");
                        if (!File.Exists(sPath)) continue;
                        Console.WriteLine("  Loading t=" + timeVal + " from " + sPath);
                        var field = OpenFoamResultReader.LoadSingleTimestep(sPath, nx, ny, nz);
                        if (field == null)
                        {
                            Console.WriteLine("    -> null (failed to load)");
                            continue;
                        }
                        double maxC = 0;
                        for (int i = 0; i < nx; i++)
                            for (int j = 0; j < ny; j++)
                                for (int k = 0; k < nz; k++)
                                    if (field[i, j, k] > maxC) maxC = field[i, j, k];
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "    -> maxC = {0:G6}, field size = [{1},{2},{3}]",
                            maxC, field.GetLength(0), field.GetLength(1), field.GetLength(2)));
                    }
                }
                return 0;
            }

            try
            {
                var result = HeadlessRunner.RunFromFile(filePath, solver, cfdConfig,
                    scenarioIndex, msg => Console.WriteLine(msg),
                    simulationSelector: simulationSelector);

                Console.WriteLine();
                if (result.Success)
                {
                    Console.WriteLine("=== Result ===");
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  Max concentration: {0:G6} kg/m^3", result.MaxConcentration));
                    if (result.TimeStepCount > 0)
                        Console.WriteLine("  Time steps: " + result.TimeStepCount);
                    if (!string.IsNullOrEmpty(result.CasePath))
                        Console.WriteLine("  Case path: " + result.CasePath);
                    if (result.MonitorData.Count > 0)
                    {
                        Console.WriteLine("  Monitor data:");
                        foreach (var md in result.MonitorData)
                        {
                            double maxC = 0;
                            foreach (var c in md.Concentrations)
                                if (c > maxC) maxC = c;
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "    {0}: max = {1:G6} kg/m^3", md.Name, maxC));
                        }
                    }
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine("FAILED: " + result.Error);
                    return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                return 3;
            }
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        static CfdConfiguration BuildEnvConfig(string envType, string openFoamPath, string wslDistro, int nProcs)
        {
            var cfg = new CfdConfiguration();
            if (!string.IsNullOrEmpty(openFoamPath)) cfg.OpenFoamPath = openFoamPath;
            if (!string.IsNullOrEmpty(wslDistro)) cfg.WslDistroName = wslDistro;
            if (nProcs > 0) cfg.NumberOfProcessors = nProcs;
            if (!string.IsNullOrEmpty(envType))
            {
                switch (envType.ToLowerInvariant())
                {
                    case "wsl": case "wsl2": cfg.DetectedEnvironment = OpenFoamEnvironmentType.WSL2; break;
                    case "docker": cfg.DetectedEnvironment = OpenFoamEnvironmentType.Docker; break;
                    case "native": cfg.DetectedEnvironment = OpenFoamEnvironmentType.NativeWindows; break;
                    case "bluecfd": cfg.DetectedEnvironment = OpenFoamEnvironmentType.BlueCFD; break;
                }
            }
            return cfg;
        }

        // ─── New CLI modes ──────────────────────────────────────────────────

        /// <summary>Enumerates OpenCL devices the FluidX3D bridge can see. Useful
        /// before pinning <c>--gpu-device &lt;id&gt;</c> on a multi-GPU machine.</summary>
        static int RunListGpus()
        {
            try
            {
                string json = FluidX3DBridge.ListDevicesJson();
                if (string.IsNullOrEmpty(json))
                {
                    Console.Error.WriteLine("FluidX3DBridge returned no device list.");
                    if (!string.IsNullOrEmpty(FluidX3DBridge.LastListDevicesError))
                        Console.Error.WriteLine("  Last error: " + FluidX3DBridge.LastListDevicesError);
                    return 1;
                }
                Console.WriteLine("OpenCL devices visible to FluidX3D:");
                Console.WriteLine();
                Console.WriteLine(json);
                Console.WriteLine();
                Console.WriteLine("Pick one with: --gpu-device <id>");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to list GPUs: " + ex.Message);
                return 1;
            }
        }

        /// <summary>Wrapper around <see cref="IogpTableTests.RunAll"/>. Returns 0
        /// on full pass, 1 on any failure (matching the App's behaviour).</summary>
        static int RunIogpSelfTest()
        {
            try
            {
                Console.Write(IogpTableTests.RunAll());
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        /// <summary>Wrapper around <see cref="GeometrySelfTest.RunAndPrint"/>.
        /// Returns 0 when every portable Point3D / Vector3D test passes, 1 otherwise.
        /// Used as a CI smoke that the engine's geometry primitives match WPF
        /// behaviour after the cross-platform port.</summary>
        static int RunGeometrySelfTest()
        {
            try
            {
                return GeometrySelfTest.RunAndPrint(Console.Out) ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        /// <summary>Dumps the embedded IOGP 434-01 leak frequency table for one
        /// or every equipment type. Output format is plain text columns suitable
        /// for piping to a spreadsheet.</summary>
        static int RunListIogp(string equipmentType)
        {
            var types = new List<IogpEquipmentType>();
            if (string.IsNullOrEmpty(equipmentType))
            {
                foreach (IogpEquipmentType t in Enum.GetValues(typeof(IogpEquipmentType)))
                    types.Add(t);
            }
            else
            {
                if (!Enum.TryParse(equipmentType, true, out IogpEquipmentType parsed))
                {
                    Console.Error.WriteLine("Unknown IOGP equipment type: " + equipmentType);
                    Console.Error.WriteLine("Valid values:");
                    foreach (IogpEquipmentType t in Enum.GetValues(typeof(IogpEquipmentType)))
                        Console.Error.WriteLine("  " + t);
                    return 1;
                }
                types.Add(parsed);
            }

            Console.WriteLine("IOGP 434-01 Process Release Frequencies (2006–2015 dataset)");
            Console.WriteLine("Reference: IOGP Report 434-01 v1.1 (May 2021)");
            Console.WriteLine();
            string fmt = "{0,-32} {1,8} {2,12} {3,12} {4,12} {5,12} {6,12}";
            foreach (var t in types)
            {
                Console.WriteLine("Equipment: " + t);
                Console.WriteLine(string.Format(fmt, "Hole band", "GM (mm)",
                    "50 mm", "150 mm", "300 mm", "600 mm", "900 mm"));
                foreach (IogpHoleSizeBand b in Enum.GetValues(typeof(IogpHoleSizeBand)))
                {
                    double gm = IogpFrequencyTable.GeometricMeanHoleSizeMm(b);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, fmt,
                        IogpFrequencyTable.DescribeBand(b),
                        gm.ToString("F2", CultureInfo.InvariantCulture),
                        Fmt(IogpFrequencyTable.FrequencyFor(t, 50,  b)),
                        Fmt(IogpFrequencyTable.FrequencyFor(t, 150, b)),
                        Fmt(IogpFrequencyTable.FrequencyFor(t, 300, b)),
                        Fmt(IogpFrequencyTable.FrequencyFor(t, 600, b)),
                        Fmt(IogpFrequencyTable.FrequencyFor(t, 900, b))));
                }
                Console.WriteLine();
            }
            return 0;
        }

        /// <summary>Prints the memory estimate for a chosen solver at a given
        /// grid size — same numbers the in-app "Memory Estimator" tab shows.
        /// Useful before kicking off a big run to confirm it fits in VRAM/RAM.
        /// Grid defaults to N×N×N/2 (the dispersion-runner default aspect).</summary>
        static int RunMemoryEstimate(string solverName, int n)
        {
            if (string.IsNullOrEmpty(solverName) || n <= 0)
            {
                Console.Error.WriteLine("Usage: --memory-estimate <solver> <N>");
                Console.Error.WriteLine("  N is the grid cell count along X (Y = X, Z = X/2).");
                Console.Error.WriteLine("  solver: one of " + string.Join(", ", Enum.GetNames(typeof(CfdSolverType))));
                return 1;
            }
            if (!Enum.TryParse(solverName, true, out CfdSolverType solverType))
            {
                Console.Error.WriteLine("Unknown solver: " + solverName);
                return 1;
            }
            // MemoryEstimator.For takes (solver, requestedGridRes, snapshotCount,
            // FluidX3DQuality). For OpenFOAM the requestedGridRes is the literal N;
            // for FluidX3D solvers it's a base resolution scaled by the quality
            // preset. We default snapshotCount=30 (arbitrary "typical run")
            // and quality=Fast (matches the default in the dialog).
            int snapshotCount = 30;
            var est = MemoryEstimator.For(solverType, n, snapshotCount,
                FluidX3DQuality.Fast);
            Console.WriteLine("Memory estimate for " + solverType + " at "
                + est.Nx + "×" + est.Ny + "×" + est.Nz
                + " (" + snapshotCount + " snapshots, quality=Fast):");
            Console.WriteLine("  VRAM: " + MemoryEstimator.HumanBytes(est.VRamBytes));
            Console.WriteLine("  RAM : " + MemoryEstimator.HumanBytes(est.RamBytes));
            Console.WriteLine("  Disk: " + MemoryEstimator.HumanBytes(est.DiskBytes));
            if (!string.IsNullOrEmpty(est.Notes))
                Console.WriteLine("  Notes: " + est.Notes);
            return 0;
        }

        /// <summary>Re-runs a saved <see cref="DetectorAllocation"/> against the
        /// project's current clouds and prints the new result. The CLI does NOT
        /// currently write the updated allocation back to the project file —
        /// the save path lives in the WPF editor control and would need to be
        /// extracted to a static helper first. For now the user can reopen the
        /// project in the app and rerun to persist; this mode is most useful
        /// for headless QRA reporting where the result is consumed via stdout.</summary>
        static int RunAllocation(string projectPath, string selector)
        {
            try
            {
                Console.WriteLine("Loading project: " + projectPath);
                var scene = SceneFileLoader.Load(projectPath);

                var alloc = scene.DetectorAllocations.FirstOrDefault(a =>
                       string.Equals(a.Id, selector, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a.Name, selector, StringComparison.OrdinalIgnoreCase));
                if (alloc == null)
                {
                    Console.Error.WriteLine("Allocation not found: " + selector);
                    return 1;
                }
                var study = scene.DispersionStudies.FirstOrDefault(s => s.Id == alloc.DispersionStudyId);
                if (study == null)
                {
                    Console.Error.WriteLine("Allocation's DispersionStudy not found: " + alloc.DispersionStudyId);
                    return 1;
                }
                Console.WriteLine("Allocation: " + alloc.Name + "  Strategy: " + alloc.Strategy);
                Console.WriteLine("Study:      " + study.Name + "  ("
                    + study.SimulationIds.Count + " sims)");
                Console.WriteLine();

                Console.WriteLine("Loading clouds...");
                var clouds = DispersionStudyEngine.LoadClouds(study, scene);
                Console.WriteLine(string.Format("  {0} of {1} clouds valid", clouds.Count(c => c.IsValid), clouds.Count));

                var obstacles = new List<BoundingBox>();
                foreach (var d in scene.Decorations)
                    if (d != null && d.BoundingBox != null) obstacles.Add(d.BoundingBox);

                double domainHalf = clouds.Where(c => c.IsValid).Select(c => c.DomainHalfM)
                    .DefaultIfEmpty(200.0).Max();

                Console.WriteLine("Running allocator...");
                var r = DetectorAllocator.Run(alloc, study, scene,
                    clouds, obstacles, scene.GasDetectors, domainHalf);

                alloc.AllocatedPositions = r.Positions;
                alloc.AchievedCoveragePercent = r.CoveragePercent;
                alloc.PerCloudCovered = r.PerCloudCovered;
                alloc.TotalRisk = r.TotalRisk;
                alloc.ResidualRisk = r.ResidualRisk;
                alloc.RiskReductionFraction = r.RiskReductionFraction;
                alloc.RiskCurveK = r.RiskCurveK;
                alloc.RiskCurveRRF = r.RiskCurveRRF;
                alloc.PerCloudResidualRisk = r.PerCloudResidualRisk;
                alloc.Status = AllocationStatus.Completed;
                alloc.StatusMessage = r.Message;
                alloc.RunAt = DateTime.Now;

                Console.WriteLine();
                Console.WriteLine("=== Result ===");
                Console.WriteLine(r.Message);
                Console.WriteLine("  Detectors placed: " + r.Positions.Count);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  Coverage:         {0:F1}%", r.CoveragePercent));
                if (alloc.Strategy == AllocationStrategy.MinResidualRisk)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  Total risk:       {0:E3}", r.TotalRisk));
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  Residual risk:    {0:E3}", r.ResidualRisk));
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  Risk reduction:   {0:P1}", r.RiskReductionFraction));
                }
                foreach (var p in r.Positions)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "    @ ({0:F2}, {1:F2}, {2:F2}) m",
                        p.X, p.Y, p.Z));
                Console.WriteLine();
                Console.WriteLine("(Result not written back to project file — reopen in the "
                    + "DisperSim3D.App UI and Rerun the allocation to persist.)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to run allocation: " + ex.Message);
                return 2;
            }
        }

        /// <summary>
        /// --validate mode. Accepts a single .dsbench file or a directory containing many.
        /// Returns 0 when EVERY benchmark passes, 2 otherwise.
        /// </summary>
        static int RunValidate(string path, CfdConfiguration envConfig)
        {
            var files = new System.Collections.Generic.List<string>();
            if (Directory.Exists(path))
                files.AddRange(Directory.EnumerateFiles(path, "*.dsbench", SearchOption.AllDirectories));
            else if (File.Exists(path))
                files.Add(path);
            else
            {
                Console.Error.WriteLine("Error: validate path not found: " + path);
                return 1;
            }
            if (files.Count == 0)
            {
                Console.Error.WriteLine("No .dsbench files found under " + path);
                return 1;
            }

            int passed = 0, failed = 0, errored = 0;
            var rows = new System.Collections.Generic.List<string[]>();
            foreach (var f in files)
            {
                Console.WriteLine();
                Console.WriteLine("=== " + Path.GetFileName(f) + " ===");
                ValidationReport report;
                try
                {
                    var spec = BenchmarkLoader.Load(f);
                    report = ValidationRunner.Run(spec, envConfig, msg => Console.WriteLine("  " + msg));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("  Failed to load/run: " + ex.Message);
                    errored++;
                    rows.Add(new[] { Path.GetFileNameWithoutExtension(f), "—", "—", "—", "—", "—", "ERROR" });
                    continue;
                }

                if (!report.Success)
                {
                    Console.Error.WriteLine("  ERROR: " + report.ErrorMessage);
                    errored++;
                    rows.Add(new[] { report.Benchmark?.Name ?? "(?)", "—", "—", "—", "—", "—", "ERROR" });
                    continue;
                }
                bool pass = report.Pass;
                if (pass) passed++; else failed++;
                rows.Add(new[]
                {
                    report.Benchmark?.Name ?? "(?)",
                    Fmt(report.Spm.MRB), Fmt(report.Spm.RMSE),
                    Fmt(report.Spm.FAC2), Fmt(report.Spm.MG), Fmt(report.Spm.VG),
                    pass ? "PASS" : "FAIL"
                });
                Console.WriteLine("  Result: " + (pass ? "PASS" : "FAIL")
                    + string.Format(CultureInfo.InvariantCulture,
                        "   MRB={0:G4}  RMSE={1:G4}  FAC2={2:G4}  MG={3:G4}  VG={4:G4}",
                        report.Spm.MRB, report.Spm.RMSE, report.Spm.FAC2,
                        report.Spm.MG, report.Spm.VG));
            }

            Console.WriteLine();
            Console.WriteLine("=== Summary ===");
            string sFmt = "{0,-32} {1,8} {2,8} {3,8} {4,8} {5,8}   {6}";
            Console.WriteLine(string.Format(sFmt, "Benchmark", "MRB", "RMSE", "FAC2", "MG", "VG", "Result"));
            foreach (var r in rows)
                Console.WriteLine(string.Format(sFmt, r[0], r[1], r[2], r[3], r[4], r[5], r[6]));
            Console.WriteLine();
            Console.WriteLine(string.Format("Passed: {0}  Failed: {1}  Errored: {2}  Total: {3}",
                passed, failed, errored, files.Count));
            return (failed == 0 && errored == 0) ? 0 : 2;
        }

        static string Fmt(double v) =>
            double.IsNaN(v) ? "NaN" : v.ToString("G4", CultureInfo.InvariantCulture);

        /// <summary>
        /// Loads the project and prints every section: gases, sources +
        /// equipment inventory + IOGP-derived leak frequency, wind fields,
        /// simulations, dispersion studies + scenario risk weights, detector
        /// allocations + risk reduction results, wind rose, monitors,
        /// detectors. Useful before invoking --simulation / --allocation to
        /// discover the names / IDs.
        /// </summary>
        static int ListProject(string filePath)
        {
            try
            {
                Console.WriteLine("Project: " + filePath);
                var scene = SceneFileLoader.Load(filePath);

                Console.WriteLine();
                Console.WriteLine("Name:        " + (scene.Name ?? ""));
                Console.WriteLine("Description: " + (scene.Description ?? ""));

                if (scene.GeneralSettings != null && scene.GeneralSettings.DefaultMeteo != null)
                {
                    var m = scene.GeneralSettings.DefaultMeteo;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "Default meteo: U={0} m/s @ {1} deg, stability {2}, T={3} K, z0={4} m",
                        m.WindSpeed, m.WindDirectionDeg, m.StabilityClass, m.AmbientTemperature, m.RoughnessLengthM));
                }

                Console.WriteLine();
                Console.WriteLine("Gas Library (" + scene.GasLibrary.Count + "):");
                foreach (var g in scene.GasLibrary)
                    Console.WriteLine("  - " + g.Name + " [" + g.Kind + "]"
                        + (g.IsCryogenic ? " (cryogenic)" : "")
                        + "  Id=" + g.Id);

                Console.WriteLine();
                Console.WriteLine("Top-level Sources (" + scene.TopLevelSources.Count + "):");
                foreach (var s in scene.TopLevelSources)
                {
                    var gas = !string.IsNullOrEmpty(s.GasRefId)
                        ? scene.GasLibrary.FirstOrDefault(g => g.Id == s.GasRefId)?.Name
                        : "(no gas)";
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  - {0}  Gas={1}  Pos=({2:G4},{3:G4},{4:G4})  Rate={5:G6} kg/s  Id={6}",
                        s.Name, gas, s.Position.X, s.Position.Y, s.Position.Z,
                        s.ReleaseRateKgPerS, s.Id));
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "      Az/El: {0:F1}°/{1:F1}°  HoleSize: {2}  LeakFreq: {3:E3} events/yr  ({4})",
                        s.ReleaseAzimuthDeg, s.ReleaseElevationDeg,
                        s.HoleSizeBand,
                        s.EffectiveLeakFrequencyPerYear,
                        s.AutoComputeLeakFrequency ? "auto from inventory" : "manual override"));
                    if (s.EquipmentInventory != null && s.EquipmentInventory.Count > 0)
                    {
                        Console.WriteLine("      Inventory:");
                        foreach (var item in s.EquipmentInventory)
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "        - {0}  d={1:F0} mm  ×{2:G4}{3}{4}",
                                item.Type, item.NominalDiameterMm, item.Count,
                                item.IsPipeLength ? " m" : "",
                                string.IsNullOrEmpty(item.Note) ? "" : "  // " + item.Note));
                    }
                }

                Console.WriteLine();
                Console.WriteLine("Wind Field Scenarios (" + scene.WindFieldScenarios.Count + "):");
                foreach (var w in scene.WindFieldScenarios)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  - {0}  [{1}]  U={2} m/s @ {3} deg  {4}  Id={5}",
                        w.Name, w.Status, w.Meteo.WindSpeed, w.Meteo.WindDirectionDeg,
                        w.UseFluidX3D ? "(FluidX3D)" : "(OpenFOAM)", w.Id));

                Console.WriteLine();
                Console.WriteLine("Simulations (" + scene.Simulations.Count + "):");
                foreach (var sim in scene.Simulations)
                {
                    var srcName = scene.TopLevelSources.FirstOrDefault(s => s.Id == sim.SourceId)?.Name ?? "(?)";
                    var wfName = scene.WindFieldScenarios.FirstOrDefault(w => w.Id == sim.WindFieldId)?.Name ?? "(?)";
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  - {0}  [{1}]  {2} / {3}  solver={4}  Id={5}",
                        sim.Name, sim.Status, srcName, wfName, sim.SolverType, sim.Id));
                }

                if (scene.DispersionStudies != null && scene.DispersionStudies.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Dispersion Studies (" + scene.DispersionStudies.Count + "):");
                    foreach (var st in scene.DispersionStudies)
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "  - {0}  ({1} sims)  Detect: {2} ≥ {3:G4}  Id={4}",
                            st.Name, st.SimulationIds.Count, st.DetectionQuantity,
                            st.DetectionThreshold, st.Id));
                        if (st.RiskWeights != null && st.RiskWeights.Count > 0)
                        {
                            Console.WriteLine("      Risk weights:");
                            foreach (var kv in st.RiskWeights)
                            {
                                var name = scene.Simulations.FirstOrDefault(s => s.Id == kv.Key)?.Name ?? kv.Key;
                                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                    "        - {0}  freq={1} ({2})  cons={3} ({4})",
                                    name, kv.Value.FreqPerYear, kv.Value.FreqMode,
                                    kv.Value.Consequence, kv.Value.ConsMode));
                            }
                        }
                    }
                }

                if (scene.DetectorAllocations != null && scene.DetectorAllocations.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Detector Allocations (" + scene.DetectorAllocations.Count + "):");
                    foreach (var a in scene.DetectorAllocations)
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "  - {0}  [{1}]  Strategy={2}  R={3:G4} m  Z=[{4:G4},{5:G4}]  Id={6}",
                            a.Name, a.Status, a.Strategy, a.DetectionRadiusM,
                            a.MinZ, a.MaxZ, a.Id));
                        if (a.Status == AllocationStatus.Completed)
                        {
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "      Coverage: {0:F1}%, {1} detector(s) placed",
                                a.AchievedCoveragePercent, a.AllocatedPositions.Count));
                            if (a.Strategy == AllocationStrategy.MinResidualRisk)
                                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                    "      Total risk: {0:E3}  Residual: {1:E3}  RRF: {2:P1}",
                                    a.TotalRisk, a.ResidualRisk, a.RiskReductionFraction));
                        }
                    }
                }

                if (scene.WindRose != null && scene.WindRose.Bins != null && scene.WindRose.Bins.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Wind Rose (" + scene.WindRose.Bins.Count + " bins):");
                    foreach (var bin in scene.WindRose.Bins)
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "  - {0,5:F1}°  freq={1,5:F1}%  U={2,5:F1} m/s  stab={3}",
                            bin.DirectionDeg, bin.Frequency, bin.WindSpeed, bin.StabilityClass));
                }

                if (scene.MonitorPoints != null && scene.MonitorPoints.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Monitor Points (" + scene.MonitorPoints.Count + "):");
                    foreach (var mp in scene.MonitorPoints)
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "  - {0}  Pos=({1:G4},{2:G4},{3:G4})  Quantity={4}  Id={5}",
                            mp.Name, mp.Position.X, mp.Position.Y, mp.Position.Z,
                            mp.MeasuredQuantity, mp.Id));
                }

                if (scene.GasDetectors != null && scene.GasDetectors.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Gas Detectors (" + scene.GasDetectors.Count + "):");
                    foreach (var d in scene.GasDetectors)
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "  - {0}  Pos=({1:G4},{2:G4},{3:G4})  Quantity={4}  Id={5}",
                            d.Name, d.Position.X, d.Position.Y, d.Position.Z,
                            d.MeasuredQuantity, d.Id));
                }

                Console.WriteLine();
                Console.WriteLine("Legacy DispersionScenarios (" + scene.DispersionScenarios.Count + "):");
                for (int i = 0; i < scene.DispersionScenarios.Count; i++)
                    Console.WriteLine("  [" + i + "] " + scene.DispersionScenarios[i].Name);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error listing project: " + ex.Message);
                return 1;
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage: DisperSim3D.CLI [options] <project-file>");
            Console.WriteLine();
            Console.WriteLine("  Project file: .dsproj (self-contained ZIP, recommended) or legacy .xml");
            Console.WriteLine();
            Console.WriteLine("Project I/O:");
            Console.WriteLine("  -f, --file <path>          Project file (.dsproj or .xml)");
            Console.WriteLine("  -l, --list                 List every section of the project (gases,");
            Console.WriteLine("                             sources + IOGP inventory + leak frequency,");
            Console.WriteLine("                             wind fields, simulations, studies, detector");
            Console.WriteLine("                             allocations, wind rose, monitors, detectors).");
            Console.WriteLine();
            Console.WriteLine("Run modes:");
            Console.WriteLine("  --simulation <name|id>     Run a project Simulation end-to-end. FluidX3D");
            Console.WriteLine("                             solvers route through the GPU bridge.");
            Console.WriteLine("  --allocation <name|id>     Re-run a saved DetectorAllocation against the");
            Console.WriteLine("                             current project clouds and print the result");
            Console.WriteLine("                             (read-only; reopen in the app to persist).");
            Console.WriteLine("  --validate <file|dir>      Validate against one or more .dsbench files.");
            Console.WriteLine("                             Computes Hanna SPMs and exits 0 if all pass.");
            Console.WriteLine("  -s, --solver <name>        Override solver: plume, puff,");
            Console.WriteLine("                             scalarTransportFoam, scalarSimpleFoam,");
            Console.WriteLine("                             pimpleFoam, buoyantPimpleFoam, reactingFoam,");
            Console.WriteLine("                             rhoSimpleFoam, rhoReactingBuoyantFoam,");
            Console.WriteLine("                             fluidx3dDispersion, fluidx3dDispersionSteady,");
            Console.WriteLine("                             fluidx3dFire.");
            Console.WriteLine();
            Console.WriteLine("Diagnostics:");
            Console.WriteLine("  --list-gpus                Enumerate OpenCL devices the FluidX3D bridge");
            Console.WriteLine("                             can see (use --gpu-device to pin one).");
            Console.WriteLine("  --iogp-selftest            Verify the embedded IOGP 434-01 table against");
            Console.WriteLine("                             the published values; exit 0 on full pass.");
            Console.WriteLine("  --list-iogp [type]         Dump IOGP leak-frequency table (one type or");
            Console.WriteLine("                             all 24). e.g. --list-iogp SteelProcessPipe");
            Console.WriteLine("  --memory-estimate <solver> <N>");
            Console.WriteLine("                             Print VRAM/RAM/disk for a solver at N×N×N/2.");
            Console.WriteLine();
            Console.WriteLine("OpenFOAM environment:");
            Console.WriteLine("  --env <type>               wsl, docker, native, bluecfd");
            Console.WriteLine("  --openfoam-path <path>     Path to OpenFOAM installation");
            Console.WriteLine("  --wsl-distro <name>        WSL distribution name (default: Ubuntu)");
            Console.WriteLine();
            Console.WriteLine("Tuning:");
            Console.WriteLine("  --gpu-device <id>          Preferred FluidX3D OpenCL device (-1 = auto)");
            Console.WriteLine("  --grid <N>                 Grid resolution override");
            Console.WriteLine("  --nprocs <N>               Number of parallel processors (OpenFOAM)");
            Console.WriteLine("  --scenario <index>         Legacy DispersionScenario index (old XML)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  DisperSim3D.CLI project.dsproj --list");
            Console.WriteLine("  DisperSim3D.CLI project.dsproj --simulation \"Stack#1 5m/s SW\"");
            Console.WriteLine("                                 --env wsl --openfoam-path /opt/openfoam2412");
            Console.WriteLine("  DisperSim3D.CLI project.dsproj --simulation \"Fast LBM\" -s fluidx3dDispersion --gpu-device 0");
            Console.WriteLine("  DisperSim3D.CLI project.dsproj --allocation \"Site A — risk\"");
            Console.WriteLine("  DisperSim3D.CLI --list-gpus");
            Console.WriteLine("  DisperSim3D.CLI --iogp-selftest");
            Console.WriteLine("  DisperSim3D.CLI --list-iogp FlangedJoint");
            Console.WriteLine("  DisperSim3D.CLI --memory-estimate FluidX3DDispersion 128");
            Console.WriteLine("  DisperSim3D.CLI --validate benchmarks/");
        }
    }
}
