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

        /// <summary>True when EVERY metric falls inside its acceptance range.</summary>
        public bool AllPass(BenchmarkAcceptance acc)
        {
            if (acc == null) return true;
            return acc.MRB.Accepts(MRB)
                && acc.RMSE.Accepts(RMSE)
                && acc.FAC2.Accepts(FAC2)
                && acc.MG.Accepts(MG)
                && acc.VG.Accepts(VG);
        }
    }

    public class SensorPair
    {
        public string Name { get; set; }
        public double Predicted { get; set; }
        public double Observed { get; set; }
    }
}
