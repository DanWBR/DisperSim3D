using System;
using System.Collections.Generic;
using System.Windows.Forms;
using DisperSim3D.Models;

namespace DisperSim3D.App.Dialogs
{
    public class ThresholdsDialog : Form
    {
        private DataGridView _grid;

        public List<DispersionThreshold> Result { get; private set; }

        public ThresholdsDialog(List<DispersionThreshold> existing = null)
        {
            BuildUI();
            if (existing != null && existing.Count > 0)
            {
                foreach (var t in existing)
                    AddThresholdRow(t);
            }
            else
            {
                AddThresholdRow(new DispersionThreshold
                {
                    Name = "LFL",
                    Type = DispersionThresholdType.LFL,
                    ConcentrationValue = 0.033,
                    Color = System.Windows.Media.Color.FromArgb(100, 255, 0, 0),
                    Opacity = 0.3,
                    Visible = true
                });
                AddThresholdRow(new DispersionThreshold
                {
                    Name = "IDLH",
                    Type = DispersionThresholdType.IDLH,
                    ConcentrationValue = 0.033,
                    Color = System.Windows.Media.Color.FromArgb(100, 255, 165, 0),
                    Opacity = 0.25,
                    Visible = true
                });
            }
        }

        private void BuildUI()
        {
            this.Text = "Dispersion Thresholds";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new System.Drawing.Size(580, 320);
            this.MinimumSize = new System.Drawing.Size(450, 280);
            this.Padding = new Padding(10);

            var outerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            _grid.Columns.Add("Name", "Name");
            _grid.Columns.Add("Concentration", "Concentration (kg/m³)");
            _grid.Columns.Add("ColorHex", "Color (ARGB hex)");

            var colOpacity = new DataGridViewTextBoxColumn { Name = "Opacity", HeaderText = "Opacity (0-1)" };
            _grid.Columns.Add(colOpacity);

            var colVisible = new DataGridViewCheckBoxColumn { Name = "Visible", HeaderText = "Visible" };
            _grid.Columns.Add(colVisible);

            outerLayout.Controls.Add(_grid, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 0, 0)
            };

            var btnAdd = new Button { Text = "Add", AutoSize = true };
            btnAdd.Click += (s, e) =>
            {
                AddThresholdRow(new DispersionThreshold
                {
                    Name = "Custom",
                    Type = DispersionThresholdType.Custom,
                    ConcentrationValue = 0.01,
                    Color = System.Windows.Media.Color.FromArgb(100, 0, 200, 0),
                    Opacity = 0.2,
                    Visible = true
                });
            };

            var btnRemove = new Button { Text = "Remove", AutoSize = true };
            btnRemove.Click += (s, e) =>
            {
                if (_grid.SelectedRows.Count > 0)
                    _grid.Rows.RemoveAt(_grid.SelectedRows[0].Index);
            };

            var spacer = new Label { AutoSize = false, Width = 40, Text = "" };

            var btnOK = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            btnOK.Click += BtnOK_Click;

            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

            buttons.Controls.Add(btnAdd);
            buttons.Controls.Add(btnRemove);
            buttons.Controls.Add(spacer);
            buttons.Controls.Add(btnOK);
            buttons.Controls.Add(btnCancel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            outerLayout.Controls.Add(buttons, 0, 1);
            this.Controls.Add(outerLayout);
        }

        private void AddThresholdRow(DispersionThreshold t)
        {
            string colorHex = string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", t.Color.A, t.Color.R, t.Color.G, t.Color.B);
            _grid.Rows.Add(t.Name, t.ConcentrationValue.ToString("G6"), colorHex, t.Opacity.ToString("F2"), t.Visible);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            Result = new List<DispersionThreshold>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;

                string name = row.Cells["Name"].Value?.ToString() ?? "Threshold";
                double conc = 0.01;
                double.TryParse(row.Cells["Concentration"].Value?.ToString(), out conc);

                double opacity = 0.3;
                double.TryParse(row.Cells["Opacity"].Value?.ToString(), out opacity);

                bool visible = true;
                if (row.Cells["Visible"].Value is bool v) visible = v;

                var color = System.Windows.Media.Color.FromArgb(100, 255, 0, 0);
                string hex = row.Cells["ColorHex"].Value?.ToString() ?? "64FF0000";
                if (hex.Length == 8)
                {
                    try
                    {
                        byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                        byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                        byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                        byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                        color = System.Windows.Media.Color.FromArgb(a, r, g, b);
                    }
                    catch { }
                }

                Result.Add(new DispersionThreshold
                {
                    Name = name,
                    Type = DispersionThresholdType.Custom,
                    ConcentrationValue = conc,
                    Color = color,
                    Opacity = opacity,
                    Visible = visible
                });
            }
        }
    }
}
