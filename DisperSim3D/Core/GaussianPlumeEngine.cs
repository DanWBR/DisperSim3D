using System;
using System.Collections.Generic;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Steady-state Gaussian plume dispersion engine with bent-plume trajectory.
    /// The plume centerline starts in the release direction and curves toward the
    /// wind direction over a momentum-based transition length.
    /// </summary>
    public class GaussianPlumeEngine : IConcentrationField
    {
        private readonly List<SourceData> _sources = new List<SourceData>();
        private double _mixingHeight;

        /// <summary>
        /// Optional pre-computed 3D wind field. When set, the wind speed and direction
        /// at each source are sampled from the field rather than from <see cref="MeteorologicalConditions"/>.
        /// </summary>
        public WindField3D WindField { get; set; }

        private static readonly double TwoPi = 2.0 * Math.PI;
        private const int TrajectoryPoints = 200;
        private const double MinWindSpeed = 0.5;

        public void Initialize(DispersionScenario scenario)
        {
            _sources.Clear();
            var meteo = scenario.Meteo;
            _mixingHeight = meteo.MixingHeightM > 0 ? meteo.MixingHeightM : 1e6;

            double windDirRad = meteo.WindDirectionDeg * Math.PI / 180.0;
            var windDir3DGlobal = new Vector3D(
                -Math.Sin(windDirRad),
                -Math.Cos(windDirRad),
                0);

            foreach (var src in scenario.Sources)
            {
                var pos = src.EffectivePosition;
                double baseHeight = pos.Z;

                Vector3D windDir3D = windDir3DGlobal;
                double localWindSpeedOverride = -1;
                if (WindField != null)
                {
                    var sampled = WindField.Interpolate(pos.X, pos.Y, baseHeight + 2.0);
                    double mag = sampled.Length;
                    if (mag > 0.05)
                    {
                        windDir3D = new Vector3D(sampled.X / mag, sampled.Y / mag, 0);
                        localWindSpeedOverride = mag;
                    }
                }

                double effDiam = src.EffectiveDiameterM;
                if (effDiam > 0 && (src.ExitVelocityMPerS > 0 || src.ExitTemperatureK > meteo.AmbientTemperature))
                {
                    double windAtStack = meteo.WindSpeedAtHeight(baseHeight);
                    double deltaH = BriggsPlumerise.ComputeDeltaH(
                        src.ExitVelocityMPerS, effDiam,
                        src.ExitTemperatureK, meteo.AmbientTemperature,
                        windAtStack, meteo.StabilityClass);
                    baseHeight += deltaH;
                }
                double H = Math.Min(baseHeight, _mixingHeight);

                // PGT sigma curves were calibrated with wind at measurement height,
                // so for near-ground sources use at least that height for consistency.
                double windEvalHeight = Math.Max(H, meteo.WindMeasurementHeightM);
                double windSpeed = localWindSpeedOverride > 0 ? localWindSpeedOverride : meteo.WindSpeedAtHeight(windEvalHeight);
                if (windSpeed < MinWindSpeed) windSpeed = MinWindSpeed;

                double exitVel = src.ComputedExitVelocity;
                var releaseDir = src.ReleaseDirection;

                // Check if release direction differs from wind direction
                double dotRW = releaseDir.X * windDir3D.X + releaseDir.Y * windDir3D.Y;
                bool hasDifferentDirection = dotRW < 0.99;

                double bendLength = 0;
                if (hasDifferentDirection)
                {
                    if (exitVel > 0 && effDiam > 0)
                    {
                        double r = exitVel / windSpeed;
                        bendLength = r * r * effDiam * (Math.PI / 4.0);
                        bendLength = Math.Max(bendLength, effDiam * 10.0);
                        bendLength = Math.Min(bendLength, scenario.DomainSizeM * 0.8);
                    }
                    else
                        bendLength = scenario.DomainSizeM * 0.15;
                }

                var sd = new SourceData
                {
                    OriginX = pos.X,
                    OriginY = pos.Y,
                    H = H,
                    Q = src.EffectiveReleaseRateKgPerS,
                    WindSpeed = windSpeed,
                    Stability = meteo.StabilityClass,
                    WindDirX = windDir3D.X,
                    WindDirY = windDir3D.Y,
                    BendLength = bendLength,
                    ReleaseDirX = releaseDir.X,
                    ReleaseDirY = releaseDir.Y,
                    ReleaseDirZ = releaseDir.Z
                };

                if (bendLength > 0)
                    ComputeTrajectory(ref sd, scenario.DomainSizeM * 2.5);

                _sources.Add(sd);
            }
        }

        public double EvaluateConcentration(double x, double y, double z)
        {
            if (z < 0) z = 0;
            double total = 0;
            for (int i = 0; i < _sources.Count; i++)
                total += EvaluateSource(_sources[i], x, y, z);
            return total;
        }

        private double EvaluateSource(SourceData src, double x, double y, double z)
        {
            if (src.BendLength > 0 && src.Trajectory != null)
                return EvaluateBentPlume(src, x, y, z);

            return EvaluateStraightPlume(src, x, y, z);
        }

        private double EvaluateStraightPlume(SourceData src, double x, double y, double z)
        {
            double dx = x - src.OriginX;
            double dy = y - src.OriginY;

            double downwind = dx * src.WindDirX + dy * src.WindDirY;
            double crosswind = dx * src.WindDirY - dy * src.WindDirX;

            if (downwind < 1.0) return 0;

            var sigma = PasquillGiffordCoefficients.ComputeSigma(downwind, src.Stability);
            double sigY = sigma.sigmaY;
            double sigZ = sigma.sigmaZ;

            double yTerm = crosswind / sigY;
            double lateralArg = -0.5 * yTerm * yTerm;
            if (lateralArg < -18.0) return 0;

            double vertTerm = ComputeVerticalTerm(z, src.H, sigZ);

            double c = src.Q / (TwoPi * src.WindSpeed * sigY * sigZ)
                       * Math.Exp(lateralArg) * vertTerm;
            return Math.Max(c, 0);
        }

        private double EvaluateBentPlume(SourceData src, double x, double y, double z)
        {
            var traj = src.Trajectory;
            double bestDist2 = double.MaxValue;
            int bestIdx = 0;

            // Find closest trajectory segment to the evaluation point (XY projection)
            for (int i = 0; i < traj.Length; i++)
            {
                double ddx = x - traj[i].X;
                double ddy = y - traj[i].Y;
                double d2 = ddx * ddx + ddy * ddy;
                if (d2 < bestDist2)
                {
                    bestDist2 = d2;
                    bestIdx = i;
                }
            }

            double arcLen = traj[bestIdx].ArcLength;
            if (arcLen < 0.5) return 0;

            // Crosswind = perpendicular distance to local centerline direction
            double tanX = traj[bestIdx].TangentX;
            double tanY = traj[bestIdx].TangentY;
            double ddx2 = x - traj[bestIdx].X;
            double ddy2 = y - traj[bestIdx].Y;
            double crosswind = ddx2 * tanY - ddy2 * tanX;

            // Height relative to the trajectory centerline height
            double centerZ = traj[bestIdx].Z;

            var sigma = PasquillGiffordCoefficients.ComputeSigma(arcLen, src.Stability);
            double sigY = sigma.sigmaY;
            double sigZ = sigma.sigmaZ;

            double yTerm = crosswind / sigY;
            double lateralArg = -0.5 * yTerm * yTerm;
            if (lateralArg < -18.0) return 0;

            double vertTerm = ComputeVerticalTerm(z, centerZ, sigZ);

            double c = src.Q / (TwoPi * src.WindSpeed * sigY * sigZ)
                       * Math.Exp(lateralArg) * vertTerm;
            return Math.Max(c, 0);
        }

        private void ComputeTrajectory(ref SourceData src, double maxDist)
        {
            double ds = maxDist / TrajectoryPoints;
            src.Trajectory = new TrajectoryPoint[TrajectoryPoints];

            double cx = src.OriginX;
            double cy = src.OriginY;
            double cz = src.H;
            double arcLen = 0;

            for (int i = 0; i < TrajectoryPoints; i++)
            {
                // Blend factor: 0 = pure release direction, 1 = pure wind direction
                double blend = src.BendLength > 0
                    ? 1.0 - Math.Exp(-arcLen / src.BendLength)
                    : 1.0;

                double dirX = src.ReleaseDirX * (1.0 - blend) + src.WindDirX * blend;
                double dirY = src.ReleaseDirY * (1.0 - blend) + src.WindDirY * blend;
                double dirZ = src.ReleaseDirZ * (1.0 - blend);

                double mag = Math.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
                if (mag > 1e-10) { dirX /= mag; dirY /= mag; dirZ /= mag; }

                src.Trajectory[i] = new TrajectoryPoint
                {
                    X = cx, Y = cy, Z = cz,
                    ArcLength = arcLen,
                    TangentX = dirX, TangentY = dirY
                };

                cx += dirX * ds;
                cy += dirY * ds;
                cz += dirZ * ds;
                if (cz < 0) cz = 0;
                if (cz > _mixingHeight) cz = _mixingHeight;
                arcLen += ds;
            }
        }

        private double ComputeVerticalTerm(double z, double H, double sigZ)
        {
            if (sigZ > 1.6 * _mixingHeight)
                return 1.0;

            double invSz2 = 1.0 / (2.0 * sigZ * sigZ);

            double dz1 = z - H;
            double dz2 = z + H;
            double term = Math.Exp(-dz1 * dz1 * invSz2) + Math.Exp(-dz2 * dz2 * invSz2);

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

        public List<List<Point3D>> GetTrajectoryPaths()
        {
            var paths = new List<List<Point3D>>();
            foreach (var src in _sources)
            {
                if (src.Trajectory == null) continue;
                var pts = new List<Point3D>();
                for (int i = 0; i < src.Trajectory.Length; i++)
                {
                    var tp = src.Trajectory[i];
                    pts.Add(new Point3D(tp.X, tp.Y, tp.Z));
                }
                paths.Add(pts);
            }
            return paths;
        }

        private struct TrajectoryPoint
        {
            public double X, Y, Z;
            public double ArcLength;
            public double TangentX, TangentY;
        }

        private struct SourceData
        {
            public double OriginX, OriginY;
            public double H, Q, WindSpeed;
            public PasquillStabilityClass Stability;
            public double WindDirX, WindDirY;
            public double ReleaseDirX, ReleaseDirY, ReleaseDirZ;
            public double BendLength;
            public TrajectoryPoint[] Trajectory;
        }
    }
}
