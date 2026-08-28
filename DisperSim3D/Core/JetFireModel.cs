using System;
using DisperSim3D.Geometry;
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
            double mdot = source.MassFlowRateKgS;
            double deltaHc = source.HeatOfCombustionJKg;

            // L = 0.2·Q^0.4 takes the heat release in kW, not W. Feeding it watts
            // inflated every jet flame by 10^1.2 ≈ 16×: a 2 kg/s methane jet (100 MW)
            // came out 317 m long instead of 20 m, which is what the correlation gives
            // and what the literature reports for that duty.
            double qKw = mdot * deltaHc / 1000.0;
            if (qKw <= 0) return 0;
            return 0.2 * Math.Pow(qKw, 0.4);
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
        /// <param name="ambientTempK">Ambient temperature (K) for the gas density.</param>
        /// <param name="ambientPressurePa">Ambient pressure (Pa) for the gas density.</param>
        /// <returns>Exit velocity in m/s.</returns>
        public static double FlameExitVelocity(FireSource source,
            double ambientTempK = 293.15, double ambientPressurePa = 101325.0)
        {
            double area = Math.PI * 0.25 * source.OrificeDiameterM * source.OrificeDiameterM;
            if (area <= 1e-10) return 50.0;

            // Ideal gas at ambient conditions with the fuel's molar mass, instead of the
            // 0.7 kg/m³ this used to hard-code — that value is methane-specific and only
            // right near 280 K. A real choked jet expands to a lower density still, so
            // this remains a lower bound on the exit velocity.
            double molarMass = source.FuelMolarMassKgMol > 0 ? source.FuelMolarMassKgMol : 0.016;
            double t = ambientTempK > 1 ? ambientTempK : 293.15;
            double p = ambientPressurePa > 1 ? ambientPressurePa : 101325.0;
            double rhoGas = p * molarMass / (8.31446 * t);
            if (rhoGas < 1e-6) rhoGas = 0.7;

            return source.MassFlowRateKgS / (rhoGas * area);
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

            // A pool fire rises: its flame axis is vertical regardless of the Direction
            // property, which only means something for a jet. Without this a pool fire
            // left at the default Direction of +X would lay its flame on the ground.
            var dir = source.IsPoolFire ? new Vector3D(0, 0, 1) : source.Direction;
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
