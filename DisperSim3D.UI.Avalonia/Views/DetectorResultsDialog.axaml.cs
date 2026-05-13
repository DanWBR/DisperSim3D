#nullable enable
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>DetectorResultsDialog</c>.
    /// Read-only results viewer: top banner with coverage / min/max/avg
    /// detection times; bottom table with one row per detector showing
    /// name, position, threshold, detected flag, and detection time.
    /// </summary>
    public partial class DetectorResultsDialog : Window
    {
        private readonly ObservableCollection<DetectorRow> _rows = new();

        public DetectorResultsDialog() : this(new DetectorEvaluationResult(), new List<GasDetector3D>()) { }

        public DetectorResultsDialog(DetectorEvaluationResult result, List<GasDetector3D> detectors)
        {
            InitializeComponent();

            var inv = CultureInfo.InvariantCulture;

            // The evaluator leaves MinDetectionTimeS at double.MaxValue when
            // nothing triggered — display 0 in that case so the banner
            // doesn't show a noise value.
            double minTime = result.MinDetectionTimeS == double.MaxValue
                ? 0
                : result.MinDetectionTimeS;
            SummaryText.Text = string.Format(inv,
                "Coverage: {0:F1}% ({1}/{2} detectors triggered)\n" +
                "Min detection time: {3:F1} s\n" +
                "Max detection time: {4:F1} s\n" +
                "Avg detection time: {5:F1} s\n\n" +
                "Coverage = fraction of detectors that saw concentration above their threshold during the simulation. " +
                "Detection time = first instant the threshold was crossed at each detector.",
                result.CoveragePercent, result.DetectorsTriggered, result.TotalDetectors,
                minTime, result.MaxDetectionTimeS, result.AvgDetectionTimeS);

            GridDetectors.ItemsSource = _rows;
            GridDetectors.Columns.Add(new DataGridTextColumn
            {
                Header = "Detector",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(DetectorRow.Name))
            });
            GridDetectors.Columns.Add(new DataGridTextColumn
            {
                Header = "Position",
                Width = new DataGridLength(150),
                Binding = new Binding(nameof(DetectorRow.Position))
            });
            GridDetectors.Columns.Add(new DataGridTextColumn
            {
                Header = "Threshold",
                Width = new DataGridLength(110),
                Binding = new Binding(nameof(DetectorRow.Threshold))
            });
            GridDetectors.Columns.Add(new DataGridTextColumn
            {
                Header = "Detected",
                Width = new DataGridLength(80),
                Binding = new Binding(nameof(DetectorRow.Detected))
            });
            GridDetectors.Columns.Add(new DataGridTextColumn
            {
                Header = "Time (s)",
                Width = new DataGridLength(80),
                Binding = new Binding(nameof(DetectorRow.Time))
            });

            foreach (var det in detectors)
            {
                _rows.Add(new DetectorRow
                {
                    Name      = det.Name ?? "(unnamed)",
                    Position  = string.Format(inv, "({0:F1}, {1:F1}, {2:F1})",
                                    det.Position.X, det.Position.Y, det.Position.Z),
                    Threshold = det.ThresholdKgM3.ToString("E2", inv),
                    Detected  = det.Detected ? "YES" : "no",
                    Time      = det.DetectionTimeS >= 0
                                ? det.DetectionTimeS.ToString("F1", inv)
                                : "-"
                });
            }
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e) => Close(true);

        /// <summary>Pre-formatted row for the detectors grid. Strings rather
        /// than doubles so the engine's invariant-culture formatting carries
        /// through without an Avalonia value-converter dance.</summary>
        public sealed class DetectorRow
        {
            public string Name { get; set; } = "";
            public string Position { get; set; } = "";
            public string Threshold { get; set; } = "";
            public string Detected { get; set; } = "";
            public string Time { get; set; } = "";
        }
    }
}
