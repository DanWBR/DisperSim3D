using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class SimulationManagerPanel : UserControl
    {
        private SimulationManager _manager;
        private TabControl _tabs;
        private DataGridView _gridScheduled;
        private DataGridView _gridComplete;
        private DataGridView _gridFailed;
        private Label _lblStatus;
        private Label _lblRunning;
        private Label _lblMemory;
        private Label _lblCpu;
        private Label _lblSolverProc;
        private Button _btnCancelAll;
        private NumericUpDown _nudParallel;
        private Timer _refreshTimer;
        private PerformanceCounter _cpuCounter;
        private float _dpi;

        public event EventHandler<CfdSimulationEntry> PlayResultRequested;

        public SimulationManagerPanel(SimulationManager manager)
        {
            _manager = manager;
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            _dpi = DeviceDpi / 96f;

            BuildUI();
            WireEvents();
            RefreshAll();

            _refreshTimer = new Timer { Interval = 1000 };
            _refreshTimer.Tick += (s, e) => RefreshStats();
            _refreshTimer.Start();

            try { _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); } catch { }
        }

        private void BuildUI()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(56 * _dpi)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(24 * _dpi)));

            root.Controls.Add(BuildHeaderPanel(), 0, 0);
            root.Controls.Add(BuildTabsPanel(), 0, 1);
            root.Controls.Add(BuildStatusBar(), 0, 2);

            this.Controls.Add(root);
        }

        private Panel BuildHeaderPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(40, 80, 120),
                Padding = new Padding((int)(8 * _dpi), (int)(4 * _dpi), (int)(8 * _dpi), (int)(4 * _dpi))
            };

            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                Padding = new Padding(0)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            _lblRunning = new Label
            {
                Text = "No Simulations Running",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            top.Controls.Add(_lblRunning, 0, 0);

            var lblParallel = new Label
            {
                Text = "Max Parallel:",
                ForeColor = Color.FromArgb(180, 210, 240),
                Font = new Font("Segoe UI", 8f),
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, (int)(2 * _dpi), (int)(4 * _dpi), 0)
            };
            top.Controls.Add(lblParallel, 1, 0);

            _nudParallel = new NumericUpDown
            {
                Minimum = 1,
                Maximum = Environment.ProcessorCount,
                Value = Math.Min(_manager.MaxParallelJobs, Environment.ProcessorCount),
                Width = (int)(50 * _dpi),
                Font = new Font("Segoe UI", 8f),
                Anchor = AnchorStyles.Right
            };
            _nudParallel.ValueChanged += (s, e) => _manager.MaxParallelJobs = (int)_nudParallel.Value;
            top.Controls.Add(_nudParallel, 2, 0);

            _btnCancelAll = MakeHeaderButton("Cancel All", Color.FromArgb(180, 60, 60));
            top.Controls.Add(_btnCancelAll, 3, 0);

            var statsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };
            top.Controls.Add(statsPanel, 0, 1);
            top.SetColumnSpan(statsPanel, 4);

            _lblMemory = new Label
            {
                Text = "Memory: — ",
                ForeColor = Color.FromArgb(180, 210, 240),
                Font = new Font("Segoe UI", 8f),
                AutoSize = true,
                Margin = new Padding(0, 0, (int)(12 * _dpi), 0)
            };
            _lblCpu = new Label
            {
                Text = "CPU: — ",
                ForeColor = Color.FromArgb(180, 210, 240),
                Font = new Font("Segoe UI", 8f),
                AutoSize = true,
                Margin = new Padding(0, 0, (int)(12 * _dpi), 0)
            };
            _lblSolverProc = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(140, 230, 140),
                Font = new Font("Segoe UI", 8f),
                AutoSize = true
            };
            statsPanel.Controls.AddRange(new Control[] { _lblMemory, _lblCpu, _lblSolverProc });

            panel.Controls.Add(top);
            return panel;
        }

        private Button MakeHeaderButton(string text, Color backColor)
        {
            var btn = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = backColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Size = new Size((int)(80 * _dpi), (int)(22 * _dpi)),
                Margin = new Padding((int)(4 * _dpi), 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private TabControl BuildTabsPanel()
        {
            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.5f)
            };

            var tabScheduled = new TabPage("Scheduled") { Padding = new Padding(0) };
            _gridScheduled = CreateGrid(true);
            tabScheduled.Controls.Add(_gridScheduled);

            var tabComplete = new TabPage("Complete") { Padding = new Padding(0) };
            _gridComplete = CreateGrid(false);
            tabComplete.Controls.Add(_gridComplete);

            var tabFailed = new TabPage("Failed") { Padding = new Padding(0) };
            _gridFailed = CreateGrid(false);
            tabFailed.Controls.Add(_gridFailed);

            _tabs.TabPages.AddRange(new[] { tabScheduled, tabComplete, tabFailed });
            return _tabs;
        }

        private DataGridView CreateGrid(bool isScheduled)
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 8.5f),
                RowTemplate = { Height = (int)(26 * _dpi) },
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(230, 230, 230),
                EnableHeadersVisualStyles = false
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding((int)(4 * _dpi));
            grid.ColumnHeadersHeight = (int)(26 * _dpi);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(200, 220, 240);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name", HeaderText = "", FillWeight = 35,
                DefaultCellStyle = { Padding = new Padding((int)(4 * _dpi), 0, 0, 0) }
            });

            if (isScheduled)
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Progress", HeaderText = "Progress", FillWeight = 15,
                    DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
                });
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Status", HeaderText = "Status", FillWeight = 30,
                    DefaultCellStyle = { ForeColor = Color.Gray }
                });
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Elapsed", HeaderText = "Elapsed / Remaining", FillWeight = 18,
                    DefaultCellStyle = { ForeColor = Color.FromArgb(80, 80, 80), Font = new Font("Consolas", 8f) }
                });
                grid.CellPainting += GridScheduled_CellPainting;
            }
            else
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Date", HeaderText = "Date", FillWeight = 20
                });
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Info", HeaderText = "Info", FillWeight = 30,
                    DefaultCellStyle = { ForeColor = Color.Gray }
                });
            }

            if (isScheduled)
            {
                var pauseCol = new DataGridViewButtonColumn
                {
                    Name = "PauseResume",
                    HeaderText = "",
                    UseColumnTextForButtonValue = false,
                    Width = (int)(36 * _dpi),
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                    FlatStyle = FlatStyle.Flat
                };
                grid.Columns.Add(pauseCol);
            }

            var cancelCol = new DataGridViewButtonColumn
            {
                Name = "Action1",
                HeaderText = "",
                Text = isScheduled ? "✖" : "▶",
                UseColumnTextForButtonValue = true,
                Width = (int)(36 * _dpi),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FlatStyle = FlatStyle.Flat
            };
            grid.Columns.Add(cancelCol);

            var removeCol = new DataGridViewButtonColumn
            {
                Name = "Action2",
                HeaderText = "",
                Text = "🗑",
                UseColumnTextForButtonValue = true,
                Width = (int)(36 * _dpi),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FlatStyle = FlatStyle.Flat
            };
            grid.Columns.Add(removeCol);

            grid.CellContentClick += Grid_CellContentClick;
            grid.CellFormatting += Grid_CellFormatting;

            return grid;
        }

        private Panel BuildStatusBar()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(240, 240, 240),
                Padding = new Padding((int)(6 * _dpi), 0, (int)(6 * _dpi), 0)
            };
            _lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(80, 80, 80)
            };
            panel.Controls.Add(_lblStatus);
            return panel;
        }

        private void WireEvents()
        {
            _manager.JobStatusChanged += (s, job) =>
            {
                if (InvokeRequired) { BeginInvoke(new Action(RefreshAll)); return; }
                RefreshAll();
            };
            _manager.JobProgressUpdated += (s, e) =>
            {
                if (InvokeRequired) { BeginInvoke(new Action(RefreshScheduledProgress)); return; }
                RefreshScheduledProgress();
            };

            _btnCancelAll.Click += (s, e) =>
            {
                if (MessageBox.Show("Cancel all running and queued simulations?",
                    "Cancel All", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _manager.CancelAll();
                }
            };
        }

        public void RefreshAll()
        {
            var jobs = _manager.AllJobs;

            var scheduled = jobs.Where(j =>
                j.Status == SimulationJobStatus.Queued || j.Status == SimulationJobStatus.Running
                || j.Status == SimulationJobStatus.Paused).ToList();
            var complete = jobs.Where(j => j.Status == SimulationJobStatus.Completed).ToList();
            var failed = jobs.Where(j =>
                j.Status == SimulationJobStatus.Failed || j.Status == SimulationJobStatus.Cancelled).ToList();

            PopulateScheduled(scheduled);
            PopulateComplete(complete);
            PopulateFailed(failed);

            int running = scheduled.Count(j => j.Status == SimulationJobStatus.Running);
            int paused = scheduled.Count(j => j.Status == SimulationJobStatus.Paused);
            int queued = scheduled.Count(j => j.Status == SimulationJobStatus.Queued);

            var parts = new List<string>();
            if (running > 0) parts.Add(string.Format("{0} Running", running));
            if (paused > 0) parts.Add(string.Format("{0} Paused", paused));
            if (queued > 0) parts.Add(string.Format("{0} Queued", queued));
            _lblRunning.Text = parts.Count > 0 ? string.Join(", ", parts) : "No Simulations Running";

            int total = scheduled.Count + complete.Count + failed.Count;
            _lblStatus.Text = string.Format("{0} total — {1} scheduled, {2} complete, {3} failed/cancelled",
                total, scheduled.Count, complete.Count, failed.Count);

            _tabs.TabPages[0].Text = string.Format("Scheduled ({0})", scheduled.Count);
            _tabs.TabPages[1].Text = string.Format("Complete ({0})", complete.Count);
            _tabs.TabPages[2].Text = string.Format("Failed ({0})", failed.Count);

            RefreshStats();
        }

        private void PopulateScheduled(List<SimulationJob> jobs)
        {
            _gridScheduled.Rows.Clear();
            foreach (var job in jobs)
            {
                int idx = _gridScheduled.Rows.Add();
                var row = _gridScheduled.Rows[idx];
                row.Tag = job;

                string icon;
                switch (job.Status)
                {
                    case SimulationJobStatus.Running: icon = "⏳ "; break;
                    case SimulationJobStatus.Paused: icon = "⏸ "; break;
                    default: icon = "📋 "; break;
                }
                row.Cells["Name"].Value = icon + job.Name;
                row.Cells["Progress"].Value = job.Progress;
                row.Cells["Status"].Value = job.StatusText;
                row.Cells["Elapsed"].Value = FormatElapsedRemaining(job);
                row.Cells["PauseResume"].Value = job.Status == SimulationJobStatus.Paused ? "▶" : "⏸";

                if (job.Status == SimulationJobStatus.Running)
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(240, 248, 255);
                    row.Cells["Name"].Style.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                }
                else if (job.Status == SimulationJobStatus.Paused)
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 250, 230);
                    row.Cells["Name"].Style.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                }
            }
        }

        private void PopulateComplete(List<SimulationJob> jobs)
        {
            _gridComplete.Rows.Clear();
            foreach (var job in jobs.OrderByDescending(j => j.CompletedAt))
            {
                int idx = _gridComplete.Rows.Add();
                var row = _gridComplete.Rows[idx];
                row.Tag = job;

                row.Cells["Name"].Value = "✅ " + job.Name;
                row.Cells["Date"].Value = job.CompletedAt?.ToString("yyyy-MM-dd HH:mm");

                string elapsed = "";
                if (job.StartedAt.HasValue && job.CompletedAt.HasValue)
                {
                    var span = job.CompletedAt.Value - job.StartedAt.Value;
                    elapsed = span.TotalMinutes >= 1
                        ? string.Format("{0:0}m {1:0}s", span.TotalMinutes, span.Seconds)
                        : string.Format("{0:0.0}s", span.TotalSeconds);
                }
                row.Cells["Info"].Value = elapsed + (job.ResultEntry != null ? " — " + job.ResultEntry.Summary : "");
            }
        }

        private void PopulateFailed(List<SimulationJob> jobs)
        {
            _gridFailed.Rows.Clear();
            foreach (var job in jobs.OrderByDescending(j => j.CompletedAt))
            {
                int idx = _gridFailed.Rows.Add();
                var row = _gridFailed.Rows[idx];
                row.Tag = job;

                string icon = job.Status == SimulationJobStatus.Cancelled ? "⏹ " : "❌ ";
                row.Cells["Name"].Value = icon + job.Name;
                row.Cells["Date"].Value = job.CompletedAt?.ToString("yyyy-MM-dd HH:mm");
                row.Cells["Info"].Value = job.StatusText;

                if (job.Status == SimulationJobStatus.Failed)
                    row.DefaultCellStyle.ForeColor = Color.Red;
            }
        }

        private void RefreshScheduledProgress()
        {
            foreach (DataGridViewRow row in _gridScheduled.Rows)
            {
                var job = row.Tag as SimulationJob;
                if (job == null) continue;
                row.Cells["Progress"].Value = job.Progress;
                row.Cells["Status"].Value = job.StatusText;
                row.Cells["Elapsed"].Value = FormatElapsedRemaining(job);
                row.Cells["PauseResume"].Value = job.Status == SimulationJobStatus.Paused ? "▶" : "⏸";
            }
            _gridScheduled.InvalidateColumn(_gridScheduled.Columns["Progress"].Index);
        }

        private string FormatElapsedRemaining(SimulationJob job)
        {
            if (!job.StartedAt.HasValue)
                return job.Status == SimulationJobStatus.Queued ? "Waiting..." : "";
            if (job.Status != SimulationJobStatus.Running && job.Status != SimulationJobStatus.Paused)
                return "";

            var elapsed = DateTime.Now - job.StartedAt.Value;
            string elStr = elapsed.TotalMinutes >= 1
                ? string.Format("{0:0}m {1:00}s", Math.Floor(elapsed.TotalMinutes), elapsed.Seconds)
                : string.Format("{0:0}s", elapsed.TotalSeconds);

            if (job.Status == SimulationJobStatus.Paused)
                return elStr + " (paused)";

            if (job.Progress > 0.01)
            {
                double totalEstimate = elapsed.TotalSeconds / job.Progress;
                double remainSec = totalEstimate - elapsed.TotalSeconds;
                if (remainSec < 0) remainSec = 0;
                string remStr = remainSec >= 60
                    ? string.Format("~{0:0}m {1:00}s", Math.Floor(remainSec / 60), (int)remainSec % 60)
                    : string.Format("~{0:0}s", remainSec);
                return elStr + " | " + remStr;
            }
            return elStr;
        }

        private void RefreshStats()
        {
            var proc = Process.GetCurrentProcess();
            double memMB = proc.WorkingSet64 / (1024.0 * 1024.0);
            double gcMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

            _lblMemory.Text = string.Format("Memory: {0:0.0} MB (GC: {1:0.0} MB)", memMB, gcMB);

            float cpuPct = 0;
            try { if (_cpuCounter != null) cpuPct = _cpuCounter.NextValue(); } catch { }
            _lblCpu.Text = string.Format("CPU: {0:0.0}%", cpuPct);

            RefreshSolverProcessInfo();
        }

        private void RefreshSolverProcessInfo()
        {
            double totalSolverMem = 0;
            int solverCount = 0;
            var procNames = new List<string>();

            foreach (DataGridViewRow row in _gridScheduled.Rows)
            {
                var job = row.Tag as SimulationJob;
                if (job == null || (job.Status != SimulationJobStatus.Running
                    && job.Status != SimulationJobStatus.Paused)) continue;

                var runner = job.Runner;
                if (runner == null) continue;

                var solverProc = runner.CurrentProcess;
                if (solverProc == null) continue;

                try
                {
                    if (solverProc.HasExited) continue;
                    solverProc.Refresh();
                    double solverMemMB = solverProc.WorkingSet64 / (1024.0 * 1024.0);
                    var cpuTime = solverProc.TotalProcessorTime;
                    int pid = solverProc.Id;
                    string procName = solverProc.ProcessName;

                    totalSolverMem += solverMemMB;
                    solverCount++;
                    if (!procNames.Contains(procName))
                        procNames.Add(procName);

                    string statusBase = job.StatusText ?? "";
                    string procInfo = string.Format("  [PID {0} ({1}): {2:0.0} MB, CPU {3:hh\\:mm\\:ss}]",
                        pid, procName, solverMemMB, cpuTime);

                    int existingIdx = statusBase.IndexOf("  [PID ");
                    if (existingIdx >= 0)
                        statusBase = statusBase.Substring(0, existingIdx);

                    row.Cells["Status"].Value = statusBase + procInfo;
                }
                catch { }
            }

            if (solverCount > 0)
            {
                string names = string.Join(", ", procNames);
                _lblSolverProc.Text = string.Format("Solver: {0} process{1} ({2}) — {3:0.0} MB",
                    solverCount, solverCount > 1 ? "es" : "", names, totalSolverMem);
            }
            else
            {
                _lblSolverProc.Text = "";
            }
        }

        private void Grid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var grid = (DataGridView)sender;
            var job = grid.Rows[e.RowIndex].Tag as SimulationJob;
            if (job == null) return;

            string colName = grid.Columns[e.ColumnIndex].Name;

            if (colName == "PauseResume")
            {
                if (job.Status == SimulationJobStatus.Running)
                    _manager.PauseJob(job);
                else if (job.Status == SimulationJobStatus.Paused)
                    _manager.ResumeJob(job);
                RefreshAll();
            }
            else if (colName == "Action1")
            {
                if (grid == _gridScheduled)
                {
                    _manager.CancelJob(job);
                    RefreshAll();
                }
                else if (grid == _gridComplete)
                {
                    if (job.ResultEntry != null)
                        PlayResultRequested?.Invoke(this, job.ResultEntry);
                }
                else if (grid == _gridFailed)
                {
                    // Re-queue: future feature
                }
            }
            else if (colName == "Action2")
            {
                _manager.RemoveJob(job);
                RefreshAll();
            }
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
        }

        private void GridScheduled_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var grid = (DataGridView)sender;
            if (grid.Columns[e.ColumnIndex].Name != "Progress") return;

            e.Handled = true;

            using (var bgBrush = new SolidBrush(e.CellStyle.BackColor))
                e.Graphics.FillRectangle(bgBrush, e.CellBounds);

            var job = grid.Rows[e.RowIndex].Tag as SimulationJob;
            double fraction = 0;
            if (job != null) fraction = Math.Max(0, Math.Min(1, job.Progress));

            var barRect = new Rectangle(
                e.CellBounds.X + 3, e.CellBounds.Y + 4,
                e.CellBounds.Width - 6, e.CellBounds.Height - 8);

            using (var trackBrush = new SolidBrush(Color.FromArgb(220, 220, 220)))
                e.Graphics.FillRectangle(trackBrush, barRect);

            if (fraction > 0)
            {
                var fillRect = new Rectangle(barRect.X, barRect.Y,
                    (int)(barRect.Width * fraction), barRect.Height);
                Color barColor;
                if (job != null && job.Status == SimulationJobStatus.Paused)
                    barColor = Color.FromArgb(200, 170, 50);
                else if (job != null && job.Status == SimulationJobStatus.Running)
                    barColor = Color.FromArgb(46, 139, 87);
                else
                    barColor = Color.FromArgb(100, 149, 237);
                using (var fillBrush = new SolidBrush(barColor))
                    e.Graphics.FillRectangle(fillBrush, fillRect);
            }

            string text = string.Format("{0:0}%", fraction * 100);
            TextRenderer.DrawText(e.Graphics, text, e.CellStyle.Font,
                barRect, Color.FromArgb(40, 40, 40),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            using (var borderPen = new Pen(Color.FromArgb(200, 200, 200)))
                e.Graphics.DrawRectangle(borderPen, barRect);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer?.Stop();
                _refreshTimer?.Dispose();
                _cpuCounter?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
