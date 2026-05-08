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

        private Panel _playbackSection;
        private Button _btnPlayPause;
        private Button _btnStopPlayback;
        private Button _btnRewind;
        private TrackBar _timeTrackBar;
        private Label _lblPlaybackTime;
        private Label _lblSimType;
        private bool _isTrackBarDragging;
        private bool _isPlaying;

        private DataGridView _grid;

        public event EventHandler CancelSolveRequested;
        public event EventHandler<CfdSimulationEntry> PlayRequested;
        public event EventHandler<CfdSimulationEntry> DeleteRequested;
        public event EventHandler<CfdSimulationEntry> OpenFolderRequested;

        public event EventHandler PlayPauseClicked;
        public event EventHandler StopPlaybackClicked;
        public event EventHandler RewindClicked;
        public event EventHandler<double> SeekRequested;

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

            _playbackSection = new Panel
            {
                Dock = DockStyle.Top,
                Height = (int)(90 * dpi),
                Visible = false,
                Padding = new Padding((int)(6 * dpi)),
                BackColor = Color.FromArgb(245, 245, 248)
            };
            BuildPlaybackSection(dpi);

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
            this.Controls.Add(_playbackSection);
            this.Controls.Add(_progressSection);
        }

        private void BuildPlaybackSection(float dpi)
        {
            var topRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = (int)(28 * dpi)
            };

            _lblSimType = new Label
            {
                Text = "",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 60, 60),
                Location = new Point(0, (int)(4 * dpi))
            };
            topRow.Controls.Add(_lblSimType);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = (int)(34 * dpi),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                Padding = new Padding(0)
            };

            int btnSize = (int)(28 * dpi);

            _btnRewind = new Button
            {
                Text = "⏮",
                Size = new Size(btnSize, btnSize),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Symbol", 11f),
                Margin = new Padding(0, 0, (int)(2 * dpi), 0)
            };
            _btnRewind.FlatAppearance.BorderSize = 1;
            _btnRewind.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            _btnRewind.Click += (s, e) => RewindClicked?.Invoke(this, EventArgs.Empty);

            _btnPlayPause = new Button
            {
                Text = "▶",
                Size = new Size(btnSize, btnSize),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Symbol", 11f),
                Margin = new Padding(0, 0, (int)(2 * dpi), 0)
            };
            _btnPlayPause.FlatAppearance.BorderSize = 1;
            _btnPlayPause.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            _btnPlayPause.Click += (s, e) => PlayPauseClicked?.Invoke(this, EventArgs.Empty);

            _btnStopPlayback = new Button
            {
                Text = "⏹",
                Size = new Size(btnSize, btnSize),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Symbol", 11f),
                Margin = new Padding(0, 0, (int)(8 * dpi), 0)
            };
            _btnStopPlayback.FlatAppearance.BorderSize = 1;
            _btnStopPlayback.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            _btnStopPlayback.Click += (s, e) => StopPlaybackClicked?.Invoke(this, EventArgs.Empty);

            _lblPlaybackTime = new Label
            {
                Text = "0.0 / 0.0 s",
                AutoSize = true,
                Font = new Font("Consolas", 9f),
                ForeColor = Color.FromArgb(40, 40, 40),
                Margin = new Padding(0, (int)(5 * dpi), 0, 0)
            };

            buttonPanel.Controls.AddRange(new Control[] {
                _btnRewind, _btnPlayPause, _btnStopPlayback, _lblPlaybackTime
            });

            _timeTrackBar = new TrackBar
            {
                Dock = DockStyle.Top,
                Minimum = 0,
                Maximum = 1000,
                Value = 0,
                TickFrequency = 100,
                SmallChange = 10,
                LargeChange = 100,
                Height = (int)(30 * dpi)
            };
            _timeTrackBar.MouseDown += (s, e) => _isTrackBarDragging = true;
            _timeTrackBar.MouseUp += (s, e) =>
            {
                _isTrackBarDragging = false;
                double fraction = _timeTrackBar.Value / 1000.0;
                SeekRequested?.Invoke(this, fraction);
            };
            _timeTrackBar.KeyUp += (s, e) =>
            {
                double fraction = _timeTrackBar.Value / 1000.0;
                SeekRequested?.Invoke(this, fraction);
            };

            _playbackSection.Controls.Add(_timeTrackBar);
            _playbackSection.Controls.Add(buttonPanel);
            _playbackSection.Controls.Add(topRow);
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

        public void ShowPlaybackControls(string simulationType, bool isDynamic)
        {
            _lblSimType.Text = simulationType;
            _playbackSection.Visible = true;

            _btnPlayPause.Enabled = isDynamic;
            _btnStopPlayback.Enabled = isDynamic;
            _btnRewind.Enabled = isDynamic;
            _timeTrackBar.Enabled = isDynamic;
            _timeTrackBar.Value = 0;

            if (!isDynamic)
            {
                _lblPlaybackTime.Text = "Steady-state";
                _btnPlayPause.Text = "▶";
            }
            else
            {
                _lblPlaybackTime.Text = "0.0 / 0.0 s";
                _btnPlayPause.Text = "▶";
            }
            _isPlaying = false;
        }

        public void HidePlaybackControls()
        {
            _playbackSection.Visible = false;
            _isPlaying = false;
        }

        public void UpdatePlaybackState(bool playing, double currentTimeS, double totalTimeS)
        {
            _isPlaying = playing;
            _btnPlayPause.Text = playing ? "⏸" : "▶";
            _lblPlaybackTime.Text = string.Format("{0:F1} / {1:F1} s", currentTimeS, totalTimeS);

            if (!_isTrackBarDragging && totalTimeS > 0)
            {
                int pos = (int)(currentTimeS / totalTimeS * 1000);
                _timeTrackBar.Value = Math.Max(0, Math.Min(1000, pos));
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
