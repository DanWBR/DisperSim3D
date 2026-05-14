using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Manages persistent application settings stored as an XML file in the user's AppData directory.
    /// Implements the singleton pattern via <see cref="Instance"/>.
    /// </summary>
    public sealed class AppSettings
    {
        private static readonly Lazy<AppSettings> _instance =
            new Lazy<AppSettings>(() => new AppSettings());

        /// <summary>
        /// Gets the singleton <see cref="AppSettings"/> instance, creating it on first access.
        /// </summary>
        public static AppSettings Instance => _instance.Value;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly string _settingsPath;

        /// <summary>
        /// Gets or sets the default CFD configuration used as a template for new simulations.
        /// </summary>
        public CfdConfiguration CfdDefaults { get; set; }

        /// <summary>Filesystem path to the DWSIM installation directory (the folder
        /// containing DWSIM.Automation.FluentAPI.dll). When empty, DWSIM-driven
        /// thermodynamics features are disabled.</summary>
        public string DwsimInstallPath { get; set; } = "";

        /// <summary>DWSIM property-package name to use for mixture flashes. Defaults
        /// to Peng-Robinson 1978 (PR78). Set via the DWSIM Settings dialog; consumed
        /// by <see cref="DwsimThermo.ComputeMixtureProperties"/>.</summary>
        public string DwsimPropertyPackage { get; set; } = "Peng-Robinson 1978 (PR78)";

        /// <summary>Preferred OpenCL device ID for FluidX3D compute (LBM wind / GPU
        /// voxelisation). -1 = auto (FluidX3D picks the fastest by TFLOPS).
        /// IDs match <see cref="FluidX3DBridge.ListDevicesJson"/>.</summary>
        public int PreferredComputeDeviceId { get; set; } = -1;

        /// <summary>Root directory for all DisperSim 3D working files (simulation
        /// cases, temp snapshots, project sessions). Defaults to
        /// <c>%LOCALAPPDATA%\DisperSim3D\Work</c>. The user can change this via
        /// the settings dialog.</summary>
        public string WorkingDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DisperSim3D", "Work");

        private readonly List<string> _recentFiles = new List<string>();

        /// <summary>Read-only view of the most-recently-used project paths
        /// (most recent first). Populated by <see cref="AddRecentFile"/> as the
        /// user opens or saves a project; persisted to <c>settings.xml</c>.
        /// Consumed by the File → Recent Files submenu.</summary>
        public IReadOnlyList<string> RecentFiles => _recentFiles;

        /// <summary>Maximum number of paths retained in <see cref="RecentFiles"/>.
        /// Older entries are dropped as new ones come in. Default 10.</summary>
        public int MaxRecentFiles { get; set; } = 10;

        /// <summary>Pushes <paramref name="path"/> to the head of the MRU list,
        /// removes any existing duplicate (case-insensitive), trims to
        /// <see cref="MaxRecentFiles"/> and persists. No-op for null/empty
        /// input or paths that fail <see cref="Path.GetFullPath(string)"/>.</summary>
        public void AddRecentFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string full;
            try { full = Path.GetFullPath(path); }
            catch { return; }
            _recentFiles.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
            _recentFiles.Insert(0, full);
            int cap = Math.Max(1, MaxRecentFiles);
            while (_recentFiles.Count > cap) _recentFiles.RemoveAt(_recentFiles.Count - 1);
            Save();
        }

        /// <summary>Removes <paramref name="path"/> from the MRU list (typically
        /// called when a recent-file load fails because the file no longer
        /// exists). Persists when it actually removed something.</summary>
        public void RemoveRecentFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            int n = _recentFiles.RemoveAll(p =>
                string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (n > 0) Save();
        }

        /// <summary>Clears the MRU list and persists. Wired to the File →
        /// Recent Files → Clear menu entry.</summary>
        public void ClearRecentFiles()
        {
            if (_recentFiles.Count == 0) return;
            _recentFiles.Clear();
            Save();
        }

        private AppSettings()
        {
            string appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DisperSim3D");

            if (!Directory.Exists(appDir))
                Directory.CreateDirectory(appDir);

            _settingsPath = Path.Combine(appDir, "settings.xml");
            CfdDefaults = new CfdConfiguration();
            Load();
        }

        /// <summary>
        /// Loads settings from the XML file on disk into <see cref="CfdDefaults"/>.
        /// If the file does not exist or cannot be parsed, the current defaults are retained.
        /// </summary>
        public void Load()
        {
            if (!File.Exists(_settingsPath)) return;

            try
            {
                var doc = XDocument.Load(_settingsPath);
                var root = doc.Root;
                if (root == null) return;

                var cfd = root.Element("CfdConfiguration");
                if (cfd != null)
                {
                    CfdDefaults.DetectedEnvironment = ParseEnum(
                        (string)cfd.Attribute("Environment"), OpenFoamEnvironmentType.None);
                    CfdDefaults.OpenFoamPath = (string)cfd.Attribute("OpenFoamPath") ?? "";
                    CfdDefaults.WslDistroName = (string)cfd.Attribute("WslDistroName") ?? "Ubuntu";
                    CfdDefaults.DockerImageName = (string)cfd.Attribute("DockerImageName") ?? "openfoam/openfoam2312-default";
                    CfdDefaults.BlueCfdPath = (string)cfd.Attribute("BlueCfdPath") ?? "";
                    CfdDefaults.WorkingDirectory = (string)cfd.Attribute("WorkingDirectory") ?? CfdDefaults.WorkingDirectory;
                    CfdDefaults.DiffusivityM2PerS = ParseDouble(cfd, "DiffusivityM2PerS", 1e-5);
                    CfdDefaults.NumberOfProcessors = ParseInt(cfd, "NumberOfProcessors", 1);
                    CfdDefaults.WriteIntervalS = ParseDouble(cfd, "WriteIntervalS", -1);
                    CfdDefaults.GridResolution = ParseInt(cfd, "GridResolution", 40);
                    CfdDefaults.SolverTolerance = ParseDouble(cfd, "SolverTolerance", 1e-8);
                    CfdDefaults.NumericalScheme = (string)cfd.Attribute("NumericalScheme") ?? "linearUpwind";
                    CfdDefaults.AdjustableTimeStep = ParseBool(cfd, "AdjustableTimeStep", true);
                    CfdDefaults.MaxCourantNumber = ParseDouble(cfd, "MaxCourantNumber", 10.0);
                    CfdDefaults.PurgeWrite = ParseInt(cfd, "PurgeWrite", 0);
                    CfdDefaults.CleanCaseOnCompletion = ParseBool(cfd, "CleanCaseOnCompletion", true);
                    CfdDefaults.UseGaussianSubgrid = ParseBool(cfd, "UseGaussianSubgrid", true);
                    CfdDefaults.SubgridMarginFactor = ParseDouble(cfd, "SubgridMarginFactor", 1.5);
                    CfdDefaults.UseWindField = ParseBool(cfd, "UseWindField", true);
                }
                var dwsim = root.Element("Dwsim");
                if (dwsim != null)
                {
                    DwsimInstallPath = (string)dwsim.Attribute("InstallPath") ?? "";
                    string pp = (string)dwsim.Attribute("PropertyPackage");
                    if (!string.IsNullOrEmpty(pp)) DwsimPropertyPackage = pp;
                }
                var gpu = root.Element("Gpu");
                if (gpu != null)
                {
                    int id;
                    if (int.TryParse((string)gpu.Attribute("PreferredComputeDeviceId") ?? "-1",
                        System.Globalization.NumberStyles.Integer, Inv, out id))
                        PreferredComputeDeviceId = id;
                }
                var workDir = (string)root.Element("WorkingDirectory");
                if (!string.IsNullOrEmpty(workDir))
                    WorkingDirectory = workDir;
                var recent = root.Element("RecentFiles");
                if (recent != null)
                {
                    _recentFiles.Clear();
                    foreach (var f in recent.Elements("File"))
                    {
                        var path = ((string)f.Attribute("Path") ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(path)) continue;
                        // Defensive against duplicates and over-cap files from older
                        // settings.xml schemas that didn't enforce these invariants.
                        if (_recentFiles.Any(p => string.Equals(p, path,
                                StringComparison.OrdinalIgnoreCase))) continue;
                        _recentFiles.Add(path);
                        if (_recentFiles.Count >= Math.Max(1, MaxRecentFiles)) break;
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Persists the current <see cref="CfdDefaults"/> to the XML settings file on disk.
        /// Errors during serialization are silently ignored.
        /// </summary>
        public void Save()
        {
            try
            {
                var doc = new XDocument(
                    new XElement("AppSettings",
                        new XAttribute("Version", "1"),
                        new XElement("CfdConfiguration",
                            new XAttribute("Environment", CfdDefaults.DetectedEnvironment.ToString()),
                            new XAttribute("OpenFoamPath", CfdDefaults.OpenFoamPath ?? ""),
                            new XAttribute("WslDistroName", CfdDefaults.WslDistroName ?? ""),
                            new XAttribute("DockerImageName", CfdDefaults.DockerImageName ?? ""),
                            new XAttribute("BlueCfdPath", CfdDefaults.BlueCfdPath ?? ""),
                            new XAttribute("WorkingDirectory", CfdDefaults.WorkingDirectory ?? ""),
                            new XAttribute("DiffusivityM2PerS", CfdDefaults.DiffusivityM2PerS.ToString(Inv)),
                            new XAttribute("NumberOfProcessors", CfdDefaults.NumberOfProcessors),
                            new XAttribute("WriteIntervalS", CfdDefaults.WriteIntervalS.ToString(Inv)),
                            new XAttribute("GridResolution", CfdDefaults.GridResolution),
                            new XAttribute("SolverTolerance", CfdDefaults.SolverTolerance.ToString(Inv)),
                            new XAttribute("NumericalScheme", CfdDefaults.NumericalScheme ?? "linearUpwind"),
                            new XAttribute("AdjustableTimeStep", CfdDefaults.AdjustableTimeStep),
                            new XAttribute("MaxCourantNumber", CfdDefaults.MaxCourantNumber.ToString(Inv)),
                            new XAttribute("PurgeWrite", CfdDefaults.PurgeWrite),
                            new XAttribute("CleanCaseOnCompletion", CfdDefaults.CleanCaseOnCompletion),
                            new XAttribute("UseGaussianSubgrid", CfdDefaults.UseGaussianSubgrid),
                            new XAttribute("SubgridMarginFactor", CfdDefaults.SubgridMarginFactor.ToString(Inv)),
                            new XAttribute("UseWindField", CfdDefaults.UseWindField)
                        ),
                        new XElement("Dwsim",
                            new XAttribute("InstallPath", DwsimInstallPath ?? ""),
                            new XAttribute("PropertyPackage", DwsimPropertyPackage ?? "")),
                        new XElement("Gpu",
                            new XAttribute("PreferredComputeDeviceId",
                                PreferredComputeDeviceId.ToString(Inv))),
                        new XElement("WorkingDirectory", WorkingDirectory ?? ""),
                        new XElement("RecentFiles",
                            _recentFiles.Select(p => new XElement("File",
                                new XAttribute("Path", p ?? string.Empty))))
                    )
                );

                doc.Save(_settingsPath);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Creates a new <see cref="CfdConfiguration"/> instance pre-populated with the current default values.
        /// </summary>
        /// <returns>A new <see cref="CfdConfiguration"/> cloned from <see cref="CfdDefaults"/>.</returns>
        public CfdConfiguration CreateCfdConfig()
        {
            return new CfdConfiguration
            {
                DetectedEnvironment = CfdDefaults.DetectedEnvironment,
                OpenFoamPath = CfdDefaults.OpenFoamPath,
                WslDistroName = CfdDefaults.WslDistroName,
                DockerImageName = CfdDefaults.DockerImageName,
                BlueCfdPath = CfdDefaults.BlueCfdPath,
                WorkingDirectory = CfdDefaults.WorkingDirectory,
                DiffusivityM2PerS = CfdDefaults.DiffusivityM2PerS,
                NumberOfProcessors = CfdDefaults.NumberOfProcessors,
                WriteIntervalS = CfdDefaults.WriteIntervalS,
                GridResolution = CfdDefaults.GridResolution,
                SolverTolerance = CfdDefaults.SolverTolerance,
                NumericalScheme = CfdDefaults.NumericalScheme,
                AdjustableTimeStep = CfdDefaults.AdjustableTimeStep,
                MaxCourantNumber = CfdDefaults.MaxCourantNumber,
                PurgeWrite = CfdDefaults.PurgeWrite,
                CleanCaseOnCompletion = CfdDefaults.CleanCaseOnCompletion,
                UseGaussianSubgrid = CfdDefaults.UseGaussianSubgrid,
                SubgridMarginFactor = CfdDefaults.SubgridMarginFactor,
                UseWindField = CfdDefaults.UseWindField
            };
        }

        /// <summary>
        /// Copies all properties from the specified configuration into <see cref="CfdDefaults"/> and saves to disk.
        /// </summary>
        /// <param name="config">The <see cref="CfdConfiguration"/> whose values will replace the current defaults.</param>
        public void UpdateFromConfig(CfdConfiguration config)
        {
            CfdDefaults.DetectedEnvironment = config.DetectedEnvironment;
            CfdDefaults.OpenFoamPath = config.OpenFoamPath;
            CfdDefaults.WslDistroName = config.WslDistroName;
            CfdDefaults.DockerImageName = config.DockerImageName;
            CfdDefaults.BlueCfdPath = config.BlueCfdPath;
            CfdDefaults.WorkingDirectory = config.WorkingDirectory;
            CfdDefaults.DiffusivityM2PerS = config.DiffusivityM2PerS;
            CfdDefaults.NumberOfProcessors = config.NumberOfProcessors;
            CfdDefaults.WriteIntervalS = config.WriteIntervalS;
            CfdDefaults.GridResolution = config.GridResolution;
            CfdDefaults.SolverTolerance = config.SolverTolerance;
            CfdDefaults.NumericalScheme = config.NumericalScheme;
            CfdDefaults.AdjustableTimeStep = config.AdjustableTimeStep;
            CfdDefaults.MaxCourantNumber = config.MaxCourantNumber;
            CfdDefaults.PurgeWrite = config.PurgeWrite;
            CfdDefaults.CleanCaseOnCompletion = config.CleanCaseOnCompletion;
            CfdDefaults.UseGaussianSubgrid = config.UseGaussianSubgrid;
            CfdDefaults.SubgridMarginFactor = config.SubgridMarginFactor;
            CfdDefaults.UseWindField = config.UseWindField;
            Save();
        }

        private static T ParseEnum<T>(string value, T defaultValue) where T : struct
        {
            T result;
            if (Enum.TryParse(value, true, out result))
                return result;
            return defaultValue;
        }

        private static double ParseDouble(XElement el, string attr, double defaultValue)
        {
            var s = (string)el.Attribute(attr);
            double v;
            if (s != null && double.TryParse(s, NumberStyles.Float, Inv, out v))
                return v;
            return defaultValue;
        }

        private static int ParseInt(XElement el, string attr, int defaultValue)
        {
            var s = (string)el.Attribute(attr);
            int v;
            if (s != null && int.TryParse(s, NumberStyles.Integer, Inv, out v))
                return v;
            return defaultValue;
        }

        private static bool ParseBool(XElement el, string attr, bool defaultValue)
        {
            var s = (string)el.Attribute(attr);
            bool v;
            if (s != null && bool.TryParse(s, out v))
                return v;
            return defaultValue;
        }
    }
}
