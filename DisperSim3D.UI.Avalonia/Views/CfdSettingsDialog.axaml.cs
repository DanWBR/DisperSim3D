#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>CfdSettingsDialog</c>.
    /// Edits a <see cref="CfdConfiguration"/> (OpenFOAM environment +
    /// solver + atmospheric BL + optimization). The "Test" button runs
    /// <c>blockMesh -help</c> via <see cref="OpenFoamEnvironment.StartCommand"/>
    /// to verify the configured environment actually launches.
    /// </summary>
    public partial class CfdSettingsDialog : Window
    {
        private readonly OpenFoamEnvironment _environment;
        public CfdConfiguration Result { get; private set; }

        public CfdSettingsDialog() : this(new CfdConfiguration(), new OpenFoamEnvironment()) { }

        public CfdSettingsDialog(CfdConfiguration config, OpenFoamEnvironment env)
        {
            _environment = env ?? new OpenFoamEnvironment();
            Result = config ?? new CfdConfiguration();

            InitializeComponent();

            // The Parallel CPUs max + status hint adapt to the machine.
            int cores = Environment.ProcessorCount;
            NudProcessors.Maximum = cores;
            LblCpuRange.Text = string.Format(CultureInfo.InvariantCulture,
                "1-{0} (1 = serial)", cores);

            LoadFromConfig();
            UpdateCellEstimate();
        }

        private void LoadFromConfig()
        {
            var inv = CultureInfo.InvariantCulture;

            TxtPath.Text         = Result.OpenFoamPath ?? "";
            TxtWslDistro.Text    = Result.WslDistroName ?? "Ubuntu";

            CmbEnvType.SelectedIndex = Result.DetectedEnvironment switch
            {
                OpenFoamEnvironmentType.NativeWindows => 1,
                OpenFoamEnvironmentType.WSL2          => 2,
                OpenFoamEnvironmentType.Docker        => 3,
                OpenFoamEnvironmentType.BlueCFD       => 4,
                _                                     => 0
            };

            TxtDiffusivity.Text     = Result.DiffusivityM2PerS.ToString("E2", inv);
            TxtWriteInterval.Text   = Result.WriteIntervalS > 0
                                       ? Result.WriteIntervalS.ToString("F1", inv)
                                       : "auto";
            NudGridRes.Value        = Result.GridResolution;
            NudProcessors.Value     = Math.Min(Result.NumberOfProcessors, Environment.ProcessorCount);
            TxtSolverTolerance.Text = Result.SolverTolerance.ToString("E1", inv);

            // Match the scheme by string content; default to linearUpwind.
            CmbScheme.SelectedIndex = 0;
            for (int i = 0; i < CmbScheme.Items.Count; i++)
                if (CmbScheme.Items[i] is ComboBoxItem cbi
                    && string.Equals(cbi.Content?.ToString(), Result.NumericalScheme,
                        StringComparison.OrdinalIgnoreCase))
                {
                    CmbScheme.SelectedIndex = i;
                    break;
                }

            ChkAdjustableDt.IsChecked = Result.AdjustableTimeStep;
            TxtMaxCourant.Text        = Result.MaxCourantNumber.ToString("F2", inv);
            TxtMaxCourant.IsEnabled   = Result.AdjustableTimeStep;
            NudPurgeWrite.Value       = Result.PurgeWrite;

            ChkAtmBL.IsChecked = Result.UseAtmosphericBL;
            TxtSct.Text        = Result.TurbulentSchmidtNumber.ToString("G4", inv);
            TxtPrt.Text        = Result.TurbulentPrandtlNumber.ToString("G4", inv);
            TxtSigmaEps.Text   = Result.KEpsilonSigmaEpsilon.ToString("G4", inv);
            TxtCeps3.Text      = Result.BuoyancyEpsCoefficient.HasValue
                ? Result.BuoyancyEpsCoefficient.Value.ToString("G4", inv)
                : "";

            // Match ground BC by enum-name string.
            string gbcName = Result.GroundThermalBC.ToString();
            CmbGroundBC.SelectedIndex = 0;
            for (int i = 0; i < CmbGroundBC.Items.Count; i++)
                if (CmbGroundBC.Items[i] is ComboBoxItem cbi
                    && string.Equals(cbi.Content?.ToString(), gbcName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    CmbGroundBC.SelectedIndex = i;
                    break;
                }

            TxtGroundT.Text = Result.GroundTemperatureK.ToString("G6", inv);
            TxtGroundQ.Text = Result.GroundHeatFluxWPerM2.ToString("G6", inv);

            ChkSubgrid.IsChecked    = Result.UseGaussianSubgrid;
            TxtSubgridMargin.Text   = Result.SubgridMarginFactor.ToString("F1", inv);
            TxtSubgridMargin.IsEnabled = Result.UseGaussianSubgrid;
            ChkWindField.IsChecked  = Result.UseWindField;

            bool gpuAvailable = DisperSim3D.Core.FluidX3DBridge.IsAvailable();
            ChkGpuTracer.IsChecked = Result.UseGpuBuoyantTracer && gpuAvailable;
            ChkGpuTracer.IsEnabled = gpuAvailable;
            if (!gpuAvailable)
                global::Avalonia.Controls.ToolTip.SetTip(ChkGpuTracer,
                    "DISABLED: " + DisperSim3D.Core.FluidX3DBridge.LastAvailabilityError);

            OnEnvTypeChanged();
        }

        // ── Environment type selector ────────────────────────────────────────
        private void CmbEnvType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
            => OnEnvTypeChanged();

        private void OnEnvTypeChanged()
        {
            bool isWsl    = CmbEnvType.SelectedIndex == 2;
            bool isDocker = CmbEnvType.SelectedIndex == 3;
            // Some fields aren't materialised during the very first XAML pass,
            // so guard each before touching them.
            if (LblWslDistro != null) LblWslDistro.IsVisible = isWsl;
            if (TxtWslDistro != null) TxtWslDistro.IsVisible = isWsl;
            if (BtnBrowse    != null) BtnBrowse.IsVisible    = !isDocker;
            UpdateStatus();
        }

        private OpenFoamEnvironmentType GetSelectedType() => CmbEnvType.SelectedIndex switch
        {
            1 => OpenFoamEnvironmentType.NativeWindows,
            2 => OpenFoamEnvironmentType.WSL2,
            3 => OpenFoamEnvironmentType.Docker,
            4 => OpenFoamEnvironmentType.BlueCFD,
            _ => OpenFoamEnvironmentType.None
        };

        private void UpdateStatus()
        {
            var type = GetSelectedType();
            string path = (TxtPath.Text ?? "").Trim();

            if (type == OpenFoamEnvironmentType.None || string.IsNullOrEmpty(path))
            {
                LblEnvStatus.Text = "No OpenFOAM environment configured.";
                LblEnvStatus.Foreground = global::Avalonia.Media.Brushes.Gray;
                return;
            }

            _environment.Configure(path, type, (TxtWslDistro.Text ?? "").Trim());
            LblEnvStatus.Text = _environment.StatusMessage ?? "Configured";
            LblEnvStatus.Foreground = _environment.IsAvailable
                ? global::Avalonia.Media.Brushes.DarkGreen
                : global::Avalonia.Media.Brushes.Red;
        }

        // ── Browse for OpenFOAM install folder ───────────────────────────────
        private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select OpenFOAM installation folder",
                AllowMultiple = false
            });
            if (folders == null || folders.Count == 0) return;

            string path = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
            TxtPath.Text = path;
            UpdateStatus();
        }

        // ── "Test" button — runs blockMesh -help in the configured env ───────
        private async void BtnTest_Click(object? sender, RoutedEventArgs e)
        {
            var type = GetSelectedType();
            string path = (TxtPath.Text ?? "").Trim();

            if (type == OpenFoamEnvironmentType.None || string.IsNullOrEmpty(path))
            {
                LblEnvStatus.Text = "Select an environment type and path first.";
                LblEnvStatus.Foreground = global::Avalonia.Media.Brushes.OrangeRed;
                return;
            }

            _environment.Configure(path, type, (TxtWslDistro.Text ?? "").Trim());
            if (!_environment.IsAvailable)
            {
                LblEnvStatus.Text = _environment.StatusMessage ?? "Environment not available";
                LblEnvStatus.Foreground = global::Avalonia.Media.Brushes.Red;
                return;
            }

            BtnTest.IsEnabled = false;
            LblEnvStatus.Text = "Testing...";
            LblEnvStatus.Foreground = global::Avalonia.Media.Brushes.Gray;

            try
            {
                // Build a throwaway "case" containing only a system/ folder so
                // blockMesh -help has somewhere to chdir to. The command itself
                // doesn't actually mesh anything; we only want to confirm the
                // OpenFOAM toolchain responds.
                string tempCase = Path.Combine(DisperSim3D.Core.TempManager.GetWorkDir(),
                    "DisperSim_OF_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(Path.Combine(tempCase, "system"));
                File.WriteAllText(Path.Combine(tempCase, "system", "controlDict"), "");

                bool success = false;
                string stderr = "";

                await Task.Run(() =>
                {
                    var proc = _environment.StartCommand(tempCase, "blockMesh -help");
                    string stdout = proc.StandardOutput.ReadToEnd();
                    stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(15000);
                    success = stdout.Contains("blockMesh") || stdout.Contains("OpenFOAM");
                });

                try { Directory.Delete(tempCase, true); } catch { }

                if (success)
                {
                    LblEnvStatus.Text = "OK — blockMesh responded. " + (_environment.StatusMessage ?? "");
                    LblEnvStatus.Foreground = global::Avalonia.Media.Brushes.DarkGreen;
                }
                else
                {
                    string msg = string.IsNullOrWhiteSpace(stderr) ? "No response from blockMesh" : stderr.Trim();
                    if (msg.Length > 120) msg = msg.Substring(0, 120) + "...";
                    LblEnvStatus.Text = "FAILED: " + msg;
                    LblEnvStatus.Foreground = global::Avalonia.Media.Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                LblEnvStatus.Text = "Error: " + ex.Message;
                LblEnvStatus.Foreground = global::Avalonia.Media.Brushes.Red;
            }
            finally
            {
                BtnTest.IsEnabled = true;
            }
        }

        // ── Solver fields callbacks ──────────────────────────────────────────
        private void NudGridRes_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
            => UpdateCellEstimate();

        private void ChkAdjustableDt_Changed(object? sender, RoutedEventArgs e)
        {
            if (TxtMaxCourant != null)
                TxtMaxCourant.IsEnabled = ChkAdjustableDt.IsChecked == true;
        }

        private void ChkSubgrid_Changed(object? sender, RoutedEventArgs e)
        {
            if (TxtSubgridMargin != null)
                TxtSubgridMargin.IsEnabled = ChkSubgrid.IsChecked == true;
        }

        private void UpdateCellEstimate()
        {
            int n = (int)(NudGridRes.Value ?? 50m);
            long cells = (long)n * n * n;
            string category;
            global::Avalonia.Media.IBrush clr;
            if (cells <= 1_000_000)            { category = "Simple / Fast";          clr = global::Avalonia.Media.Brushes.DarkGreen; }
            else if (cells <= 10_000_000)      { category = "Moderate";               clr = global::Avalonia.Media.Brushes.DarkGoldenrod; }
            else if (cells <= 50_000_000)      { category = "Heavy — may take several minutes"; clr = global::Avalonia.Media.Brushes.OrangeRed; }
            else                                { category = "Very heavy — requires powerful hardware"; clr = global::Avalonia.Media.Brushes.Red; }

            LblCellEstimate.Text = string.Format(CultureInfo.InvariantCulture,
                "≈ {0:N0} cells ({1}³)  —  {2}", cells, n, category);
            LblCellEstimate.Foreground = clr;
        }

        // ── OK / Cancel ──────────────────────────────────────────────────────
        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            var inv = CultureInfo.InvariantCulture;

            if (double.TryParse(TxtDiffusivity.Text, NumberStyles.Float, inv, out double diff))
                Result.DiffusivityM2PerS = diff;

            string writeText = (TxtWriteInterval.Text ?? "").Trim();
            if (string.Equals(writeText, "auto", StringComparison.OrdinalIgnoreCase))
                Result.WriteIntervalS = -1;
            else if (double.TryParse(writeText, NumberStyles.Float, inv, out double wi))
                Result.WriteIntervalS = wi;

            Result.GridResolution      = (int)(NudGridRes.Value ?? 50m);
            Result.NumberOfProcessors  = (int)(NudProcessors.Value ?? 1m);

            if (double.TryParse(TxtSolverTolerance.Text, NumberStyles.Float, inv, out double tol))
                Result.SolverTolerance = tol;

            Result.NumericalScheme = (CmbScheme.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "linearUpwind";
            Result.AdjustableTimeStep = ChkAdjustableDt.IsChecked == true;

            if (double.TryParse(TxtMaxCourant.Text, NumberStyles.Float, inv, out double co))
                Result.MaxCourantNumber = co;

            Result.PurgeWrite        = (int)(NudPurgeWrite.Value ?? 0m);
            Result.UseGaussianSubgrid = ChkSubgrid.IsChecked == true;

            if (double.TryParse(TxtSubgridMargin.Text, NumberStyles.Float, inv, out double margin))
                Result.SubgridMarginFactor = Math.Max(1.0, Math.Min(3.0, margin));

            Result.UseWindField    = ChkWindField.IsChecked == true;
            Result.UseAtmosphericBL = ChkAtmBL.IsChecked == true;
            Result.UseGpuBuoyantTracer = ChkGpuTracer.IsChecked == true;

            if (double.TryParse(TxtSct.Text, NumberStyles.Float, inv, out double dv) && dv > 0)
                Result.TurbulentSchmidtNumber = dv;
            if (double.TryParse(TxtPrt.Text, NumberStyles.Float, inv, out dv) && dv > 0)
                Result.TurbulentPrandtlNumber = dv;
            if (double.TryParse(TxtSigmaEps.Text, NumberStyles.Float, inv, out dv) && dv > 0)
                Result.KEpsilonSigmaEpsilon = dv;

            if (string.IsNullOrWhiteSpace(TxtCeps3.Text))
                Result.BuoyancyEpsCoefficient = null;
            else if (double.TryParse(TxtCeps3.Text, NumberStyles.Float, inv, out dv))
                Result.BuoyancyEpsCoefficient = dv;

            string gbcText = (CmbGroundBC.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Adiabatic";
            if (Enum.TryParse<GroundThermalBoundary>(gbcText, out var gbc))
                Result.GroundThermalBC = gbc;

            if (double.TryParse(TxtGroundT.Text, NumberStyles.Float, inv, out dv) && dv > 0)
                Result.GroundTemperatureK = dv;
            if (double.TryParse(TxtGroundQ.Text, NumberStyles.Float, inv, out dv))
                Result.GroundHeatFluxWPerM2 = dv;

            var type = GetSelectedType();
            string path = (TxtPath.Text ?? "").Trim();
            _environment.Configure(path, type, (TxtWslDistro.Text ?? "").Trim());

            Result.DetectedEnvironment = type;
            Result.OpenFoamPath        = path;
            Result.WslDistroName       = (TxtWslDistro.Text ?? "").Trim();
            if (type == OpenFoamEnvironmentType.Docker)   Result.DockerImageName = path;
            else if (type == OpenFoamEnvironmentType.BlueCFD) Result.BlueCfdPath = path;

            Close(true);
        }
    }
}
