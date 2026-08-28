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
            bool fireRoundTripSelftest = false;
            bool solidFlameSelftest = false;
            bool flashFireSelftest = false;
            bool thermalDoseSelftest = false;
            bool fireStudySelftest = false;
            bool tracerGpuSelftest = false;
            string listIogpType = null;        // null = "no --list-iogp"; "" = all 24; otherwise the type name
            bool memoryEstimate = false;
            string memSolverArg = null;
            int memNxArg = 0;
            bool twoPhaseTest = false;
            int gpuDeviceId = int.MinValue;     // sentinel: unspecified
            string allocationSelector = null;
            string fieldComparePath = null;
            string windCompareBench = null;
            string windCaseA = null;
            string windCaseB = null;

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
                    case "--tracer-gpu-selftest":
                        tracerGpuSelftest = true;
                        break;
                    case "--gpu-tracer":
                        AppSettings.Instance.UseGpuBuoyantTracerPreferred = true;
                        break;
                    case "--geometry-selftest":
                        geometrySelftest = true;
                        break;
                    case "--fire-roundtrip-selftest":
                        fireRoundTripSelftest = true;
                        break;
                    case "--solid-flame-selftest":
                        solidFlameSelftest = true;
                        break;
                    case "--flash-fire-selftest":
                        flashFireSelftest = true;
                        break;
                    case "--thermal-dose-selftest":
                        thermalDoseSelftest = true;
                        break;
                    case "--fire-study-selftest":
                        fireStudySelftest = true;
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
                    case "--twophase-test":
                        twoPhaseTest = true;
                        break;
                    case "--gpu-device":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int devId))
                            gpuDeviceId = devId;
                        break;
                    case "--allocation":
                        if (i + 1 < args.Length) allocationSelector = args[++i];
                        break;
                    case "--field-compare":
                        if (i + 1 < args.Length) fieldComparePath = args[++i];
                        break;
                    case "--wind-compare":
                        if (i + 1 < args.Length) windCompareBench = args[++i];
                        break;
                    case "--case-a":
                        if (i + 1 < args.Length) windCaseA = args[++i];
                        break;
                    case "--case-b":
                        if (i + 1 < args.Length) windCaseB = args[++i];
                        break;
                }
            }

            // ── Standalone modes (don't need a project file) ──────────────
            if (listGpus) return RunListGpus();
            if (iogpSelftest) return RunIogpSelfTest();
            if (tracerGpuSelftest) return RunTracerGpuSelfTest();
            if (geometrySelftest) return RunGeometrySelfTest();
            if (fireRoundTripSelftest) return RunFireRoundTripSelfTest();
            if (solidFlameSelftest) return RunSolidFlameSelfTest();
            if (flashFireSelftest) return RunFlashFireSelfTest();
            if (thermalDoseSelftest) return RunThermalDoseSelfTest();
            if (fireStudySelftest) return RunFireStudySelfTest();
            if (listIogpType != null) return RunListIogp(listIogpType);
            if (memoryEstimate) return RunMemoryEstimate(memSolverArg, memNxArg);
            if (twoPhaseTest) return RunTwoPhaseTest();

            if (gpuDeviceId != int.MinValue)
            {
                AppSettings.Instance.PreferredComputeDeviceId = gpuDeviceId;
                Console.WriteLine("FluidX3D compute device set to id " + gpuDeviceId);
            }

            // --validate is a stand-alone mode (doesn't need a project file).
            if (!string.IsNullOrEmpty(validatePath))
            {
                var envCfg = BuildEnvConfig(envType, openFoamPath, wslDistro, nProcs);
                return RunValidate(validatePath, envCfg, solver);
            }

            // --field-compare: run the same benchmark with both OpenFOAM and FluidX3D,
            // then compare the resulting concentration fields cell-by-cell.
            if (!string.IsNullOrEmpty(fieldComparePath))
            {
                var envCfg = BuildEnvConfig(envType, openFoamPath, wslDistro, nProcs);
                return RunFieldCompare(fieldComparePath, envCfg);
            }

            if (!string.IsNullOrEmpty(windCompareBench))
            {
                var envCfg = BuildEnvConfig(envType, openFoamPath, wslDistro, nProcs);
                return RunWindCompare(windCompareBench, envCfg, windCaseA, windCaseB);
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

        static double MaxVal(double[,,] f)
        {
            double m = 0;
            int nx = f.GetLength(0), ny = f.GetLength(1), nz = f.GetLength(2);
            for (int k = 0; k < nz; k++)
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                        if (f[i, j, k] > m) m = f[i, j, k];
            return m;
        }

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

        /// <summary>Smoke-test the GPU port of BuoyantTracerEngine: seeds a
        /// Gaussian blob on a 32³ grid, advects 10 steps in uniform wind, checks
        /// the centre-of-mass shifted by U·dt·steps within one cell. Validates
        /// host → device → kernel → device → host plumbing works end-to-end.</summary>
        static int RunTracerGpuSelfTest()
        {
            Console.WriteLine("BuoyantTracerEngineGpu self-test");
            Console.WriteLine();
            if (!FluidX3DBridge.IsAvailable())
            {
                Console.WriteLine("  SKIP: FluidX3D.dll not available — " + FluidX3DBridge.LastAvailabilityError);
                return 1;
            }
            try
            {
                Console.WriteLine("[1/2] Smoke test — Gaussian blob advection");
                var (ok1, msg1) = BuoyantTracerEngineGpu.SelfTest();
                Console.WriteLine("  " + msg1);
                Console.WriteLine();
                Console.WriteLine("[2/2] Cross-validation vs CPU baseline");
                var (ok2, msg2) = BuoyantTracerEngineGpu.CrossValidateVsCpu();
                Console.WriteLine("  " + msg2);
                return (ok1 && ok2) ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ERROR: " + ex.GetType().Name + ": " + ex.Message);
                return 2;
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

        /// <summary>Wrapper around <see cref="FireStudySelfTest.RunAndPrint"/>.
        /// Returns 0 when the fire study scores and ranks its scenarios correctly and
        /// survives a save/load cycle, 1 otherwise.</summary>
        static int RunFireStudySelfTest()
        {
            try
            {
                return FireStudySelfTest.RunAndPrint(Console.Out) ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        /// <summary>Wrapper around <see cref="ThermalDoseSelfTest.RunAndPrint"/>.
        /// Returns 0 when the dose, the probits and the error function match their
        /// published anchors — 20 s at ~18 kW/m² for 1% lethality and ~36 kW/m² for
        /// 50% — 1 otherwise.</summary>
        static int RunThermalDoseSelfTest()
        {
            try
            {
                return ThermalDoseSelfTest.RunAndPrint(Console.Out) ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        /// <summary>Wrapper around <see cref="FlashFireSelfTest.RunAndPrint"/>.
        /// Returns 0 when igniting a synthetic cloud burns exactly the pocket it
        /// should — connectivity, obstacle blocking, envelope extent and burn-back
        /// timing — 1 otherwise.</summary>
        static int RunFlashFireSelfTest()
        {
            try
            {
                return FlashFireSelfTest.RunAndPrint(Console.Out) ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        /// <summary>Wrapper around <see cref="SolidFlameSelfTest.RunAndPrint"/>.
        /// Returns 0 when the solid-flame radiation model passes its physics checks
        /// — panel areas, emissive power, far-field agreement with the point source,
        /// monotonic falloff and the transmissivity curve — 1 otherwise.</summary>
        static int RunSolidFlameSelfTest()
        {
            try
            {
                return SolidFlameSelfTest.RunAndPrint(Console.Out) ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        /// <summary>Wrapper around <see cref="FireRoundTripSelfTest.RunAndPrint"/>.
        /// Returns 0 when a FireScenario survives a save/load cycle unchanged,
        /// 1 otherwise. Guards the SceneFileSaver/SceneFileLoader contract: a
        /// FireSource property written by one side and not read by the other
        /// fails here instead of silently resetting to its default.</summary>
        static int RunFireRoundTripSelfTest()
        {
            try
            {
                return FireRoundTripSelfTest.RunAndPrint(Console.Out) ? 0 : 1;
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

        /// <summary>Smoke-tests the <see cref="TwoPhaseSourceCalculator"/> against
        /// canned pressurized-release scenarios (CO2, NH3, Cl2). When DWSIM is
        /// configured, prints the real-fluid flash result; otherwise the ideal-gas
        /// fallback. Exit code 0 always (informational).</summary>
        static int RunTwoPhaseTest()
        {
            Console.WriteLine("Two-Phase Source Calculator — smoke test");
            Console.WriteLine();

            // DWSIMCore is bundled into the engine (lib/DWSIMCore/); no install path needed.
            DwsimThermo.SetPropertyPackage(AppSettings.Instance.DwsimPropertyPackage);
            bool dwsimOk = DwsimThermo.Initialize();
            Console.WriteLine("DWSIMCore: " + (dwsimOk
                ? "initialised (PP=" + AppSettings.Instance.DwsimPropertyPackage + ")"
                : "NOT initialised (" + DwsimThermo.LastError + ") — analytical fallback active"));
            if (dwsimOk)
            {
                var avail = DwsimThermo.AvailableCompounds();
                if (avail != null && avail.Count > 0)
                {
                    Console.WriteLine("  available compounds: " + avail.Count
                        + " (e.g. " + string.Join(", ", avail.Take(8)) + " ...)");
                    foreach (var probe in new[] { "Carbon dioxide", "Carbon Dioxide", "CO2",
                                                  "Ammonia", "Chlorine", "Methane", "Water" })
                    {
                        bool present = avail.Any(c => string.Equals(c, probe, StringComparison.OrdinalIgnoreCase));
                        Console.WriteLine("    [" + (present ? "OK" : "  ") + "] " + probe);
                    }
                }
            }
            Console.WriteLine();

            // Quick PT flash smoke test — verifies DWSIMCore's PR78 flash works.
            if (dwsimOk)
            {
                Console.WriteLine("--- DWSIMCore PT flash smoke test ---");
                foreach (var (name, T, P) in new[] {
                    ("Carbon dioxide", 290.0, 8.5e6),  // supercritical CO2
                    ("Carbon dioxide", 195.0, 101325.0),  // ~boiling point
                    ("Chlorine", 290.0, 7.5e5),  // pressurized liquid Cl2
                    ("Ammonia", 290.0, 9.0e5),  // pressurized liquid NH3
                    ("Water", 298.0, 101325.0),  // ambient water (liquid)
                })
                {
                    var p = DwsimThermo.ComputeMixtureProperties(
                        new Dictionary<string, double> { [name] = 1.0 }, T, P);
                    Console.WriteLine(string.Format(
                        "  {0,-20} @ T={1,5:F1}K, P={2,9:E2}Pa → ρ={3,7:F2} kg/m³  x_v={4:F3}  M={5:F4} kg/mol{6}",
                        name, T, P, p.DensityKgM3, p.VaporFraction, p.MolarMassKgMol,
                        string.IsNullOrEmpty(p.Error) ? "" : "  ERR: " + p.Error));
                }
                Console.WriteLine();
            }

            var cases = new[]
            {
                new {
                    Name = "Spadeadam DF1 Test 5 (BP cold-liquid CO2, 158 barg / 25.62 mm)",
                    Compound = "Carbon dioxide",
                    Leak = new HighPressureLeakParams
                    {
                        VesselPressurePa = 15.768e6 + 101325, // 157.68 barg → Pa absolute
                        VesselTemperatureK = 278.15,           // 5 °C
                        OrificeDiameterM = 0.02562,
                        VesselVolumeM3 = 50.0,
                        GasGamma = 1.30,
                        GasMolarMassKgMol = 0.04401,
                        AmbientPressurePa = 101325,
                        DischargeCoefficient = 0.65,
                    },
                    AmbientK = 279.0
                },
                new {
                    Name = "CO2PipeHaz INERIS (supercritical CO2, 85 barg / 6 mm)",
                    Compound = "Carbon dioxide",
                    Leak = new HighPressureLeakParams
                    {
                        VesselPressurePa = 8.5e6 + 101325,
                        VesselTemperatureK = 290.0,
                        OrificeDiameterM = 0.006,
                        VesselVolumeM3 = 2.0,
                        GasGamma = 1.30,
                        GasMolarMassKgMol = 0.04401,
                        AmbientPressurePa = 101325,
                        DischargeCoefficient = 0.65,
                    },
                    AmbientK = 290.0
                },
                new {
                    Name = "Desert Tortoise T4 (pressurized liquid NH3, ~9 barg / 94.5 mm)",
                    Compound = "Ammonia",
                    Leak = new HighPressureLeakParams
                    {
                        VesselPressurePa = 9.0e5,
                        VesselTemperatureK = 290.0,
                        OrificeDiameterM = 0.0945,
                        VesselVolumeM3 = 50.0,
                        GasGamma = 1.31,
                        GasMolarMassKgMol = 0.01703,
                        AmbientPressurePa = 91500,
                        DischargeCoefficient = 0.65,
                    },
                    AmbientK = 305.0
                },
                new {
                    Name = "Jack Rabbit II T1 (pressurized liquid Cl2, ~6.5 barg / 152 mm)",
                    Compound = "Chlorine",
                    Leak = new HighPressureLeakParams
                    {
                        VesselPressurePa = 6.5e5 + 101325,
                        VesselTemperatureK = 290.0,
                        OrificeDiameterM = 0.152,
                        VesselVolumeM3 = 8.0,
                        GasGamma = 1.32,
                        GasMolarMassKgMol = 0.07090,
                        AmbientPressurePa = 87000,
                        DischargeCoefficient = 0.65,
                    },
                    AmbientK = 305.0
                },
            };

            foreach (var c in cases)
            {
                Console.WriteLine("--- " + c.Name + " ---");
                var r = TwoPhaseSourceCalculator.Compute(
                    c.Leak, c.Compound, c.Leak.AmbientPressurePa, c.AmbientK);

                if (!string.IsNullOrEmpty(r.Error))
                {
                    Console.WriteLine("  ERROR: " + r.Error);
                    continue;
                }
                Console.WriteLine(string.Format(
                    "  m_dot total      = {0:G4} kg/s",
                    r.VaporMassFlowKgPerS + r.DropletMassFlowKgPerS));
                Console.WriteLine(string.Format(
                    "  vapor fraction   = {0:F3} ({1})",
                    r.VaporFraction, r.IsTwoPhase ? "two-phase" : "single-phase"));
                Console.WriteLine(string.Format(
                    "  m_dot vapor      = {0:G4} kg/s  (to dispersion engine)",
                    r.VaporMassFlowKgPerS));
                Console.WriteLine(string.Format(
                    "  m_dot rainout    = {0:G4} kg/s  (pool re-evaporation)",
                    r.DropletMassFlowKgPerS));
                Console.WriteLine(string.Format(
                    "  T_exit           = {0:F1} K", r.TempExitK));
                Console.WriteLine(string.Format(
                    "  ρ_exit           = {0:F2} kg/m³", r.DensityExitKgM3));
                Console.WriteLine(string.Format(
                    "  D_pseudo (Birch) = {0:F4} m  @ V = {1:F0} m/s",
                    r.DiameterPseudoM, r.VelocityExitMS));
                Console.WriteLine("  DWSIM used       = " + r.DwsimUsed);
                Console.WriteLine("  notes            = " + r.Notes);
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
        static int RunValidate(string path, CfdConfiguration envConfig, string solverOverride = null)
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
                    if (!string.IsNullOrEmpty(solverOverride))
                    {
                        Console.WriteLine("  Solver override: " + solverOverride);
                        spec.Solver = solverOverride;
                    }
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

        static int RunFieldCompare(string benchPath, CfdConfiguration envConfig)
        {
            BenchmarkSpec spec;
            try { spec = BenchmarkLoader.Load(benchPath); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to load benchmark: " + ex.Message);
                return 1;
            }

            Console.WriteLine("Field comparison: " + spec.Name);
            Console.WriteLine("  Declared solver (A): " + spec.Solver);
            Console.WriteLine("  FluidX3D solver (B): FluidX3DDispersion");
            Console.WriteLine();

            var solverA = spec.ResolveSolverType();
            var solverB = CfdSolverType.FluidX3DDispersion;
            int nx = spec.Domain.GridResolution;
            int ny = nx;
            int nz = Math.Max(1, nx / 2);
            double half = spec.Domain.SizeM;
            string fieldName = spec.ResolveConcentrationField();

            Console.WriteLine("--- Running solver A: " + solverA + " ---");
            Scene3D sceneA;
            var resultA = RunSolverForBench(spec, solverA, envConfig, out sceneA);
            if (resultA == null)
            {
                Console.Error.WriteLine("Solver A failed — cannot compare.");
                return 2;
            }
            Console.WriteLine("  Solver A: " + resultA.TimeSteps.Count + " timesteps loaded.");

            Console.WriteLine();
            Console.WriteLine("--- Running solver B: " + solverB + " ---");
            Scene3D sceneB;
            var resultB = RunSolverForBench(spec, solverB, null, out sceneB);
            if (resultB == null)
            {
                Console.Error.WriteLine("Solver B failed — cannot compare.");
                return 2;
            }
            Console.WriteLine("  Solver B: " + resultB.TimeSteps.Count + " timesteps loaded.");

            Console.WriteLine();
            Console.WriteLine("--- Comparing concentration fields (normalized to [0,1]) ---");
            var report = FieldComparer.Compare(resultA, resultB, normalize: true);
            Console.WriteLine(report.ToMarkdown());

            string csvPath = Path.ChangeExtension(benchPath, ".field-compare.csv");
            try
            {
                File.WriteAllText(csvPath, report.ToCsv());
                Console.WriteLine("CSV saved: " + csvPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Could not write CSV: " + ex.Message);
            }

            // --- Wind field comparison ---
            Console.WriteLine();
            Console.WriteLine("--- Comparing wind fields ---");
            WindField3D windA = null, windB = null;
            try
            {
                var wfA = sceneA?.WindFieldScenarios?.Count > 0 ? sceneA.WindFieldScenarios[0] : null;
                var wfB = sceneB?.WindFieldScenarios?.Count > 0 ? sceneB.WindFieldScenarios[0] : null;

                windA = wfA?.WindField;
                if (windA == null && wfA != null)
                    windA = WindFieldRunner.LoadFromCase(wfA);
                if (windA == null && wfA != null)
                    windA = FluidX3DWindFieldRunner.LoadFromCase(wfA);

                windB = wfB?.WindField;
                if (windB == null && wfB != null)
                    windB = FluidX3DWindFieldRunner.LoadFromCase(wfB);
                if (windB == null && wfB != null)
                    windB = WindFieldRunner.LoadFromCase(wfB);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("  Wind field load error: " + ex.Message);
            }

            if (windA != null && windB != null)
            {
                var src = spec.Source;
                double srcX = src?.Position != null && src.Position.Length > 0 ? src.Position[0] : 0;
                double srcY = src?.Position != null && src.Position.Length > 1 ? src.Position[1] : 0;
                double srcZ = src?.Position != null && src.Position.Length > 2 ? src.Position[2] : 0;

                var windReport = WindFieldComparer.Compare(windA, windB,
                    spec.Domain.SizeM, spec.Domain.SizeM,
                    srcX, srcY, srcZ);
                Console.WriteLine(windReport.ToMarkdown());

                string windCsvPath = Path.ChangeExtension(benchPath, ".wind-compare.csv");
                try
                {
                    File.WriteAllText(windCsvPath, windReport.ProfileToCsv());
                    Console.WriteLine("Wind CSV saved: " + windCsvPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Could not write wind CSV: " + ex.Message);
                }
            }
            else
            {
                Console.Error.WriteLine("  Could not load both wind fields for comparison.");
                if (windA == null) Console.Error.WriteLine("    Wind A (solver A): not available");
                if (windB == null) Console.Error.WriteLine("    Wind B (solver B): not available");
            }

            return 0;
        }

        static int RunWindCompare(string benchPath, CfdConfiguration envConfig,
            string casePathA, string casePathB)
        {
            BenchmarkSpec spec;
            try { spec = BenchmarkLoader.Load(benchPath); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to load benchmark: " + ex.Message);
                return 1;
            }

            int nx = spec.Domain.GridResolution;
            int ny = nx;
            int nz = Math.Max(1, nx / 2);
            double half = spec.Domain.SizeM;
            double height = spec.Domain.SizeM;

            Console.WriteLine("Wind field comparison: " + spec.Name);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  Grid: {0}×{1}×{2}, domain: ±{3}m, height: {4}m", nx, ny, nz, half, height));
            Console.WriteLine();

            WindField3D windA = null, windB = null;

            // --- Load wind A (OpenFOAM) ---
            if (!string.IsNullOrEmpty(casePathA) && Directory.Exists(casePathA))
            {
                Console.WriteLine("  Loading wind A from existing case: " + casePathA);
                windA = OpenFoamResultReader.ReadWindField(casePathA, nx, ny, nz,
                    -half, half, -half, half, height);
                if (windA != null)
                    Console.WriteLine("    Loaded ({0}×{1}×{2})", windA.Nx, windA.Ny, windA.Nz);
                else
                    Console.Error.WriteLine("    Failed to read U from case.");
            }
            else
            {
                Console.WriteLine("  No --case-a provided; running OpenFOAM wind field...");
                var scene = ValidationRunner.BuildScenePublic(spec);
                var wf = scene.WindFieldScenarios[0];
                if (envConfig != null)
                {
                    wf.CfdConfig.OpenFoamPath = envConfig.OpenFoamPath;
                    wf.CfdConfig.WslDistroName = envConfig.WslDistroName;
                    wf.CfdConfig.DetectedEnvironment = envConfig.DetectedEnvironment;
                    if (envConfig.NumberOfProcessors > 0)
                        wf.CfdConfig.NumberOfProcessors = envConfig.NumberOfProcessors;
                }
                var env = new OpenFoamEnvironment();
                env.Configure(wf.CfdConfig.OpenFoamPath, wf.CfdConfig.DetectedEnvironment,
                    wf.CfdConfig.WslDistroName);
                var runner = new WindFieldRunner(env);
                bool ok = runner.Run(wf, new List<BoundingBox>(),
                    (frac, msg) => Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "    [{0:P0}] {1}", frac, msg)));
                if (ok) windA = wf.WindField;
                if (windA != null && !string.IsNullOrEmpty(wf.CasePath))
                    Console.WriteLine("    OF wind case: " + wf.CasePath + "  (reuse with --case-a)");
                if (windA == null)
                    Console.Error.WriteLine("    OpenFOAM wind field failed.");
            }

            // --- Load wind B (FluidX3D) ---
            if (!string.IsNullOrEmpty(casePathB) && Directory.Exists(casePathB))
            {
                Console.WriteLine("  Loading wind B from existing case: " + casePathB);
                var wfProxy = new WindFieldScenario
                {
                    CasePath = casePathB,
                    DomainSizeM = half,
                    DomainHeightM = height,
                    GridResolution = nx
                };
                windB = FluidX3DWindFieldRunner.LoadFromCase(wfProxy);
                if (windB != null)
                    Console.WriteLine("    Loaded ({0}×{1}×{2})", windB.Nx, windB.Ny, windB.Nz);
                else
                    Console.Error.WriteLine("    Failed to read windfield.bin from case.");
            }
            else
            {
                Console.WriteLine("  No --case-b provided; running FluidX3D wind field...");
                var scene = ValidationRunner.BuildScenePublic(spec);
                var wf = scene.WindFieldScenarios[0];
                wf.UseFluidX3D = true;
                var runner = new FluidX3DWindFieldRunner();
                bool ok = runner.Run(wf, new List<BoundingBox>(),
                    (frac, msg) => Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "    [{0:P0}] {1}", frac, msg)));
                if (ok) windB = wf.WindField;
                if (windB == null)
                    Console.Error.WriteLine("    FluidX3D wind field failed.");
            }

            if (windA == null || windB == null)
            {
                Console.Error.WriteLine("Cannot compare — missing wind field(s).");
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("--- Comparing wind fields ---");
            var src = spec.Source;
            double srcX = src?.Position != null && src.Position.Length > 0 ? src.Position[0] : 0;
            double srcY = src?.Position != null && src.Position.Length > 1 ? src.Position[1] : 0;
            double srcZ = src?.Position != null && src.Position.Length > 2 ? src.Position[2] : 0;

            var report = WindFieldComparer.Compare(windA, windB, half, height,
                srcX, srcY, srcZ);
            Console.WriteLine(report.ToMarkdown());

            string csvPath = Path.ChangeExtension(benchPath, ".wind-compare.csv");
            try
            {
                File.WriteAllText(csvPath, report.ProfileToCsv());
                Console.WriteLine("Wind CSV saved: " + csvPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Could not write CSV: " + ex.Message);
            }

            // --- Hybrid test: run FluidX3D dispersion with OpenFOAM wind ---
            Console.WriteLine();
            Console.WriteLine("--- Hybrid test: OpenFOAM wind + FluidX3D dispersion ---");
            var hybridScene = ValidationRunner.BuildScenePublic(spec);
            var hybridWf = hybridScene.WindFieldScenarios[0];
            hybridWf.WindField = windA;
            hybridWf.Status = WindFieldStatus.Ready;
            hybridWf.UseFluidX3D = true;

            Scene3D hybridSceneOut;
            var hybridResult = RunSolverForBench(spec, CfdSolverType.FluidX3DDispersion,
                null, out hybridSceneOut, windOverride: windA);

            if (hybridResult != null && hybridResult.TimeSteps.Count > 0)
            {
                Console.WriteLine("  Hybrid: " + hybridResult.TimeSteps.Count + " timesteps loaded.");
                Console.WriteLine("  Max concentration: " +
                    hybridResult.TimeSteps.Max(t => { var f = hybridResult.GetField(t); return f == null ? 0 : MaxVal(f); }).ToString("G4"));

                bool peakKind = string.Equals(spec.ConcentrationKind, "PeakOverTime",
                    StringComparison.OrdinalIgnoreCase);
                var hybridPairs = new List<SensorPair>();

                if (peakKind)
                {
                    var maxByIdx = new double[spec.Sensors.Count];
                    foreach (var t in hybridResult.TimeSteps)
                    {
                        var f = hybridResult.GetField(t);
                        if (f == null) continue;
                        var fld = new OpenFoamConcentrationField(f, half, nx);
                        for (int si = 0; si < spec.Sensors.Count; si++)
                        {
                            var p = spec.Sensors[si].Position;
                            double c = fld.EvaluateConcentration(p[0], p[1], p[2]);
                            if (c > maxByIdx[si]) maxByIdx[si] = c;
                        }
                    }
                    for (int si = 0; si < spec.Sensors.Count; si++)
                        hybridPairs.Add(new SensorPair
                        {
                            Name = spec.Sensors[si].Name,
                            Predicted = maxByIdx[si],
                            Observed = spec.Sensors[si].MeasuredKgM3
                        });
                }
                else
                {
                    var lastT = hybridResult.TimeSteps[hybridResult.TimeSteps.Count - 1];
                    var f = hybridResult.GetField(lastT);
                    if (f != null)
                    {
                        var fld = new OpenFoamConcentrationField(f, half, nx);
                        foreach (var s in spec.Sensors)
                        {
                            double c = fld.EvaluateConcentration(s.Position[0], s.Position[1], s.Position[2]);
                            hybridPairs.Add(new SensorPair
                            {
                                Name = s.Name,
                                Predicted = c,
                                Observed = s.MeasuredKgM3
                            });
                        }
                    }
                }

                if (hybridPairs.Count > 0)
                {
                    var hybridSpm = SpmCalculator.Compute(hybridPairs);
                    Console.WriteLine();
                    Console.WriteLine("--- Hybrid (OF wind + FX3D dispersion) vs measurements ---");
                    Console.WriteLine("  Sensors: " + hybridPairs.Count);
                    foreach (var p in hybridPairs)
                        Console.WriteLine("    {0}: pred={1:G4} obs={2:G4} ratio={3}",
                            p.Name, p.Predicted, p.Observed,
                            p.Observed != 0 ? (p.Predicted / p.Observed).ToString("G3") : "n/a");
                    Console.WriteLine("  SPM: N={0} MRB={1:F4} RMSE={2:G4} NMSE={3:G4} FAC2={4:F4} MG={5:F4} VG={6:F4}",
                        hybridSpm.N, hybridSpm.MRB, hybridSpm.RMSE,
                        hybridSpm.NMSE, hybridSpm.FAC2, hybridSpm.MG, hybridSpm.VG);
                }
            }
            else
            {
                Console.Error.WriteLine("  Hybrid dispersion failed.");
            }

            return 0;
        }

        static OpenFoamResult RunSolverForBench(BenchmarkSpec spec, CfdSolverType solver,
            CfdConfiguration envConfig, out Scene3D outScene, WindField3D windOverride = null)
        {
            var scene = ValidationRunner.BuildScenePublic(spec);
            outScene = scene;
            var sim = scene.Simulations[0];
            sim.SolverType = solver;

            if (envConfig != null && sim.SnapshotCfdConfig != null)
            {
                sim.SnapshotCfdConfig.OpenFoamPath = envConfig.OpenFoamPath;
                sim.SnapshotCfdConfig.WslDistroName = envConfig.WslDistroName;
                sim.SnapshotCfdConfig.DetectedEnvironment = envConfig.DetectedEnvironment;
                if (envConfig.NumberOfProcessors > 0)
                    sim.SnapshotCfdConfig.NumberOfProcessors = envConfig.NumberOfProcessors;
            }

            CfdConfigurationPresets.ApplyForSolver(
                sim.SnapshotCfdConfig, solver,
                sim.SnapshotGas,
                sim.SnapshotMeteo ?? scene.WindFieldScenarios[0].Meteo);

            // FluidX3D dispersion needs a pre-computed wind field. Run it automatically.
            if (solver == CfdSolverType.FluidX3DDispersion ||
                solver == CfdSolverType.FluidX3DDispersionSteady)
            {
                var wf = scene.WindFieldScenarios.Count > 0 ? scene.WindFieldScenarios[0] : null;
                if (wf != null && windOverride != null)
                {
                    wf.WindField = windOverride;
                    wf.Status = WindFieldStatus.Ready;
                    Console.WriteLine("  Using wind field override ({0}×{1}×{2})",
                        windOverride.Nx, windOverride.Ny, windOverride.Nz);
                }
                else if (wf != null && wf.WindField == null)
                {
                    Console.WriteLine("  Pre-computing FluidX3D wind field...");
                    wf.UseFluidX3D = true;
                    var windRunner = new FluidX3DWindFieldRunner();
                    bool windOk = windRunner.Run(wf, new List<BoundingBox>(),
                        (frac, msg) => Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "    [{0:P0}] {1}", frac, msg)));
                    if (!windOk)
                    {
                        Console.Error.WriteLine("  Wind field failed: " + wf.StatusMessage);
                        return null;
                    }
                    Console.WriteLine("  Wind field ready.");
                }
            }

            try
            {
                var hr = HeadlessRunner.RunSimulation(scene, sim, msg => Console.WriteLine("  " + msg));
                if (!hr.Success)
                {
                    Console.Error.WriteLine("  Solver error: " + hr.Error);
                }

                string casePath = hr.CasePath;
                if (string.IsNullOrEmpty(casePath) || !Directory.Exists(casePath))
                    return null;

                int nx = spec.Domain.GridResolution;
                int ny = nx;
                int nz = Math.Max(1, nx / 2);
                double half = spec.Domain.SizeM;

                // FluidX3D writes <time>.bin flat in the case dir; OpenFOAM writes
                // <time>/<fieldName> in subdirectories. Try the OpenFOAM reader first,
                // fall back to scanning .bin files.
                string fieldName = spec.ResolveConcentrationField();
                var result = OpenFoamResultReader.ReadResults(casePath, nx, ny, nz, half,
                    scalarFieldName: fieldName);
                if (result != null && result.IsLoaded && result.TimeSteps.Count > 0)
                    return result;

                // Fallback: scan for .bin files (FluidX3D format)
                result = new OpenFoamResult
                {
                    GridNx = nx, GridNy = ny, GridNz = nz,
                    DomainSizeM = half,
                    DomainXMin = -half, DomainXMax = half,
                    DomainYMin = -half, DomainYMax = half,
                    DomainZMax = half,
                    CaseDir = casePath
                };
                foreach (var binFile in Directory.EnumerateFiles(casePath, "*.bin"))
                {
                    string name = Path.GetFileNameWithoutExtension(binFile);
                    if (double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out double t))
                    {
                        result.TimeSteps.Add(t);
                        result.TimeStepPaths[t] = binFile;
                    }
                }
                result.TimeSteps.Sort();
                result.IsLoaded = result.TimeSteps.Count > 0;
                return result.IsLoaded ? result : null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("  Exception: " + ex.Message);
                return null;
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
            Console.WriteLine("  --fire-roundtrip-selftest  Save and reload a FireScenario and compare");
            Console.WriteLine("                             every field; exit 0 on full pass.");
            Console.WriteLine("  --solid-flame-selftest     Check the solid-flame radiation model against");
            Console.WriteLine("                             the point source and its limiting cases.");
            Console.WriteLine("  --flash-fire-selftest      Ignite synthetic clouds and check the burnt");
            Console.WriteLine("                             envelope, connectivity and burn-back timing.");
            Console.WriteLine("  --thermal-dose-selftest    Check the thermal dose, the harm probits and");
            Console.WriteLine("                             the error function against published anchors.");
            Console.WriteLine("  --fire-study-selftest      Score and rank a synthetic fire study, and check");
            Console.WriteLine("                             it survives a save/load cycle.");
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
