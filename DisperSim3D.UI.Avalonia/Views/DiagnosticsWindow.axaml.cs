using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using DisperSim3D.Core;
using DisperSim3D.Geometry;
using DisperSim3D.Models;
using DisperSim3D.Validation;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Diagnostics window. Each panel exercises a different layer of the engine
    /// stack so a single visual run-through confirms cross-platform behaviour:
    ///
    ///  1. Portable Point3D/Vector3D operators  (GeometrySelfTest)
    ///  2. Embedded IOGP 434-01 leak-frequency table (IogpTableTests)
    ///  3. Native FluidX3D bridge + OpenCL ICD discovery (FluidX3DBridge)
    ///  4. End-to-end Gaussian plume — gas → source → meteo → engine → MaxC
    ///
    /// Opened on demand from the main shell via Help → Diagnostics; not the
    /// startup window any more.
    /// </summary>
    public partial class DiagnosticsWindow : Window
    {
        public DiagnosticsWindow()
        {
            InitializeComponent();
            EnvLine.Text = BuildEnvironmentLine();
        }

        private static string BuildEnvironmentLine()
        {
            var os = Environment.OSVersion.Platform == PlatformID.Unix
                ? "Linux/Unix"
                : Environment.OSVersion.Platform.ToString();
            return string.Format(CultureInfo.InvariantCulture,
                ".NET {0}  •  OS {1} ({2})  •  Avalonia {3}  •  cores {4}",
                Environment.Version,
                os,
                System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim(),
                typeof(global::Avalonia.Application).Assembly.GetName().Version,
                Environment.ProcessorCount);
        }

        // ── Panel 1 ──────────────────────────────────────────────────────────
        private void BtnGeometry_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            using var sw = new StringWriter();
            bool ok = GeometrySelfTest.RunAndPrint(sw);
            GeometryOutput.Text = sw.ToString();
            GeometryOutput.Foreground = ok
                ? global::Avalonia.Media.Brushes.DarkGreen
                : global::Avalonia.Media.Brushes.Firebrick;
        }

        // ── Panel 2 ──────────────────────────────────────────────────────────
        private void BtnIogp_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                string output = IogpTableTests.RunAll();
                IogpOutput.Text = output;
                IogpOutput.Foreground = output.Contains("0 failed")
                    ? global::Avalonia.Media.Brushes.DarkGreen
                    : global::Avalonia.Media.Brushes.Firebrick;
            }
            catch (Exception ex)
            {
                IogpOutput.Text = ex.GetType().Name + ": " + ex.Message;
                IogpOutput.Foreground = global::Avalonia.Media.Brushes.Firebrick;
            }
        }

        // ── Panel 3 ──────────────────────────────────────────────────────────
        private void BtnGpus_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            string json = FluidX3DBridge.ListDevicesJson();
            if (string.IsNullOrEmpty(json))
            {
                GpusOutput.Text = "(no devices)\n\nLastListDevicesError: " +
                    FluidX3DBridge.LastListDevicesError +
                    "\n\nLastAvailabilityError: " +
                    FluidX3DBridge.LastAvailabilityError;
                GpusOutput.Foreground = global::Avalonia.Media.Brushes.Firebrick;
            }
            else
            {
                GpusOutput.Text = json;
                GpusOutput.Foreground = global::Avalonia.Media.Brushes.DarkGreen;
            }
        }

        // ── Panel 4 ──────────────────────────────────────────────────────────
        private void BtnPlume_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                // Synthetic minimal scenario — same shape as a .dsproj load would
                // produce. Stays in pure engine code (no UI types involved).
                var gas = new GasProperties
                {
                    Name = "Methane",
                    MolarMass = 0.01604,
                    LFL = 0.05,
                    UFL = 0.15
                };
                var source = new ReleaseSource3D
                {
                    Name = "Source A",
                    Gas = gas,
                    Position = new Point3D(0, 0, 5),
                    ReleaseRateKgPerS = 0.5,
                    ReleaseHeightOffset = 0,
                    ReleaseAzimuthDeg = 0,
                    ReleaseElevationDeg = 0,
                };
                var meteo = new MeteorologicalConditions
                {
                    WindSpeed = 3.0,
                    WindDirectionDeg = 270,        // wind from the west
                    StabilityClass = PasquillStabilityClass.D,
                    AmbientTemperature = 293.15,
                    AmbientPressure = 101325
                };
                var scenario = new DispersionScenario
                {
                    Name = "Smoke plume",
                    Sources = new System.Collections.Generic.List<ReleaseSource3D> { source },
                    Meteo = meteo,
                    GridResolution = 32,
                    DomainSizeM = 200
                };

                var engine = new GaussianPlumeEngine();
                engine.Initialize(scenario);

                int n = scenario.GridResolution;
                int nz = Math.Max(1, n / 2);
                double dom = scenario.DomainSizeM;
                double cell = (dom * 2.0) / n;
                double maxC = 0;
                Point3D maxAt = new Point3D();
                for (int i = 0; i < n; i++)
                {
                    double x = -dom + i * cell;
                    for (int j = 0; j < n; j++)
                    {
                        double y = -dom + j * cell;
                        for (int k = 0; k < nz; k++)
                        {
                            double z = k * cell;
                            double c = engine.EvaluateConcentration(x, y, z);
                            if (c > maxC) { maxC = c; maxAt = new Point3D(x, y, z); }
                        }
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine("Scenario:    " + scenario.Name);
                sb.AppendLine("Gas:         " + gas.Name + "  M=" + gas.MolarMass + " kg/mol");
                sb.AppendLine("Wind:        " + meteo.WindSpeed.ToString("F1", CultureInfo.InvariantCulture)
                              + " m/s from " + meteo.WindDirectionDeg + "°  class " + meteo.StabilityClass);
                sb.AppendLine("Grid:        " + n + "³  domain ±" + dom + " m  cell " +
                              cell.ToString("F2", CultureInfo.InvariantCulture) + " m");
                sb.AppendLine();
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "MaxC:        {0:G6} kg/m³", maxC));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Located at:  ({0:F1}, {1:F1}, {2:F1}) m", maxAt.X, maxAt.Y, maxAt.Z));
                sb.AppendLine();
                sb.AppendLine("Sanity:      MaxC should be > 0 and X-coord should be downwind (+x).");

                PlumeOutput.Text = sb.ToString();
                PlumeOutput.Foreground = maxC > 0
                    ? global::Avalonia.Media.Brushes.DarkGreen
                    : global::Avalonia.Media.Brushes.Firebrick;
            }
            catch (Exception ex)
            {
                PlumeOutput.Text = ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
                PlumeOutput.Foreground = global::Avalonia.Media.Brushes.Firebrick;
            }
        }
    }
}
