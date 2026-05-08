using System;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Provides jet fire and pool fire calculation methods including flame geometry and thermal radiation.
    /// </summary>
    public static class JetFireModel
    {
        /// <summary>
        /// Calculates jet flame length using the Chamberlain Q^0.4 correlation.
        /// </summary>
        /// <param name="source">Fire source parameters.</param>
        /// <returns>Flame length in meters.</returns>
        public static double FlameLength(FireSource source)
        {
            double dj = source.OrificeDiameterM;
            double mdot = source.MassFlowRateKgS;
            double deltaHc = source.HeatOfCombustionJKg;
            double Q = mdot * deltaHc;
            return 0.2 * Math.Pow(Q, 0.4);
        }

        /// <summary>
        /// Calculates the flame tilt angle due to wind using the Brzustowski method.
        /// </summary>
        /// <param name="windVector">Wind velocity vector.</param>
        /// <param name="flameExitVelocity">Gas exit velocity at the orifice in m/s.</param>
        /// <returns>Tilt angle in degrees, capped at 80.</returns>
        public static double FlameTiltAngle(Vector3D windVector, double flameExitVelocity)
        {
            double Uw = windVector.Length;
            if (flameExitVelocity < 0.1) return 0;
            double ratio = Uw / flameExitVelocity;
            double tilt = Math.Atan(ratio) * 180.0 / Math.PI;
            return Math.Min(tilt, 80);
        }

        /// <summary>
        /// Calculates the gas exit velocity at the orifice from mass flow rate and orifice area.
        /// </summary>
        /// <param name="source">Fire source parameters.</param>
        /// <returns>Exit velocity in m/s.</returns>
        public static double FlameExitVelocity(FireSource source)
        {
            double area = Math.PI * 0.25 * source.OrificeDiameterM * source.OrificeDiameterM;
            double rhoGas = 0.7;
            return area > 1e-10 ? source.MassFlowRateKgS / (rhoGas * area) : 50.0;
        }

        /// <summary>
        /// Calculates pool fire flame length using the Thomas correlation.
        /// </summary>
        /// <param name="source">Fire source parameters including pool diameter and burn rate.</param>
        /// <returns>Flame length in meters.</returns>
        public static double PoolFlameLength(FireSource source)
        {
            double D = source.PoolDiameterM;
            double mdot = source.PoolBurnRateKgM2S;
            double rhoAir = 1.2;
            double g = 9.81;
            double mStar = mdot / (rhoAir * Math.Sqrt(g * D));
            return 42 * D * Math.Pow(mStar, 0.61);
        }

        /// <summary>
        /// Calculates thermal radiation intensity at a given distance using the point source model.
        /// </summary>
        /// <param name="source">Fire source parameters.</param>
        /// <param name="distance">Distance from the fire in meters.</param>
        /// <returns>Radiation intensity in W/m^2.</returns>
        public static double RadiationAtDistance(FireSource source, double distance)
        {
            if (distance < 0.1) return double.MaxValue;
            double Q = source.MassFlowRateKgS * source.HeatOfCombustionJKg;
            double Qrad = Q * source.RadiativeFraction;
            return Qrad / (4 * Math.PI * distance * distance);
        }

        /// <summary>
        /// Calculates the distance at which a specified radiation intensity level is reached.
        /// </summary>
        /// <param name="source">Fire source parameters.</param>
        /// <param name="radiationLevel">Target radiation intensity in W/m^2.</param>
        /// <returns>Distance from the fire in meters.</returns>
        public static double RadiationDistanceForLevel(FireSource source, double radiationLevel)
        {
            if (radiationLevel <= 0) return double.MaxValue;
            double Q = source.MassFlowRateKgS * source.HeatOfCombustionJKg;
            double Qrad = Q * source.RadiativeFraction;
            return Math.Sqrt(Qrad / (4 * Math.PI * radiationLevel));
        }

        /// <summary>
        /// Computes the 3D position of the flame tip, accounting for wind-induced tilt for jet fires.
        /// </summary>
        /// <param name="source">Fire source parameters including position and direction.</param>
        /// <param name="windVector">Wind velocity vector.</param>
        /// <returns>3D point at the flame tip.</returns>
        public static Point3D FlameTip(FireSource source, Vector3D windVector)
        {
            double L = source.IsPoolFire ? PoolFlameLength(source) : FlameLength(source);
            var dir = source.Direction;
            if (dir.Length < 0.01) dir = new Vector3D(0, 0, 1);
            dir.Normalize();

            if (!source.IsPoolFire && windVector.Length > 0.1)
            {
                double exitVel = FlameExitVelocity(source);
                double tiltDeg = FlameTiltAngle(windVector, exitVel);
                double tiltRad = tiltDeg * Math.PI / 180.0;

                var windDir = windVector;
                windDir.Normalize();

                dir = new Vector3D(
                    dir.X * Math.Cos(tiltRad) + windDir.X * Math.Sin(tiltRad),
                    dir.Y * Math.Cos(tiltRad) + windDir.Y * Math.Sin(tiltRad),
                    dir.Z * Math.Cos(tiltRad));
                dir.Normalize();
            }

            return new Point3D(
                source.Position.X + dir.X * L,
                source.Position.Y + dir.Y * L,
                source.Position.Z + dir.Z * L);
        }
    }
}
