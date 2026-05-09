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

        public void ShowPlaybackControls(string simulationType, bool isDynamic)
        {
            // Playback is now handled by the unified bar at the bottom of the 3D viewport.
            // Keep this method as a no-op for backward compatibility.
            _playbackSection.Visible = false;
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
