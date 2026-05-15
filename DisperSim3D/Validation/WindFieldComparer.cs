using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DisperSim3D.Models;

namespace DisperSim3D.Validation
{
    public static class WindFieldComparer
    {
        public static WindFieldComparisonReport Compare(
            WindField3D a, WindField3D b,
            double domainHalf, double domainHeight,
            double sourceX = 0, double sourceY = 0, double sourceZ = 0,
            int nSamples = 32)
        {
            var report = new WindFieldComparisonReport();

            double xMin = -domainHalf, xMax = domainHalf;
            double yMin = -domainHalf, yMax = domainHalf;
            double zMax = domainHeight;

            double sumDiffMag2 = 0;
            double sumMagA = 0, sumMagB = 0;
            double sumMagA2 = 0, sumMagB2 = 0, sumMagAB = 0;
            double sumCos = 0;
            int nTotal = 0, nFac2 = 0, nCosValid = 0;
            double sumDx2 = 0, sumDy2 = 0, sumDz2 = 0;

            for (int k = 0; k < nSamples; k++)
            {
                double z = (k + 0.5) / nSamples * zMax;
                for (int j = 0; j < nSamples; j++)
                {
                    double y = yMin + (j + 0.5) / nSamples * (yMax - yMin);
                    for (int i = 0; i < nSamples; i++)
                    {
                        double x = xMin + (i + 0.5) / nSamples * (xMax - xMin);

                        var va = a.Interpolate(x, y, z);
                        var vb = b.Interpolate(x, y, z);

                        double magA = Math.Sqrt(va.X * va.X + va.Y * va.Y + va.Z * va.Z);
                        double magB = Math.Sqrt(vb.X * vb.X + vb.Y * vb.Y + vb.Z * vb.Z);

                        double dx = va.X - vb.X;
                        double dy = va.Y - vb.Y;
                        double dz = va.Z - vb.Z;

                        sumDiffMag2 += dx * dx + dy * dy + dz * dz;
                        sumMagA += magA;
                        sumMagB += magB;
                        sumMagA2 += magA * magA;
                        sumMagB2 += magB * magB;
                        sumMagAB += magA * magB;
                        sumDx2 += dx * dx;
                        sumDy2 += dy * dy;
                        sumDz2 += dz * dz;

                        if (magA > 1e-6 && magB > 1e-6)
                        {
                            double dot = va.X * vb.X + va.Y * vb.Y + va.Z * vb.Z;
                            sumCos += dot / (magA * magB);
                            nCosValid++;
                        }

                        if (magA > 1e-6)
                        {
                            double ratio = magB / magA;
                            if (ratio >= 0.5 && ratio <= 2.0) nFac2++;
                        }

                        nTotal++;
                    }
                }
            }

            if (nTotal > 0)
            {
                double meanA = sumMagA / nTotal;
                double meanB = sumMagB / nTotal;
                double covAB = sumMagAB / nTotal - meanA * meanB;
                double varA = sumMagA2 / nTotal - meanA * meanA;
                double varB = sumMagB2 / nTotal - meanB * meanB;
                double denom = varA * varB;

                report.VectorRMSE = Math.Sqrt(sumDiffMag2 / nTotal);
                report.UxRMSE = Math.Sqrt(sumDx2 / nTotal);
                report.UyRMSE = Math.Sqrt(sumDy2 / nTotal);
                report.UzRMSE = Math.Sqrt(sumDz2 / nTotal);
                report.MeanMagnitudeA = meanA;
                report.MeanMagnitudeB = meanB;
                report.MagnitudeR2 = denom > 0 ? (covAB * covAB) / denom : double.NaN;
                report.DirectionCosineMean = nCosValid > 0 ? sumCos / nCosValid : double.NaN;
                report.FAC2 = (double)nFac2 / nTotal;
                report.FractionalBias = (meanA + meanB) > 0
                    ? 2.0 * (meanA - meanB) / (meanA + meanB) : 0;
                report.SampleCount = nTotal;
            }

            int nProfile = 64;

            report.CenterlineProfile = new List<WindProfilePoint>(nProfile);
            for (int i = 0; i < nProfile; i++)
            {
                double x = xMin + (i + 0.5) / nProfile * (xMax - xMin);
                var va = a.Interpolate(x, sourceY, sourceZ);
                var vb = b.Interpolate(x, sourceY, sourceZ);
                report.CenterlineProfile.Add(new WindProfilePoint
                {
                    Position = x,
                    UxA = va.X, UyA = va.Y, UzA = va.Z,
                    UxB = vb.X, UyB = vb.Y, UzB = vb.Z
                });
            }

            report.VerticalProfile = new List<WindProfilePoint>(nProfile);
            for (int k = 0; k < nProfile; k++)
            {
                double z = (k + 0.5) / nProfile * zMax;
                var va = a.Interpolate(sourceX, sourceY, z);
                var vb = b.Interpolate(sourceX, sourceY, z);
                report.VerticalProfile.Add(new WindProfilePoint
                {
                    Position = z,
                    UxA = va.X, UyA = va.Y, UzA = va.Z,
                    UxB = vb.X, UyB = vb.Y, UzB = vb.Z
                });
            }

            return report;
        }
    }

