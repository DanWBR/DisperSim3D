using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DisperSim3D.Validation
{
    public class FieldComparisonReport
    {
        public int Nx { get; set; }
        public int Ny { get; set; }
        public int Nz { get; set; }
        public long TotalCells { get; set; }
        public bool Normalized { get; set; }

        public List<TimestepComparison> Timesteps { get; set; } = new List<TimestepComparison>();

        public TimestepComparison Aggregate { get; set; }

        public string ToMarkdown()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("# Field-to-field comparison — FluidX3D vs OpenFOAM");
            if (Normalized)
                sb.AppendLine("(both fields normalized to [0, 1] per timestep)");
            sb.AppendLine();
            sb.AppendFormat(inv, "Grid: {0} × {1} × {2} = {3:N0} cells\n\n",
                Nx, Ny, Nz, TotalCells);

            sb.AppendLine("## Per-timestep metrics");
            sb.AppendLine();
            sb.AppendLine("| Time (s) | MAE | RMSE | NRMSE | R² | FAC2 | Peak Err | Non-zero |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var t in Timesteps)
            {
                sb.AppendFormat(inv,
                    "| {0:G6} | {1:E3} | {2:E3} | {3:F4} | {4:F4} | {5:F4} | {6:E3} | {7:N0} |\n",
                    t.Time, t.MAE, t.RMSE, t.NRMSE, t.R2, t.FAC2,
                    t.PeakAbsError, t.NonZeroCells);
            }

            if (Aggregate != null)
            {
                sb.AppendLine();
                sb.AppendLine("## Aggregate (all timesteps pooled)");
                sb.AppendLine();
                sb.AppendFormat(inv, "- MAE:  {0:E4}\n", Aggregate.MAE);
                sb.AppendFormat(inv, "- RMSE: {0:E4}\n", Aggregate.RMSE);
                sb.AppendFormat(inv, "- NRMSE: {0:F4}\n", Aggregate.NRMSE);
                sb.AppendFormat(inv, "- R²:   {0:F4}\n", Aggregate.R2);
                sb.AppendFormat(inv, "- FAC2: {0:F4}\n", Aggregate.FAC2);
                sb.AppendFormat(inv, "- Peak absolute error: {0:E4}\n", Aggregate.PeakAbsError);
                sb.AppendFormat(inv, "- Cells compared: {0:N0}\n", Aggregate.NonZeroCells);
            }

            return sb.ToString();
        }

        public string ToCsv()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("Time_s,MAE,RMSE,NRMSE,R2,FAC2,PeakAbsError,NonZeroCells,MeanA,MeanB,MaxA,MaxB");
            foreach (var t in Timesteps)
            {
                sb.AppendFormat(inv,
                    "{0:G},{1:E6},{2:E6},{3:G6},{4:G6},{5:G6},{6:E6},{7},{8:E6},{9:E6},{10:E6},{11:E6}\n",
                    t.Time, t.MAE, t.RMSE, t.NRMSE, t.R2, t.FAC2,
                    t.PeakAbsError, t.NonZeroCells,
                    t.MeanA, t.MeanB, t.MaxA, t.MaxB);
            }
            return sb.ToString();
        }
    }

    public class TimestepComparison
    {
        public double Time { get; set; }

        public double MAE { get; set; }
        public double RMSE { get; set; }
        public double NRMSE { get; set; }
        public double R2 { get; set; }
        public double FAC2 { get; set; }
        public double PeakAbsError { get; set; }

        public long NonZeroCells { get; set; }

        public double MeanA { get; set; }
        public double MeanB { get; set; }
        public double MaxA { get; set; }
        public double MaxB { get; set; }

        internal double _sumAB;
        internal double _sumA2;
        internal double _sumB2;
        internal long _fac2Count;
    }
}
