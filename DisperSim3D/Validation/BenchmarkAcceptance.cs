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
