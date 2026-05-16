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

        /// <summary>Checks whether DisperSim 3D's SPMs are no worse than the
        /// reference model achieved on the same experiment (within the declared
        /// tolerance). The acceptance direction is metric-specific:
        ///   FAC2: higher is better (ours at least reference - tolerance).
        ///   |MRB|: smaller is better (ours abs at most |reference| + tolerance).
        ///   MG: closer to 1 in log space (ours |log MG| at most |log ref MG| + tol).
        ///   VG, RMSE: smaller is better (ours at most reference + tolerance).
        /// Returns (pass, reason); reason describes the failing metric on FAIL.</summary>
        public (bool Pass, string Reason) MatchesReference(
            BenchmarkReferenceModelSpms reference,
            ReferenceMatchTolerance tol)
        {
            if (reference == null || tol == null) return (true, "");

            if (reference.FAC2.HasValue)
            {
                double threshold = reference.FAC2.Value - tol.FAC2;
                if (FAC2 < threshold)
                    return (false, string.Format(
                        "FAC2 = {0:F3} below reference {1:F3} - {2:F2} = {3:F3}",
                        FAC2, reference.FAC2.Value, tol.FAC2, threshold));
            }

            if (reference.MRB.HasValue)
            {
                double allowed = System.Math.Abs(reference.MRB.Value) + tol.MRB;
                if (System.Math.Abs(MRB) > allowed)
                    return (false, string.Format(
                        "|MRB| = {0:F3} above |reference| {1:F3} + {2:F2} = {3:F3}",
                        System.Math.Abs(MRB), System.Math.Abs(reference.MRB.Value),
                        tol.MRB, allowed));
            }

            if (reference.MG.HasValue && MG > 0 && reference.MG.Value > 0)
            {
                double oursDev = System.Math.Abs(System.Math.Log(MG));
                double refDev = System.Math.Abs(System.Math.Log(reference.MG.Value));
                if (oursDev > refDev + tol.MG)
                    return (false, string.Format(
                        "|log(MG)| = {0:F3} above |log(ref MG)| {1:F3} + {2:F2} = {3:F3}",
                        oursDev, refDev, tol.MG, refDev + tol.MG));
            }

            if (reference.VG.HasValue)
            {
                double allowed = reference.VG.Value + tol.VG;
                if (VG > allowed)
                    return (false, string.Format(
                        "VG = {0:F3} above reference {1:F3} + {2:F2} = {3:F3}",
                        VG, reference.VG.Value, tol.VG, allowed));
            }

            if (reference.RMSE.HasValue)
            {
                double allowed = reference.RMSE.Value + tol.RMSE;
                if (RMSE > allowed)
                    return (false, string.Format(
                        "RMSE = {0:F3} above reference {1:F3} + {2:F2} = {3:F3}",
                        RMSE, reference.RMSE.Value, tol.RMSE, allowed));
            }

            return (true, "");
        }
    }

    public class SensorPair
    {
        public string Name { get; set; }
        public double Predicted { get; set; }
        public double Observed { get; set; }
    }
}