    public class WindFieldComparisonReport
    {
        public double VectorRMSE;
        public double UxRMSE, UyRMSE, UzRMSE;
        public double MeanMagnitudeA, MeanMagnitudeB;
        public double MagnitudeR2;
        public double DirectionCosineMean;
        public double FractionalBias;
        public double FAC2;
        public int SampleCount;

        public List<WindProfilePoint> CenterlineProfile;
        public List<WindProfilePoint> VerticalProfile;

        public string ToMarkdown()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("# Wind field comparison — A vs B");
            sb.AppendLine();
            sb.AppendFormat(inv, "Sampled {0:N0} points\n\n", SampleCount);

            sb.AppendLine("## Global metrics");
            sb.AppendLine();
            sb.AppendFormat(inv, "- Mean |U| (A): {0:F4} m/s\n", MeanMagnitudeA);
            sb.AppendFormat(inv, "- Mean |U| (B): {0:F4} m/s\n", MeanMagnitudeB);
            sb.AppendFormat(inv, "- Fractional bias: {0:F4}\n", FractionalBias);
            sb.AppendFormat(inv, "- Vector RMSE: {0:F4} m/s\n", VectorRMSE);
            sb.AppendFormat(inv, "- Component RMSE: Ux={0:F4}  Uy={1:F4}  Uz={2:F4} m/s\n",
                UxRMSE, UyRMSE, UzRMSE);
            sb.AppendFormat(inv, "- |U| R²: {0:F4}\n", MagnitudeR2);
            sb.AppendFormat(inv, "- Direction cosine (mean): {0:F4}\n", DirectionCosineMean);
            sb.AppendFormat(inv, "- FAC2 (magnitude): {0:F4}\n", FAC2);

            if (CenterlineProfile != null && CenterlineProfile.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Centerline profile (y=src, z=src)");
                sb.AppendLine();
                sb.AppendLine("| x (m) | |U|_A | |U|_B | Ux_A | Ux_B | Uy_A | Uy_B |");
                sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|");
                foreach (var p in CenterlineProfile)
                {
                    double magA = Math.Sqrt(p.UxA * p.UxA + p.UyA * p.UyA + p.UzA * p.UzA);
                    double magB = Math.Sqrt(p.UxB * p.UxB + p.UyB * p.UyB + p.UzB * p.UzB);
                    sb.AppendFormat(inv,
                        "| {0:F2} | {1:F4} | {2:F4} | {3:F4} | {4:F4} | {5:F4} | {6:F4} |\n",
                        p.Position, magA, magB, p.UxA, p.UxB, p.UyA, p.UyB);
                }
            }

            if (VerticalProfile != null && VerticalProfile.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Vertical profile (x=src, y=src)");
                sb.AppendLine();
                sb.AppendLine("| z (m) | |U|_A | |U|_B | Ux_A | Ux_B | Uy_A | Uy_B |");
                sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|");
                foreach (var p in VerticalProfile)
                {
                    double magA = Math.Sqrt(p.UxA * p.UxA + p.UyA * p.UyA + p.UzA * p.UzA);
                    double magB = Math.Sqrt(p.UxB * p.UxB + p.UyB * p.UyB + p.UzB * p.UzB);
                    sb.AppendFormat(inv,
                        "| {0:F3} | {1:F4} | {2:F4} | {3:F4} | {4:F4} | {5:F4} | {6:F4} |\n",
                        p.Position, magA, magB, p.UxA, p.UxB, p.UyA, p.UyB);
                }
            }

            return sb.ToString();
        }

        public string ProfileToCsv()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("Section,Position_m,UxA,UyA,UzA,UxB,UyB,UzB,MagA,MagB");
            if (CenterlineProfile != null)
                foreach (var p in CenterlineProfile)
                {
                    double magA = Math.Sqrt(p.UxA * p.UxA + p.UyA * p.UyA + p.UzA * p.UzA);
                    double magB = Math.Sqrt(p.UxB * p.UxB + p.UyB * p.UyB + p.UzB * p.UzB);
                    sb.AppendFormat(inv, "centerline,{0:G},{1:G6},{2:G6},{3:G6},{4:G6},{5:G6},{6:G6},{7:G6},{8:G6}\n",
                        p.Position, p.UxA, p.UyA, p.UzA, p.UxB, p.UyB, p.UzB, magA, magB);
                }
            if (VerticalProfile != null)
                foreach (var p in VerticalProfile)
                {
                    double magA = Math.Sqrt(p.UxA * p.UxA + p.UyA * p.UyA + p.UzA * p.UzA);
                    double magB = Math.Sqrt(p.UxB * p.UxB + p.UyB * p.UyB + p.UzB * p.UzB);
                    sb.AppendFormat(inv, "vertical,{0:G},{1:G6},{2:G6},{3:G6},{4:G6},{5:G6},{6:G6},{7:G6},{8:G6}\n",
                        p.Position, p.UxA, p.UyA, p.UzA, p.UxB, p.UyB, p.UzB, magA, magB);
                }
            return sb.ToString();
        }
    }

    public class WindProfilePoint
    {
        public double Position;
        public double UxA, UyA, UzA;
        public double UxB, UyB, UzB;
    }
}
