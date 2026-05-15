using System.Collections.Generic;

namespace DisperSim3D.Validation
{
    /// <summary>Hanna Statistical Performance Measures result for one (predicted, observed) pair set.</summary>
    public class SpmReport
    {
        public int N { get; set; }

        public double MRB { get; set; }
        public double RMSE { get; set; }
        public double NMSE { get; set; }
        public double FAC2 { get; set; }
        public double MG { get; set; }
        public double VG { get; set; }

        public List<SensorPair> Pairs { get; set; } = new List<SensorPair>();

        /// <summary>Predicted flammable cloud volume (m³), set when the benchmark specifies ExpectedCloudVolumeM3.</summary>
        public double PredictedCloudVolumeM3 { get; set; }
        /// <summary>Expected flammable cloud volume from the benchmark (m³).</summary>
        public double ExpectedCloudVolumeM3 { get; set; }
        /// <summary>Ratio Predicted / Expected. NaN when expected is zero or not specified.</summary>
        public double CloudVolumeRatio { get; set; } = double.NaN;

        /// <summary>True when EVERY metric falls inside its acceptance range.</summary>
        public bool AllPass(BenchmarkAcceptance acc)
        {
            if (acc == null) return true;
            bool spmOk = acc.MRB.Accepts(MRB)
                && acc.RMSE.Accepts(RMSE)
                && acc.FAC2.Accepts(FAC2)
                && acc.MG.Accepts(MG)
                && acc.VG.Accepts(VG);
            if (!spmOk) return false;
            if (acc.CloudVolumeRatio != null && !double.IsNaN(CloudVolumeRatio))
                return acc.CloudVolumeRatio.Accepts(CloudVolumeRatio);
            return true;
        }
    }

    public class SensorPair
    {
        public string Name { get; set; }
        public double Predicted { get; set; }
        public double Observed { get; set; }
    }
}
