using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class ExceedanceDialog : Form
    {
        private List<ExceedanceCurveResult> _results;
        private Panel _chartPanel;

        public ExceedanceDialog(List<ExceedanceCurveResult> results)
        {
            _results = results;
            BuildUI();
        }

        private void BuildUI()
        {
            this.Text = "Exceedance Curves";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            var dpi = DeviceDpi / 96f;
            this.Size = new Size((int)(600 * dpi), (int)(450 * dpi));
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;

            var outerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4
            };
            outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var summary = new Label
            {
                Dock = DockStyle.Fill, AutoSize = true,
                Padding = new Padding(10),
                Text = string.Format("{0} monitor location(s) analyzed", _results.Count)
            };
            outerLayout.Controls.Add(summary, 0, 0);

            _chartPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            _chartPanel.Paint += ChartPanel_Paint;
            outerLayout.Controls.Add(_chartPanel, 0, 1);

            var dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true, AllowUserToAddRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window
            };
            dgv.Columns.Add("Location", "Location");
            dgv.Columns.Add("Threshold", "Threshold (kg/m³)");
            dgv.Columns.Add("Probability", "P(exceed)");

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var r in _results)
            {
                foreach (var p in r.Points)
                {
                    dgv.Rows.Add(r.LocationName,
                        p.Threshold.ToString("E2", inv),
                        p.Probability.ToString("F3", inv));
                }
            }
            outerLayout.Controls.Add(dgv, 0, 2);

            var btnPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 2, RowCount = 1, Padding = new Padding(4)
            };
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var btnClose = new Button
            {
                Text = "Close", DialogResult = DialogResult.OK, AutoSize = true
            };
            btnPanel.Controls.Add(new Label(), 0, 0);
            btnPanel.Controls.Add(btnClose, 1, 0);
            outerLayout.Controls.Add(btnPanel, 0, 3);

            this.Controls.Add(outerLayout);
            this.ApplyDpiScaling();
        }

        private void ChartPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int margin = 50;
            int w = _chartPanel.Width - 2 * margin;
            int h = _chartPanel.Height - 2 * margin;
            if (w < 20 || h < 20) return;

            g.DrawRectangle(Pens.Black, margin, margin, w, h);

            g.DrawString("Threshold (kg/m³)", SystemFonts.DefaultFont, Brushes.Black,
                margin + w / 2 - 50, margin + h + 10);
            using (var sf = new StringFormat { FormatFlags = StringFormatFlags.DirectionVertical })
                g.DrawString("P(exceed)", SystemFonts.DefaultFont, Brushes.Black, 5, margin + h / 2 - 20, sf);

            for (int i = 0; i <= 4; i++)
            {
                float yy = margin + h - i * h / 4f;
                g.DrawLine(Pens.LightGray, margin, yy, margin + w, yy);
                g.DrawString((i * 0.25).ToString("F2"), SystemFonts.SmallCaptionFont, Brushes.Gray,
                    margin - 35, yy - 6);
            }

            Color[] lineColors = { Color.Blue, Color.Red, Color.Green, Color.Purple, Color.Orange };

            for (int r = 0; r < _results.Count; r++)
            {
                var result = _results[r];
                if (result.Points.Count < 2) continue;

                var color = lineColors[r % lineColors.Length];
                using (var pen = new Pen(color, 2f))
                {
                    double minT = result.Points[0].Threshold;
                    double maxT = result.Points[result.Points.Count - 1].Threshold;
                    if (maxT <= minT) maxT = minT + 1;

                    double logMin = Math.Log10(Math.Max(minT, 1e-15));
                    double logMax = Math.Log10(Math.Max(maxT, 1e-15));
                    if (logMax <= logMin) logMax = logMin + 1;

                    for (int i = 1; i < result.Points.Count; i++)
                    {
                        double logT0 = Math.Log10(Math.Max(result.Points[i - 1].Threshold, 1e-15));
                        double logT1 = Math.Log10(Math.Max(result.Points[i].Threshold, 1e-15));

                        float x0 = margin + (float)((logT0 - logMin) / (logMax - logMin) * w);
                        float x1 = margin + (float)((logT1 - logMin) / (logMax - logMin) * w);
                        float y0 = margin + h - (float)(result.Points[i - 1].Probability * h);
                        float y1 = margin + h - (float)(result.Points[i].Probability * h);

                        g.DrawLine(pen, x0, y0, x1, y1);
                    }

                    float legY = margin + 5 + r * 16;
                    g.FillRectangle(new SolidBrush(color), margin + 10, legY, 12, 12);
                    g.DrawString(result.LocationName, SystemFonts.SmallCaptionFont, Brushes.Black,
                        margin + 26, legY - 1);
                }
            }
        }
    }
}
