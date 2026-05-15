using System;
using System.Collections.Generic;
using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    /// <summary>
    /// Cell-by-cell comparison of two <see cref="OpenFoamResult"/> concentration
    /// fields (e.g. FluidX3D vs OpenFOAM). Both results must share the same grid
    /// dimensions (Nx, Ny, Nz). Timesteps are matched by nearest-neighbour within
    /// a configurable tolerance.
    /// </summary>
    public static class FieldComparer
    {
        private const double ConcentrationFloor = 1e-15;

        public static FieldComparisonReport Compare(
            OpenFoamResult a,
            OpenFoamResult b,
            double timeToleranceS = 0.5,
            double noiseFloor = 1e-12,
            bool normalize = false)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            int nx = a.GridNx, ny = a.GridNy, nz = a.GridNz;
            if (nx != b.GridNx || ny != b.GridNy || nz != b.GridNz)
                throw new InvalidOperationException(
                    $"Grid mismatch: A=({a.GridNx},{a.GridNy},{a.GridNz}) vs B=({b.GridNx},{b.GridNy},{b.GridNz})");

            long totalCells = (long)nx * ny * nz;
            var report = new FieldComparisonReport
            {
                Nx = nx, Ny = ny, Nz = nz, TotalCells = totalCells,
                Normalized = normalize
            };

            var matchedTimes = MatchTimesteps(a.TimeSteps, b.TimeSteps, timeToleranceS);
            if (matchedTimes.Count == 0)
                return report;

            double poolSumAE = 0, poolSumSq = 0;
            double poolSumA = 0, poolSumB = 0;
            double poolSumAB = 0, poolSumA2 = 0, poolSumB2 = 0;
            double poolPeak = 0;
            long poolFac2 = 0, poolN = 0;
            double poolMaxA = 0, poolMaxB = 0;

            foreach (var (tA, tB) in matchedTimes)
            {
                var fA = a.GetField(tA);
                var fB = b.GetField(tB);
                if (fA == null || fB == null) continue;

                if (normalize)
                {
                    fA = NormalizeField(fA, nx, ny, nz);
                    fB = NormalizeField(fB, nx, ny, nz);
                }

                var ts = CompareFields(fA, fB, nx, ny, nz, noiseFloor, tA);
                report.Timesteps.Add(ts);

                long n = ts.NonZeroCells;
                if (n == 0) continue;

                poolSumAE += ts.MAE * n;
                poolSumSq += ts.RMSE * ts.RMSE * n;
                poolSumA += ts.MeanA * n;
                poolSumB += ts.MeanB * n;
                poolSumAB += ts._sumAB;
                poolSumA2 += ts._sumA2;
                poolSumB2 += ts._sumB2;
                poolPeak = Math.Max(poolPeak, ts.PeakAbsError);
                poolFac2 += ts._fac2Count;
                poolN += n;
                poolMaxA = Math.Max(poolMaxA, ts.MaxA);
                poolMaxB = Math.Max(poolMaxB, ts.MaxB);
            }

            if (poolN > 0)
            {
                double meanA = poolSumA / poolN;
                double meanB = poolSumB / poolN;
                double rmse = Math.Sqrt(poolSumSq / poolN);
                double range = Math.Max(poolMaxA, poolMaxB);
                report.Aggregate = new TimestepComparison
                {
                    Time = double.NaN,
                    MAE = poolSumAE / poolN,
                    RMSE = rmse,
                    NRMSE = range > 0 ? rmse / range : double.NaN,
                    R2 = ComputeR2(poolSumAB, poolSumA2, poolSumB2, meanA, meanB, poolN),
                    FAC2 = (double)poolFac2 / poolN,
                    PeakAbsError = poolPeak,
                    NonZeroCells = poolN,
                    MeanA = meanA,
                    MeanB = meanB,
                    MaxA = poolMaxA,
                    MaxB = poolMaxB
                };
            }

            return report;
        }

        private static TimestepComparison CompareFields(
            double[,,] fA, double[,,] fB,
            int nx, int ny, int nz,
            double noiseFloor, double time)
        {
            double sumAE = 0, sumSq = 0;
            double sumA = 0, sumB = 0;
            double sumAB = 0, sumA2 = 0, sumB2 = 0;
            double peak = 0, maxA = 0, maxB = 0;
            long fac2 = 0, nonZero = 0;

            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            for (int k = 0; k < nz; k++)
            {
                double va = fA[i, j, k];
                double vb = fB[i, j, k];

                if (va < noiseFloor && vb < noiseFloor)
                    continue;

                nonZero++;
                double diff = va - vb;
                double absDiff = Math.Abs(diff);

                sumAE += absDiff;
                sumSq += diff * diff;
                sumA += va;
                sumB += vb;
                sumAB += va * vb;
                sumA2 += va * va;
                sumB2 += vb * vb;
                if (absDiff > peak) peak = absDiff;
                if (va > maxA) maxA = va;
                if (vb > maxB) maxB = vb;

                double floor = Math.Max(va, ConcentrationFloor);
                double ratio = vb / floor;
                if (ratio >= 0.5 && ratio <= 2.0) fac2++;
            }

            if (nonZero == 0)
            {
                return new TimestepComparison
                {
                    Time = time,
                    R2 = double.NaN,
                    NRMSE = double.NaN
                };
            }

            double meanA = sumA / nonZero;
            double meanB = sumB / nonZero;
            double rmse = Math.Sqrt(sumSq / nonZero);
            double range = Math.Max(maxA, maxB);

            var ts = new TimestepComparison
            {
                Time = time,
                MAE = sumAE / nonZero,
                RMSE = rmse,
                NRMSE = range > 0 ? rmse / range : double.NaN,
                R2 = ComputeR2(sumAB, sumA2, sumB2, meanA, meanB, nonZero),
                FAC2 = (double)fac2 / nonZero,
                PeakAbsError = peak,
                NonZeroCells = nonZero,
                MeanA = meanA,
                MeanB = meanB,
                MaxA = maxA,
                MaxB = maxB,
                _sumAB = sumAB,
                _sumA2 = sumA2,
                _sumB2 = sumB2,
                _fac2Count = fac2
            };
            return ts;
        }

        private static double ComputeR2(
            double sumAB, double sumA2, double sumB2,
            double meanA, double meanB, long n)
        {
            double covAB = sumAB / n - meanA * meanB;
            double varA = sumA2 / n - meanA * meanA;
            double varB = sumB2 / n - meanB * meanB;
            double denom = varA * varB;
            if (denom <= 0) return double.NaN;
            double r = covAB / Math.Sqrt(denom);
            return r * r;
        }

        private static List<(double tA, double tB)> MatchTimesteps(
            List<double> timesA, List<double> timesB, double tolerance)
        {
            var result = new List<(double, double)>();
            if (timesA == null || timesB == null) return result;

            var sortedB = timesB.OrderBy(t => t).ToList();
            foreach (double tA in timesA)
            {
                int idx = sortedB.BinarySearch(tA);
                if (idx < 0) idx = ~idx;

                double bestDist = double.MaxValue;
                double bestT = double.NaN;
                for (int probe = Math.Max(0, idx - 1);
                     probe <= Math.Min(sortedB.Count - 1, idx + 1);
                     probe++)
                {
                    double dist = Math.Abs(sortedB[probe] - tA);
                    if (dist < bestDist) { bestDist = dist; bestT = sortedB[probe]; }
                }

                if (bestDist <= tolerance)
                    result.Add((tA, bestT));
            }

            return result;
        }

        private static double[,,] NormalizeField(double[,,] field, int nx, int ny, int nz)
        {
            double max = 0;
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            for (int k = 0; k < nz; k++)
                if (field[i, j, k] > max) max = field[i, j, k];

            if (max <= 0) return field;

            var norm = new double[nx, ny, nz];
            double inv = 1.0 / max;
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            for (int k = 0; k < nz; k++)
                norm[i, j, k] = field[i, j, k] * inv;
            return norm;
        }
    }
}
