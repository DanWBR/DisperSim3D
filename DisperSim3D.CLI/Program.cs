using System;
using System.Globalization;
using System.IO;
using System.Linq;
using DisperSim3D.Core;
using DisperSim3D.Models;
using DisperSim3D.Validation;

namespace DisperSim3D.CLI
{
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
                }
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

            CfdConfiguration cfdConfig = null;
            bool needsCfd = solver != null &&
                solver != "plume" && solver != "steadystate" && solver != "gaussian-plume" &&
                solver != "puff" && solver != "gaussian-puff";

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
            string fmt = "{0,-32} {1,8} {2,8} {3,8} {4,8} {5,8}   {6}";
            Console.WriteLine(string.Format(fmt, "Benchmark", "MRB", "RMSE", "FAC2", "MG", "VG", "Result"));
            foreach (var r in rows)
                Console.WriteLine(string.Format(fmt, r[0], r[1], r[2], r[3], r[4], r[5], r[6]));
            Console.WriteLine();
            Console.WriteLine(string.Format("Passed: {0}  Failed: {1}  Errored: {2}  Total: {3}",
                passed, failed, errored, files.Count));
            return (failed == 0 && errored == 0) ? 0 : 2;
        }

        static string Fmt(double v) =>
            double.IsNaN(v) ? "NaN" : v.ToString("G4", CultureInfo.InvariantCulture);

        /// <summary>
        /// Loads the project and prints its sections (gases, sources, wind fields, simulations).
        /// Useful before invoking --simulation to discover the available names/IDs.
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
                }

                Console.WriteLine();
                Console.WriteLine("Wind Field Scenarios (" + scene.WindFieldScenarios.Count + "):");
                foreach (var w in scene.WindFieldScenarios)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  - {0}  [{1}]  U={2} m/s @ {3} deg  Id={4}",
                        w.Name, w.Status, w.Meteo.WindSpeed, w.Meteo.WindDirectionDeg, w.Id));

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
            Console.WriteLine("Options:");
            Console.WriteLine("  -f, --file <path>          Project file (.dsproj or .xml)");
            Console.WriteLine("  -l, --list                 List the project sections (gases, sources,");
            Console.WriteLine("                             wind fields, simulations) and exit");
            Console.WriteLine("  --simulation <name|id>     Run a Simulation from the project tree");
            Console.WriteLine("                             (matches by name or Guid)");
            Console.WriteLine("  --validate <file|dir>      Validate against one or more .dsbench files.");
            Console.WriteLine("                             Computes Hanna SPMs and exits 0 if all pass.");
            Console.WriteLine("  -s, --solver <name>        Override solver: plume, puff,");
            Console.WriteLine("                             scalarTransportFoam, scalarSimpleFoam,");
            Console.WriteLine("                             pimpleFoam, buoyantPimpleFoam, reactingFoam,");
            Console.WriteLine("                             rhoSimpleFoam, rhoReactingBuoyantFoam");
            Console.WriteLine("  --env <type>               OpenFOAM environment: wsl, docker, native, bluecfd");
            Console.WriteLine("  --openfoam-path <path>     Path to OpenFOAM installation");
            Console.WriteLine("  --wsl-distro <name>        WSL distribution name (default: Ubuntu)");
            Console.WriteLine("  --scenario <index>         Legacy DispersionScenario index");
            Console.WriteLine("                             (only for old .xml files)");
            Console.WriteLine("  --grid <N>                 Grid resolution override");
            Console.WriteLine("  --nprocs <N>               Number of parallel processors");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  DisperSim3D.CLI project.dsproj --list");
            Console.WriteLine("  DisperSim3D.CLI project.dsproj --simulation \"Stack#1 x 5m/s SW\"");
            Console.WriteLine("                                 --env wsl --openfoam-path /opt/openfoam2412");
            Console.WriteLine("  DisperSim3D.CLI project.dsproj -s plume");
            Console.WriteLine("  DisperSim3D.CLI legacy.xml -s rhoReactingBuoyantFoam --env wsl --grid 60");
            Console.WriteLine("  DisperSim3D.CLI --validate benchmarks/gauss-D-smoketest.dsbench");
            Console.WriteLine("  DisperSim3D.CLI --validate benchmarks/ --env native --openfoam-path \"%APPDATA%\\ESI-OpenCFD\\OpenFOAM\\v2512\\msys64\\home\\ofuser\\OpenFOAM\\OpenFOAM-v2512\"");
        }
    }
}
