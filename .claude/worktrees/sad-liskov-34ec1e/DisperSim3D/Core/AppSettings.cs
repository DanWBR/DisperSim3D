using System;
using System.Globalization;
using System.IO;
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
                    CfdDefaults.MaxCourantNumber = ParseDouble(cfd, "MaxCourantNumber", 0.5);
                    CfdDefaults.PurgeWrite = ParseInt(cfd, "PurgeWrite", 0);
                    CfdDefaults.CleanCaseOnCompletion = ParseBool(cfd, "CleanCaseOnCompletion", true);
                    CfdDefaults.UseGaussianSubgrid = ParseBool(cfd, "UseGaussianSubgrid", true);
                    CfdDefaults.SubgridMarginFactor = ParseDouble(cfd, "SubgridMarginFactor", 1.5);
                    CfdDefaults.UseWindField = ParseBool(cfd, "UseWindField", true);
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
                        )
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
