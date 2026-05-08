using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Manages the detection, configuration, and process launching for an OpenFOAM installation.
    /// Supports native Windows, WSL2, Docker, and BlueCFD-Core environments.
    /// </summary>
    public class OpenFoamEnvironment
    {
        /// <summary>
        /// Gets the type of OpenFOAM environment currently configured.
        /// </summary>
        public OpenFoamEnvironmentType EnvironmentType { get; private set; }

        /// <summary>
        /// Gets the root path to the OpenFOAM installation or Docker image name.
        /// </summary>
        public string OpenFoamPath { get; private set; }

        /// <summary>
        /// Gets the name of the WSL2 distribution to use (e.g., "Ubuntu").
        /// </summary>
        public string WslDistroName { get; private set; }

        /// <summary>
        /// Gets the Docker image name used for Docker-based OpenFOAM execution.
        /// </summary>
        public string DockerImage { get; private set; }

        /// <summary>
        /// Gets the root path to the BlueCFD-Core installation.
        /// </summary>
        public string BlueCfdPath { get; private set; }

        /// <summary>
        /// Gets a human-readable message describing the current environment status or any configuration errors.
        /// </summary>
        public string StatusMessage { get; private set; }

        /// <summary>
        /// Gets the path to the platform-specific bin directory containing OpenFOAM executables.
        /// </summary>
        public string PlatformBinDir { get; private set; }

        /// <summary>
        /// Gets the path to the platform-specific MPI library directory, if available.
        /// </summary>
        public string PlatformLibDir { get; private set; }

        /// <summary>
        /// Gets the path to the MPI executor (mpiexec.exe), or <c>null</c> if not found.
        /// </summary>
        public string MpiExecPath { get; private set; }

        /// <summary>
        /// Gets the OpenFOAM project directory containing the etc/controlDict file.
        /// </summary>
        public string ProjectDir { get; private set; }

        /// <summary>
        /// Gets a value indicating whether an OpenFOAM environment is configured and available for use.
        /// </summary>
        public bool IsAvailable => EnvironmentType != OpenFoamEnvironmentType.None;

        /// <summary>
        /// Gets a value indicating whether the current environment supports parallel (MPI) execution.
        /// For native Windows, this requires a valid MPI installation. WSL2, Docker, and BlueCFD always support parallel.
        /// </summary>
        public bool CanRunParallel
        {
            get
            {
                switch (EnvironmentType)
                {
                    case OpenFoamEnvironmentType.NativeWindows:
                        return !string.IsNullOrEmpty(MpiExecPath);
                    case OpenFoamEnvironmentType.WSL2:
                    case OpenFoamEnvironmentType.Docker:
                    case OpenFoamEnvironmentType.BlueCFD:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Builds the MPI command string for parallel execution, using the appropriate MPI launcher
        /// for the current environment type (mpiexec for native Windows, mpirun for WSL2/Docker/BlueCFD).
        /// </summary>
        /// <param name="nProcs">The number of MPI processes to launch.</param>
        /// <param name="solverCommand">The OpenFOAM solver command with any flags (e.g., "scalarTransportFoam -parallel").</param>
        /// <returns>The full command string including the MPI launcher prefix.</returns>
        public string BuildMpiCommand(int nProcs, string solverCommand)
        {
            switch (EnvironmentType)
            {
                case OpenFoamEnvironmentType.NativeWindows:
                    return "mpiexec -np " + nProcs + " " + solverCommand;
                case OpenFoamEnvironmentType.WSL2:
                case OpenFoamEnvironmentType.Docker:
                case OpenFoamEnvironmentType.BlueCFD:
                    return "mpirun -np " + nProcs + " " + solverCommand;
                default:
                    return solverCommand;
            }
        }

        /// <summary>
        /// Configures the OpenFOAM environment by setting the installation path, environment type,
        /// and optional WSL distribution name. Validates the path and detects binaries, libraries, and MPI.
        /// </summary>
        /// <param name="path">The root path to the OpenFOAM installation, Docker image name, or BlueCFD path.</param>
        /// <param name="type">The type of OpenFOAM environment to configure.</param>
        /// <param name="wslDistro">The WSL2 distribution name. Defaults to "Ubuntu" if not specified.</param>
        public void Configure(string path, OpenFoamEnvironmentType type, string wslDistro = null)
        {
            OpenFoamPath = path ?? "";
            EnvironmentType = type;
            WslDistroName = wslDistro ?? "Ubuntu";
            PlatformBinDir = null;
            PlatformLibDir = null;
            MpiExecPath = null;
            ProjectDir = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                EnvironmentType = OpenFoamEnvironmentType.None;
                StatusMessage = "No OpenFOAM path configured.";
                return;
            }

            switch (type)
            {
                case OpenFoamEnvironmentType.NativeWindows:
                    ConfigureNativeWindows(path);
                    break;

                case OpenFoamEnvironmentType.WSL2:
                    StatusMessage = "WSL2 (" + WslDistroName + ") — path: " + path;
                    break;

                case OpenFoamEnvironmentType.Docker:
                    DockerImage = path;
                    StatusMessage = "Docker image: " + path;
                    break;

                case OpenFoamEnvironmentType.BlueCFD:
                    BlueCfdPath = path;
                    if (!Directory.Exists(path))
                    {
                        EnvironmentType = OpenFoamEnvironmentType.None;
                        StatusMessage = "BlueCFD path not found: " + path;
                    }
                    else
                    {
                        StatusMessage = "BlueCFD-Core at " + path;
                    }
                    break;

                default:
                    EnvironmentType = OpenFoamEnvironmentType.None;
                    StatusMessage = "No OpenFOAM environment configured.";
                    break;
            }
        }

        private void ConfigureNativeWindows(string path)
        {
            if (!Directory.Exists(path))
            {
                EnvironmentType = OpenFoamEnvironmentType.None;
                StatusMessage = "Path not found: " + path;
                return;
            }

            PlatformBinDir = FindPlatformBinDir(path);
            ProjectDir = FindProjectDir(path);

            if (string.IsNullOrEmpty(PlatformBinDir))
            {
                EnvironmentType = OpenFoamEnvironmentType.None;
                StatusMessage = "No OpenFOAM binaries found under " + path +
                    ". Expected platforms/*/bin/ with blockMesh.exe.";
                return;
            }

            if (string.IsNullOrEmpty(ProjectDir))
            {
                EnvironmentType = OpenFoamEnvironmentType.None;
                StatusMessage = "No OpenFOAM project directory found under " + path +
                    ". Expected etc/controlDict.";
                return;
            }

            string platDir = Path.GetDirectoryName(PlatformBinDir);
            string mpiLib = Path.Combine(platDir, "lib", "mpi");
            if (Directory.Exists(mpiLib))
                PlatformLibDir = mpiLib;

            MpiExecPath = FindMpiExec();

            string version = DetectVersion(ProjectDir);
            StatusMessage = "OpenFOAM " + version + " — " + PlatformBinDir;
            if (string.IsNullOrEmpty(MpiExecPath))
                StatusMessage += " (MPI not found — parallel disabled)";
        }

        private static string DetectVersion(string projectDir)
        {
            string name = Path.GetFileName(projectDir);
            if (name.StartsWith("OpenFOAM-", StringComparison.OrdinalIgnoreCase))
                return name.Substring("OpenFOAM-".Length);
            return "(unknown version)";
        }

        /// <summary>
        /// Starts an OpenFOAM command as a new process in the specified case directory,
        /// using the appropriate execution method for the current environment type.
        /// The returned process has stdout and stderr redirected for capture.
        /// </summary>
        /// <param name="casePath">The absolute path to the OpenFOAM case directory.</param>
        /// <param name="command">The OpenFOAM command to execute (e.g., "blockMesh", "scalarTransportFoam").</param>
        /// <returns>The started <see cref="Process"/> with redirected standard output and error streams.</returns>
        public Process StartCommand(string casePath, string command)
        {
            switch (EnvironmentType)
            {
                case OpenFoamEnvironmentType.NativeWindows:
                    return StartNativeWindowsCommand(casePath, command);
                case OpenFoamEnvironmentType.WSL2:
                    return StartWSL2Command(casePath, command);
                case OpenFoamEnvironmentType.Docker:
                    return StartDockerCommand(casePath, command);
                case OpenFoamEnvironmentType.BlueCFD:
                    return StartBlueCFDCommand(casePath, command);
                default:
                    throw new InvalidOperationException("No OpenFOAM environment available");
            }
        }

        /// <summary>
        /// Converts a Windows file path to the equivalent WSL2 mount path (e.g., "C:\foo" becomes "/mnt/c/foo").
        /// </summary>
        /// <param name="windowsPath">The Windows path to convert.</param>
        /// <returns>The equivalent Linux-style path under /mnt/.</returns>
        public static string WindowsToWslPath(string windowsPath)
        {
            string full = Path.GetFullPath(windowsPath);
            string drive = full.Substring(0, 1).ToLowerInvariant();
            string rest = full.Substring(2).Replace('\\', '/');
            return "/mnt/" + drive + rest;
        }

        private Process StartNativeWindowsCommand(string casePath, string command)
        {
            string fullCase = Path.GetFullPath(casePath);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = fullCase
            };

            var pathParts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(PlatformBinDir))
                pathParts.Add(PlatformBinDir);
            if (!string.IsNullOrEmpty(PlatformLibDir))
                pathParts.Add(PlatformLibDir);
            if (!string.IsNullOrEmpty(MpiExecPath))
                pathParts.Add(Path.GetDirectoryName(MpiExecPath));
            pathParts.Add(Environment.GetEnvironmentVariable("PATH"));
            psi.EnvironmentVariables["PATH"] = string.Join(";", pathParts);

            if (!string.IsNullOrEmpty(ProjectDir))
            {
                psi.EnvironmentVariables["WM_PROJECT_DIR"] = ProjectDir;
                psi.EnvironmentVariables["FOAM_ETC"] = Path.Combine(ProjectDir, "etc");
                psi.EnvironmentVariables["FOAM_CASE"] = fullCase;
            }

            return Process.Start(psi);
        }

        private static string FindMpiExec()
        {
            string msMpi = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft MPI", "Bin", "mpiexec.exe");
            if (File.Exists(msMpi)) return msMpi;

            string msMpiX86 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft MPI", "Bin", "mpiexec.exe");
            if (File.Exists(msMpiX86)) return msMpiX86;

            string envMsMpi = Environment.GetEnvironmentVariable("MSMPI_BIN");
            if (!string.IsNullOrEmpty(envMsMpi))
            {
                string p = Path.Combine(envMsMpi, "mpiexec.exe");
                if (File.Exists(p)) return p;
            }

            return null;
        }

        private static string FindPlatformBinDir(string openFoamPath)
        {
            if (string.IsNullOrEmpty(openFoamPath)) return null;

            var root = new DirectoryInfo(openFoamPath);
            var queue = new System.Collections.Generic.Queue<DirectoryInfo>();
            queue.Enqueue(root);
            int depth = 0;

            while (queue.Count > 0 && depth < 8)
            {
                int count = queue.Count;
                for (int i = 0; i < count; i++)
                {
                    var dir = queue.Dequeue();
                    string candidate = Path.Combine(dir.FullName, "platforms");
                    if (Directory.Exists(candidate))
                    {
                        foreach (var plat in Directory.GetDirectories(candidate))
                        {
                            string bin = Path.Combine(plat, "bin");
                            if (Directory.Exists(bin) &&
                                Directory.GetFiles(bin, "blockMesh*").Length > 0)
                                return bin;
                        }
                    }
                    try
                    {
                        foreach (var sub in dir.GetDirectories())
                            queue.Enqueue(sub);
                    }
                    catch { }
                }
                depth++;
            }
            return null;
        }

        private static string FindProjectDir(string openFoamPath)
        {
            if (string.IsNullOrEmpty(openFoamPath)) return null;

            var root = new DirectoryInfo(openFoamPath);
            var queue = new System.Collections.Generic.Queue<DirectoryInfo>();
            queue.Enqueue(root);
            int depth = 0;

            while (queue.Count > 0 && depth < 6)
            {
                int count = queue.Count;
                for (int i = 0; i < count; i++)
                {
                    var dir = queue.Dequeue();
                    string etcDir = Path.Combine(dir.FullName, "etc");
                    if (Directory.Exists(etcDir) &&
                        File.Exists(Path.Combine(etcDir, "controlDict")))
                        return dir.FullName;
                    try
                    {
                        foreach (var sub in dir.GetDirectories())
                            queue.Enqueue(sub);
                    }
                    catch { }
                }
                depth++;
            }
            return null;
        }

        private Process StartWSL2Command(string casePath, string command)
        {
            string linuxPath = WindowsToWslPath(casePath);
            string bashCmd = "source /opt/openfoam*/etc/bashrc 2>/dev/null; cd '" + linuxPath + "' && " + command;
            var psi = new ProcessStartInfo
            {
                FileName = "wsl",
                Arguments = "-d " + WslDistroName + " -- bash -c \"" + bashCmd + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            return Process.Start(psi);
        }

        private Process StartDockerCommand(string casePath, string command)
        {
            string winPath = Path.GetFullPath(casePath).Replace('\\', '/');
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "run --rm -v \"" + winPath + ":/case\" -w /case " +
                    DockerImage + " bash -c \"" + command + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            return Process.Start(psi);
        }

        private Process StartBlueCFDCommand(string casePath, string command)
        {
            string bashExe = Path.Combine(BlueCfdPath, "msys64", "usr", "bin", "bash.exe");
            string winCase = Path.GetFullPath(casePath).Replace('\\', '/');
            var psi = new ProcessStartInfo
            {
                FileName = bashExe,
                Arguments = "--login -c \"cd '" + winCase + "' && " + command + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            return Process.Start(psi);
        }
    }
}
