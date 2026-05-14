#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DisperSim3D.Models;
using DisperSim3D.UI.Avalonia.Core;

namespace DisperSim3D.UI.Avalonia.Views
{
    public class SimulationManagerPanel : UserControl
    {
        private readonly SimulationManager _manager;
        private readonly TabControl _tabs;
        private readonly StackPanel _scheduledList;
        private readonly StackPanel _completeList;
        private readonly StackPanel _failedList;
        private readonly TextBlock _lblRunning;
        private readonly TextBlock _lblStatus;
        private readonly NumericUpDown _nudParallel;
        private readonly DispatcherTimer _refreshTimer;

        public event EventHandler<CfdSimulationEntry>? PlayResultRequested;

        public SimulationManagerPanel(SimulationManager manager)
        {
            _manager = manager;

            // ── Header ──────────────────────────────────────────────────
            _lblRunning = new TextBlock
            {
                Text = "No Simulations Running",
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };

            var lblParallel = new TextBlock
            {
                Text = "Max Parallel:",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 210, 240)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 4, 0)
            };

            _nudParallel = new NumericUpDown
            {
                Minimum = 1,
                Maximum = Environment.ProcessorCount,
                Value = Math.Min(_manager.MaxParallelJobs, Environment.ProcessorCount),
                Width = 70,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            _nudParallel.ValueChanged += (_, _) =>
            {
                if (_nudParallel.Value.HasValue)
                    _manager.MaxParallelJobs = (int)_nudParallel.Value.Value;
            };

            var btnCancelAll = new Button
            {
                Content = "Cancel All",
                Background = new SolidColorBrush(Color.FromRgb(180, 60, 60)),
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Padding = new Thickness(10, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            btnCancelAll.Click += async (_, _) =>
            {
                var dlg = new Window
                {
                    Title = "Cancel All",
                    Width = 340, Height = 130,
                    CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    SystemDecorations = SystemDecorations.BorderOnly
                };
                bool confirmed = false;
                var btnYes = new Button { Content = "Yes", Width = 80 };
                var btnNo = new Button { Content = "No", Width = 80 };
                btnYes.Click += (_, _) => { confirmed = true; dlg.Close(); };
                btnNo.Click += (_, _) => dlg.Close();

                dlg.Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = "Cancel all running and queued simulations?", FontSize = 13 },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Margin = new Thickness(0, 14, 0, 0),
                            Children = { btnNo, btnYes }
                        }
                    }
                };

