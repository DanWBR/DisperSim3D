using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Application-level GPU + performance settings. Split into two tabs so each
    /// section gets enough room: "Compute GPU" lists detected OpenCL devices and
    /// lets the user pin one for FluidX3D; "Memory Estimator" previews VRAM / RAM
    /// / disk consumption for a chosen solver + grid combo. Layout uses
    /// TableLayoutPanel only. Cancel left, OK right (project convention).
    /// </summary>
    public class GpuPerformanceSettingsDialog : Form
    {
        // GPU tab controls
        private ListView _lvDevices;
        private ComboBox _cmbPreferred;
        private Button _btnRefreshDevices, _btnWinGfx;
        private Label _lblGpuStatus;

        // Estimator tab controls
        private ComboBox _cmbSolver;
        private NumericUpDown _nudGrid, _nudSnapshots, _nudRefinement;
        private ComboBox _cmbQuality;
        private Label _lblEstimate;

        public GpuPerformanceSettingsDialog()
        {
            InitializeComponent();
            Text = "GPU Performance Settings (Application)";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            BuildUI();
            RefreshDevices();
            UpdateEstimate();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding((int)(10 * dpi)),
                ColumnCount = 1,
                RowCount = 2
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildComputeTab(dpi));
            tabs.TabPages.Add(BuildEstimatorTab(dpi));
            outer.Controls.Add(tabs, 0, 0);
            outer.Controls.Add(BuildButtonRow(dpi), 0, 1);
            Controls.Add(outer);
        }

        // ── Tab 1: Compute GPU ──────────────────────────────────────────

        private TabPage BuildComputeTab(float dpi)
        {
            var tab = new TabPage("Compute GPU") { Padding = new Padding((int)(10 * dpi)) };

            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // header text
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));      // ListView
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // status line
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // preferred + refresh
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // win gfx settings row

            var headerLabel = new Label
            {
                Text = "OpenCL devices available to FluidX3D. Selecting a specific device pins " +
                       "every FluidX3D run (wind, dispersion, fire) to that GPU.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(0, 0, 0, (int)(6 * dpi))
            };
            t.Controls.Add(headerLabel, 0, 0);

            _lvDevices = new ListView
            {
                Dock = DockStyle.Fill,
                View = System.Windows.Forms.View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                MinimumSize = new Size(0, (int)(180 * dpi))
            };
            _lvDevices.Columns.Add("ID", (int)(40 * dpi));
            _lvDevices.Columns.Add("Name", (int)(280 * dpi));
            _lvDevices.Columns.Add("Vendor", (int)(120 * dpi));
            _lvDevices.Columns.Add("VRAM", (int)(80 * dpi));
            _lvDevices.Columns.Add("TFLOPS", (int)(70 * dpi));
            _lvDevices.Columns.Add("CUs", (int)(50 * dpi));
            _lvDevices.Columns.Add("Type", (int)(60 * dpi));
            t.Controls.Add(_lvDevices, 0, 1);

            _lblGpuStatus = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(0, (int)(4 * dpi), 0, 0),
                Text = ""
            };
            t.Controls.Add(_lblGpuStatus, 0, 2);

            // Preferred device row.
            var prefRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(0, (int)(8 * dpi), 0, 0)
            };
            prefRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            prefRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            prefRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            prefRow.Controls.Add(new Label
            {
                Text = "Preferred compute device:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, (int)(6 * dpi), (int)(6 * dpi), 0)
            }, 0, 0);
            _cmbPreferred = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            prefRow.Controls.Add(_cmbPreferred, 1, 0);
            _btnRefreshDevices = new Button
            {
                Text = "Refresh",
                AutoSize = true,
                Padding = new Padding(10, 2, 10, 2),
                Margin = new Padding(6, 0, 0, 0)
            };
            _btnRefreshDevices.Click += (s, e) => RefreshDevices();
            prefRow.Controls.Add(_btnRefreshDevices, 2, 0);
            t.Controls.Add(prefRow, 0, 3);

            // Windows Graphics Settings row.
            var wgsRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0, (int)(12 * dpi), 0, 0)
            };
            wgsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            wgsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            wgsRow.Controls.Add(new Label
            {
                Text = "3D viewport rendering uses the Windows default GPU. " +
                       "To force the discrete GPU, pin DisperSim3D.exe in Windows Graphics Settings.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                MaximumSize = new Size((int)(620 * dpi), 0),
                Padding = new Padding(0, (int)(6 * dpi), (int)(8 * dpi), 0)
            }, 0, 0);
            _btnWinGfx = new Button
            {
                Text = "Open Windows Graphics Settings",
                AutoSize = true,
                Padding = new Padding(10, 4, 10, 4)
            };
            _btnWinGfx.Click += (s, e) => OpenWindowsGraphicsSettings();
            wgsRow.Controls.Add(_btnWinGfx, 1, 0);
            t.Controls.Add(wgsRow, 0, 4);

            tab.Controls.Add(t);
            return tab;
        }

        // ── Tab 2: Memory Estimator ─────────────────────────────────────

        private TabPage BuildEstimatorTab(float dpi)
        {
            var tab = new TabPage("Memory Estimator") { Padding = new Padding((int)(10 * dpi)) };

            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Left: inputs.
            var left = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 7,
                Padding = new Padding(0, 0, (int)(8 * dpi), 0)
            };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++) left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            int row = 0;
            left.Controls.Add(L("Solver:", dpi), 0, row);
            _cmbSolver = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (CfdSolverType s in Enum.GetValues(typeof(CfdSolverType)))
                _cmbSolver.Items.Add(s);
            _cmbSolver.SelectedItem = CfdSolverType.FluidX3DDispersion;
            _cmbSolver.SelectedIndexChanged += (s, e) => UpdateEstimate();
            left.Controls.Add(_cmbSolver, 1, row++);

            left.Controls.Add(L("Grid resolution (N):", dpi), 0, row);
            _nudGrid = new NumericUpDown { Minimum = 10m, Maximum = 1024m, Value = 40m, Anchor = AnchorStyles.Left, Width = (int)(110 * dpi) };
            _nudGrid.ValueChanged += (s, e) => UpdateEstimate();
            left.Controls.Add(_nudGrid, 1, row++);

            left.Controls.Add(L("FluidX3D Quality:", dpi), 0, row);
            _cmbQuality = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (FluidX3DQuality q in Enum.GetValues(typeof(FluidX3DQuality)))
                _cmbQuality.Items.Add(q);
            _cmbQuality.SelectedItem = FluidX3DQuality.Fast;
            _cmbQuality.SelectedIndexChanged += (s, e) => UpdateEstimate();
            left.Controls.Add(_cmbQuality, 1, row++);

            left.Controls.Add(L("Snapshot count:", dpi), 0, row);
            _nudSnapshots = new NumericUpDown { Minimum = 1m, Maximum = 1000m, Value = 20m, Anchor = AnchorStyles.Left, Width = (int)(110 * dpi) };
            _nudSnapshots.ValueChanged += (s, e) => UpdateEstimate();
            left.Controls.Add(_nudSnapshots, 1, row++);

            left.Controls.Add(L("OpenFOAM mesh refinement:", dpi), 0, row);
            _nudRefinement = new NumericUpDown { Minimum = 0m, Maximum = 3m, Value = 0m, Anchor = AnchorStyles.Left, Width = (int)(70 * dpi) };
            _nudRefinement.ValueChanged += (s, e) => UpdateEstimate();
            left.Controls.Add(_nudRefinement, 1, row++);

            left.Controls.Add(new Label
            {
                Text = "(0 = base mesh, 1 ≈ 3× cells, 2 ≈ 10× cells. Only used for OpenFOAM solvers.)",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                MaximumSize = new Size((int)(360 * dpi), 0)
            }, 1, row++);

            // Spacer
            left.Controls.Add(new Label(), 1, row);

            t.Controls.Add(left, 0, 0);

            // Right: output text box.
            var rightWrap = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            rightWrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rightWrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightWrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rightWrap.Controls.Add(new Label
            {
                Text = "Estimated consumption:",
                AutoSize = true,
                Padding = new Padding(0, 0, 0, (int)(4 * dpi))
            }, 0, 0);
            _lblEstimate = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                ForeColor = SystemColors.ControlText,
                Padding = new Padding((int)(8 * dpi)),
                Font = new Font("Consolas", 10f),
                BackColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                Text = "",
                TextAlign = ContentAlignment.TopLeft
            };
            rightWrap.Controls.Add(_lblEstimate, 0, 1);
            t.Controls.Add(rightWrap, 1, 0);

            tab.Controls.Add(t);
            return tab;
        }

        private TableLayoutPanel BuildButtonRow(float dpi)
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 3,
                Padding = new Padding(0, (int)(8 * dpi), 0, 0)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var btnCancel = new Button
            {
                Text = "Cancel",
                AutoSize = true,
                Padding = new Padding(12, 2, 12, 2),
                DialogResult = DialogResult.Cancel
            };
            var btnOK = new Button
            {
                Text = "OK",
                AutoSize = true,
                Padding = new Padding(16, 2, 16, 2)
            };
            btnOK.Click += (s, e) => CommitAndClose();
            t.Controls.Add(new Label(), 0, 0);
            t.Controls.Add(btnCancel, 1, 0);
            t.Controls.Add(btnOK, 2, 0);
            AcceptButton = btnOK;
            CancelButton = btnCancel;
            return t;
        }

        private static Label L(string text, float dpi) => new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, (int)(6 * dpi), (int)(6 * dpi), 0)
        };

        // ── Device enumeration ──────────────────────────────────────────

        private void RefreshDevices()
        {
            _lvDevices.Items.Clear();
            _cmbPreferred.Items.Clear();
            _cmbPreferred.Items.Add(new DeviceItem { Id = -1, Display = "Auto (fastest available)" });
            _lblGpuStatus.ForeColor = SystemColors.GrayText;
            _lblGpuStatus.Text = "Probing OpenCL devices...";
            Application.DoEvents();

            // Force a tiny LBM create so FluidX3D's OpenCL context comes up, then list.
            string json = null;
            bool dllOk = false;
            try { dllOk = FluidX3DBridge.IsAvailable(); }
            catch (Exception ex) { _lblGpuStatus.Text = "FluidX3D load failed: " + ex.Message; }

            if (!dllOk)
            {
                _lvDevices.Items.Add(new ListViewItem(new[] {
                    "—",
                    "FluidX3D.dll didn't load — verify CUDA / OpenCL drivers and that " +
                    "FluidX3D.dll sits next to DisperSim3D.exe.",
                    "—", "—", "—", "—", "—"
                }));
                _cmbPreferred.SelectedIndex = 0;
                _lblGpuStatus.ForeColor = Color.Firebrick;
                _lblGpuStatus.Text = "FluidX3D unavailable.";
                return;
            }

            try { json = FluidX3DBridge.ListDevicesJson(); }
            catch (Exception ex) { _lblGpuStatus.Text = "Device enumeration failed: " + ex.Message; }

            if (string.IsNullOrEmpty(json) || json == "[]")
            {
                string err = FluidX3DBridge.LastListDevicesError;
                string row = string.IsNullOrEmpty(err)
                    ? "No OpenCL devices reported. Drivers may need updating."
                    : err;
                _lvDevices.Items.Add(new ListViewItem(new[] {
                    "—", row, "—", "—", "—", "—", "—"
                }));
                _cmbPreferred.SelectedIndex = 0;
                _lblGpuStatus.ForeColor = Color.Firebrick;
                _lblGpuStatus.Text = string.IsNullOrEmpty(err) ? "No OpenCL devices."
                    : err.Length > 140 ? err.Substring(0, 137) + "..." : err;
                return;
            }

            int count = 0;
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    int id = el.GetProperty("id").GetInt32();
                    string name = el.GetProperty("name").GetString() ?? "";
                    string vendor = el.GetProperty("vendor").GetString() ?? "";
                    int mem = el.GetProperty("memory_mb").GetInt32();
                    double tflops = el.GetProperty("tflops").GetDouble();
                    int cu = el.GetProperty("compute_units").GetInt32();
                    bool gpu = el.GetProperty("is_gpu").GetBoolean();
                    var lvi = new ListViewItem(id.ToString());
                    lvi.SubItems.Add(name);
                    lvi.SubItems.Add(vendor);
                    lvi.SubItems.Add(MemoryEstimator.HumanBytes((long)mem * 1024L * 1024L));
                    lvi.SubItems.Add(tflops.ToString("F2"));
                    lvi.SubItems.Add(cu.ToString());
                    lvi.SubItems.Add(gpu ? "GPU" : "CPU");
                    _lvDevices.Items.Add(lvi);
                    _cmbPreferred.Items.Add(new DeviceItem
                    {
                        Id = id,
                        Display = string.Format("[{0}] {1}  ({2})", id, name,
                            MemoryEstimator.HumanBytes((long)mem * 1024L * 1024L))
                    });
                    count++;
                }
            }
            catch (Exception ex)
            {
                _lvDevices.Items.Add(new ListViewItem(new[] { "—",
                    "JSON parse failed: " + ex.Message,
                    "—", "—", "—", "—", "—" }));
                _lblGpuStatus.ForeColor = Color.Firebrick;
                _lblGpuStatus.Text = "Enumeration error.";
                _cmbPreferred.SelectedIndex = 0;
                return;
            }

            _lblGpuStatus.ForeColor = Color.DarkGreen;
            _lblGpuStatus.Text = string.Format("Detected {0} OpenCL device(s).", count);

            int target = AppSettings.Instance.PreferredComputeDeviceId;
            int idx = 0;
            for (int i = 0; i < _cmbPreferred.Items.Count; i++)
                if (((DeviceItem)_cmbPreferred.Items[i]).Id == target) { idx = i; break; }
            _cmbPreferred.SelectedIndex = idx;
        }

        private void OpenWindowsGraphicsSettings()
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
                MessageBox.Show(this,
                    "Open Settings → System → Display → Graphics, then add the path to " +
                    "DisperSim3D.exe and choose your discrete GPU under 'High performance'.",
                    "Windows Graphics Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ── Memory estimator ────────────────────────────────────────────

        private void UpdateEstimate()
        {
            var solver = (CfdSolverType)(_cmbSolver.SelectedItem ?? CfdSolverType.FluidX3DDispersion);
            var quality = (FluidX3DQuality)(_cmbQuality.SelectedItem ?? FluidX3DQuality.Fast);
            int grid = (int)_nudGrid.Value;
            int snaps = (int)_nudSnapshots.Value;
            int refLvl = (int)_nudRefinement.Value;
            var est = MemoryEstimator.For(solver, grid, snaps, quality, refLvl);
            _lblEstimate.Text = est.Format();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            // 
            // GpuPerformanceSettingsDialog
            // 
            ClientSize = new Size(751, 453);
            Name = "GpuPerformanceSettingsDialog";
            Load += GpuPerformanceSettingsDialog_Load;
            ResumeLayout(false);

        }

        private void CommitAndClose()
        {
            var sel = _cmbPreferred.SelectedItem as DeviceItem;
            AppSettings.Instance.PreferredComputeDeviceId = sel?.Id ?? -1;
            AppSettings.Instance.Save();
            DialogResult = DialogResult.OK;
            Close();
        }

        private sealed class DeviceItem
        {
            public int Id;
            public string Display;
            public override string ToString() => Display;
        }

        private void GpuPerformanceSettingsDialog_Load(object sender, EventArgs e)
        {

        }
    }
}
