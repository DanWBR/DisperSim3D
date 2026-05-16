namespace DisperSim3D.Validation
{
    /// <summary>
    /// Acceptance ranges per Hanna SPM. Defaults match the consensus ranges from
    /// Chang &amp; Hanna 2004 / Vu 2019 §1.4.2:
    ///   MRB ∈ [-0.4, 0.4],  RMSE &lt; 2.3,  FAC2 ∈ [0.5, 2.0],
    ///   MG  ∈ [0.67, 1.5],  VG &lt; 3.3.
    /// </summary>
    public class BenchmarkAcceptance
    {
        public MetricRange MRB { get; set; } = new MetricRange { Min = -0.4, Max = 0.4 };
        public MetricRange RMSE { get; set; } = new MetricRange { Max = 2.3 };
        public MetricRange FAC2 { get; set; } = new MetricRange { Min = 0.5, Max = 2.0 };
        public MetricRange MG { get; set; } = new MetricRange { Min = 0.67, Max = 1.5 };
        public MetricRange VG { get; set; } = new MetricRange { Max = 3.3 };

        /// <summary>
        /// Acceptance range for the ratio PredictedCloudVolume / ExpectedCloudVolume.
        /// Only evaluated when <see cref="BenchmarkSpec.ExpectedCloudVolumeM3"/> is set.
        /// Typical range: [0.5, 2.0] (FAC2-equivalent) or tighter [0.8, 1.2] for CFD comparison.
        /// </summary>
        public MetricRange CloudVolumeRatio { get; set; }

        /// <summary>When the bench declares a <see cref="BenchmarkSpec.ReferenceModelSpms"/>
        /// block, this tolerance defines what "matching the reference" means.
        /// A bench PASSES the reference-match check when, for every metric the
        /// reference paper reports, the DisperSim 3D value is no worse than the
        /// reference value plus the tolerance. "No worse than" depends on the
        /// metric:
        /// <list type="bullet">
        /// <item>FAC2: DisperSim ≥ reference - tolerance (higher is better).</item>
        /// <item>|MRB|, |log MG|: DisperSim absolute deviation from perfect is
        ///   at most (reference deviation) + tolerance (closer to 0 / 1 is better).</item>
        /// <item>RMSE, VG: DisperSim ≤ reference + tolerance (lower is better).</item>
        /// </list>
        /// When this block is absent, the bench falls back to the standard
        /// per-metric Min/Max ranges above.</summary>
        public ReferenceMatchTolerance ReferenceMatchTolerance { get; set; }
    }

    /// <summary>Tolerances for the "match reference model" acceptance mode.
    /// Each tolerance is additive against the reference paper's SPM value:
    /// DisperSim's metric is allowed to be at most this much worse than what
    /// the reference model achieved on the same experiment.</summary>
    public class ReferenceMatchTolerance
    {
        public double MRB { get; set; } = 0.2;
        public double RMSE { get; set; } = 0.5;
        public double FAC2 { get; set; } = 0.15;
        public double MG { get; set; } = 0.3;
        public double VG { get; set; } = 0.5;
    }

    public class MetricRange
    {
        public double? Min { get; set; }
        public double? Max { get; set; }

        public bool Accepts(double value)
        {
            if (Min.HasValue && value < Min.Value) return false;
            if (Max.HasValue && value > Max.Value) return false;
            return true;
        }
    }
}
