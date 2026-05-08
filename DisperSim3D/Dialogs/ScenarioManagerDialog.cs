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

        public List<DispersionScenario> Scenarios { get; private set; }
        public int SelectedIndex { get; private set; }

        public ScenarioManagerDialog(List<DispersionScenario> scenarios, int activeIndex)
        {
            Scenarios = new List<DispersionScenario>(scenarios);
            SelectedIndex = activeIndex;
            BuildUI();
            RefreshList();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            this.Text = "Scenario Manager";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Size = new System.Drawing.Size((int)(380 * dpi), (int)(320 * dpi));
            this.Padding = new Padding((int)(10 * dpi));

            var outerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2
            };
            outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            lstScenarios = new ListBox { Dock = DockStyle.Fill };
            outerLayout.Controls.Add(lstScenarios, 0, 0);

            var buttonPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1, RowCount = 4,
                AutoSize = true, Padding = new Padding(4)
            };
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttonPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            buttonPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            buttonPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            buttonPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnNew = new Button { Text = "New", Width = (int)(100 * dpi), AutoSize = true };
            btnNew.Click += (s, e) =>
            {
                var sc = new DispersionScenario { Name = "Scenario " + (Scenarios.Count + 1) };
                Scenarios.Add(sc);
                RefreshList();
                lstScenarios.SelectedIndex = Scenarios.Count - 1;
            };

            btnRename = new Button { Text = "Rename", Width = (int)(100 * dpi), AutoSize = true };
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

            btnDuplicate = new Button { Text = "Duplicate", Width = (int)(100 * dpi), AutoSize = true };
            btnDuplicate.Click += (s, e) =>
            {
                int idx = lstScenarios.SelectedIndex;
                if (idx < 0) return;
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
                        ReleaseDurationS = src.ReleaseDurationS,
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

            btnDelete = new Button { Text = "Delete", Width = (int)(100 * dpi), AutoSize = true };
            btnDelete.Click += (s, e) =>
            {
                int idx = lstScenarios.SelectedIndex;
                if (idx < 0 || Scenarios.Count <= 1) return;
                Scenarios.RemoveAt(idx);
                if (SelectedIndex >= Scenarios.Count) SelectedIndex = Scenarios.Count - 1;
                RefreshList();
            };

            buttonPanel.Controls.Add(btnNew, 0, 0);
            buttonPanel.Controls.Add(btnRename, 0, 1);
            buttonPanel.Controls.Add(btnDuplicate, 0, 2);
            buttonPanel.Controls.Add(btnDelete, 0, 3);
            outerLayout.Controls.Add(buttonPanel, 1, 0);

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
                SelectedIndex = Math.Max(0, lstScenarios.SelectedIndex);
            };
            bottomButtons.Controls.Add(new Label(), 0, 0);
            bottomButtons.Controls.Add(btnCancel, 1, 0);
            bottomButtons.Controls.Add(btnOK, 2, 0);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            outerLayout.Controls.Add(bottomButtons, 0, 1);
            outerLayout.SetColumnSpan(bottomButtons, 2);
            this.Controls.Add(outerLayout);
            this.ApplyDpiScaling();
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
