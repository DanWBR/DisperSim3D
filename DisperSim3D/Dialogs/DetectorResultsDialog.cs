using System;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class DetectorResultsDialog : Form
    {
        public DetectorResultsDialog(DetectorEvaluationResult result,
            System.Collections.Generic.List<GasDetector3D> detectors)
        {
            BuildUI(result, detectors);
        }

        private void BuildUI(DetectorEvaluationResult result,
            System.Collections.Generic.List<GasDetector3D> detectors)
        {
            this.Text = "Detector Evaluation Results";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            var dpi = DeviceDpi / 96f;
            this.Size = new System.Drawing.Size((int)(500 * dpi), (int)(400 * dpi));
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;

            var inv = System.Globalization.CultureInfo.InvariantCulture;

            var outerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var summary = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(10),
                MaximumSize = new System.Drawing.Size((int)(480 * dpi), 0),
                Text = string.Format(inv,
                    "Coverage: {0:F1}% ({1}/{2} detectors triggered)\n" +
                    "Min detection time: {3:F1} s\n" +
                    "Max detection time: {4:F1} s\n" +
                    "Avg detection time: {5:F1} s\n\n",
                    result.CoveragePercent, result.DetectorsTriggered, result.TotalDetectors,
                    result.MinDetectionTimeS == double.MaxValue ? 0 : result.MinDetectionTimeS,
                    result.MaxDetectionTimeS, result.AvgDetectionTimeS) +
                    "Coverage = fraction of detectors that saw concentration above their threshold during the simulation. " +
                    "Detection time = first instant the threshold was crossed at each detector."
            };
            outerLayout.Controls.Add(summary, 0, 0);

            var dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = System.Drawing.SystemColors.Window
            };
            dgv.Columns.Add("Name", "Detector");
            dgv.Columns.Add("Pos", "Position");
            dgv.Columns.Add("Threshold", "Threshold");
            dgv.Columns.Add("Detected", "Detected");
            dgv.Columns.Add("Time", "Time (s)");

            foreach (var det in detectors)
            {
                string pos = string.Format(inv, "({0:F1}, {1:F1}, {2:F1})",
                    det.Position.X, det.Position.Y, det.Position.Z);
                dgv.Rows.Add(det.Name, pos,
                    det.ThresholdKgM3.ToString("E2", inv),
                    det.Detected ? "YES" : "no",
                    det.DetectionTimeS >= 0 ? det.DetectionTimeS.ToString("F1", inv) : "-");
            }
            outerLayout.Controls.Add(dgv, 0, 1);

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
            outerLayout.Controls.Add(btnPanel, 0, 2);

            this.Controls.Add(outerLayout);
            this.ApplyDpiScaling();
        }
    }
}
