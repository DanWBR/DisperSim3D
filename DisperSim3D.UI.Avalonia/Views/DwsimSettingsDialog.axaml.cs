#nullable enable
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DisperSim3D.Core;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>DwsimSettingsDialog</c>.
    /// Edits the application-level DWSIM configuration (install directory +
    /// default property package) stored on <see cref="AppSettings"/>. The
    /// "Test connection" button asks <see cref="DwsimThermo"/> to actually
    /// load the FluentAPI from the chosen folder and reports compound /
    /// property-package counts on success.
    /// </summary>
    public partial class DwsimSettingsDialog : Window
    {
        // Fallback list when DWSIM hasn't loaded yet — keeps the combo
        // populated even before the user points at a real install.
        private static readonly string[] CommonPackages =
        {
            "Peng-Robinson 1978 (PR78)",
            "Peng-Robinson (PR)",
            "Peng-Robinson 1978 Advanced",
            "Peng-Robinson-Stryjek-Vera 2 (PRSV2-M)",
            "Peng-Robinson-Stryjek-Vera 2 (PRSV2-VL)",
            "Soave-Redlich-Kwong (SRK)",
            "Soave-Redlich-Kwong Advanced",
            "Lee-Kesler-Plöcker",
            "Chao-Seader",
            "Grayson-Streed",
            "Raoult's Law",
            "NRTL",
            "UNIQUAC",
            "Wilson",
            "UNIFAC",
            "Modified UNIFAC (Dortmund)",
            "Steam Tables (IAPWS-IF97)",
            "CoolProp",
            "GERG-2008",
            "PC-SAFT"
        };

        public DwsimSettingsDialog()
        {
            InitializeComponent();
            TxtInstallPath.Text = AppSettings.Instance.DwsimInstallPath ?? "";
            PopulatePackages();
        }

        private void PopulatePackages()
        {
            CmbPropertyPackage.Items.Clear();
            // Prefer the live list if DwsimThermo has already initialised
            // — keeps the combo accurate vs. what the engine can actually use.
            var live = DwsimThermo.AvailablePropertyPackages();
            var packages = live.Count > 0 ? (System.Collections.Generic.IEnumerable<string>)live : CommonPackages;
            foreach (var p in packages)
                CmbPropertyPackage.Items.Add(new ComboBoxItem { Content = p });

            string? current = AppSettings.Instance.DwsimPropertyPackage;
            int idx = -1;
            if (!string.IsNullOrEmpty(current))
            {
                for (int i = 0; i < CmbPropertyPackage.Items.Count; i++)
                    if (CmbPropertyPackage.Items[i] is ComboBoxItem cbi
                        && string.Equals(cbi.Content?.ToString(), current,
                                StringComparison.OrdinalIgnoreCase))
                    { idx = i; break; }
                if (idx < 0)
                {
                    // Keep the saved-but-unknown package in the list so we
                    // don't silently downgrade it to the default on Save.
                    CmbPropertyPackage.Items.Insert(0, new ComboBoxItem { Content = current });
                    idx = 0;
                }
            }
            if (idx < 0) idx = 0;
            CmbPropertyPackage.SelectedIndex = idx;
        }

        private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select the DWSIM install directory (contains DWSIM.Automation.FluentAPI.dll)",
                AllowMultiple = false
            });
            if (folders == null || folders.Count == 0) return;
            TxtInstallPath.Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
        }

        private async void BtnTest_Click(object? sender, RoutedEventArgs e)
        {
            LblStatus.Text = "Loading DWSIM.Automation.FluentAPI...";
            LblStatus.Foreground = Brushes.Gray;
            BtnTest.IsEnabled = false;
            try
            {
                // Off the UI thread — Initialize loads native libraries and
                // can take a few seconds on a fresh install.
                string installPath = (TxtInstallPath.Text ?? "").Trim();
                bool ok = await Task.Run(() => DwsimThermo.Initialize(installPath));

                if (ok)
                {
                    int compounds = DwsimThermo.AvailableCompounds().Count;
                    int packages = DwsimThermo.AvailablePropertyPackages().Count;
                    LblStatus.Text = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "Connected — {0} compounds, {1} property packages.",
                        compounds, packages);
                    LblStatus.Foreground = Brushes.DarkGreen;
                    PopulatePackages();
                }
                else
                {
                    LblStatus.Text = "Failed: " + (DwsimThermo.LastError ?? "(unknown error)");
                    LblStatus.Foreground = Brushes.Firebrick;
                }
            }
            finally
            {
                BtnTest.IsEnabled = true;
            }
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            AppSettings.Instance.DwsimInstallPath = (TxtInstallPath.Text ?? "").Trim();
            AppSettings.Instance.DwsimPropertyPackage =
                (CmbPropertyPackage.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? "Peng-Robinson 1978 (PR78)";
            AppSettings.Instance.Save();
            // Drop the cached flowsheet so the next flash picks up the new
            // path / property package combination.
            DwsimThermo.ResetFlowsheetCache();
            Close(true);
        }
    }
}
