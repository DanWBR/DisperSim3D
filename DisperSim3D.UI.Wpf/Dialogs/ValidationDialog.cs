using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DisperSim3D.Models;
using DisperSim3D.Validation;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Picks one or more .dsbench files, runs each via <see cref="ValidationRunner"/>, and
    /// shows a colour-coded SPM table. Pass = green, fail = red.
    /// </summary>
    public class ValidationDialog : Form
    {
        private readonly CfdConfiguration _envConfig;
        private ListBox _lstFiles;
        private DataGridView _grid;
        private TextBox _txtLog;
        private Button _btnAdd, _btnRemove, _btnClear, _btnRun, _btnExport;

        private readonly List<ValidationReport> _reports = new List<ValidationReport>();

        public ValidationDialog(CfdConfiguration envConfig)
        {
            _envConfig = envConfig;
            BuildUI();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            this.Text = "Validate against Benchmarks";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size((int)(820 * dpi), (int)(560 * dpi));
            this.Size = new Size((int)(960 * dpi), (int)(680 * dpi));

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));     // header
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));      // file list + buttons
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));     // run button
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));      // grid
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));     // log + bottom buttons
            this.Controls.Add(root);

            var lblHeader = new Label
            {
                Text = "Pick one or more .dsbench files. Each is run end-to-end and scored against the published acceptance ranges (Hanna SPMs).",
                Dock = DockStyle.Fill,
                AutoSize = false
            };
            root.Controls.Add(lblHeader, 0, 0);

            // ── File list + add/remove ──
            var fileGroup = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            fileGroup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80));
            fileGroup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            _lstFiles = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended };
            fileGroup.Controls.Add(_lstFiles, 0, 0);

            var fileBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
            _btnAdd = new Button { Text = "Add benchmark...", AutoSize = true };
            _btnAdd.Click += (s, e) => AddBenchmarks();
            _btnRemove = new Button { Text = "Remove", AutoSize = true };
            _btnRemove.Click += (s, e) => RemoveSelected();
            _btnClear = new Button { Text = "Clear", AutoSize = true };
            _btnClear.Click += (s, e) => { _lstFiles.Items.Clear(); _reports.Clear(); RefreshGrid(); };
            fileBtns.Controls.Add(_btnAdd);
            fileBtns.Controls.Add(_btnRemove);
            fileBtns.Controls.Add(_btnClear);
            fileGroup.Controls.Add(fileBtns, 1, 0);
            root.Controls.Add(fileGroup, 0, 1);

            // ── Run button ──
            _btnRun = new Button { Text = "Run All", Dock = DockStyle.Fill, Font = new Font(this.Font, FontStyle.Bold) };
            _btnRun.Click += (s, e) => RunAll();
            root.Controls.Add(_btnRun, 0, 2);

            // ── Results grid ──
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _grid.Columns.Add("Bench", "Benchmark");
            _grid.Columns.Add("MRB", "MRB");
            _grid.Columns.Add("RMSE", "RMSE");
            _grid.Columns.Add("FAC2", "FAC2");
            _grid.Columns.Add("MG", "MG");
            _grid.Columns.Add("VG", "VG");
            _grid.Columns.Add("Result", "Result");
            root.Controls.Add(_grid, 0, 3);

            // ── Log + buttons row ──
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            _txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font(FontFamily.GenericMonospace, 8)
            };
            bottom.Controls.Add(_txtLog, 0, 0);

            var bottomBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
            _btnExport = new Button { Text = "Export Markdown...", AutoSize = true };
            _btnExport.Click += (s, e) => ExportMarkdown();
            var btnClose = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
            bottomBtns.Controls.Add(_btnExport);
            bottomBtns.Controls.Add(btnClose);
            bottom.Controls.Add(bottomBtns, 1, 0);
            root.Controls.Add(bottom, 0, 4);
        }

        private void AddBenchmarks()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "DisperSim Benchmarks (*.dsbench)|*.dsbench|All files (*.*)|*.*",
                Multiselect = true
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                foreach (var f in dlg.FileNames)
                    if (!_lstFiles.Items.Cast<string>().Contains(f))
                        _lstFiles.Items.Add(f);
            }
        }

        private void RemoveSelected()
        {
            var toRemove = _lstFiles.SelectedItems.Cast<object>().ToList();
            foreach (var item in toRemove) _lstFiles.Items.Remove(item);
        }

        private void RunAll()
        {
            if (_lstFiles.Items.Count == 0)
            {
                MessageBox.Show("Add at least one .dsbench file first.", "Validate",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _reports.Clear();
            _txtLog.Clear();
            _btnRun.Enabled = false;
            try
            {
                foreach (string path in _lstFiles.Items)
                {
                    AppendLog("=== " + Path.GetFileName(path) + " ===");
                    BenchmarkSpec spec;
                    try { spec = BenchmarkLoader.Load(path); }
                    catch (Exception ex)
                    {
                        AppendLog("Failed to load: " + ex.Message);
                        _reports.Add(new ValidationReport
                        {
                            Success = false,
                            ErrorMessage = ex.Message,
                            Benchmark = new BenchmarkSpec { Name = Path.GetFileName(path) }
                        });
                        continue;
                    }
                    var report = ValidationRunner.Run(spec, _envConfig, m => AppendLog(m));
                    _reports.Add(report);
                    AppendLog(report.Pass ? "PASS" : (report.Success ? "FAIL" : "ERROR: " + report.ErrorMessage));
                    Application.DoEvents();
                }
                RefreshGrid();
            }
            finally
            {
                _btnRun.Enabled = true;
            }
        }

        private void RefreshGrid()
        {
            _grid.Rows.Clear();
            foreach (var r in _reports)
            {
                int idx;
                if (r.Spm == null)
                {
                    idx = _grid.Rows.Add(r.Benchmark?.Name ?? "(unnamed)", "—", "—", "—", "—", "—",
                        r.Success ? "no SPM" : "ERROR");
                    _grid.Rows[idx].DefaultCellStyle.BackColor = Color.LightGray;
                    continue;
                }
                idx = _grid.Rows.Add(
                    r.Benchmark?.Name ?? "(unnamed)",
                    Fmt(r.Spm.MRB), Fmt(r.Spm.RMSE), Fmt(r.Spm.FAC2),
                    Fmt(r.Spm.MG), Fmt(r.Spm.VG),
                    r.Pass ? "PASS" : "FAIL");
                ColorCell(idx, 1, r.Spm.MRB, r.Benchmark.Acceptance?.MRB);
                ColorCell(idx, 2, r.Spm.RMSE, r.Benchmark.Acceptance?.RMSE);
                ColorCell(idx, 3, r.Spm.FAC2, r.Benchmark.Acceptance?.FAC2);
                ColorCell(idx, 4, r.Spm.MG, r.Benchmark.Acceptance?.MG);
                ColorCell(idx, 5, r.Spm.VG, r.Benchmark.Acceptance?.VG);
                _grid.Rows[idx].Cells[6].Style.BackColor = r.Pass ? Color.LightGreen : Color.LightSalmon;
            }
        }

        private static string Fmt(double v) =>
            double.IsNaN(v) ? "NaN" : v.ToString("G5", System.Globalization.CultureInfo.InvariantCulture);

        private void ColorCell(int row, int col, double value, MetricRange range)
        {
            if (range == null) { _grid.Rows[row].Cells[col].Style.BackColor = Color.LightGray; return; }
            _grid.Rows[row].Cells[col].Style.BackColor =
                range.Accepts(value) ? Color.LightGreen : Color.LightSalmon;
        }

        private void ExportMarkdown()
        {
            if (_reports.Count == 0)
            {
                MessageBox.Show("Run at least one benchmark first.", "Export",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dlg = new SaveFileDialog
            {
                Filter = "Markdown (*.md)|*.md|All files (*.*)|*.*",
                FileName = "validation-report.md"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                var sb = new StringBuilder();
                sb.AppendLine("# Validation report");
                sb.AppendLine();
                sb.AppendLine("Generated " + DateTime.Now.ToString("o"));
                sb.AppendLine();
                foreach (var r in _reports)
                {
                    sb.AppendLine(r.ToMarkdown());
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
                File.WriteAllText(dlg.FileName, sb.ToString());
                MessageBox.Show("Report saved to " + dlg.FileName, "Export",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void AppendLog(string msg)
        {
            if (msg == null) return;
            _txtLog.AppendText(msg + Environment.NewLine);
            _txtLog.SelectionStart = _txtLog.Text.Length;
            _txtLog.ScrollToCaret();
        }
    }
}
