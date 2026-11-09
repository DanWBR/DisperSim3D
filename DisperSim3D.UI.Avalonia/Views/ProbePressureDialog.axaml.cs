#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using DisperSim3D.Core;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// The overpressure an explosion simulation recorded at its probes (the scene's point
    /// monitors) against time, one curve per probe, and per probe the peak, its time and the
    /// positive impulse. The chart is drawn on a <see cref="Canvas"/> and redrawn on resize.
    /// </summary>
    public partial class ProbePressureDialog : Window
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly ProbeSeries? _series;
        private readonly ObservableCollection<ProbeRow> _rows = new();

        private static readonly IBrush[] LineColors =
        {
            Brushes.Blue, Brushes.Red, Brushes.Green, Brushes.Purple, Brushes.Orange,
            Brushes.Teal, Brushes.Magenta, Brushes.DarkOliveGreen
        };

        public ProbePressureDialog() : this(null, "") { }

        public ProbePressureDialog(ProbeSeries? series, string simulationName)
        {
            _series = series;
            InitializeComponent();

            int n = series?.Names.Count ?? 0;
            SummaryText.Text = n == 0
                ? "'" + simulationName + "' has no probe record. An explosion records the overpressure at the scene's point monitors."
                : string.Format(Inv, "'{0}': overpressure at {1} probe(s), {2} samples to {3:F3} s. The impulse is the time integral of the positive overpressure.",
                    simulationName, n, series!.Times.Length, series.Times.Length > 0 ? series.Times[^1] : 0);

            GridProbes.ItemsSource = _rows;
            void Column(string header, string path, double width) => GridProbes.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(path),
            });
            Column("Probe", nameof(ProbeRow.Name), 0);
            Column("Position (m)", nameof(ProbeRow.Position), 170);
            Column("Peak (kPa)", nameof(ProbeRow.Peak), 100);
            Column("At (s)", nameof(ProbeRow.PeakTime), 80);
            Column("Impulse (Pa·s)", nameof(ProbeRow.Impulse), 110);
            for (int q = 0; q < n; q++)
            {
                var (peak, impulse, at) = series!.Summary(q);
                var p = series.Positions[q];
                _rows.Add(new ProbeRow
                {
                    Name = series.Names[q],
                    Position = string.Format(Inv, "{0:F1}, {1:F1}, {2:F1}", p.X, p.Y, p.Z),
                    Peak = (peak / 1000).ToString("F2", Inv),
                    PeakTime = at.ToString("F3", Inv),
                    Impulse = impulse.ToString("F1", Inv),
                });
            }

            ChartCanvas.SizeChanged += (_, _) => RedrawChart();
            global::Avalonia.Threading.Dispatcher.UIThread.Post(RedrawChart,
                global::Avalonia.Threading.DispatcherPriority.Background);
        }

        private void RedrawChart()
        {
            ChartCanvas.Children.Clear();
            double w = ChartCanvas.Bounds.Width, h = ChartCanvas.Bounds.Height;
            const double left = 56, right = 16, top = 16, bottom = 40;
            double plotW = w - left - right, plotH = h - top - bottom;
            var s = _series;
            if (s == null || s.Names.Count == 0 || s.Times.Length < 2 || plotW < 10 || plotH < 10)
            {
                ChartEmpty.IsVisible = s == null || s.Names.Count == 0 || s.Times.Length < 2;
                return;
            }
            ChartEmpty.IsVisible = false;

            double t0 = s.Times[0], t1 = s.Times[^1];
            double pMin = 0, pMax = 0;
            foreach (var row in s.Values)
                foreach (double v in row) { pMin = Math.Min(pMin, v); pMax = Math.Max(pMax, v); }
            if (!(pMax > pMin)) pMax = pMin + 1;
            if (!(t1 > t0)) t1 = t0 + 1;
            double X(double t) => left + (t - t0) / (t1 - t0) * plotW;
            double Y(double p) => top + plotH - (p - pMin) / (pMax - pMin) * plotH;

            ChartCanvas.Children.Add(new Rectangle
            {
                Stroke = Brushes.Black, StrokeThickness = 1, Width = plotW, Height = plotH, Fill = Brushes.Transparent,
                [Canvas.LeftProperty] = left, [Canvas.TopProperty] = top,
            });
            for (int i = 0; i <= 4; i++)
            {
                double p = pMin + i * (pMax - pMin) / 4, y = Y(p);
                ChartCanvas.Children.Add(new Line { StartPoint = new Point(left, y), EndPoint = new Point(left + plotW, y), Stroke = Brushes.LightGray, StrokeThickness = 1 });
                ChartCanvas.Children.Add(new TextBlock
                {
                    Text = (p / 1000).ToString("G3", Inv), Foreground = Brushes.Gray, FontSize = 10,
                    [Canvas.LeftProperty] = 4, [Canvas.TopProperty] = y - 7,
                });
                double t = t0 + i * (t1 - t0) / 4, x = X(t);
                ChartCanvas.Children.Add(new TextBlock
                {
                    Text = t.ToString("F3", Inv), Foreground = Brushes.Gray, FontSize = 10,
                    [Canvas.LeftProperty] = x - 14, [Canvas.TopProperty] = top + plotH + 4,
                });
            }
            if (pMin < 0)
                ChartCanvas.Children.Add(new Line { StartPoint = new Point(left, Y(0)), EndPoint = new Point(left + plotW, Y(0)), Stroke = Brushes.DarkGray, StrokeThickness = 1 });
            ChartCanvas.Children.Add(new TextBlock
            {
                Text = "Time (s)", Foreground = Brushes.Black, FontSize = 11,
                [Canvas.LeftProperty] = left + plotW / 2 - 24, [Canvas.TopProperty] = top + plotH + 20,
            });
            ChartCanvas.Children.Add(new TextBlock
            {
                Text = "kPa", Foreground = Brushes.Black, FontSize = 11,
                [Canvas.LeftProperty] = 4, [Canvas.TopProperty] = top + plotH + 20,
            });

            // One polyline per probe, thinned to about two points per pixel column.
            int stride = Math.Max(1, s.Times.Length / Math.Max(1, (int)(2 * plotW)));
            for (int q = 0; q < s.Names.Count; q++)
            {
                var brush = LineColors[q % LineColors.Length];
                var pts = new Points();
                for (int k = 0; k < s.Times.Length; k += stride) pts.Add(new Point(X(s.Times[k]), Y(s.Values[q][k])));
                ChartCanvas.Children.Add(new Polyline { Stroke = brush, StrokeThickness = 1.5, Points = pts });
                double legY = top + 6 + q * 16;
                ChartCanvas.Children.Add(new Rectangle
                {
                    Width = 12, Height = 12, Fill = brush,
                    [Canvas.LeftProperty] = left + plotW - 140, [Canvas.TopProperty] = legY,
                });
                ChartCanvas.Children.Add(new TextBlock
                {
                    Text = s.Names[q], Foreground = Brushes.Black, FontSize = 10,
                    [Canvas.LeftProperty] = left + plotW - 124, [Canvas.TopProperty] = legY - 1,
                });
            }
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e) => Close(true);

        /// <summary>Row of the probe table, pre-formatted.</summary>
        public sealed class ProbeRow
        {
            public string Name { get; set; } = "";
            public string Position { get; set; } = "";
            public string Peak { get; set; } = "";
            public string PeakTime { get; set; } = "";
            public string Impulse { get; set; } = "";
        }
    }
}
