using System;
using System.Globalization;
using System.IO;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.CLI
{
    class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.WriteLine("DisperSim 3D — Command Line Interface");
            Console.WriteLine();

            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                PrintUsage();
                return 0;
            }

            string xmlPath = null;
            string solver = null;
            string openFoamPath = null;
            string wslDistro = "Ubuntu";
            int scenarioIndex = -1;
            int gridRes = 0;
            int nProcs = 0;
            string envType = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--file":
                    case "-f":
                        if (i + 1 < args.Length) xmlPath = args[++i];
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
                    case "--grid":
                        if (i + 1 < args.Length) gridRes = int.Parse(args[++i]);
                        break;
                    case "--nprocs":
                        if (i + 1 < args.Length) nProcs = int.Parse(args[++i]);
                        break;
                }
            }

            if (xmlPath == null)
            {
                if (args.Length > 0 && !args[0].StartsWith("-") && (File.Exists(args[0]) || Directory.Exists(args[0])))
                    xmlPath = args[0];
                else
                {
                    Console.Error.WriteLine("Error: no XML file specified.");
                    PrintUsage();
                    return 1;
                }
            }

            if (!File.Exists(xmlPath) && !Directory.Exists(xmlPath))
            {
                Console.Error.WriteLine("Error: file not found: " + xmlPath);
                return 1;
            }

            CfdConfiguration cfdConfig = null;
            bool needsCfd = solver != null &&
                solver != "plume" && solver != "steadystate" && solver != "gaussian-plume" &&
                solver != "puff" && solver != "gaussian-puff";

            if (needsCfd || openFoamPath != null || envType != null)
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

            if (solver == "read-case" && xmlPath != null && Directory.Exists(xmlPath))
            {
                Console.WriteLine("Reading existing case: " + xmlPath);
                int nx = gridRes > 0 ? gridRes : 40;
                int ny = nx;
                int nz = nx / 2;
                var dirs = Directory.GetDirectories(xmlPath);
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
                var result = HeadlessRunner.RunFromFile(xmlPath, solver, cfdConfig,
                    scenarioIndex, msg => Console.WriteLine(msg));

                Console.WriteLine();
                if (result.Success)
                {
                    Console.WriteLine("=== Result ===");
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  Max concentration: {0:G6} kg/m³", result.MaxConcentration));
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
                                "    {0}: max = {1:G6} kg/m³", md.Name, maxC));
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

        static void PrintUsage()
        {
            Console.WriteLine("Usage: DisperSim3D.CLI [options] <scene.xml>");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -f, --file <path>       XML scene file");
            Console.WriteLine("  -s, --solver <name>     Solver: plume, puff, buoyantPimpleFoam,");
            Console.WriteLine("                          scalarTransportFoam, pimpleFoam, reactingFoam,");
            Console.WriteLine("                          scalarSimpleFoam, rhoSimpleFoam");
            Console.WriteLine("  --env <type>            OpenFOAM environment: wsl, docker, native, bluecfd");
            Console.WriteLine("  --openfoam-path <path>  Path to OpenFOAM installation");
            Console.WriteLine("  --wsl-distro <name>     WSL distribution name (default: Ubuntu)");
            Console.WriteLine("  --scenario <index>      Scenario index (default: active scenario)");
            Console.WriteLine("  --grid <N>              Grid resolution override");
            Console.WriteLine("  --nprocs <N>            Number of parallel processors");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  DisperSim3D.CLI scene.xml");
            Console.WriteLine("  DisperSim3D.CLI -f scene.xml -s plume");
            Console.WriteLine("  DisperSim3D.CLI -f scene.xml -s buoyantPimpleFoam --env wsl --grid 40");
        }
    }
}
