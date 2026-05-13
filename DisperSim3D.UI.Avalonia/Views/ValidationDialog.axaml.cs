#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DisperSim3D.Models;
using DisperSim3D.Validation;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>ValidationDialog</c>.
    /// Picks one or more <c>.dsbench</c> files, runs each via
    /// <see cref="ValidationRunner"/> and displays a colour-coded SPM table
    /// (MRB / RMSE / FAC2 / MG / VG). PASS rows are green, FAIL rows are
    /// salmon. The log pane shows the runner's incremental messages; the
    /// "Export Markdown" button writes a publishable report.
    /// </summary>
    public partial class ValidationDialog : Window
    {
        private readonly CfdConfiguration _envConfig;
        private readonly List<ValidationReport> _reports = new();
        private readonly ObservableCollection<ReportRow> _rows = new();
        private readonly ObservableCollection<string> _files = new();

        public ValidationDialog() : this(new CfdConfiguration()) { }

        public ValidationDialog(CfdConfiguration envConfig)
        {
            _envConfig = envConfig ?? new CfdConfiguration();
            InitializeComponent();

            LstFiles.ItemsSource = _files;

            // Columns: benchmark name + 5 SPM metric cells + result. Each
            // metric cell colours itself via a per-row brush on the matching
            // *Brush property; we put the brush on the row, not the column,
            // because acceptance can differ per benchmark.
            GridResults.ItemsSource = _rows;
            AddTextColumn("Benchmark", nameof(ReportRow.Benchmark), 1, 220);
            AddMetricColumn("MRB",   nameof(ReportRow.MRB),   nameof(ReportRow.MRBBrush));
            AddMetricColumn("RMSE",  nameof(ReportRow.RMSE),  nameof(ReportRow.RMSEBrush));
            AddMetricColumn("FAC2",  nameof(ReportRow.FAC2),  nameof(ReportRow.FAC2Brush));
            AddMetricColumn("MG",    nameof(ReportRow.MG),    nameof(ReportRow.MGBrush));
            AddMetricColumn("VG",    nameof(ReportRow.VG),    nameof(ReportRow.VGBrush));
            AddMetricColumn("Result", nameof(ReportRow.Result), nameof(ReportRow.ResultBrush), 100);
        }

        private void AddTextColumn(string header, string path, double star, double absoluteWidth)
        {
            var col = new DataGridTextColumn
            {
                Header = header,
                Width = star > 0
                    ? new DataGridLength(star, DataGridLengthUnitType.Star)
                    : new DataGridLength(absoluteWidth),
                Binding = new Binding(path)
            };
            GridResults.Columns.Add(col);
        }

        /// <summary>Adds a column whose cell is colour-coded by binding the
        /// containing cell's Background to a per-row brush property. We do
        /// this via a DataGridTemplateColumn that wraps the text in a Border
        /// — DataGridTextColumn's cells don't expose a background binding
        /// that survives row recycling cleanly.</summary>
        private void AddMetricColumn(string header, string textPath, string brushPath,
            double absoluteWidth = 90)
        {
            var col = new DataGridTemplateColumn
            {
                Header = header,
                Width = new DataGridLength(absoluteWidth),
                CellTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<ReportRow>(
                    (row, _) =>
                    {
                        var tb = new TextBlock
                        {
                            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                            Padding = new global::Avalonia.Thickness(4, 0)
                        };
                        tb.Bind(TextBlock.TextProperty, new Binding(textPath));
                        var border = new Border { Child = tb };
                        border.Bind(Border.BackgroundProperty, new Binding(brushPath));
                        return border;
                    })
            };
            GridResults.Columns.Add(col);
        }

        // ── File list management ─────────────────────────────────────────────
        private async void BtnAdd_Click(object? sender, RoutedEventArgs e)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select benchmark files",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("DisperSim Benchmarks")
                    {
                        Patterns = new[] { "*.dsbench" }
                    },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                }
            });
            if (files == null) return;
            foreach (var f in files)
            {
                string path = f.TryGetLocalPath() ?? f.Path.LocalPath;
                if (!_files.Contains(path)) _files.Add(path);
            }
        }

        private void BtnRemove_Click(object? sender, RoutedEventArgs e)
        {
            var sel = LstFiles.SelectedItems?.Cast<string>().ToList() ?? new List<string>();
            foreach (var s in sel) _files.Remove(s);
        }

        private void BtnClear_Click(object? sender, RoutedEventArgs e)
        {
            _files.Clear();
            _reports.Clear();
            _rows.Clear();
            TxtLog.Text = "";
        }

        // ── Run ──────────────────────────────────────────────────────────────
        private async void BtnRun_Click(object? sender, RoutedEventArgs e)
        {
            if (_files.Count == 0)
            {
                AppendLog("Add at least one .dsbench file first.");
                return;
            }

            _reports.Clear();
            _rows.Clear();
            TxtLog.Text = "";
            BtnRun.IsEnabled = false;
            try
            {
                // Run sequentially on the thread pool so the UI thread stays
                // responsive; progress callbacks marshal back via Dispatcher.
                foreach (var path in _files.ToList())
                {
                    AppendLog("=== " + Path.GetFileName(path) + " ===");
                    BenchmarkSpec? spec = null;
                    string? loadErr = null;
                    try { spec = await Task.Run(() => BenchmarkLoader.Load(path)); }
                    catch (Exception ex) { loadErr = ex.Message; }

                    if (spec is null)
                    {
                        AppendLog("Failed to load: " + (loadErr ?? "(unknown)"));
                        _reports.Add(new ValidationReport
                        {
                            Success = false,
                            ErrorMessage = loadErr ?? "load failed",
                            Benchmark = new BenchmarkSpec { Name = Path.GetFileName(path) }
                        });
                        continue;
                    }

                    var report = await Task.Run(() =>
                        ValidationRunner.Run(spec, _envConfig,
                            m => Dispatcher.UIThread.Post(() => AppendLog(m))));
                    _reports.Add(report);
                    AppendLog(report.Pass ? "PASS"
                        : (report.Success ? "FAIL" : "ERROR: " + report.ErrorMessage));
                }
                RefreshGrid();
            }
            finally
            {
                BtnRun.IsEnabled = true;
            }
        }

        private void RefreshGrid()
        {
            _rows.Clear();
            foreach (var r in _reports)
            {
                if (r.Spm == null)
                {
                    _rows.Add(new ReportRow
                    {
                        Benchmark = r.Benchmark?.Name ?? "(unnamed)",
                        MRB = "—", RMSE = "—", FAC2 = "—", MG = "—", VG = "—",
                        Result = r.Success ? "no SPM" : "ERROR",
                        ResultBrush = Brushes.LightGray,
                        MRBBrush = Brushes.LightGray,
                        RMSEBrush = Brushes.LightGray,
                        FAC2Brush = Brushes.LightGray,
                        MGBrush = Brushes.LightGray,
                        VGBrush = Brushes.LightGray
                    });
                    continue;
                }

                var acc = r.Benchmark?.Acceptance;
                _rows.Add(new ReportRow
                {
                    Benchmark = r.Benchmark?.Name ?? "(unnamed)",
                    MRB  = Fmt(r.Spm.MRB),
                    RMSE = Fmt(r.Spm.RMSE),
                    FAC2 = Fmt(r.Spm.FAC2),
                    MG   = Fmt(r.Spm.MG),
                    VG   = Fmt(r.Spm.VG),
                    Result = r.Pass ? "PASS" : "FAIL",
                    MRBBrush  = MetricBrush(r.Spm.MRB,  acc?.MRB),
                    RMSEBrush = MetricBrush(r.Spm.RMSE, acc?.RMSE),
                    FAC2Brush = MetricBrush(r.Spm.FAC2, acc?.FAC2),
                    MGBrush   = MetricBrush(r.Spm.MG,   acc?.MG),
                    VGBrush   = MetricBrush(r.Spm.VG,   acc?.VG),
                    ResultBrush = r.Pass ? Brushes.LightGreen : Brushes.LightSalmon
                });
            }
        }

        private static IBrush MetricBrush(double value, MetricRange? range)
        {
            if (range is null) return Brushes.LightGray;
            return range.Accepts(value) ? Brushes.LightGreen : Brushes.LightSalmon;
        }

        private static string Fmt(double v) =>
            double.IsNaN(v) ? "NaN" : v.ToString("G5", CultureInfo.InvariantCulture);

        // ── Markdown export ──────────────────────────────────────────────────
        private async void BtnExport_Click(object? sender, RoutedEventArgs e)
        {
            if (_reports.Count == 0)
            {
                AppendLog("Run at least one benchmark first.");
                return;
            }

            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save validation report",
                SuggestedFileName = "validation-report.md",
                DefaultExtension = "md",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Markdown") { Patterns = new[] { "*.md" } }
                }
            });
            if (file is null) return;

            string path = file.TryGetLocalPath() ?? file.Path.LocalPath;
            var sb = new StringBuilder();
            sb.AppendLine("# Validation report");
            sb.AppendLine();
            sb.AppendLine("Generated " + DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
            sb.AppendLine();
            foreach (var r in _reports)
            {
                sb.AppendLine(r.ToMarkdown());
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
            try
            {
                File.WriteAllText(path, sb.ToString());
                AppendLog("Report saved to " + path);
            }
            catch (Exception ex)
            {
                AppendLog("Export failed: " + ex.Message);
            }
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e) => Close(true);

        private void AppendLog(string? msg)
        {
            if (msg == null) return;
            TxtLog.Text = (TxtLog.Text ?? "") + msg + Environment.NewLine;
            TxtLog.CaretIndex = TxtLog.Text.Length;
        }

        /// <summary>Row backing for the SPM grid. Each metric column reads
        /// both a text value and a brush so cells can colour themselves
        /// independently — keeps the colour logic in C# instead of bleeding
        /// into XAML triggers.</summary>
        public sealed class ReportRow
        {
            public string Benchmark { get; set; } = "";
            public string MRB { get; set; } = "";
            public string RMSE { get; set; } = "";
            public string FAC2 { get; set; } = "";
            public string MG { get; set; } = "";
            public string VG { get; set; } = "";
            public string Result { get; set; } = "";
            public IBrush MRBBrush { get; set; } = Brushes.Transparent;
            public IBrush RMSEBrush { get; set; } = Brushes.Transparent;
            public IBrush FAC2Brush { get; set; } = Brushes.Transparent;
            public IBrush MGBrush { get; set; } = Brushes.Transparent;
            public IBrush VGBrush { get; set; } = Brushes.Transparent;
            public IBrush ResultBrush { get; set; } = Brushes.Transparent;
        }
    }
}
