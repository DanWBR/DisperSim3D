using System;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Computes the volume of the flammable gas cloud — the integral over all cells
    /// whose concentration falls between the Lower Flammability Limit (LFL) and the
    /// Upper Flammability Limit (UFL). This is the standard explosion-modelling metric
    /// per Fiates &amp; Vianna 2016.
    /// </summary>
    public static class FlammableCloudCalculator
    {
        public class CloudResult
        {
            /// <summary>Total flammable volume (m³).</summary>
            public double VolumeM3;
            /// <summary>Number of cells inside the LFL..UFL range.</summary>
            public int CellCount;
            /// <summary>Peak concentration anywhere in the field (kg/m³).</summary>
            public double MaxConcentration;
            /// <summary>Volume between LFL and ½(LFL+UFL) — "lean" portion.</summary>
            public double LeanVolumeM3;
            /// <summary>Volume between ½(LFL+UFL) and UFL — "rich" portion.</summary>
            public double RichVolumeM3;
        }

        /// <summary>
        /// Integrates over the concentration field. Cell volume is cellSizeX*cellSizeY*cellSizeZ.
        /// </summary>
        public static CloudResult Compute(double[,,] concentration,
            double cellSizeX, double cellSizeY, double cellSizeZ,
            double lfl, double ufl)
        {
            var r = new CloudResult();
            if (concentration == null || lfl <= 0 || ufl <= lfl) return r;

            int nx = concentration.GetLength(0);
            int ny = concentration.GetLength(1);
            int nz = concentration.GetLength(2);
            double cellVol = cellSizeX * cellSizeY * cellSizeZ;
            double mid = 0.5 * (lfl + ufl);

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        double c = concentration[i, j, k];
                        if (c > r.MaxConcentration) r.MaxConcentration = c;
                        if (c >= lfl && c <= ufl)
                        {
                            r.VolumeM3 += cellVol;
                            r.CellCount++;
                            if (c <= mid) r.LeanVolumeM3 += cellVol;
                            else r.RichVolumeM3 += cellVol;
                        }
                    }

            return r;
        }
    }
}
