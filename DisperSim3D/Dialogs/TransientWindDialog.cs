using System;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class TransientWindDialog : Form
    {
        private DataGridView _grid;
        private CheckBox _chkEnabled;
        private NumericUpDown _nudESD;

        public TransientWindProfile Result { get; private set; }

        public TransientWindDialog(TransientWindProfile existing)
        {
            Result = existing ?? new TransientWindProfile();
            BuildUI();
        }

        private void BuildUI()
        {
            this.Text = "Transient Wind Profile / ESD";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            var dpi = DeviceDpi / 96f;
            this.Size = new System.Drawing.Size((int)(550 * dpi), (int)(400 * dpi));
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;

            var topPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4, RowCount = 1, Padding = new Padding(8)
            };
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _chkEnabled = new CheckBox { Text = "Enable transient wind", Checked = Result.Enabled, AutoSize = true };
            topPanel.Controls.Add(_chkEnabled, 0, 0);

            topPanel.Controls.Add(new Label { Text = "ESD time (s):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(16, 6, 4, 0) }, 1, 0);
            _nudESD = new NumericUpDown
            {
                Minimum = -1, Maximum = 100000, Value = (decimal)Math.Max(-1, Result.ESDTimeS),
                DecimalPlaces = 1, Width = (int)(80 * dpi)
            };
            topPanel.Controls.Add(_nudESD, 2, 0);
            topPanel.Controls.Add(new Label { Text = "(-1 = disabled)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 6, 0, 0) }, 3, 0);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = System.Drawing.SystemColors.Window
            };
            _grid.Columns.Add("Time", "Time (s)");
            _grid.Columns.Add("Speed", "Wind Speed (m/s)");
            _grid.Columns.Add("Dir", "Direction (°)");
            var stabCol = new DataGridViewComboBoxColumn
            {
                Name = "Stability", HeaderText = "Stability",
                DataSource = Enum.GetValues(typeof(PasquillStabilityClass))
            };
            _grid.Columns.Add(stabCol);

            foreach (var e in Result.Entries)
            {
                int idx = _grid.Rows.Add(
                    e.TimeS.ToString("F1"),
                    e.WindSpeed.ToString("F1"),
                    e.WindDirectionDeg.ToString("F1"),
                    e.StabilityClass);
                _grid.Rows[idx].Cells["Stability"].Value = e.StabilityClass;
            }

            var btnPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom, AutoSize = true,
                ColumnCount = 3, RowCount = 1, Padding = new Padding(4)
            };
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnOK = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            btnOK.Click += (s, ev) => BuildResult();
            btnPanel.Controls.Add(new Label(), 0, 0);
            btnPanel.Controls.Add(btnCancel, 1, 0);
            btnPanel.Controls.Add(btnOK, 2, 0);
            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            this.Controls.Add(_grid);
            this.Controls.Add(topPanel);
            this.Controls.Add(btnPanel);
            this.ApplyDpiScaling();
        }

        private void BuildResult()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Result = new TransientWindProfile
            {
                Enabled = _chkEnabled.Checked,
                ESDTimeS = (double)_nudESD.Value
            };

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;
                try
                {
                    var entry = new WindProfileEntry
                    {
                        TimeS = double.Parse(row.Cells["Time"].Value?.ToString() ?? "0", inv),
                        WindSpeed = double.Parse(row.Cells["Speed"].Value?.ToString() ?? "5", inv),
                        WindDirectionDeg = double.Parse(row.Cells["Dir"].Value?.ToString() ?? "270", inv)
                    };
                    if (row.Cells["Stability"].Value != null)
                        entry.StabilityClass = (PasquillStabilityClass)row.Cells["Stability"].Value;
                    Result.Entries.Add(entry);
                }
                catch { }
            }

            Result.Entries.Sort((a, b) => a.TimeS.CompareTo(b.TimeS));
        }
    }
}
