using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Steady-state Gaussian plume dispersion engine. Evaluates the analytical
    /// concentration field for continuous point-source releases in a uniform wind,
    /// with ground reflection and mixing-height lid.
    /// </summary>
    public class GaussianPlumeEngine : IConcentrationField
    {
        private readonly List<SourceData> _sources = new List<SourceData>();
        private double _mixingHeight;

        private static readonly double TwoPi = 2.0 * Math.PI;

        /// <summary>
        /// Initializes the plume engine from the given scenario, computing effective
        /// release heights and wind-rotated coordinate frames for each source.
        /// </summary>
        /// <param name="scenario">The dispersion scenario containing sources and meteorology.</param>
        public void Initialize(DispersionScenario scenario)
        {
            _sources.Clear();
            var meteo = scenario.Meteo;
            _mixingHeight = meteo.MixingHeightM > 0 ? meteo.MixingHeightM : 1e6;

            double windDirRad = meteo.WindDirectionDeg * Math.PI / 180.0;
            double sinW = Math.Sin(windDirRad);
            double cosW = Math.Cos(windDirRad);

            foreach (var src in scenario.Sources)
            {
                var pos = src.EffectivePosition;
                double baseHeight = pos.Z;

                double effDiam = src.EffectiveDiameterM;
                if (effDiam > 0 && (src.ExitVelocityMPerS > 0 || src.ExitTemperatureK > meteo.AmbientTemperature))
                {
                    double windAtStack = meteo.WindSpeedAtHeight(baseHeight);
                    double deltaH = BriggsPlumerise.ComputeDeltaH(
                        src.ExitVelocityMPerS,
                        effDiam,
                        src.ExitTemperatureK,
                        meteo.AmbientTemperature,
                        windAtStack,
                        meteo.StabilityClass);
                    baseHeight += deltaH;
                }
                double H = Math.Min(baseHeight, _mixingHeight);

                double windSpeed = meteo.WindSpeedAtHeight(H);
                if (windSpeed < 0.5) windSpeed = 0.5;

                _sources.Add(new SourceData
                {
                    OriginX = pos.X,
                    OriginY = pos.Y,
                    H = H,
                    Q = src.ReleaseRateKgPerS,
                    WindSpeed = windSpeed,
                    Stability = meteo.StabilityClass,
                    SinWind = sinW,
                    CosWind = cosW
                });
            }
        }

        /// <summary>
        /// Evaluates the steady-state concentration at the specified 3D point
        /// by summing contributions from all configured sources.
        /// </summary>
        /// <param name="x">The x-coordinate in meters.</param>
        /// <param name="y">The y-coordinate in meters.</param>
        /// <param name="z">The z-coordinate (height) in meters.</param>
        /// <returns>The steady-state concentration in kg/m³.</returns>
        public double EvaluateConcentration(double x, double y, double z)
        {
            if (z < 0) z = 0;
            double total = 0;

            for (int i = 0; i < _sources.Count; i++)
            {
                total += EvaluateSource(_sources[i], x, y, z);
            }

            return total;
        }

        private double EvaluateSource(SourceData src, double x, double y, double z)
        {
            double dx = x - src.OriginX;
            double dy = y - src.OriginY;

            // Rotate into wind-aligned coordinates: downwind and crosswind
            double downwind = dx * src.SinWind + dy * src.CosWind;
            double crosswind = dx * src.CosWind - dy * src.SinWind;

            if (downwind < 1.0) return 0;

            var sigma = PasquillGiffordCoefficients.ComputeSigma(downwind, src.Stability);
            double sigY = sigma.sigmaY;
            double sigZ = sigma.sigmaZ;

            // Lateral term: exp(-y²/2σy²)
            double yTerm = crosswind / sigY;
            double lateralArg = -0.5 * yTerm * yTerm;
            if (lateralArg < -18.0) return 0;

            // Vertical term with ground reflection and mixing height
            double vertTerm = ComputeVerticalTerm(z, src.H, sigZ);

            double c = src.Q / (TwoPi * src.WindSpeed * sigY * sigZ)
                       * Math.Exp(lateralArg)
                       * vertTerm;

            return Math.Max(c, 0);
        }

        private double ComputeVerticalTerm(double z, double H, double sigZ)
        {
            if (sigZ > 1.6 * _mixingHeight)
                return 1.0;

            double invSz2 = 1.0 / (2.0 * sigZ * sigZ);

            // Direct + ground reflection
            double dz1 = z - H;
            double dz2 = z + H;
            double term = Math.Exp(-dz1 * dz1 * invSz2) + Math.Exp(-dz2 * dz2 * invSz2);

            // Mixing height reflections (3 images each side)
            double L = _mixingHeight;
            for (int n = 1; n <= 3; n++)
            {
                double offset = 2.0 * n * L;
                term += Math.Exp(-(z - H - offset) * (z - H - offset) * invSz2);
                term += Math.Exp(-(z + H - offset) * (z + H - offset) * invSz2);
                term += Math.Exp(-(z - H + offset) * (z - H + offset) * invSz2);
                term += Math.Exp(-(z + H + offset) * (z + H + offset) * invSz2);
            }

            return term;
        }

        private struct SourceData
        {
            public double OriginX;
            public double OriginY;
            public double H;
            public double Q;
            public double WindSpeed;
            public PasquillStabilityClass Stability;
            public double SinWind;
            public double CosWind;
        }
    }
}
