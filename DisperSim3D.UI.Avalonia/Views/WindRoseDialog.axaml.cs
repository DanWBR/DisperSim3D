#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>WindRoseDialog</c>.
    /// Edits a <see cref="WindRoseData"/> set of bins (direction, frequency,
    /// speed, stability) and draws a live rose preview as a wedge polygon
    /// per bin. Presets create 8- and 16-direction starter sets; the
    /// <see cref="GenerateScenarios"/> toggle drives downstream scenario
    /// generation when the dialog closes with OK.
    /// </summary>
    public partial class WindRoseDialog : Window
    {
        public WindRoseData Result { get; private set; }
        public bool GenerateScenarios => ChkGenerate.IsChecked == true;

        private readonly ObservableCollection<WindRoseBin> _rows = new();

        public WindRoseDialog() : this(null) { }

        public WindRoseDialog(WindRoseData? existing)
        {
            Result = existing ?? WindRoseData.Create8Directions();
            InitializeComponent();

            GridBins.ItemsSource = _rows;
            GridBins.Columns.Add(new DataGridTextColumn
            {
                Header = "Direction (°)",
                Width = new DataGridLength(110),
                Binding = new Binding(nameof(WindRoseBin.DirectionDeg)) { Mode = BindingMode.TwoWay }
            });
            GridBins.Columns.Add(new DataGridTextColumn
            {
                Header = "Frequency (%)",
                Width = new DataGridLength(110),
                Binding = new Binding(nameof(WindRoseBin.Frequency)) { Mode = BindingMode.TwoWay }
            });
            GridBins.Columns.Add(new DataGridTextColumn
            {
                Header = "Wind Speed (m/s)",
                Width = new DataGridLength(120),
                Binding = new Binding(nameof(WindRoseBin.WindSpeed)) { Mode = BindingMode.TwoWay }
            });
            GridBins.Columns.Add(new DataGridTextColumn
            {
                Header = "Stability",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(WindRoseBin.StabilityClass)) { Mode = BindingMode.TwoWay }
            });

            PopulateGrid();

            // Repaint the rose when the user edits any bin or resizes the
            // canvas. CellEditEnded fires after a value commits — that's the
            // moment a wedge would need to grow/shrink.
            GridBins.CellEditEnded += (_, _) => RedrawChart();
            _rows.CollectionChanged += (_, _) => RedrawChart();
            ChartCanvas.SizeChanged += (_, _) => RedrawChart();

            // First draw — Dispatcher.Post so the Canvas has measured its
            // arrange bounds before we ask for ChartCanvas.Bounds.
            global::Avalonia.Threading.Dispatcher.UIThread.Post(RedrawChart,
                global::Avalonia.Threading.DispatcherPriority.Background);
        }

        private void PopulateGrid()
        {
            _rows.Clear();
            foreach (var bin in Result.Bins)
                _rows.Add(bin);
        }

        private void BtnPreset8_Click(object? sender, RoutedEventArgs e)
        {
            Result = WindRoseData.Create8Directions();
            PopulateGrid();
            RedrawChart();
        }

        private void BtnPreset16_Click(object? sender, RoutedEventArgs e)
        {
            Result = WindRoseData.Create16Directions();
            PopulateGrid();
            RedrawChart();
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            // The DataGrid edited the WindRoseBin instances in-place, but we
            // rebuild the list to drop any rows the user fully cleared and to
            // pick up the current order.
            Result.Bins.Clear();
            foreach (var b in _rows)
                Result.Bins.Add(b);
            Close(true);
        }

        // ── Rose preview ─────────────────────────────────────────────────────
        private void RedrawChart()
        {
            ChartCanvas.Children.Clear();

            double w = ChartCanvas.Bounds.Width;
            double h = ChartCanvas.Bounds.Height;
            if (w < 40 || h < 40) return;

            double cx = w / 2;
            double cy = h / 2;
            double radius = Math.Min(cx, cy) - 30;
            if (radius < 10) return;

            // Compass guide circles (outer + half).
            ChartCanvas.Children.Add(new Ellipse
            {
                Width = radius * 2, Height = radius * 2,
                Stroke = Brushes.LightGray, StrokeThickness = 1,
                Fill = Brushes.Transparent,
                [Canvas.LeftProperty] = cx - radius,
                [Canvas.TopProperty] = cy - radius
            });
            ChartCanvas.Children.Add(new Ellipse
            {
                Width = radius, Height = radius,
                Stroke = Brushes.LightGray, StrokeThickness = 1,
                Fill = Brushes.Transparent,
                [Canvas.LeftProperty] = cx - radius / 2,
                [Canvas.TopProperty] = cy - radius / 2
            });

            // Cardinal labels (N/E/S/W). We use the same angle math as the
            // WinForms version: 270° = N, 0° = E, 90° = S, 180° = W after
            // converting to screen coords (Y down).
            string[] labels = { "N", "E", "S", "W" };
            int[] labelAngles = { 270, 0, 90, 180 };
            for (int i = 0; i < 4; i++)
            {
                double rad = labelAngles[i] * Math.PI / 180.0;
                double lx = cx + Math.Cos(rad) * (radius + 15);
                double ly = cy + Math.Sin(rad) * (radius + 15);
                ChartCanvas.Children.Add(new TextBlock
                {
                    Text = labels[i],
                    Foreground = Brushes.Black,
                    FontSize = 11,
                    [Canvas.LeftProperty] = lx - 5,
                    [Canvas.TopProperty]  = ly - 8
                });
            }

            if (_rows.Count == 0) return;
            double maxFreq = _rows.Max(b => b.Frequency);
            if (maxFreq < 0.01) return;

            double wedgeHalfAngle = _rows.Count > 1 ? 180.0 / _rows.Count : 15;

            var fill = new SolidColorBrush(Color.FromArgb(150, 70, 130, 200));
            var stroke = new SolidColorBrush(Color.FromArgb(200, 30, 80, 160));

            foreach (var bin in _rows)
            {
                double dirRad = (bin.DirectionDeg - 90) * Math.PI / 180.0;
                double len = (bin.Frequency / maxFreq) * radius;
                double halfRad = wedgeHalfAngle * Math.PI / 180.0;

                var poly = new Polygon
                {
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = 1.5,
                    Points = new Points
                    {
                        new Point(cx, cy),
                        new Point(cx + Math.Cos(dirRad - halfRad) * len,
                                  cy + Math.Sin(dirRad - halfRad) * len),
                        new Point(cx + Math.Cos(dirRad + halfRad) * len,
                                  cy + Math.Sin(dirRad + halfRad) * len)
                    }
                };
                ChartCanvas.Children.Add(poly);
            }
        }
    }
}
