using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Manages the library of pre-computed wind field scenarios for a scene.
    /// Allows creating, editing, running and deleting wind field scenarios.
    /// </summary>
    public class WindFieldManagerDialog : Form
    {
        private readonly Scene3D _scene;
        private readonly OpenFoamEnvironment _env;
        private ListBox _list;
        private Panel _propsPanel;

        private TextBox _txtName;
        private NumericUpDown _nudDomainSize;
        private NumericUpDown _nudDomainHeight;
        private NumericUpDown _nudGridRes;
        private NumericUpDown _nudWindSpeed;
        private NumericUpDown _nudWindDir;
        private ComboBox _cmbStability;
        private NumericUpDown _nudTemperature;
        private Label _lblStatus;
        private Button _btnRun;
        private ProgressBar _progress;

        private BackgroundWorker _worker;

        public List<WindFieldScenario> Scenarios { get; private set; }

        public WindFieldManagerDialog(Scene3D scene, OpenFoamEnvironment env)
            : this(scene, env, null)
        {
        }

        public WindFieldManagerDialog(Scene3D scene, OpenFoamEnvironment env, string preselectedId)
        {
            _scene = scene;
            _env = env ?? new OpenFoamEnvironment();
            Scenarios = new List<WindFieldScenario>(scene.WindFieldScenarios);
            BuildUI();
            RefreshList();

            if (!string.IsNullOrEmpty(preselectedId))
            {
                for (int i = 0; i < Scenarios.Count; i++)
                {
                    if (Scenarios[i].Id == preselectedId)
                    {
                        _list.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            this.Text = "Wind Field Manager";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Size = new Size((int)(820 * dpi), (int)(640 * dpi));
            this.Padding = new Padding((int)(10 * dpi));

            var outerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2
            };
            outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Left: list + buttons
            var leftPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1, RowCount = 2
            };
            leftPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _list = new ListBox { Dock = DockStyle.Fill };
            _list.SelectedIndexChanged += (s, e) => LoadSelected();
            leftPanel.Controls.Add(_list, 0, 0);

            var listButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            var btnNew = new Button { Text = "New", AutoSize = true };
            btnNew.Click += (s, e) =>
            {
                SaveCurrent();
                var wf = new WindFieldScenario { Name = "Wind Field " + (Scenarios.Count + 1) };
                if (wf.CfdConfig == null) wf.CfdConfig = new CfdConfiguration();
                // ScalarSimpleFoam preset retired — use the universal solver defaults.
                DisperSim3D.Core.CfdConfigurationPresets.ApplyForSolver(
                    wf.CfdConfig, CfdSolverType.RhoReactingBuoyantFoam, null, wf.Meteo);
                Scenarios.Add(wf);
                RefreshList();
                _list.SelectedIndex = Scenarios.Count - 1;
            };

            var btnRename = new Button { Text = "Rename", AutoSize = true };
            btnRename.Click += (s, e) =>
            {
                int idx = _list.SelectedIndex;
                if (idx < 0) return;
                string name = PromptInput("Rename Wind Field", "Enter new name:", Scenarios[idx].Name);
                if (!string.IsNullOrEmpty(name))
                {
                    Scenarios[idx].Name = name;
                    RefreshList();
                    _list.SelectedIndex = idx;
                }
            };

            var btnDuplicate = new Button { Text = "Duplicate", AutoSize = true };
            btnDuplicate.Click += (s, e) =>
            {
                int idx = _list.SelectedIndex;
                if (idx < 0) return;
                SaveCurrent();
                var orig = Scenarios[idx];
                var copy = new WindFieldScenario
                {
                    Name = orig.Name + " (copy)",
                    DomainSizeM = orig.DomainSizeM,
                    DomainHeightM = orig.DomainHeightM,
                    GridResolution = orig.GridResolution,
                    Meteo = new MeteorologicalConditions
                    {
                        WindSpeed = orig.Meteo.WindSpeed,
                        WindDirectionDeg = orig.Meteo.WindDirectionDeg,
                        StabilityClass = orig.Meteo.StabilityClass,
                        AmbientTemperature = orig.Meteo.AmbientTemperature,
                        AmbientPressure = orig.Meteo.AmbientPressure
                    }
                };
                Scenarios.Add(copy);
                RefreshList();
                _list.SelectedIndex = Scenarios.Count - 1;
            };

            var btnDelete = new Button { Text = "Delete", AutoSize = true };
            btnDelete.Click += (s, e) =>
            {
                int idx = _list.SelectedIndex;
                if (idx < 0) return;
                Scenarios.RemoveAt(idx);
                RefreshList();
            };

            listButtons.Controls.AddRange(new Control[] { btnNew, btnRename, btnDuplicate, btnDelete });
            leftPanel.Controls.Add(listButtons, 0, 1);
            outerLayout.Controls.Add(leftPanel, 0, 0);

            // Right: properties
            _propsPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var propsTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2, Padding = new Padding(4)
            };
            propsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            propsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;
            AddSectionHeader(propsTable, row++, "Identity");

            _txtName = new TextBox { Dock = DockStyle.Fill, Text = "Wind Field" };
            DialogHelpers.AddRowWithHelp(propsTable, ref row, "Name:", _txtName,
                "Display name shown in the dispersion scenario picker.");

            AddSectionHeader(propsTable, row++, "Domain");

            _nudDomainSize = MakeNud(10m, 100000m, 200m, 0);
            DialogHelpers.AddRowWithHelp(propsTable, ref row, "Domain Half-Size (m):", _nudDomainSize,
                "Half-extent of the simulation box in each horizontal direction.");

            _nudDomainHeight = MakeNud(10m, 5000m, 100m, 0);
            DialogHelpers.AddRowWithHelp(propsTable, ref row, "Domain Height (m):", _nudDomainHeight,
                "Maximum vertical extent of the wind field.");

            _nudGridRes = MakeNud(10m, 200m, 40m, 0);
            DialogHelpers.AddRowWithHelp(propsTable, ref row, "Grid Resolution:", _nudGridRes,
                "Number of cells per horizontal axis (Z uses N/2).");

            AddSectionHeader(propsTable, row++, "Inlet Wind");

            _nudWindSpeed = MakeNud(0.1m, 100m, 5m, 1);
            DialogHelpers.AddRowWithHelp(propsTable, ref row, "Wind Speed (m/s):", _nudWindSpeed,
                "Free-stream wind speed at the inlet boundary.");

            _nudWindDir = MakeNud(0m, 359m, 270m, 0);
            DialogHelpers.AddRowWithHelp(propsTable, ref row, "Wind Direction (°):", _nudWindDir,
                "Meteorological convention: direction the wind blows FROM (0°=N, 90°=E, 180°=S, 270°=W).");

            _cmbStability = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cmbStability.Items.AddRange(new object[] { "A", "B", "C", "D", "E", "F" });
            _cmbStability.SelectedIndex = 3;
            DialogHelpers.AddRowWithHelp(propsTable, ref row, "Stability Class:", _cmbStability,
                "Pasquill-Gifford stability — informational only for the wind field run, used by referenced dispersion runs.");

            _nudTemperature = MakeNud(-50m, 60m, 20m, 1);
            DialogHelpers.AddRowWithHelp(propsTable, ref row, "Ambient Temp (°C):", _nudTemperature,
                "Reference temperature for thermophysical properties.");

            AddSectionHeader(propsTable, row++, "Status");

            _lblStatus = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 0, 6),
                Text = "(no scenario selected)"
            };
            propsTable.SetColumnSpan(_lblStatus, 2);
            propsTable.Controls.Add(_lblStatus, 0, row++);

            var actionPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight, WrapContents = false
            };
            var lblCfdNote = new Label
            {
                AutoSize = true,
                ForeColor = System.Drawing.SystemColors.GrayText,
                Margin = new Padding(0, 6, 12, 0),
                Text = "CFD settings come from Dispersion → CFD Settings (Application)."
            };
            _btnRun = new Button { Text = "Run", AutoSize = true };
            _btnRun.Click += (s, e) => DoRun();
            actionPanel.Controls.Add(lblCfdNote);
            actionPanel.Controls.Add(_btnRun);
            propsTable.SetColumnSpan(actionPanel, 2);
            propsTable.Controls.Add(actionPanel, 0, row++);

            _progress = new ProgressBar { Dock = DockStyle.Fill, Height = (int)(16 * dpi), Visible = false };
            propsTable.SetColumnSpan(_progress, 2);
            propsTable.Controls.Add(_progress, 0, row++);

            _propsPanel.Controls.Add(propsTable);
            outerLayout.Controls.Add(_propsPanel, 2, 0);

            // Bottom buttons
            var bottomButtons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 3, RowCount = 1, Padding = new Padding(4)
            };
            bottomButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottomButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomButtons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnOK = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            btnOK.Click += (s, e) => SaveCurrent();
            bottomButtons.Controls.Add(new Label(), 0, 0);
            bottomButtons.Controls.Add(btnCancel, 1, 0);
            bottomButtons.Controls.Add(btnOK, 2, 0);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            outerLayout.Controls.Add(bottomButtons, 0, 1);
            outerLayout.SetColumnSpan(bottomButtons, 3);
            this.Controls.Add(outerLayout);
            this.ApplyDpiScaling();
        }

        private void RefreshList()
        {
            _list.Items.Clear();
            for (int i = 0; i < Scenarios.Count; i++)
            {
                var wf = Scenarios[i];
                _list.Items.Add(string.Format("{0} [{1}]", wf.Name ?? "(unnamed)", wf.Status));
            }
        }

        private void LoadSelected()
        {
            int idx = _list.SelectedIndex;
            if (idx < 0 || idx >= Scenarios.Count)
            {
                _propsPanel.Enabled = false;
                return;
            }
            _propsPanel.Enabled = true;
            var wf = Scenarios[idx];
            _txtName.Text = wf.Name ?? "";
            _nudDomainSize.Value = (decimal)Math.Max((double)_nudDomainSize.Minimum, Math.Min((double)_nudDomainSize.Maximum, wf.DomainSizeM));
            _nudDomainHeight.Value = (decimal)Math.Max((double)_nudDomainHeight.Minimum, Math.Min((double)_nudDomainHeight.Maximum, wf.DomainHeightM));
            _nudGridRes.Value = (decimal)Math.Max((double)_nudGridRes.Minimum, Math.Min((double)_nudGridRes.Maximum, wf.GridResolution));
            if (wf.Meteo != null)
            {
                _nudWindSpeed.Value = (decimal)Math.Max((double)_nudWindSpeed.Minimum, Math.Min((double)_nudWindSpeed.Maximum, wf.Meteo.WindSpeed));
                _nudWindDir.Value = (decimal)Math.Max((double)_nudWindDir.Minimum, Math.Min((double)_nudWindDir.Maximum, wf.Meteo.WindDirectionDeg));
                _cmbStability.SelectedIndex = (int)wf.Meteo.StabilityClass;
                _nudTemperature.Value = (decimal)Math.Max((double)_nudTemperature.Minimum, Math.Min((double)_nudTemperature.Maximum, wf.Meteo.AmbientTemperature - 273.15));
            }
            _lblStatus.Text = string.Format("Status: {0}{1}", wf.Status,
                string.IsNullOrEmpty(wf.StatusMessage) ? "" : " — " + wf.StatusMessage);
        }

        private void SaveCurrent()
        {
            int idx = _list.SelectedIndex;
            if (idx < 0 || idx >= Scenarios.Count) return;
            var wf = Scenarios[idx];
            wf.Name = _txtName.Text;
            wf.DomainSizeM = (double)_nudDomainSize.Value;
            wf.DomainHeightM = (double)_nudDomainHeight.Value;
            wf.GridResolution = (int)_nudGridRes.Value;
            if (wf.Meteo == null) wf.Meteo = new MeteorologicalConditions();
            wf.Meteo.WindSpeed = (double)_nudWindSpeed.Value;
            wf.Meteo.WindDirectionDeg = (double)_nudWindDir.Value;
            wf.Meteo.AmbientTemperature = (double)_nudTemperature.Value + 273.15;
            wf.Meteo.StabilityClass = (PasquillStabilityClass)_cmbStability.SelectedIndex;
        }

        private void DoRun()
        {
            int idx = _list.SelectedIndex;
            if (idx < 0) return;
            SaveCurrent();
            var wf = Scenarios[idx];

            wf.CfdConfig = AppSettings.Instance.CreateCfdConfig();

            var obstacles = new List<BoundingBox>();
            foreach (var deco in _scene.Decorations)
                if (deco.BoundingBox != null) obstacles.Add(deco.BoundingBox);

            _btnRun.Enabled = false;
            _progress.Visible = true;
            _progress.Value = 0;
            _lblStatus.Text = "Running...";

            _worker = new BackgroundWorker { WorkerReportsProgress = true };
            _worker.DoWork += (s, e) =>
            {
                var runner = new WindFieldRunner(_env);
                runner.Run(wf, obstacles, (frac, msg) =>
                {
                    _worker.ReportProgress((int)(frac * 100), msg);
                });
                e.Result = wf;
            };
            _worker.ProgressChanged += (s, e) =>
            {
                _progress.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
                _lblStatus.Text = (string)e.UserState ?? "";
            };
            _worker.RunWorkerCompleted += (s, e) =>
            {
                _btnRun.Enabled = true;
                _progress.Visible = false;
                if (e.Error != null)
                {
                    _lblStatus.Text = "Failed: " + e.Error.Message;
                }
                else
                {
                    _lblStatus.Text = string.Format("Status: {0}{1}", wf.Status,
                        string.IsNullOrEmpty(wf.StatusMessage) ? "" : " — " + wf.StatusMessage);
                }
                RefreshList();
                _list.SelectedIndex = idx;
            };
            _worker.RunWorkerAsync();
        }

        private static void AddSectionHeader(TableLayoutPanel table, int row, string text)
        {
            var lbl = new Label
            {
                Text = text, AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Margin = new Padding(0, 10, 0, 4)
            };
            table.SetColumnSpan(lbl, 2);
            table.Controls.Add(lbl, 0, row);
        }

        private static NumericUpDown MakeNud(decimal min, decimal max, decimal value, int decimals)
        {
            var nud = new NumericUpDown
            {
                Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals,
                Dock = DockStyle.Fill
            };
            nud.Increment = decimals > 0 ? (decimal)Math.Pow(10, -decimals) : 1;
            return nud;
        }

        private static string PromptInput(string title, string prompt, string defaultValue)
        {
            using (var dlg = new Form())
            {
                dlg.Text = title;
                dlg.AutoScaleMode = AutoScaleMode.Dpi;
                dlg.AutoScaleDimensions = new SizeF(96F, 96F);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.StartPosition = FormStartPosition.CenterParent;
                var dpi = dlg.DeviceDpi / 96f;
                dlg.ClientSize = new Size((int)(320 * dpi), (int)(120 * dpi));
                dlg.Padding = new Padding((int)(8 * dpi));

                var lbl = new Label { Text = prompt, Dock = DockStyle.Top, AutoSize = true };
                var txt = new TextBox { Text = defaultValue, Dock = DockStyle.Top };
                var pnl = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom, AutoSize = true,
                    FlowDirection = FlowDirection.RightToLeft
                };
                var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
                var btnC = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                pnl.Controls.Add(btnOk);
                pnl.Controls.Add(btnC);
                dlg.Controls.Add(txt);
                dlg.Controls.Add(lbl);
                dlg.Controls.Add(pnl);
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnC;
                return dlg.ShowDialog() == DialogResult.OK ? txt.Text : null;
            }
        }
    }
}
