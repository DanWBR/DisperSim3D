using System;
using System.Collections.Generic;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// Hanna Statistical Performance Measures (Chang &amp; Hanna 2004; Vu 2019 §1.4.2).
    ///   MRB  = 2·mean((Co - Cp) / (Co + Cp))                  ideal 0
    ///   NMSE = mean((Co - Cp)²) / (mean(Co)·mean(Cp))         ideal 0
    ///   RMSE = sqrt(mean((Co - Cp)²)) / mean(Co)              ideal 0  (normalised RMSE)
    ///   FAC2 = fraction of pairs with 0.5 ≤ Cp/Co ≤ 2.0       ideal 1
    ///   MG   = exp(mean(ln Co - ln Cp))                       ideal 1
    ///   VG   = exp(var(ln Co - ln Cp))                        ideal 1
    ///
    /// Pure function: no allocations besides the report, no I/O. Zeros are floored to a
    /// small epsilon when feeding the geometric (log-based) metrics so they don't produce
    /// NaN — the dataset should ideally not contain hard zeros.
    /// </summary>
    public static class SpmCalculator
    {
        private const double LogFloor = 1e-12;

        public static SpmReport Compute(IList<SensorPair> pairs)
        {
            var report = new SpmReport { Pairs = pairs as List<SensorPair> ?? new List<SensorPair>(pairs) };
            int n = pairs.Count;
            report.N = n;
            if (n == 0) return report;

            double sumMRB = 0;
            double sumSq = 0;
            double sumCo = 0;
            double sumCp = 0;
            double sumLogDiff = 0;
            int fac2Hits = 0;
            var logDiffs = new double[n];

            for (int i = 0; i < n; i++)
            {
                double co = pairs[i].Observed;
                double cp = pairs[i].Predicted;

                double denom = co + cp;
                if (denom != 0) sumMRB += 2.0 * (co - cp) / denom;

                double diff = co - cp;
                sumSq += diff * diff;
                sumCo += co;
                sumCp += cp;

                if (co > 0 && 0.5 * co <= cp && cp <= 2.0 * co) fac2Hits++;

                double lcO = Math.Log(Math.Max(co, LogFloor));
                double lcP = Math.Log(Math.Max(cp, LogFloor));
                double ld = lcO - lcP;
                sumLogDiff += ld;
                logDiffs[i] = ld;
            }

            double meanCo = sumCo / n;
            double meanCp = sumCp / n;

            report.MRB = sumMRB / n;
            report.NMSE = (meanCo > 0 && meanCp > 0) ? (sumSq / n) / (meanCo * meanCp) : double.NaN;
            report.RMSE = meanCo > 0 ? Math.Sqrt(sumSq / n) / meanCo : double.NaN;
            report.FAC2 = (double)fac2Hits / n;

            double meanLog = sumLogDiff / n;
            double varLog = 0;
            for (int i = 0; i < n; i++)
            {
                double d = logDiffs[i] - meanLog;
                varLog += d * d;
            }
            varLog /= n;

            report.MG = Math.Exp(meanLog);
            report.VG = Math.Exp(varLog);
            return report;
        }
    }
}
