using System;
using System.Drawing;
using System.Windows.Forms;

namespace DisperSim3D.Controls
{
    /// <summary>
    /// Compact playback bar shown at the bottom of the 3D viewport.
    /// Hidden unless a transient simulation is loaded. Exposes Play/Pause/Stop/Speed.
    /// </summary>
    public class PlaybackBar : UserControl
    {
        private Button _btnPlay, _btnPause, _btnStop;
        private ComboBox _cmbSpeed;
        private Label _lblTitle;
        private Label _lblTime;

        public event EventHandler PlayClicked;
        public event EventHandler PauseClicked;
        public event EventHandler StopClicked;
        public event EventHandler<double> SpeedChanged;

        public PlaybackBar()
        {
            this.Height = 36;
            this.Dock = DockStyle.Bottom;
            this.BackColor = SystemColors.ControlLight;
            this.Padding = new Padding(6, 4, 6, 4);

            _lblTitle = new Label
            {
                AutoSize = false,
                Width = 220,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Dock = DockStyle.Left,
                Text = ""
            };

            _btnPlay = new Button { Text = "▶", Width = 36, Dock = DockStyle.Left, FlatStyle = FlatStyle.Flat };
            _btnPlay.Click += (s, e) => PlayClicked?.Invoke(this, EventArgs.Empty);
            _btnPause = new Button { Text = "❚❚", Width = 36, Dock = DockStyle.Left, FlatStyle = FlatStyle.Flat };
            _btnPause.Click += (s, e) => PauseClicked?.Invoke(this, EventArgs.Empty);
            _btnStop = new Button { Text = "■", Width = 36, Dock = DockStyle.Left, FlatStyle = FlatStyle.Flat };
            _btnStop.Click += (s, e) => StopClicked?.Invoke(this, EventArgs.Empty);

            _cmbSpeed = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 60, Dock = DockStyle.Left
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
                AutoSize = false, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(8, 0, 8, 0),
                Text = ""
            };

            // Add right-to-left so Dock=Left stacks correctly
            Controls.Add(_lblTime);
            Controls.Add(_cmbSpeed);
            Controls.Add(new Label { Text = "  Speed: ", AutoSize = false, Width = 56, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight });
            Controls.Add(_btnStop);
            Controls.Add(_btnPause);
            Controls.Add(_btnPlay);
            Controls.Add(_lblTitle);
        }

        public void SetTitle(string title) { _lblTitle.Text = title ?? ""; }
        public void SetTimeText(string time) { _lblTime.Text = time ?? ""; }

        public void SetButtons(bool playEnabled, bool pauseEnabled, bool stopEnabled)
        {
            _btnPlay.Enabled = playEnabled;
            _btnPause.Enabled = pauseEnabled;
            _btnStop.Enabled = stopEnabled;
        }
    }
}
