using System;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Computes plume rise above a stack using the Briggs plume rise equations.
    /// </summary>
    public static class BriggsPlumerise
    {
        private const double G = 9.81;

        /// <summary>
        /// Computes the plume rise height (delta H) above the stack, taking into account
        /// both buoyancy-driven and momentum-driven rise, and returns the larger of the two.
        /// </summary>
        /// <param name="exitVelocityMPerS">Stack exit velocity in meters per second.</param>
        /// <param name="stackDiameterM">Inner stack diameter in meters.</param>
        /// <param name="exitTemperatureK">Exhaust gas temperature in Kelvin.</param>
        /// <param name="ambientTemperatureK">Ambient air temperature in Kelvin.</param>
        /// <param name="windSpeedAtStack">Wind speed at stack height in meters per second.</param>
        /// <param name="stability">Pasquill-Gifford atmospheric stability class.</param>
        /// <returns>The plume rise height in meters.</returns>
        public static double ComputeDeltaH(
            double exitVelocityMPerS,
            double stackDiameterM,
            double exitTemperatureK,
            double ambientTemperatureK,
            double windSpeedAtStack,
            PasquillStabilityClass stability)
        {
            if (windSpeedAtStack < 0.5) windSpeedAtStack = 0.5;
            if (stackDiameterM <= 0) return 0;

            double deltaHBuoy = BuoyancyRise(exitVelocityMPerS, stackDiameterM,
                exitTemperatureK, ambientTemperatureK, windSpeedAtStack, stability);

            double deltaHMom = MomentumRise(exitVelocityMPerS, stackDiameterM, windSpeedAtStack);

            return Math.Max(deltaHBuoy, deltaHMom);
        }

        private static double BuoyancyRise(
            double vs, double ds, double ts, double ta, double u,
            PasquillStabilityClass stability)
        {
            if (ts <= ta) return 0;

            double fb = G * vs * ds * ds * (ts - ta) / (4.0 * ts);
            if (fb < 0.001) return 0;

            bool isStable = (stability == PasquillStabilityClass.E ||
                             stability == PasquillStabilityClass.F);

            if (isStable)
            {
                double dtdz = (stability == PasquillStabilityClass.F) ? 0.035 : 0.020;
                double s = G / ta * (dtdz + 0.0098);
                if (s <= 0) return 0;
                return 2.6 * Math.Pow(fb / (u * s), 1.0 / 3.0);
            }

            double xf;
            if (fb < 55)
                xf = Math.Min(3.5 * 14.0 * Math.Pow(fb, 0.625), 10000);
            else
                xf = Math.Min(3.5 * 34.0 * Math.Pow(fb, 0.4), 10000);

            return 1.6 * Math.Pow(fb, 1.0 / 3.0) * Math.Pow(xf, 2.0 / 3.0) / u;
        }

        private static double MomentumRise(double vs, double ds, double u)
        {
            if (vs <= 0 || ds <= 0) return 0;
            return 1.44 * Math.Pow(vs * ds / u, 2.0 / 3.0) * Math.Pow(ds, 1.0 / 3.0);
        }
    }
}
