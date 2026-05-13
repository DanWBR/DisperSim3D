#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>GpuPerformanceSettingsDialog</c>.
    /// Application-level GPU + performance settings split into two tabs:
    /// "Compute GPU" lists OpenCL devices reported by FluidX3D and lets the
    /// user pin one (stored on <see cref="AppSettings.PreferredComputeDeviceId"/>);
    /// "Memory Estimator" previews VRAM / RAM / disk usage for a chosen
    /// solver + grid combo via <see cref="MemoryEstimator.For"/>.
    /// </summary>
    public partial class GpuPerformanceSettingsDialog : Window
    {
        private readonly ObservableCollection<DeviceRow> _deviceRows = new();

        public GpuPerformanceSettingsDialog()
        {
            InitializeComponent();

            // ── Compute GPU tab ──────────────────────────────────────────────
            GridDevices.ItemsSource = _deviceRows;
            GridDevices.Columns.Add(new DataGridTextColumn
            {
                Header = "ID", Width = new DataGridLength(40),
                Binding = new Binding(nameof(DeviceRow.Id))
            });
            GridDevices.Columns.Add(new DataGridTextColumn
            {
                Header = "Name", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(DeviceRow.Name))
            });
            GridDevices.Columns.Add(new DataGridTextColumn
            {
                Header = "Vendor", Width = new DataGridLength(120),
                Binding = new Binding(nameof(DeviceRow.Vendor))
            });
            GridDevices.Columns.Add(new DataGridTextColumn
            {
                Header = "VRAM", Width = new DataGridLength(80),
                Binding = new Binding(nameof(DeviceRow.Vram))
            });
            GridDevices.Columns.Add(new DataGridTextColumn
            {
                Header = "TFLOPS", Width = new DataGridLength(70),
                Binding = new Binding(nameof(DeviceRow.Tflops))
            });
            GridDevices.Columns.Add(new DataGridTextColumn
            {
                Header = "CUs", Width = new DataGridLength(50),
                Binding = new Binding(nameof(DeviceRow.CUs))
            });
            GridDevices.Columns.Add(new DataGridTextColumn
            {
                Header = "Type", Width = new DataGridLength(60),
                Binding = new Binding(nameof(DeviceRow.Type))
            });

            // ── Memory Estimator tab ─────────────────────────────────────────
            // Solver combo: enum-typed items, no string mapping necessary.
            foreach (CfdSolverType s in Enum.GetValues(typeof(CfdSolverType)))
                CmbSolver.Items.Add(new ComboBoxItem { Content = s.ToString(), Tag = s });
            SelectComboByTag(CmbSolver, CfdSolverType.FluidX3DDispersion);
            foreach (FluidX3DQuality q in Enum.GetValues(typeof(FluidX3DQuality)))
                CmbQuality.Items.Add(new ComboBoxItem { Content = q.ToString(), Tag = q });
            SelectComboByTag(CmbQuality, FluidX3DQuality.Fast);

            RefreshDevices();
            UpdateEstimate();
        }

        private static void SelectComboByTag<T>(ComboBox cb, T value) where T : struct
        {
            for (int i = 0; i < cb.Items.Count; i++)
                if (cb.Items[i] is ComboBoxItem cbi && cbi.Tag is T t && t.Equals(value))
                {
                    cb.SelectedIndex = i;
                    return;
                }
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        }

        // ── Device enumeration ───────────────────────────────────────────────
        private void BtnRefresh_Click(object? sender, RoutedEventArgs e) => RefreshDevices();

        private void RefreshDevices()
        {
            _deviceRows.Clear();
            CmbPreferred.Items.Clear();
            CmbPreferred.Items.Add(new DeviceComboItem { Id = -1, Display = "Auto (fastest available)" });
            LblGpuStatus.Foreground = Brushes.Gray;
            LblGpuStatus.Text = "Probing OpenCL devices...";

            // FluidX3D.dll must load before we can ask for devices — surface a
            // clear message when the native dependency isn't sitting next to
            // the .NET binary.
            bool dllOk = false;
            try { dllOk = FluidX3DBridge.IsAvailable(); }
            catch (Exception ex)
            {
                LblGpuStatus.Foreground = Brushes.Firebrick;
                LblGpuStatus.Text = "FluidX3D load failed: " + ex.Message;
            }

            if (!dllOk)
            {
                _deviceRows.Add(new DeviceRow
                {
                    Id = "—",
                    Name = "FluidX3D.dll didn't load — verify CUDA/OpenCL drivers and that FluidX3D.dll sits next to the binary.",
                    Vendor = "—", Vram = "—", Tflops = "—", CUs = "—", Type = "—"
                });
                CmbPreferred.SelectedIndex = 0;
                LblGpuStatus.Foreground = Brushes.Firebrick;
                LblGpuStatus.Text = "FluidX3D unavailable.";
                return;
            }

            string? json = null;
            try { json = FluidX3DBridge.ListDevicesJson(); }
            catch (Exception ex)
            {
                LblGpuStatus.Foreground = Brushes.Firebrick;
                LblGpuStatus.Text = "Device enumeration failed: " + ex.Message;
            }

            if (string.IsNullOrEmpty(json) || json == "[]")
            {
                string? err = FluidX3DBridge.LastListDevicesError;
                string row = string.IsNullOrEmpty(err)
                    ? "No OpenCL devices reported. Drivers may need updating."
                    : err;
                _deviceRows.Add(new DeviceRow
                {
                    Id = "—", Name = row, Vendor = "—",
                    Vram = "—", Tflops = "—", CUs = "—", Type = "—"
                });
                CmbPreferred.SelectedIndex = 0;
                LblGpuStatus.Foreground = Brushes.Firebrick;
                LblGpuStatus.Text = string.IsNullOrEmpty(err) ? "No OpenCL devices."
                    : err.Length > 140 ? err.Substring(0, 137) + "..." : err;
                return;
            }

            int count = 0;
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    int id    = el.GetProperty("id").GetInt32();
                    string nm = el.GetProperty("name").GetString() ?? "";
                    string vd = el.GetProperty("vendor").GetString() ?? "";
                    int mem   = el.GetProperty("memory_mb").GetInt32();
                    double tf = el.GetProperty("tflops").GetDouble();
                    int cu    = el.GetProperty("compute_units").GetInt32();
                    bool gpu  = el.GetProperty("is_gpu").GetBoolean();
                    var inv   = CultureInfo.InvariantCulture;
                    string vram = MemoryEstimator.HumanBytes((long)mem * 1024L * 1024L);
                    _deviceRows.Add(new DeviceRow
                    {
                        Id     = id.ToString(inv),
                        Name   = nm,
                        Vendor = vd,
                        Vram   = vram,
                        Tflops = tf.ToString("F2", inv),
                        CUs    = cu.ToString(inv),
                        Type   = gpu ? "GPU" : "CPU"
                    });
                    CmbPreferred.Items.Add(new DeviceComboItem
                    {
                        Id = id,
                        Display = string.Format(inv, "[{0}] {1}  ({2})", id, nm, vram)
                    });
                    count++;
                }
            }
            catch (Exception ex)
            {
                _deviceRows.Add(new DeviceRow
                {
                    Id = "—", Name = "JSON parse failed: " + ex.Message,
                    Vendor = "—", Vram = "—", Tflops = "—", CUs = "—", Type = "—"
                });
                LblGpuStatus.Foreground = Brushes.Firebrick;
                LblGpuStatus.Text = "Enumeration error.";
                CmbPreferred.SelectedIndex = 0;
                return;
            }

            LblGpuStatus.Foreground = Brushes.DarkGreen;
            LblGpuStatus.Text = string.Format(CultureInfo.InvariantCulture,
                "Detected {0} OpenCL device(s).", count);

            // Restore the saved preference if we can match it to an enumerated
            // device; otherwise fall back to "Auto".
            int target = AppSettings.Instance.PreferredComputeDeviceId;
            int idx = 0;
            for (int i = 0; i < CmbPreferred.Items.Count; i++)
                if (CmbPreferred.Items[i] is DeviceComboItem di && di.Id == target)
                {
                    idx = i; break;
                }
            CmbPreferred.SelectedIndex = idx;
        }

        // ── Windows Graphics Settings shortcut ───────────────────────────────
        private void BtnWinGfx_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:display-advancedgraphics",
                    UseShellExecute = true
                });
            }
            catch
            {
                // On non-Windows the URI scheme doesn't exist; surface a hint
                // instead of throwing.
                LblGpuStatus.Foreground = Brushes.Gray;
                LblGpuStatus.Text = "Open Settings → System → Display → Graphics; add DisperSim3D.UI.Avalonia.exe and pick 'High performance'.";
            }
        }

        // ── Estimator ────────────────────────────────────────────────────────
        private void EstimateInput_Changed(object? sender, SelectionChangedEventArgs e) => UpdateEstimate();
        private void EstimateInput_Changed_Numeric(object? sender, NumericUpDownValueChangedEventArgs e)
            => UpdateEstimate();

        private void UpdateEstimate()
        {
            // Defensive defaults so the estimate refreshes even if a control
            // hasn't been wired yet during the initial XAML pass.
            var solver = (CmbSolver?.SelectedItem as ComboBoxItem)?.Tag is CfdSolverType cst
                ? cst : CfdSolverType.FluidX3DDispersion;
            var quality = (CmbQuality?.SelectedItem as ComboBoxItem)?.Tag is FluidX3DQuality fq
                ? fq : FluidX3DQuality.Fast;
            int grid    = (int)(NudGrid?.Value ?? 40m);
            int snaps   = (int)(NudSnapshots?.Value ?? 20m);
            int refLvl  = (int)(NudRefinement?.Value ?? 0m);

            var est = MemoryEstimator.For(solver, grid, snaps, quality, refLvl);
            if (LblEstimate != null) LblEstimate.Text = est.Format();
        }

        // ── OK / Cancel ──────────────────────────────────────────────────────
        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            int id = -1;
            if (CmbPreferred.SelectedItem is DeviceComboItem item) id = item.Id;
            AppSettings.Instance.PreferredComputeDeviceId = id;
            AppSettings.Instance.Save();
            Close(true);
        }

        /// <summary>Row backing for the device DataGrid. Strings throughout so
        /// the formatted numbers come straight from the engine without re-
        /// running culture-sensitive ToString in the column bindings.</summary>
        public sealed class DeviceRow
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Vendor { get; set; } = "";
            public string Vram { get; set; } = "";
            public string Tflops { get; set; } = "";
            public string CUs { get; set; } = "";
            public string Type { get; set; } = "";
        }

        /// <summary>Combo entry that carries the OpenCL device id plus a
        /// rendered display string. ToString() is what ComboBox uses by
        /// default when no template is set.</summary>
        private sealed class DeviceComboItem
        {
            public int Id;
            public string Display = "";
            public override string ToString() => Display;
        }
    }
}
