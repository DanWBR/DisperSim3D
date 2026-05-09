using System;
using System.Collections.Generic;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class ScenarioManagerDialog : Form
    {
        private ListBox lstScenarios;
        private Button btnRename;
        private Button btnDuplicate;
        private Button btnDelete;

        private NumericUpDown nudDuration;
        private NumericUpDown nudTimeStep;
        private NumericUpDown nudDomainSize;
        private NumericUpDown nudGridRes;
        private NumericUpDown nudWindSpeed;
        private NumericUpDown nudWindDir;
        private NumericUpDown nudAmbientTemp;
        private ComboBox cmbStability;
        private ComboBox cmbWindField;
        private Panel propsPanel;

        private readonly List<WindFieldScenario> _windFields;

        public List<DispersionScenario> Scenarios { get; private set; }
        public int SelectedIndex { get; private set; }

        public ScenarioManagerDialog(List<DispersionScenario> scenarios, int activeIndex)
            : this(scenarios, activeIndex, null)
        {
        }

        public ScenarioManagerDialog(List<DispersionScenario> scenarios, int activeIndex,
            List<WindFieldScenario> windFieldScenarios)
        {
            Scenarios = new List<DispersionScenario>(scenarios);
            SelectedIndex = activeIndex;
            _windFields = windFieldScenarios ?? new List<WindFieldScenario>();
            BuildUI();
            RefreshList();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            this.Text = "Scenario Manager";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Size = new System.Drawing.Size((int)(750 * dpi), (int)(640 * dpi));
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

            lstScenarios = new ListBox { Dock = DockStyle.Fill };
            lstScenarios.SelectedIndexChanged += (s, e) => LoadSelectedScenario();
            leftPanel.Controls.Add(lstScenarios, 0, 0);

            var listButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            var btnNew = new Button { Text = "New", AutoSize = true };
            btnNew.Click += (s, e) =>
            {
                SaveCurrentScenario();
                var sc = new DispersionScenario { Name = "Scenario " + (Scenarios.Count + 1) };
                Scenarios.Add(sc);
                RefreshList();
                lstScenarios.SelectedIndex = Scenarios.Count - 1;
            };

            btnRename = new Button { Text = "Rename", AutoSize = true };
            btnRename.Click += (s, e) =>
            {
                int idx = lstScenarios.SelectedIndex;
                if (idx < 0) return;
                string name = PromptInput("Rename Scenario", "Enter new name:", Scenarios[idx].Name);
                if (!string.IsNullOrEmpty(name))
                {
                    Scenarios[idx].Name = name;
                    RefreshList();
                    lstScenarios.SelectedIndex = idx;
                }
            };

            btnDuplicate = new Button { Text = "Duplicate", AutoSize = true };
            btnDuplicate.Click += (s, e) =>
            {
                int idx = lstScenarios.SelectedIndex;
                if (idx < 0) return;
                SaveCurrentScenario();
                var orig = Scenarios[idx];
                var copy = new DispersionScenario
                {
                    Name = orig.Name + " (copy)",
                    SimulationDurationS = orig.SimulationDurationS,
                    TimeStepS = orig.TimeStepS,
                    DomainSizeM = orig.DomainSizeM,
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
                foreach (var src in orig.Sources)
                {
                    copy.Sources.Add(new ReleaseSource3D
                    {
                        Name = src.Name,
                        Position = src.Position,
                        Gas = src.Gas,
                        ReleaseRateKgPerS = src.ReleaseRateKgPerS,
                        PuffIntervalS = src.PuffIntervalS,
                        ReleaseHeightOffset = src.ReleaseHeightOffset,
                        AttachedUnitId = src.AttachedUnitId
                    });
                }
                foreach (var t in orig.Thresholds)
                {
                    copy.Thresholds.Add(new DispersionThreshold
                    {
                        Name = t.Name,
                        Type = t.Type,
                        ConcentrationValue = t.ConcentrationValue,
                        Color = t.Color,
                        Opacity = t.Opacity,
                        Visible = t.Visible
                    });
                }
                Scenarios.Add(copy);
                RefreshList();
                lstScenarios.SelectedIndex = Scenarios.Count - 1;
            };

            btnDelete = new Button { Text = "Delete", AutoSize = true };
            btnDelete.Click += (s, e) =>
            {
                int idx = lstScenarios.SelectedIndex;
                if (idx < 0 || Scenarios.Count <= 1) return;
                Scenarios.RemoveAt(idx);
                if (SelectedIndex >= Scenarios.Count) SelectedIndex = Scenarios.Count - 1;
                RefreshList();
            };

            listButtons.Controls.AddRange(new Control[] { btnNew, btnRename, btnDuplicate, btnDelete });
            leftPanel.Controls.Add(listButtons, 0, 1);
            outerLayout.Controls.Add(leftPanel, 0, 0);

            // Right: scenario properties
            propsPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var propsTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2, Padding = new Padding(4)
            };
            propsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            propsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;

            AddSectionHeader(propsTable, row++, "Simulation");

            nudDuration = MakeNud(1m, 100000m, 300m, 0);
            AddRow(propsTable, ref row, "Duration (s):", nudDuration,
                "Total simulation time. Transient solvers run from 0 to this value.");

            nudTimeStep = MakeNud(0.01m, 60m, 0.5m, 2);
            AddRow(propsTable, ref row, "Time Step (s):", nudTimeStep,
                "Output write interval. Smaller values = more frames but larger case size.");

            AddSectionHeader(propsTable, row++, "Domain");

            nudDomainSize = MakeNud(10m, 100000m, 200m, 0);
            AddRow(propsTable, ref row, "Domain Size (m):", nudDomainSize,
                "Half-extent of the simulation box in each horizontal direction (full size = 2×).");

            nudGridRes = MakeNud(10m, 1000m, 80m, 0);
            AddRow(propsTable, ref row, "Grid Resolution:", nudGridRes,
                "Number of cells per axis. Higher = finer detail but much slower.");

            AddSectionHeader(propsTable, row++, "Meteorology");

            nudWindSpeed = MakeNud(0.1m, 100m, 3m, 1);
            AddRow(propsTable, ref row, "Wind Speed (m/s):", nudWindSpeed,
                "Mean wind magnitude at reference height (10 m).");

            nudWindDir = MakeNud(0m, 359m, 270m, 0);
            AddRow(propsTable, ref row, "Wind Direction (°):", nudWindDir,
                "Meteorological convention: direction the wind is blowing FROM (0°=N, 90°=E, 180°=S, 270°=W).");

            cmbStability = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbStability.Items.AddRange(new object[] { "A", "B", "C", "D", "E", "F" });
            cmbStability.SelectedIndex = 3;
            AddRow(propsTable, ref row, "Stability Class:", cmbStability,
                "Pasquill-Gifford atmospheric stability. A=very unstable, D=neutral, F=very stable.");

            nudAmbientTemp = MakeNud(-50m, 60m, 20m, 1);
            AddRow(propsTable, ref row, "Ambient Temp (°C):", nudAmbientTemp,
                "Outdoor air temperature, used for buoyancy and density calculations.");

            AddSectionHeader(propsTable, row++, "Wind Field");

            cmbWindField = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            RefreshWindFieldCombo();
            AddRow(propsTable, ref row, "Wind Field:", cmbWindField,
                "Pre-computed wind field this dispersion scenario will advect through. " +
                "Required — manage these via Dispersion → Manage Wind Fields...");

            propsPanel.Controls.Add(propsTable);
            outerLayout.Controls.Add(propsPanel, 2, 0);

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
            btnOK.Click += (s, e) =>
            {
                SaveCurrentScenario();
                SelectedIndex = Math.Max(0, lstScenarios.SelectedIndex);
            };
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

        private void LoadSelectedScenario()
        {
            int idx = lstScenarios.SelectedIndex;
            if (idx < 0 || idx >= Scenarios.Count)
            {
                propsPanel.Enabled = false;
                return;
            }
            propsPanel.Enabled = true;
            var sc = Scenarios[idx];

            nudDuration.Value = (decimal)Math.Max((double)nudDuration.Minimum, Math.Min((double)nudDuration.Maximum, sc.SimulationDurationS));
            nudTimeStep.Value = (decimal)Math.Max((double)nudTimeStep.Minimum, Math.Min((double)nudTimeStep.Maximum, sc.TimeStepS));
            nudDomainSize.Value = (decimal)Math.Max((double)nudDomainSize.Minimum, Math.Min((double)nudDomainSize.Maximum, sc.DomainSizeM));
            nudGridRes.Value = (decimal)Math.Max((double)nudGridRes.Minimum, Math.Min((double)nudGridRes.Maximum, sc.GridResolution));

            if (sc.Meteo != null)
            {
                nudWindSpeed.Value = (decimal)Math.Max((double)nudWindSpeed.Minimum, Math.Min((double)nudWindSpeed.Maximum, sc.Meteo.WindSpeed));
                nudWindDir.Value = (decimal)Math.Max((double)nudWindDir.Minimum, Math.Min((double)nudWindDir.Maximum, sc.Meteo.WindDirectionDeg));
                nudAmbientTemp.Value = (decimal)Math.Max((double)nudAmbientTemp.Minimum, Math.Min((double)nudAmbientTemp.Maximum, sc.Meteo.AmbientTemperature - 273.15));
                cmbStability.SelectedIndex = (int)sc.Meteo.StabilityClass;
            }

            int wfIdx = 0;
            if (!string.IsNullOrEmpty(sc.WindFieldScenarioId))
            {
                for (int i = 0; i < _windFields.Count; i++)
                    if (_windFields[i].Id == sc.WindFieldScenarioId) { wfIdx = i + 1; break; }
            }
            if (cmbWindField.Items.Count > 0)
                cmbWindField.SelectedIndex = Math.Min(wfIdx, cmbWindField.Items.Count - 1);
        }

        private void RefreshWindFieldCombo()
        {
            cmbWindField.Items.Clear();
            cmbWindField.Items.Add("(none — required)");
            foreach (var wf in _windFields)
                cmbWindField.Items.Add(string.Format("{0} [{1}]", wf.Name, wf.Status));
        }

        private void SaveCurrentScenario()
        {
            int idx = lstScenarios.SelectedIndex;
            if (idx < 0 || idx >= Scenarios.Count) return;
            var sc = Scenarios[idx];

            sc.SimulationDurationS = (double)nudDuration.Value;
            sc.TimeStepS = (double)nudTimeStep.Value;
            sc.DomainSizeM = (double)nudDomainSize.Value;
            sc.GridResolution = (int)nudGridRes.Value;

            if (sc.Meteo == null) sc.Meteo = new MeteorologicalConditions();
            sc.Meteo.WindSpeed = (double)nudWindSpeed.Value;
            sc.Meteo.WindDirectionDeg = (double)nudWindDir.Value;
            sc.Meteo.AmbientTemperature = (double)nudAmbientTemp.Value + 273.15;
            sc.Meteo.StabilityClass = (PasquillStabilityClass)cmbStability.SelectedIndex;

            int wfIdx = cmbWindField.SelectedIndex;
            if (wfIdx <= 0 || wfIdx - 1 >= _windFields.Count)
                sc.WindFieldScenarioId = null;
            else
                sc.WindFieldScenarioId = _windFields[wfIdx - 1].Id;
        }

        private void RefreshList()
        {
            lstScenarios.Items.Clear();
            for (int i = 0; i < Scenarios.Count; i++)
            {
                lstScenarios.Items.Add(Scenarios[i].Name ?? "Scenario " + (i + 1));
            }
            if (SelectedIndex >= 0 && SelectedIndex < lstScenarios.Items.Count)
                lstScenarios.SelectedIndex = SelectedIndex;
        }

        private static void AddSectionHeader(TableLayoutPanel table, int row, string text)
        {
            var lbl = new Label
            {
                Text = text,
                AutoSize = true,
                Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold),
                Margin = new Padding(0, 10, 0, 4)
            };
            table.SetColumnSpan(lbl, 2);
            table.Controls.Add(lbl, 0, row);
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
        {
            var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
            table.Controls.Add(lbl, 0, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            table.Controls.Add(control, 1, row);
        }

        private static void AddRow(TableLayoutPanel table, ref int row, string label, Control control, string description)
        {
            DialogHelpers.AddRowWithHelp(table, ref row, label, control, description);
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
                dlg.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.StartPosition = FormStartPosition.CenterParent;
                var dpiF = dlg.DeviceDpi / 96f;
                dlg.ClientSize = new System.Drawing.Size((int)(320 * dpiF), (int)(130 * dpiF));

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    Padding = new Padding((int)(10 * dpiF)),
                    AutoSize = false
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var lbl = new Label { Text = prompt, Dock = DockStyle.Fill, AutoSize = true };
                layout.Controls.Add(lbl, 0, 0);

                var txt = new TextBox { Text = defaultValue, Dock = DockStyle.Fill };
                layout.Controls.Add(txt, 0, 1);

                layout.Controls.Add(new Label(), 0, 2);

                var btnPanel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, AutoSize = true,
                    ColumnCount = 3, RowCount = 1, Padding = new Padding(4)
                };
                btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                btnPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var btnC = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
                btnPanel.Controls.Add(new Label(), 0, 0);
                btnPanel.Controls.Add(btnC, 1, 0);
                btnPanel.Controls.Add(btnOk, 2, 0);
                layout.Controls.Add(btnPanel, 0, 3);

                dlg.Controls.Add(layout);
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnC;

                return dlg.ShowDialog() == DialogResult.OK ? txt.Text : null;
            }
        }
    }
}
