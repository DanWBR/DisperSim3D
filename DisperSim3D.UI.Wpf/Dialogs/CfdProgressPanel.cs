using System;
using System.Drawing;
using System.Windows.Forms;
using DisperSim3D.Core;

namespace DisperSim3D.Dialogs
{
    public class CfdProgressPanel : UserControl
    {
        private ProgressBar _progressBar;
        private Label _lblStep;
        private Label _lblLastLogLine;
        private Button _btnCancel;
        private Button _btnOpenFolder;

        public event EventHandler CancelRequested;
        public event EventHandler OpenFolderRequested;

        public CfdProgressPanel()
        {
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            BuildUI();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            this.BackColor = SystemColors.Control;
            this.Padding = new Padding(0);

            var headerLabel = new Label
            {
                Text = "CFD Solver",
                Dock = DockStyle.Top,
                Height = (int)(28 * dpi),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(40, 80, 120),
                ForeColor = Color.White,
                Padding = new Padding(8, 0, 0, 0)
            };

            var contentPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoSize = false,
                Padding = new Padding((int)(8 * dpi))
            };
            contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;

            contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _lblStep = new Label
            {
                Text = "Idle",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            contentPanel.Controls.Add(_lblStep, 0, row++);

            contentPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(30 * dpi)));
            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };
            contentPanel.Controls.Add(_progressBar, 0, row++);

            contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _lblLastLogLine = new Label
            {
                Text = "",
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = new Font("Consolas", 8f)
            };
            contentPanel.Controls.Add(_lblLastLogLine, 0, row++);

            contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var btnPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 3, RowCount = 1, Padding = new Padding(4)
            };
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btnPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _btnCancel = new Button { Text = "Cancel", AutoSize = true };
            _btnCancel.Click += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);

            _btnOpenFolder = new Button { Text = "Open Folder", AutoSize = true };
            _btnOpenFolder.Click += (s, e) => OpenFolderRequested?.Invoke(this, EventArgs.Empty);

            btnPanel.Controls.Add(_btnCancel, 0, 0);
            btnPanel.Controls.Add(_btnOpenFolder, 1, 0);
            contentPanel.Controls.Add(btnPanel, 0, row++);

            this.Controls.Add(contentPanel);
            this.Controls.Add(headerLabel);
        }

        public void UpdateProgress(OpenFoamProgress progress)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<OpenFoamProgress>(UpdateProgress), progress);
                return;
            }

            _lblStep.Text = progress.Step;
            _progressBar.Value = Math.Min(100, (int)(progress.Fraction * 100));

            if (!string.IsNullOrEmpty(progress.LogLine))
            {
                _lblLastLogLine.Text = progress.LogLine;
            }

            if (progress.IsError)
                _lblStep.ForeColor = Color.Red;
            else if (progress.IsComplete)
            {
                _lblStep.ForeColor = Color.DarkGreen;
                _btnCancel.Enabled = false;
            }
        }

        public void Reset()
        {
            _lblStep.Text = "Idle";
            _lblStep.ForeColor = SystemColors.ControlText;
            _progressBar.Value = 0;
            _lblLastLogLine.Text = "";
            _btnCancel.Enabled = true;
        }
    }
}
