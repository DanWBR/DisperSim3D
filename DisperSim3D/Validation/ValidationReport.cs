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

        /// <summary>Convenience: true when the run completed AND the engine
        /// matched the validation criteria. When the bench declares a
        /// <see cref="BenchmarkSpec.ReferenceModelSpms"/> block, PASS means
        /// "DisperSim 3D's SPMs are no worse than the cited reference model
        /// (FLACS / PHAST / etc.) achieved on the same experiment, within
        /// tolerance". Otherwise PASS falls back to the standard Hanna
        /// acceptance ranges in <see cref="BenchmarkSpec.Acceptance"/>.</summary>
        public bool Pass
        {
            get
            {
                if (!Success || Spm == null) return false;
                if (Benchmark?.ReferenceModelSpms != null
                    && Benchmark.Acceptance?.ReferenceMatchTolerance != null)
                {
                    var match = Spm.MatchesReference(
                        Benchmark.ReferenceModelSpms,
                        Benchmark.Acceptance.ReferenceMatchTolerance);
                    if (!match.Pass) return false;
                    // Cloud volume (if applicable) still uses the standard range.
                    if (Benchmark.Acceptance.CloudVolumeRatio != null
                        && !double.IsNaN(Spm.CloudVolumeRatio))
                        return Benchmark.Acceptance.CloudVolumeRatio.Accepts(Spm.CloudVolumeRatio);
                    return true;
                }
                return Spm.AllPass(Benchmark != null ? Benchmark.Acceptance : null);
            }
        }

        /// <summary>
        /// True when the observed values in the bench were never checked against a
        /// source, so neither a pass nor a failure here says anything about the model.
        /// </summary>
        public bool IsUnverified =>
            string.Equals(Benchmark?.DataConfidence, "Unverified",
                StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether this bench counts towards the headline score. An unverified bench
        /// is run and printed but never counted: a green tick against numbers nobody
        /// confirmed is worse than having no test, and a red one blames the model for
        /// the benchmark's own missing data.
        /// </summary>
        public bool Counted => Success && !IsUnverified;

        /// <summary>
        /// One word for how far this bench's evidence reaches: measurement, the
        /// engine's own previous output, or its own analytical solution.
        /// </summary>
        public string EvidenceClass
        {
            get
            {
                string c = Benchmark?.DataConfidence ?? "";
                if (string.Equals(c, "RegressionBaseline", StringComparison.OrdinalIgnoreCase))
                    return "regression baseline";
                if (string.Equals(c, "SelfConsistency", StringComparison.OrdinalIgnoreCase))
                    return "self-consistency";
                if (string.Equals(c, "Unverified", StringComparison.OrdinalIgnoreCase))
                    return "unverified";
                return "field trial";
            }
        }

        /// <summary>The reason (failing metric) when <see cref="Pass"/> is false
        /// due to reference-match. Empty when not applicable.</summary>
        public string FailureReason
        {
            get
            {
                if (Spm == null || Benchmark?.ReferenceModelSpms == null
                    || Benchmark.Acceptance?.ReferenceMatchTolerance == null) return "";
                var match = Spm.MatchesReference(
                    Benchmark.ReferenceModelSpms,
                    Benchmark.Acceptance.ReferenceMatchTolerance);
                return match.Reason ?? "";
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
