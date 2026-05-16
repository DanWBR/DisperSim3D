using System;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Provides Pasquill-Gifford dispersion coefficients for continuous plume and instantaneous puff models.
    /// </summary>
    public static class PasquillGiffordCoefficients
    {
        /// <summary>
        /// Computes the crosswind (sigma Y) and vertical (sigma Z) dispersion coefficients
        /// for a continuous Gaussian plume at a given downwind distance.
        /// </summary>
        /// <param name="downwindDistanceM">Downwind distance from the source in meters.</param>
        /// <param name="cls">Pasquill-Gifford atmospheric stability class.</param>
        /// <returns>A tuple of (sigmaY, sigmaZ) dispersion coefficients in meters.</returns>
        public static (double sigmaY, double sigmaZ) ComputeSigma(
            double downwindDistanceM, PasquillStabilityClass cls)
        {
            if (downwindDistanceM < 1.0)
                downwindDistanceM = 1.0;

            double ay, by, az, bz;
            GetCoefficients(cls, out ay, out by, out az, out bz);

            double sigmaY = ay * Math.Pow(downwindDistanceM, by);
            double sigmaZ = az * Math.Pow(downwindDistanceM, bz);

            if (sigmaY < 0.5) sigmaY = 0.5;
            if (sigmaZ < 0.5) sigmaZ = 0.5;

            return (sigmaY, sigmaZ);
        }

        /// <summary>
        /// Computes the along-wind (sigma X), crosswind (sigma Y), and vertical (sigma Z) dispersion
        /// coefficients for an instantaneous Gaussian puff using Slade (1968) correlations.
        /// </summary>
        /// <param name="travelDistanceM">Travel distance of the puff from the source in meters.</param>
        /// <param name="cls">Pasquill-Gifford atmospheric stability class.</param>
        /// <returns>A tuple of (sigmaX, sigmaY, sigmaZ) dispersion coefficients in meters.</returns>
        public static (double sigmaX, double sigmaY, double sigmaZ) ComputePuffSigma(
            double travelDistanceM, PasquillStabilityClass cls)
        {
            if (travelDistanceM < 1.0)
                travelDistanceM = 1.0;

            double ay, by, az, bz, fx;
            GetPuffCoefficients(cls, out ay, out by, out az, out bz, out fx);

            double sigmaY = ay * Math.Pow(travelDistanceM, by);
            double sigmaZ = az * Math.Pow(travelDistanceM, bz);
            double sigmaX = fx * sigmaY;

            if (sigmaX < 0.5) sigmaX = 0.5;
            if (sigmaY < 0.5) sigmaY = 0.5;
            if (sigmaZ < 0.5) sigmaZ = 0.5;

            return (sigmaX, sigmaY, sigmaZ);
        }

        private static void GetCoefficients(PasquillStabilityClass cls,
            out double ay, out double by, out double az, out double bz)
        {
            // Power-law fits to Briggs (1973) open-country formulas over the
            // 50-1000 m range.  σy = ay·x^by  and  σz = az·x^bz  (x in metres).
            // Reference: Briggs, G.A. (1973). Diffusion Estimation for Small
            // Emissions. ATDL Contribution File No. 79, NOAA.
            switch (cls)
            {
                case PasquillStabilityClass.A:
                    ay = 0.2293; by = 0.9894;
                    az = 0.2000; bz = 1.0000;
                    break;
                case PasquillStabilityClass.B:
                    ay = 0.1667; by = 0.9894;
                    az = 0.1200; bz = 1.0000;
                    break;
                case PasquillStabilityClass.C:
                    ay = 0.1146; by = 0.9894;
                    az = 0.0872; bz = 0.9790;
                    break;
                case PasquillStabilityClass.D:
                    ay = 0.0833; by = 0.9894;
                    az = 0.1090; bz = 0.8550;
                    break;
                case PasquillStabilityClass.E:
                    ay = 0.0625; by = 0.9894;
                    az = 0.0383; bz = 0.9400;
                    break;
                case PasquillStabilityClass.F:
                    ay = 0.0417; by = 0.9894;
                    az = 0.0202; bz = 0.9430;
                    break;
                default:
                    ay = 0.0833; by = 0.9894;
                    az = 0.1090; bz = 0.8550;
                    break;
            }
        }

        // Slade (1968) instantaneous puff correlations
        // fx = along-wind/crosswind ratio (sigmaX = fx * sigmaY)
        private static void GetPuffCoefficients(PasquillStabilityClass cls,
            out double ay, out double by, out double az, out double bz, out double fx)
        {
            switch (cls)
            {
                case PasquillStabilityClass.A:
                    ay = 0.40; by = 0.91; az = 0.40; bz = 0.91; fx = 0.5;
                    break;
                case PasquillStabilityClass.B:
                    ay = 0.36; by = 0.86; az = 0.33; bz = 0.86; fx = 0.5;
                    break;
                case PasquillStabilityClass.C:
                    ay = 0.32; by = 0.78; az = 0.22; bz = 0.78; fx = 0.55;
                    break;
                case PasquillStabilityClass.D:
                    ay = 0.31; by = 0.71; az = 0.15; bz = 0.71; fx = 0.6;
                    break;
                case PasquillStabilityClass.E:
                    ay = 0.31; by = 0.68; az = 0.10; bz = 0.68; fx = 0.65;
                    break;
                case PasquillStabilityClass.F:
                    ay = 0.31; by = 0.68; az = 0.05; bz = 0.68; fx = 0.7;
                    break;
                default:
                    ay = 0.31; by = 0.71; az = 0.15; bz = 0.71; fx = 0.6;
                    break;
            }
        }
    }
}