                if (this.VisualRoot is Window owner)
                    await dlg.ShowDialog(owner);
                if (confirmed) { _manager.CancelAll(); RefreshAll(); }
            };

            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(40, 80, 120)),
                Padding = new Thickness(10, 6),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { _lblRunning, lblParallel, _nudParallel, btnCancelAll }
                }
            };

            // ── Tab content ─────────────────────────────────────────────
            _scheduledList = new StackPanel { Spacing = 1 };
            _completeList = new StackPanel { Spacing = 1 };
            _failedList = new StackPanel { Spacing = 1 };

            var tabScheduled = new TabItem
            {
                Header = "Scheduled (0)",
                Content = new ScrollViewer { Content = _scheduledList }
            };
            var tabComplete = new TabItem
            {
                Header = "Complete (0)",
                Content = new ScrollViewer { Content = _completeList }
            };
            var tabFailed = new TabItem
            {
                Header = "Failed (0)",
                Content = new ScrollViewer { Content = _failedList }
            };

            _tabs = new TabControl { Items = { tabScheduled, tabComplete, tabFailed } };

            // ── Status bar ──────────────────────────────────────────────
            _lblStatus = new TextBlock
            {
                Text = "0 total",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Margin = new Thickness(8, 3)
            };

            var statusBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Child = _lblStatus
            };

            // ── Root layout ─────────────────────────────────────────────
            Content = new DockPanel
            {
                Children =
                {
                    SetDock(header, Dock.Top),
                    SetDock(statusBar, Dock.Bottom),
                    _tabs
                }
            };

            // Wire events
            _manager.JobStatusChanged += (_, _) =>
                Dispatcher.UIThread.Post(RefreshAll);
            _manager.JobProgressUpdated += (_, _) =>
                Dispatcher.UIThread.Post(RefreshScheduledProgress);

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refreshTimer.Tick += (_, _) => RefreshScheduledProgress();
            _refreshTimer.Start();

            RefreshAll();
        }

        private static Control SetDock(Control c, Dock dock)
        {
            DockPanel.SetDock(c, dock);
            return c;
        }

        private void RefreshAll()
        {
            var jobs = _manager.AllJobs;

            var scheduled = jobs.Where(j =>
                j.Status is SimulationJobStatus.Queued or SimulationJobStatus.Running
                or SimulationJobStatus.Paused).ToList();
            var complete = jobs.Where(j => j.Status == SimulationJobStatus.Completed).ToList();
            var failed = jobs.Where(j =>
                j.Status is SimulationJobStatus.Failed or SimulationJobStatus.Cancelled).ToList();

            RebuildScheduled(scheduled);
            RebuildComplete(complete);
            RebuildFailed(failed);

            int running = scheduled.Count(j => j.Status == SimulationJobStatus.Running);
            int paused = scheduled.Count(j => j.Status == SimulationJobStatus.Paused);
            int queued = scheduled.Count(j => j.Status == SimulationJobStatus.Queued);

            var parts = new List<string>();
            if (running > 0) parts.Add($"{running} Running");
            if (paused > 0) parts.Add($"{paused} Paused");
            if (queued > 0) parts.Add($"{queued} Queued");
            _lblRunning.Text = parts.Count > 0 ? string.Join(", ", parts) : "No Simulations Running";

            int total = scheduled.Count + complete.Count + failed.Count;
            _lblStatus.Text = $"{total} total — {scheduled.Count} scheduled, {complete.Count} complete, {failed.Count} failed/cancelled";

            ((TabItem)_tabs.Items[0]!).Header = $"Scheduled ({scheduled.Count})";
            ((TabItem)_tabs.Items[1]!).Header = $"Complete ({complete.Count})";
            ((TabItem)_tabs.Items[2]!).Header = $"Failed ({failed.Count})";
        }

        private void RebuildScheduled(List<SimulationJob> jobs)
        {
            _scheduledList.Children.Clear();
            foreach (var job in jobs)
                _scheduledList.Children.Add(BuildScheduledRow(job));
        }

        private void RebuildComplete(List<SimulationJob> jobs)
        {
            _completeList.Children.Clear();
            foreach (var job in jobs.OrderByDescending(j => j.CompletedAt))
                _completeList.Children.Add(BuildCompleteRow(job));
        }

        private void RebuildFailed(List<SimulationJob> jobs)
        {
            _failedList.Children.Clear();
            foreach (var job in jobs.OrderByDescending(j => j.CompletedAt))
                _failedList.Children.Add(BuildFailedRow(job));
        }

        private Control BuildScheduledRow(SimulationJob job)
        {
            string icon = job.Status switch
            {
                SimulationJobStatus.Running => "Running",
                SimulationJobStatus.Paused => "Paused",
                _ => "Queued"
            };

            var lblName = new TextBlock
            {
                Text = $"[{icon}] {job.Name}",
                FontWeight = job.Status is SimulationJobStatus.Running or SimulationJobStatus.Paused
                    ? FontWeight.Bold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Width = double.NaN
            };

            var progressBar = new ProgressBar
            {
                Minimum = 0, Maximum = 1,
                Value = job.Progress,
                Width = 100, Height = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
            progressBar.Tag = job;

            var lblPercent = new TextBlock
            {
                Text = $"{job.Progress * 100:0}%",
                FontSize = 11, Width = 36,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right
            };
            lblPercent.Tag = job;

            var lblStatus = new TextBlock
            {
                Text = job.StatusText ?? "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Width = double.NaN
            };
            lblStatus.Tag = job;

            var lblElapsed = new TextBlock
            {
                Text = FormatElapsedRemaining(job),
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 120
            };
            lblElapsed.Tag = job;

            var btnPause = new Button
            {
                Content = job.Status == SimulationJobStatus.Paused ? "Resume" : "Pause",
                FontSize = 11, Padding = new Thickness(6, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            btnPause.Click += (_, _) =>
            {
                if (job.Status == SimulationJobStatus.Running) _manager.PauseJob(job);
                else if (job.Status == SimulationJobStatus.Paused) _manager.ResumeJob(job);
                RefreshAll();
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                FontSize = 11, Padding = new Thickness(6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            btnCancel.Click += (_, _) => { _manager.CancelJob(job); RefreshAll(); };

            var btnRemove = new Button
            {
                Content = "Remove",
                FontSize = 11, Padding = new Thickness(6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            btnRemove.Click += (_, _) => { _manager.RemoveJob(job); RefreshAll(); };

            var row = new Border
            {
                Background = job.Status switch
                {
                    SimulationJobStatus.Running => new SolidColorBrush(Color.FromRgb(240, 248, 255)),
                    SimulationJobStatus.Paused => new SolidColorBrush(Color.FromRgb(255, 250, 230)),
                    _ => Brushes.White
                },
                Padding = new Thickness(8, 4),
                Tag = job,
                Child = new Grid
                {
                    ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto,Auto,120,Auto,Auto,Auto"),
                    Children =
                    {
                        Set(lblName, 0),
                        Set(progressBar, 1),
                        Set(lblPercent, 2),
                        Set(lblStatus, 3),
                        Set(lblElapsed, 4),
                        Set(btnPause, 5),
                        Set(btnCancel, 6),
                        Set(btnRemove, 7)
                    }
                }
            };

            return row;
        }

        private Control BuildCompleteRow(SimulationJob job)
        {
            string elapsed = "";
            if (job.StartedAt.HasValue && job.CompletedAt.HasValue)
            {
                var span = job.CompletedAt.Value - job.StartedAt.Value;
                elapsed = span.TotalMinutes >= 1
                    ? $"{span.TotalMinutes:0}m {span.Seconds:0}s"
                    : $"{span.TotalSeconds:0.0}s";
            }

            var btnPlay = new Button
            {
                Content = "Play",
                FontSize = 11, Padding = new Thickness(6, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            btnPlay.Click += (_, _) =>
            {
                if (job.ResultEntry != null)
                    PlayResultRequested?.Invoke(this, job.ResultEntry);
            };

            var btnRemove = new Button
            {
                Content = "Remove",
                FontSize = 11, Padding = new Thickness(6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            btnRemove.Click += (_, _) => { _manager.RemoveJob(job); RefreshAll(); };

            return new Border
            {
                Background = Brushes.White,
                Padding = new Thickness(8, 4),
                Child = new Grid
                {
                    ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto,Auto,Auto"),
                    Children =
                    {
                        Set(new TextBlock
                        {
                            Text = job.Name,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        }, 0),
                        Set(new TextBlock
                        {
                            Text = job.CompletedAt?.ToString("yyyy-MM-dd HH:mm") ?? "",
                            FontSize = 11,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(12, 0)
                        }, 1),
                        Set(new TextBlock
                        {
                            Text = elapsed,
                            FontSize = 11,
                            FontFamily = new FontFamily("Consolas"),
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 0, 8, 0)
                        }, 2),
                        Set(btnPlay, 3),
                        Set(btnRemove, 4)
                    }
                }
            };
        }

        private Control BuildFailedRow(SimulationJob job)
        {
            string icon = job.Status == SimulationJobStatus.Cancelled ? "Cancelled" : "Failed";

            var btnRemove = new Button
            {
                Content = "Remove",
                FontSize = 11, Padding = new Thickness(6, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            btnRemove.Click += (_, _) => { _manager.RemoveJob(job); RefreshAll(); };

            return new Border
            {
                Background = Brushes.White,
                Padding = new Thickness(8, 4),
                Child = new Grid
                {
                    ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto,Auto"),
                    Children =
                    {
                        Set(new TextBlock
                        {
                            Text = $"[{icon}] {job.Name}",
                            Foreground = job.Status == SimulationJobStatus.Failed ? Brushes.Red : Brushes.Gray,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        }, 0),
                        Set(new TextBlock
                        {
                            Text = job.CompletedAt?.ToString("yyyy-MM-dd HH:mm") ?? "",
                            FontSize = 11,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(12, 0)
                        }, 1),
                        Set(new TextBlock
                        {
                            Text = job.StatusText ?? "",
                            FontSize = 11,
                            Foreground = Brushes.Gray,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            MaxWidth = 300,
                            Margin = new Thickness(0, 0, 8, 0)
                        }, 2),
                        Set(btnRemove, 3)
                    }
                }
            };
        }

        private void RefreshScheduledProgress()
        {
            foreach (var child in _scheduledList.Children)
            {
                if (child is not Border border || border.Tag is not SimulationJob job) continue;
                if (border.Child is not Grid grid) continue;

                foreach (var gc in grid.Children)
                {
                    if (gc is ProgressBar pb && pb.Tag == job)
                        pb.Value = job.Progress;
                    else if (gc is TextBlock tb && tb.Tag == job)
                    {
                        if (tb.FontFamily?.Name == "Consolas")
                            tb.Text = FormatElapsedRemaining(job);
                        else if (tb.Foreground is SolidColorBrush scb && scb.Color == Color.FromRgb(100, 100, 100))
                            tb.Text = job.StatusText ?? "";
                        else if (tb.Width == 36)
                            tb.Text = $"{job.Progress * 100:0}%";
                    }
                }
            }
        }

        private static string FormatElapsedRemaining(SimulationJob job)
        {
            if (!job.StartedAt.HasValue)
                return job.Status == SimulationJobStatus.Queued ? "Waiting..." : "";
            if (job.Status is not (SimulationJobStatus.Running or SimulationJobStatus.Paused))
                return "";

            var elapsed = DateTime.Now - job.StartedAt.Value;
            string elStr = elapsed.TotalMinutes >= 1
                ? $"{Math.Floor(elapsed.TotalMinutes):0}m {elapsed.Seconds:00}s"
                : $"{elapsed.TotalSeconds:0}s";

            if (job.Status == SimulationJobStatus.Paused)
                return elStr + " (paused)";

            if (job.Progress > 0.01)
            {
                double totalEstimate = elapsed.TotalSeconds / job.Progress;
                double remainSec = Math.Max(0, totalEstimate - elapsed.TotalSeconds);
                string remStr = remainSec >= 60
                    ? $"~{Math.Floor(remainSec / 60):0}m {(int)remainSec % 60:00}s"
                    : $"~{remainSec:0}s";
                return elStr + " | " + remStr;
            }
            return elStr;
        }

        private static Control Set(Control c, int col)
        {
            Grid.SetColumn(c, col);
            return c;
        }
    }
}
