using System;
using System.Drawing;
using System.Windows.Forms;

namespace DisperSim3D.Controls
{
    /// <summary>
    /// Compact playback bar shown at the bottom of the 3D viewport.
    /// Title | Play/Pause/Stop | Slider | Speed | Time
    /// </summary>
    public class PlaybackBar : UserControl
    {
        private Button _btnPlay, _btnPause, _btnStop;
        private ComboBox _cmbSpeed;
        private Label _lblTitle;
        private Label _lblTime;
        private TrackBar _slider;

        private bool _suppressSliderEvent;

        public event EventHandler PlayClicked;
        public event EventHandler PauseClicked;
        public event EventHandler StopClicked;
        public event EventHandler<double> SpeedChanged;
        /// <summary>Fires when the user drags the slider. Argument is normalized 0..1.</summary>
        public event EventHandler<double> SeekRequested;

        public PlaybackBar()
        {
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            var dpi = DeviceDpi / 96f;
            int rowHeight = (int)(28 * dpi);
            this.Height = rowHeight + (int)(4 * dpi); // 4px total vertical padding
            this.Dock = DockStyle.Bottom;
            this.BackColor = SystemColors.ControlLight;
            this.Padding = new Padding((int)(4 * dpi), (int)(2 * dpi), (int)(4 * dpi), (int)(2 * dpi));

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 8,
                RowCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // title
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // play
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // pause
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // stop
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // slider (fills)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // "Speed:"
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // speed combo
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // time

            _lblTitle = new Label
            {
                AutoSize = false,
                Width = (int)(80 * dpi),
                Height = rowHeight,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Margin = new Padding(0, 0, (int)(8 * dpi), 0)
            };

            _btnPlay = MakeButton("▶", rowHeight, dpi);
            _btnPlay.Click += (s, e) => PlayClicked?.Invoke(this, EventArgs.Empty);

            _btnPause = MakeButton("❚❚", rowHeight, dpi);
            _btnPause.Click += (s, e) => PauseClicked?.Invoke(this, EventArgs.Empty);

            _btnStop = MakeButton("■", rowHeight, dpi);
            _btnStop.Click += (s, e) => StopClicked?.Invoke(this, EventArgs.Empty);

            _slider = new TrackBar
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Minimum = 0,
                Maximum = 1000,
                Value = 0,
                TickStyle = TickStyle.None,
                AutoSize = false,
                Height = rowHeight,
                Margin = new Padding((int)(8 * dpi), 0, (int)(8 * dpi), 0)
            };
            _slider.Scroll += (s, e) =>
            {
                if (_suppressSliderEvent) return;
                SeekRequested?.Invoke(this, _slider.Value / 1000.0);
            };

            var lblSpeed = new Label
            {
                Text = "Speed:",
                AutoSize = false,
                Width = (int)(46 * dpi),
                Height = rowHeight,
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding((int)(8 * dpi), 0, (int)(4 * dpi), 0)
            };

            _cmbSpeed = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = (int)(60 * dpi),
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, (int)(2 * dpi), 0, (int)(2 * dpi))
            };
            _cmbSpeed.Items.AddRange(new object[] { "0.25x", "0.5x", "1x", "2x", "5x", "10x" });
            _cmbSpeed.SelectedIndex = 2;
            _cmbSpeed.SelectedIndexChanged += (s, e) =>
            {
                double[] speeds = { 0.25, 0.5, 1.0, 2.0, 5.0, 10.0 };
                SpeedChanged?.Invoke(this, speeds[_cmbSpeed.SelectedIndex]);
            };

            _lblTime = new Label
            {
                AutoSize = false,
                Width = (int)(120 * dpi),
                Height = rowHeight,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding((int)(8 * dpi), 0, 0, 0)
            };

            layout.Controls.Add(_lblTitle, 0, 0);
            layout.Controls.Add(_btnPlay, 1, 0);
            layout.Controls.Add(_btnPause, 2, 0);
            layout.Controls.Add(_btnStop, 3, 0);
            layout.Controls.Add(_slider, 4, 0);
            layout.Controls.Add(lblSpeed, 5, 0);
            layout.Controls.Add(_cmbSpeed, 6, 0);
            layout.Controls.Add(_lblTime, 7, 0);

            Controls.Add(layout);
        }

        private Button MakeButton(string text, int rowHeight, float dpi)
        {
            return new Button
            {
                Text = text,
                Width = (int)(32 * dpi),
                Height = (int)(rowHeight * dpi),
                FlatStyle = FlatStyle.Standard,
                Margin = new Padding(1, 0, 1, 0),
                Padding = new Padding(0),
                TextAlign = ContentAlignment.MiddleCenter,
                UseCompatibleTextRendering = false
            };
        }

        public void SetTitle(string title) { _lblTitle.Text = title ?? ""; }
        public void SetTimeText(string time) { _lblTime.Text = time ?? ""; }

        public void SetButtons(bool playEnabled, bool pauseEnabled, bool stopEnabled)
        {
            _btnPlay.Enabled = playEnabled;
            _btnPause.Enabled = pauseEnabled;
            _btnStop.Enabled = stopEnabled;
        }

        /// <summary>Sets slider position (0..1) without firing the SeekRequested event.</summary>
        public void SetProgress(double fraction)
        {
            _suppressSliderEvent = true;
            int v = (int)Math.Max(0, Math.Min(1000, fraction * 1000));
            if (_slider.Value != v) _slider.Value = v;
            _suppressSliderEvent = false;
        }

        public void SetSliderEnabled(bool enabled) { _slider.Enabled = enabled; }
    }
}
