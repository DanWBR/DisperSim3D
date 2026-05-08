using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class WindRoseDialog : Form
    {
        private DataGridView dgv;
        private Panel chartPanel;

        public WindRoseData Result { get; private set; }
        public bool GenerateScenarios { get; private set; }

        public WindRoseDialog(WindRoseData existing = null)
        {
            Result = existing ?? WindRoseData.Create8Directions();
            BuildUI();
            PopulateGrid();
        }

        private void BuildUI()
        {
            this.Text = "Wind Rose";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            var dpi = DeviceDpi / 96f;
            this.Size = new Size((int)(700 * dpi), (int)(500 * dpi));
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = (int)(320 * dpi),
                Orientation = Orientation.Vertical
            };

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgv.Columns.Add("Dir", "Direction (°)");
            dgv.Columns.Add("Freq", "Frequency (%)");
            dgv.Columns.Add("Speed", "Wind Speed (m/s)");
            dgv.Columns.Add("Stability", "Stability Class");
            dgv.CellValueChanged += (s, e) => chartPanel.Invalidate();
            dgv.RowsRemoved += (s, e) => chartPanel.Invalidate();

            chartPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            chartPanel.Paint += ChartPanel_Paint;

            split.Panel1.Controls.Add(dgv);
            split.Panel2.Controls.Add(chartPanel);

            var bottomPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom, AutoSize = true,
                ColumnCount = 6, RowCount = 1, Padding = new Padding(4)
            };
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnOK = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            btnOK.Click += BtnOK_Click;

            var chkGenerate = new CheckBox { Text = "Generate scenarios from bins", AutoSize = true, Checked = false };
            chkGenerate.CheckedChanged += (s, e) => GenerateScenarios = chkGenerate.Checked;

            var btnPreset8 = new Button { Text = "8 Dirs", AutoSize = true };
            btnPreset8.Click += (s, e) => { Result = WindRoseData.Create8Directions(); PopulateGrid(); chartPanel.Invalidate(); };

            var btnPreset16 = new Button { Text = "16 Dirs", AutoSize = true };
            btnPreset16.Click += (s, e) => { Result = WindRoseData.Create16Directions(); PopulateGrid(); chartPanel.Invalidate(); };

            bottomPanel.Controls.Add(btnPreset8, 0, 0);
            bottomPanel.Controls.Add(btnPreset16, 1, 0);
            bottomPanel.Controls.Add(chkGenerate, 2, 0);
            bottomPanel.Controls.Add(new Label(), 3, 0);
            bottomPanel.Controls.Add(btnOK, 4, 0);
            bottomPanel.Controls.Add(btnCancel, 5, 0);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            this.Controls.Add(split);
            this.Controls.Add(bottomPanel);
            this.ApplyDpiScaling();
        }

        private void PopulateGrid()
        {
            dgv.Rows.Clear();
            foreach (var bin in Result.Bins)
            {
                dgv.Rows.Add(bin.DirectionDeg, bin.Frequency, bin.WindSpeed, bin.StabilityClass);
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            Result.Bins.Clear();
            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.IsNewRow) continue;
                try
                {
                    var bin = new WindRoseBin
                    {
                        DirectionDeg = Convert.ToDouble(row.Cells[0].Value ?? 0),
                        Frequency = Convert.ToDouble(row.Cells[1].Value ?? 0),
                        WindSpeed = Convert.ToDouble(row.Cells[2].Value ?? 5),
                        StabilityClass = PasquillStabilityClass.D
                    };
                    var stabStr = row.Cells[3].Value?.ToString();
                    if (!string.IsNullOrEmpty(stabStr) && Enum.TryParse(stabStr, out PasquillStabilityClass sc))
                        bin.StabilityClass = sc;
                    Result.Bins.Add(bin);
                }
                catch { }
            }
        }

        private void ChartPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int cx = chartPanel.Width / 2;
            int cy = chartPanel.Height / 2;
            int radius = Math.Min(cx, cy) - 30;

            g.DrawEllipse(Pens.LightGray, cx - radius, cy - radius, radius * 2, radius * 2);
            g.DrawEllipse(Pens.LightGray, cx - radius / 2, cy - radius / 2, radius, radius);

            string[] labels = { "N", "E", "S", "W" };
            int[] labelAngles = { 270, 0, 90, 180 };
            var font = new Font("Segoe UI", 8);
            for (int i = 0; i < 4; i++)
            {
                double rad = labelAngles[i] * Math.PI / 180;
                float lx = cx + (float)(Math.Cos(rad) * (radius + 15));
                float ly = cy + (float)(Math.Sin(rad) * (radius + 15));
                var sz = g.MeasureString(labels[i], font);
                g.DrawString(labels[i], font, Brushes.Black, lx - sz.Width / 2, ly - sz.Height / 2);
            }

            var bins = GetCurrentBins();
            if (bins.Count == 0) return;

            double maxFreq = 0;
            foreach (var b in bins) if (b.Frequency > maxFreq) maxFreq = b.Frequency;
            if (maxFreq < 0.01) return;

            double wedgeHalfAngle = bins.Count > 1 ? 180.0 / bins.Count : 15;

            using (var brush = new SolidBrush(Color.FromArgb(150, 70, 130, 200)))
            using (var pen = new Pen(Color.FromArgb(200, 30, 80, 160), 1.5f))
            {
                foreach (var bin in bins)
                {
                    double dirRad = (bin.DirectionDeg - 90) * Math.PI / 180;
                    double len = (bin.Frequency / maxFreq) * radius;
                    double halfRad = wedgeHalfAngle * Math.PI / 180;

                    var pts = new PointF[3];
                    pts[0] = new PointF(cx, cy);
                    pts[1] = new PointF(
                        cx + (float)(Math.Cos(dirRad - halfRad) * len),
                        cy + (float)(Math.Sin(dirRad - halfRad) * len));
                    pts[2] = new PointF(
                        cx + (float)(Math.Cos(dirRad + halfRad) * len),
                        cy + (float)(Math.Sin(dirRad + halfRad) * len));

                    g.FillPolygon(brush, pts);
                    g.DrawPolygon(pen, pts);
                }
            }
            font.Dispose();
        }

        private List<WindRoseBin> GetCurrentBins()
        {
            var bins = new List<WindRoseBin>();
            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.IsNewRow) continue;
                try
                {
                    bins.Add(new WindRoseBin
                    {
                        DirectionDeg = Convert.ToDouble(row.Cells[0].Value ?? 0),
                        Frequency = Convert.ToDouble(row.Cells[1].Value ?? 0)
                    });
                }
                catch { }
            }
            return bins;
        }
    }
}
