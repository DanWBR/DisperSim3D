#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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
    /// Avalonia equivalent of the WPF / WinForms <c>ExceedanceDialog</c>.
    /// Displays exceedance curves (probability that concentration exceeds a
    /// given threshold) across one or more monitor locations. The chart is
    /// hand-drawn on a <see cref="Canvas"/> using <see cref="Polyline"/>
    /// shapes; we redraw on size change so the curves scale with the window.
    /// The table below lists each individual (threshold, probability) sample.
    /// </summary>
    public partial class ExceedanceDialog : Window
    {
        private readonly List<ExceedanceCurveResult> _results;
        private readonly ObservableCollection<PointRow> _rows = new();

        private static readonly IBrush[] LineColors =
        {
            Brushes.Blue, Brushes.Red, Brushes.Green, Brushes.Purple, Brushes.Orange,
            Brushes.Teal, Brushes.Magenta, Brushes.DarkOliveGreen
        };

        public ExceedanceDialog() : this(new List<ExceedanceCurveResult>()) { }

        public ExceedanceDialog(List<ExceedanceCurveResult> results)
        {
            _results = results ?? new List<ExceedanceCurveResult>();
            InitializeComponent();

            SummaryText.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} monitor location(s) analyzed. " +
                "Exceedance curve = probability that the concentration at a monitor exceeds a given threshold, " +
                "weighted by wind-rose frequency. X is log scale; Y is 0–1. " +
                "Useful for siting decisions and comparing against LFL/IDLH limits.",
                _results.Count);

            GridPoints.ItemsSource = _rows;
            GridPoints.Columns.Add(new DataGridTextColumn
            {
                Header = "Location",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(PointRow.Location))
            });
            GridPoints.Columns.Add(new DataGridTextColumn
            {
                Header = "Threshold (kg/m³)",
                Width = new DataGridLength(160),
                Binding = new Binding(nameof(PointRow.ThresholdText))
            });
            GridPoints.Columns.Add(new DataGridTextColumn
            {
                Header = "P(exceed)",
                Width = new DataGridLength(110),
                Binding = new Binding(nameof(PointRow.ProbabilityText))
            });

            var inv = CultureInfo.InvariantCulture;
            foreach (var r in _results)
                foreach (var p in r.Points)
                    _rows.Add(new PointRow
                    {
                        Location        = r.LocationName ?? "",
                        ThresholdText   = p.Threshold.ToString("E2", inv),
                        ProbabilityText = p.Probability.ToString("F3", inv)
                    });

            ChartCanvas.SizeChanged += (_, _) => RedrawChart();
            // First draw kicks off after the window has measured its canvas;
            // schedule via Dispatcher so we render even if SizeChanged never
            // fires (e.g. fixed-size window initial layout).
            global::Avalonia.Threading.Dispatcher.UIThread.Post(RedrawChart,
                global::Avalonia.Threading.DispatcherPriority.Background);
        }

        private void RedrawChart()
        {
            ChartCanvas.Children.Clear();

            double w = ChartCanvas.Bounds.Width;
            double h = ChartCanvas.Bounds.Height;
            if (w < 40 || h < 40) return;

            const double margin = 40;
            double plotW = w - 2 * margin;
            double plotH = h - 2 * margin;
            if (plotW < 10 || plotH < 10) return;

            // Anything plottable? Skip the entire frame so the "no curves"
            // overlay can show through.
            int totalPoints = _results.Sum(r => r.Points.Count);
            if (totalPoints < 2 || _results.All(r => r.Points.Count < 2))
            {
                ChartEmpty.IsVisible = true;
                return;
            }
            ChartEmpty.IsVisible = false;

            // Axes box.
            ChartCanvas.Children.Add(new Rectangle
            {
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Width = plotW,
                Height = plotH,
                Fill = Brushes.Transparent,
                [Canvas.LeftProperty] = margin,
                [Canvas.TopProperty] = margin
            });

            // Y gridlines + tick labels (probability 0..1 in quarters).
            for (int i = 0; i <= 4; i++)
            {
                double yy = margin + plotH - i * plotH / 4.0;
                ChartCanvas.Children.Add(new Line
                {
                    StartPoint = new Point(margin, yy),
                    EndPoint   = new Point(margin + plotW, yy),
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 1
                });
                ChartCanvas.Children.Add(new TextBlock
                {
                    Text = (i * 0.25).ToString("F2", CultureInfo.InvariantCulture),
                    Foreground = Brushes.Gray,
                    FontSize = 10,
                    [Canvas.LeftProperty] = margin - 32,
                    [Canvas.TopProperty]  = yy - 7
                });
            }

            // Axis titles.
            ChartCanvas.Children.Add(new TextBlock
            {
                Text = "Threshold (kg/m³)",
                Foreground = Brushes.Black,
                FontSize = 11,
                [Canvas.LeftProperty] = margin + plotW / 2 - 50,
                [Canvas.TopProperty] = margin + plotH + 10
            });
            ChartCanvas.Children.Add(new TextBlock
            {
                Text = "P(exceed)",
                Foreground = Brushes.Black,
                FontSize = 11,
                [Canvas.LeftProperty] = 6,
                [Canvas.TopProperty] = margin + plotH / 2 - 7
            });

            // Compute the global log-X range so all series share one X axis.
            double globalMin = double.PositiveInfinity;
            double globalMax = double.NegativeInfinity;
            foreach (var r in _results)
            {
                foreach (var p in r.Points)
                {
                    double t = Math.Max(p.Threshold, 1e-15);
                    if (t < globalMin) globalMin = t;
                    if (t > globalMax) globalMax = t;
                }
            }
            if (!(globalMax > globalMin)) globalMax = globalMin * 10;
            double logMin = Math.Log10(Math.Max(globalMin, 1e-15));
            double logMax = Math.Log10(Math.Max(globalMax, 1e-15));
            if (logMax <= logMin) logMax = logMin + 1;

            // Curves + per-series legend swatches.
            for (int r = 0; r < _results.Count; r++)
            {
                var result = _results[r];
                if (result.Points.Count < 2) continue;

                var brush = LineColors[r % LineColors.Length];
                var poly = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = 2
                };
                var pts = new Points();
                foreach (var p in result.Points)
                {
                    double lt = Math.Log10(Math.Max(p.Threshold, 1e-15));
                    double x = margin + (lt - logMin) / (logMax - logMin) * plotW;
                    double y = margin + plotH - p.Probability * plotH;
                    pts.Add(new Point(x, y));
                }
                poly.Points = pts;
                ChartCanvas.Children.Add(poly);

                // Legend row: 12 px swatch + monitor name. Stacked at top-left
                // of the plot area, one per series.
                double legY = margin + 5 + r * 16;
                ChartCanvas.Children.Add(new Rectangle
                {
                    Width = 12, Height = 12, Fill = brush,
                    [Canvas.LeftProperty] = margin + 10,
                    [Canvas.TopProperty] = legY
                });
                ChartCanvas.Children.Add(new TextBlock
                {
                    Text = result.LocationName ?? "(unnamed)",
                    Foreground = Brushes.Black,
                    FontSize = 10,
                    [Canvas.LeftProperty] = margin + 26,
                    [Canvas.TopProperty] = legY - 1
                });
            }
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e) => Close(true);

        /// <summary>Row backing for the per-point DataGrid. Pre-formats numeric
        /// columns as strings so the grid doesn't apply double's culture-
        /// dependent ToString() to scientific values.</summary>
        public sealed class PointRow
        {
            public string Location { get; set; } = "";
            public string ThresholdText { get; set; } = "";
            public string ProbabilityText { get; set; } = "";
        }
    }
}
