using System;
using System.Globalization;
using System.Text;

namespace DisperSim3D.Validation
{
    public class ValidationReport
    {
        public BenchmarkSpec Benchmark { get; set; }
        public SpmReport Spm { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string CasePath { get; set; }
        public TimeSpan RunDuration { get; set; }

        /// <summary>Convenience: true when the run completed AND every SPM passed.</summary>
        public bool Pass
        {
            get
            {
                if (!Success || Spm == null) return false;
                return Spm.AllPass(Benchmark != null ? Benchmark.Acceptance : null);
            }
        }

        /// <summary>Render a human-readable Markdown summary.</summary>
        public string ToMarkdown()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("# Validation report — ").AppendLine(Benchmark != null ? Benchmark.Name : "(unnamed)");
            sb.AppendLine();
            if (Benchmark != null)
            {
                sb.Append("Citation: ").AppendLine(Benchmark.Citation ?? "");
                sb.Append("Description: ").AppendLine(Benchmark.Description ?? "");
                sb.Append("Solver: ").AppendLine(Benchmark.Solver ?? "");
                sb.Append("Concentration kind: ").AppendLine(Benchmark.ConcentrationKind ?? "");
                sb.Append("Unit: ").AppendLine(Benchmark.Unit ?? "");
            }
            sb.AppendLine();
            sb.Append("Status: ").AppendLine(Success ? (Pass ? "PASS" : "FAIL") : "ERROR");
            if (!string.IsNullOrEmpty(ErrorMessage))
                sb.Append("Error: ").AppendLine(ErrorMessage);
            if (!string.IsNullOrEmpty(CasePath))
                sb.Append("Case path: ").AppendLine(CasePath);
            sb.Append("Run duration: ").AppendLine(RunDuration.ToString("c"));
            sb.AppendLine();

            if (Spm != null)
            {
                sb.AppendLine("## Statistical Performance Measures");
                sb.AppendLine();
                sb.AppendLine("| Metric | Value | Range | Pass |");
                sb.AppendLine("|---|---:|---|:-:|");
                AppendRow(sb, "MRB", Spm.MRB, Benchmark?.Acceptance?.MRB);
                AppendRow(sb, "RMSE", Spm.RMSE, Benchmark?.Acceptance?.RMSE);
                AppendRow(sb, "NMSE", Spm.NMSE, null);
                AppendRow(sb, "FAC2", Spm.FAC2, Benchmark?.Acceptance?.FAC2);
                AppendRow(sb, "MG", Spm.MG, Benchmark?.Acceptance?.MG);
                AppendRow(sb, "VG", Spm.VG, Benchmark?.Acceptance?.VG);

                if (!double.IsNaN(Spm.CloudVolumeRatio))
                {
                    sb.AppendLine();
                    sb.AppendLine("## Cloud Volume");
                    sb.AppendLine();
                    sb.AppendFormat(inv, "| Metric | Value | Range | Pass |\n");
                    sb.AppendLine("|---|---:|---|:-:|");
                    sb.AppendFormat(inv, "| Predicted | {0:G6} m³ | — | — |\n", Spm.PredictedCloudVolumeM3);
                    sb.AppendFormat(inv, "| Expected | {0:G6} m³ | — | — |\n", Spm.ExpectedCloudVolumeM3);
                    AppendRow(sb, "Ratio (P/E)", Spm.CloudVolumeRatio, Benchmark?.Acceptance?.CloudVolumeRatio);
                }

                sb.AppendLine();
                sb.AppendLine("## Per-sensor pairs");
                sb.AppendLine();
                sb.AppendLine("| Sensor | Predicted | Observed | Cp/Co |");
                sb.AppendLine("|---|---:|---:|---:|");
                foreach (var p in Spm.Pairs)
                {
                    double ratio = p.Observed != 0 ? p.Predicted / p.Observed : double.NaN;
                    sb.AppendFormat(inv, "| {0} | {1:G6} | {2:G6} | {3:G4} |\n",
                        p.Name, p.Predicted, p.Observed, ratio);
                }
            }
            return sb.ToString();
        }

        private static void AppendRow(StringBuilder sb, string name, double value, MetricRange range)
        {
            string rangeStr = range == null
                ? "—"
                : (range.Min.HasValue ? range.Min.Value.ToString("G4", CultureInfo.InvariantCulture) : "−∞")
                  + " … "
                  + (range.Max.HasValue ? range.Max.Value.ToString("G4", CultureInfo.InvariantCulture) : "+∞");
            string pass = range == null ? "—" : (range.Accepts(value) ? "✓" : "✗");
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "| {0} | {1:G6} | {2} | {3} |\n", name, value, rangeStr, pass);
        }
    }
}
