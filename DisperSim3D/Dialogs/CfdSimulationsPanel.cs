using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class CfdSimulationsPanel : UserControl
    {
        private Panel _progressSection;
        private ProgressBar _progressBar;
        private Label _lblStep;
        private Label _lblElapsed;
        private TextBox _txtLog;
        private Button _btnCancelSolve;
        private DateTime _solveStartTime;
        private Timer _elapsedTimer;
        private double _lastFraction;

        private DataGridView _grid;

        public event EventHandler CancelSolveRequested;
        public event EventHandler<CfdSimulationEntry> PlayRequested;
        public event EventHandler<CfdSimulationEntry> DeleteRequested;
        public event EventHandler<CfdSimulationEntry> OpenFolderRequested;

        public CfdSimulationsPanel()
        {
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            BuildUI();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;

            _progressSection = new Panel
            {
                Dock = DockStyle.Top,
                Height = (int)(220 * dpi),
                Visible = false,
                Padding = new Padding((int)(4 * dpi))
            };
            BuildProgressSection(dpi);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 8.5f),
                RowTemplate = { Height = (int)(28 * dpi) }
            };

            _grid.Columns.Add("Name", "Name");
            _grid.Columns.Add("Type", "Type");
            _grid.Columns.Add("Date", "Date");
            _grid.Columns.Add("Info", "Info");

            var playCol = new DataGridViewButtonColumn
            {
                Name = "Play",
                HeaderText = "",
                Text = "▶",
                UseColumnTextForButtonValue = true,
                Width = (int)(36 * dpi),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FlatStyle = FlatStyle.Flat
            };
            _grid.Columns.Add(playCol);

            var folderCol = new DataGridViewButtonColumn
            {
                Name = "Folder",
                HeaderText = "",
                Text = "📂",
                UseColumnTextForButtonValue = true,
                Width = (int)(36 * dpi),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FlatStyle = FlatStyle.Flat
            };
            _grid.Columns.Add(folderCol);

            var delCol = new DataGridViewButtonColumn
            {
                Name = "Delete",
                HeaderText = "",
                Text = "✖",
                UseColumnTextForButtonValue = true,
                Width = (int)(36 * dpi),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FlatStyle = FlatStyle.Flat
            };
            _grid.Columns.Add(delCol);

            _grid.Columns["Name"].FillWeight = 30;
            _grid.Columns["Type"].FillWeight = 15;
            _grid.Columns["Date"].FillWeight = 25;
            _grid.Columns["Info"].FillWeight = 30;

            _grid.CellContentClick += Grid_CellContentClick;

            this.Controls.Add(_grid);
            this.Controls.Add(_progressSection);
        }

        private void BuildProgressSection(float dpi)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding((int)(4 * dpi))
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _lblStep = new Label
            {
                Text = "Idle",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            layout.Controls.Add(_lblStep, 0, row++);

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _lblElapsed = new Label
            {
                Text = "",
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8f)
            };
            layout.Controls.Add(_lblElapsed, 0, row++);

            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(24 * dpi)));
            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous
            };
            layout.Controls.Add(_progressBar, 0, row++);

            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 8f),
                BackColor = Color.FromArgb(30, 30, 35),
                ForeColor = Color.LightGreen,
                WordWrap = false
            };
            layout.Controls.Add(_txtLog, 0, row++);

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _btnCancelSolve = new Button { Text = "Cancel", AutoSize = true };
            _btnCancelSolve.Click += (s, e) => CancelSolveRequested?.Invoke(this, EventArgs.Empty);
            layout.Controls.Add(_btnCancelSolve, 0, row++);

            _progressSection.Controls.Add(layout);
        }

        private void Grid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var entry = _grid.Rows[e.RowIndex].Tag as CfdSimulationEntry;
            if (entry == null) return;

            if (_grid.Columns[e.ColumnIndex].Name == "Play")
                PlayRequested?.Invoke(this, entry);
            else if (_grid.Columns[e.ColumnIndex].Name == "Folder")
                OpenFolderRequested?.Invoke(this, entry);
            else if (_grid.Columns[e.ColumnIndex].Name == "Delete")
                DeleteRequested?.Invoke(this, entry);
        }

        public void ShowSolveProgress()
        {
            _lblStep.Text = "Starting...";
            _lblStep.ForeColor = SystemColors.ControlText;
            _lblElapsed.Text = "Elapsed: 00:00:00";
            _progressBar.Value = 0;
            _txtLog.Clear();
            _btnCancelSolve.Enabled = true;
            _progressSection.Visible = true;
            _lastFraction = 0;

            _solveStartTime = DateTime.Now;
            if (_elapsedTimer == null)
            {
                _elapsedTimer = new Timer { Interval = 1000 };
                _elapsedTimer.Tick += (s, e) => UpdateElapsedLabel();
            }
            _elapsedTimer.Start();
        }

        public void EnsureSolveProgressVisible()
        {
            if (!_progressSection.Visible)
                _progressSection.Visible = true;
        }

        public void HideSolveProgress()
        {
            StopTimer();
            _progressSection.Visible = false;
        }

        public void UpdateProgress(OpenFoamProgress progress)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<OpenFoamProgress>(UpdateProgress), progress);
                return;
            }

            if (progress.Fraction >= 0)
            {
                _lastFraction = progress.Fraction;
                if (progress.Step != null)
                    _lblStep.Text = progress.Step;
                _progressBar.Value = Math.Min(100, (int)(progress.Fraction * 100));
                UpdateElapsedLabel();
            }

            if (!string.IsNullOrEmpty(progress.LogLine))
            {
                _txtLog.AppendText(progress.LogLine + Environment.NewLine);
                if (_txtLog.TextLength > 50000)
                    _txtLog.Text = _txtLog.Text.Substring(_txtLog.TextLength - 30000);
            }

            if (progress.IsError)
            {
                StopTimer();
                _lblStep.ForeColor = Color.Red;
                _btnCancelSolve.Enabled = false;
            }
            else if (progress.IsComplete)
            {
                StopTimer();
                var elapsed = DateTime.Now - _solveStartTime;
                _lblElapsed.Text = string.Format("Completed in {0}", FormatTimeSpan(elapsed));
                _lblStep.ForeColor = Color.DarkGreen;
                _btnCancelSolve.Enabled = false;
            }
        }

        private void UpdateElapsedLabel()
        {
            var elapsed = DateTime.Now - _solveStartTime;
            string text = "Elapsed: " + FormatTimeSpan(elapsed);

            if (_lastFraction > 0.05 && _lastFraction < 1.0)
            {
                double totalEstimated = elapsed.TotalSeconds / _lastFraction;
                double remaining = totalEstimated - elapsed.TotalSeconds;
                if (remaining > 0)
                    text += "  |  Remaining: ~" + FormatTimeSpan(TimeSpan.FromSeconds(remaining));
            }

            _lblElapsed.Text = text;
        }

        private void StopTimer()
        {
            if (_elapsedTimer != null)
                _elapsedTimer.Stop();
        }

        private static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return string.Format("{0}h {1:D2}m {2:D2}s",
                    (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            if (ts.TotalMinutes >= 1)
                return string.Format("{0}m {1:D2}s", (int)ts.TotalMinutes, ts.Seconds);
            return string.Format("{0}s", (int)ts.TotalSeconds);
        }

        public void RefreshList(List<CfdSimulationEntry> entries)
        {
            _grid.Rows.Clear();
            if (entries == null) return;

            foreach (var entry in entries)
            {
                int idx = _grid.Rows.Add(
                    entry.Name,
                    entry.SolverType ?? "OpenFOAM",
                    entry.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    entry.Summary
                );
                _grid.Rows[idx].Tag = entry;
            }
        }

        public void AddEntry(CfdSimulationEntry entry)
        {
            int idx = _grid.Rows.Add(
                entry.Name,
                entry.SolverType ?? "OpenFOAM",
                entry.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                entry.Summary
            );
            _grid.Rows[idx].Tag = entry;
            _grid.ClearSelection();
            _grid.Rows[idx].Selected = true;
        }

        public void RemoveEntry(CfdSimulationEntry entry)
        {
            for (int i = _grid.Rows.Count - 1; i >= 0; i--)
            {
                if (_grid.Rows[i].Tag == entry)
                {
                    _grid.Rows.RemoveAt(i);
                    break;
                }
            }
        }
    }
}
