#nullable enable
using Avalonia.Controls;
using Avalonia.Interactivity;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>MonitorPointDialog</c>.
    /// Edits a <see cref="MonitorPoint3D"/>: name + (X, Y, Z) world position.
    /// Returns via <see cref="Result"/> + <see cref="BuildMonitor"/>.
    /// </summary>
    public partial class MonitorPointDialog : Window
    {
        public string MonitorName { get; private set; } = "Monitor1";
        // Renamed from Position to avoid hiding Window.Position (which is the
        // window's screen pixel coordinate, completely unrelated to our model).
        public Point3D MonitorPosition { get; private set; }

        public MonitorPointDialog() : this("Monitor1", 0, 0, 2) { }

        public MonitorPointDialog(string name, double x, double y, double z)
        {
            InitializeComponent();
            TxtName.Text = name;
            NudX.Value = (decimal)x;
            NudY.Value = (decimal)y;
            NudZ.Value = (decimal)z;
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtName.Text))
            {
                TxtName.Text = "Monitor1";
            }
            MonitorName = TxtName.Text.Trim();
            MonitorPosition = new Point3D(
                (double)(NudX.Value ?? 0m),
                (double)(NudY.Value ?? 0m),
                (double)(NudZ.Value ?? 2m));
            Close(true);
        }

        public MonitorPoint3D BuildMonitor() => new MonitorPoint3D
        {
            Name = MonitorName,
            Position = MonitorPosition
        };
    }
}
