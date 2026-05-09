using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Gaussian puff dispersion engine that models atmospheric pollutant transport
    /// by releasing discrete puffs advected by wind and dispersed over time.
    /// </summary>
    public class GaussianPuffEngine : IConcentrationField
    {
        /// <summary>
        /// Stores the state of a single Gaussian puff, including its position,
        /// dispersion parameters, decay, and jet momentum data.
        /// </summary>
        public class Puff
        {
            /// <summary>Gets or sets the mass of pollutant in this puff (kg).</summary>
            public double Q { get; set; }
            /// <summary>Gets or sets the emission origin point in 3D space.</summary>
            public Point3D Origin { get; set; }
            /// <summary>Gets or sets the simulation time at which this puff was emitted (seconds).</summary>
            public double EmitTimeS { get; set; }
            /// <summary>Gets or sets the release source that produced this puff.</summary>
            public ReleaseSource3D Source { get; set; }

            /// <summary>Gets or sets the minimum corner of the puff's spatial bounding box.</summary>
            public Point3D MinBound { get; set; }
            /// <summary>Gets or sets the maximum corner of the puff's spatial bounding box.</summary>
            public Point3D MaxBound { get; set; }

            /// <summary>Gets or sets the effective release height including plume rise (meters).</summary>
            public double EffectiveReleaseHeight { get; set; }
            /// <summary>Gets or sets the wind speed at the effective release height (m/s).</summary>
            public double WindSpeedAtHeight { get; set; }
            /// <summary>Gets or sets the wind vector at the effective release height.</summary>
            public Vector3D WindVectorAtHeight { get; set; }
            /// <summary>Gets or sets the cumulative decay factor from chemical half-life and dry deposition.</summary>
            public double DecayFactor { get; set; }

            /// <summary>Gets or sets the initial jet exit velocity vector.</summary>
            public Vector3D JetVelocity { get; set; }
            /// <summary>Gets or sets the jet time constant for momentum decay (seconds).</summary>
            public double JetTimeConstantS { get; set; }
            /// <summary>Gets or sets whether this puff has significant jet momentum.</summary>
            public bool HasJet { get; set; }

            /// <summary>Gets or sets the along-wind dispersion coefficient (meters).</summary>
            public double SigmaX { get; set; }
            /// <summary>Gets or sets the crosswind dispersion coefficient (meters).</summary>
            public double SigmaY { get; set; }
            /// <summary>Gets or sets the vertical dispersion coefficient (meters).</summary>
            public double SigmaZ { get; set; }
            /// <summary>Gets or sets the cached inverse of sigma X squared (1/m^2).</summary>
            public double InvSx2 { get; set; }
            /// <summary>Gets or sets the cached inverse of sigma Y squared (1/m^2).</summary>
            public double InvSy2 { get; set; }
            /// <summary>Gets or sets the cached inverse of sigma Z squared (1/m^2).</summary>
            public double InvSz2 { get; set; }
            /// <summary>Gets or sets the current center x-coordinate of the puff (meters).</summary>
            public double CenterX { get; set; }
            /// <summary>Gets or sets the current center y-coordinate of the puff (meters).</summary>
            public double CenterY { get; set; }
            /// <summary>Gets or sets the current center z-coordinate of the puff (meters).</summary>
            public double CenterZ { get; set; }

            /// <summary>Gets or sets the accumulated wind displacement in x from the wind field (meters).</summary>
            public double WindDispX { get; set; }
            /// <summary>Gets or sets the accumulated wind displacement in y from the wind field (meters).</summary>
            public double WindDispY { get; set; }
            /// <summary>Gets or sets the accumulated wind displacement in z from the wind field (meters).</summary>
            public double WindDispZ { get; set; }
            /// <summary>Gets or sets the last simulation time at which this puff was updated (seconds).</summary>
            public double LastUpdateTimeS { get; set; }
        }

        private DispersionScenario _scenario;
        private MeteorologicalConditions _meteo;
        private readonly List<Puff> _activePuffs = new List<Puff>();
        private double _currentTimeS;
        private int _nextPuffIndex;
        private List<PuffScheduleEntry> _puffSchedule;
        private Vector3D _windVector;
        private PasquillStabilityClass _stability;
        private double _mixingHeight;
        private WindField3D _windField;

        private static readonly double TwoPiPow15 = Math.Pow(2.0 * Math.PI, 1.5);
        private static readonly double Sqrt2Pi = Math.Sqrt(2.0 * Math.PI);
        private const double NegligibleConcentration = 1e-10;
        private const double SigmaCutoff = 4.0;
        private const int MixingReflections = 3;

        /// <summary>Gets the list of currently active puffs in the simulation.</summary>
        public IReadOnlyList<Puff> ActivePuffs => _activePuffs;
        /// <summary>Gets the current simulation time in seconds.</summary>
        public double CurrentTimeS => _currentTimeS;
        /// <summary>Gets whether the simulation has finished (past duration with no active puffs).</summary>
        public bool IsFinished => _currentTimeS >= _scenario.SimulationDurationS && _activePuffs.Count == 0;
        /// <summary>Gets or sets the optional 3D wind field used for spatially varying advection.</summary>
        public WindField3D WindField { get => _windField; set => _windField = value; }

        /// <summary>
        /// Initializes the engine with the given scenario, computing effective release heights
        /// and building the puff emission schedule.
        /// </summary>
        /// <param name="scenario">The dispersion scenario containing sources, meteorology, and simulation parameters.</param>
        public void Initialize(DispersionScenario scenario)
        {
            _scenario = scenario;
            _meteo = scenario.Meteo;
            _windVector = _meteo.WindVector;
            _stability = _meteo.StabilityClass;
            _mixingHeight = _meteo.MixingHeightM > 0 ? _meteo.MixingHeightM : 1e6;
            _activePuffs.Clear();
            _currentTimeS = 0;
            _nextPuffIndex = 0;

            _puffSchedule = new List<PuffScheduleEntry>();
            foreach (var source in scenario.Sources)
            {
                int numPuffs = (int)Math.Ceiling(scenario.SimulationDurationS / source.PuffIntervalS);
                double effectiveHeight = ComputeEffectiveHeight(source);

                for (int i = 0; i < numPuffs; i++)
                {
                    _puffSchedule.Add(new PuffScheduleEntry
                    {
                        EmitTimeS = i * source.PuffIntervalS,
                        Source = source,
                        Q = source.MassPerPuff,
                        EffectiveReleaseHeight = effectiveHeight
                    });
                }
            }
            _puffSchedule.Sort((a, b) => a.EmitTimeS.CompareTo(b.EmitTimeS));
        }

        /// <summary>
        /// Advances the simulation to the specified time, emitting scheduled puffs,
        /// updating positions and dispersion parameters, and removing negligible puffs.
        /// </summary>
        /// <param name="simulationTimeS">The target simulation time in seconds.</param>
        public void StepTo(double simulationTimeS)
        {
            _currentTimeS = simulationTimeS;

            while (_nextPuffIndex < _puffSchedule.Count &&
                   _puffSchedule[_nextPuffIndex].EmitTimeS <= _currentTimeS)
            {
                var entry = _puffSchedule[_nextPuffIndex];
                var windAtH = _meteo.WindVectorAtHeight(entry.EffectiveReleaseHeight);
                double windSpeed = _meteo.WindSpeedAtHeight(entry.EffectiveReleaseHeight);

                var jetVec = entry.Source.ExitVelocityVector;
                double jetSpeed = jetVec.Length;
                double tau = 0;
                double effDiam = entry.Source.EffectiveDiameterM;
                if (jetSpeed > 0.1 && effDiam > 0)
                {
                    // TNO Yellow Book jet-in-crossflow: tau = d / U_wind
                    // Jet penetration distance = v_jet * tau = d * v_jet / U_wind
                    double effectiveWind = Math.Max(windSpeed, 0.5);
                    tau = effDiam / effectiveWind;
                    tau = Math.Max(tau, 0.001);
                    tau = Math.Min(tau, 30.0);
                }

                _activePuffs.Add(new Puff
                {
                    Q = entry.Q,
                    Origin = entry.Source.EffectivePosition,
                    EmitTimeS = entry.EmitTimeS,
                    Source = entry.Source,
                    EffectiveReleaseHeight = entry.EffectiveReleaseHeight,
                    WindSpeedAtHeight = windSpeed,
                    WindVectorAtHeight = windAtH,
                    JetVelocity = jetVec,
                    JetTimeConstantS = tau,
                    HasJet = tau > 0 && jetSpeed > 0.1,
                    DecayFactor = 1.0,
                    LastUpdateTimeS = entry.EmitTimeS
                });
                _nextPuffIndex++;
            }

            UpdatePuffCache();

            _activePuffs.RemoveAll(p => IsPuffNegligible(p));
        }

        /// <summary>
        /// Evaluates the total concentration at the specified 3D point by summing
        /// contributions from all active puffs whose bounding boxes contain the point.
        /// </summary>
        /// <param name="x">The x-coordinate in meters.</param>
        /// <param name="y">The y-coordinate in meters.</param>
        /// <param name="z">The z-coordinate (height) in meters.</param>
        /// <returns>The total concentration at the given point.</returns>
        public double EvaluateConcentration(double x, double y, double z)
        {
            double total = 0;

            for (int i = 0; i < _activePuffs.Count; i++)
            {
                var p = _activePuffs[i];

                if (x < p.MinBound.X || x > p.MaxBound.X ||
                    y < p.MinBound.Y || y > p.MaxBound.Y ||
                    z < p.MinBound.Z || z > p.MaxBound.Z)
                    continue;

                total += EvaluatePuff(p, x, y, z);
            }

            return total;
        }

        /// <summary>
        /// Updates the wind vector and stability class, recalculating wind conditions
        /// at each active puff's effective release height.
        /// </summary>
        /// <param name="windVector">The new wind vector.</param>
        /// <param name="stability">The new Pasquill-Gifford atmospheric stability class.</param>
        public void UpdateWind(Vector3D windVector, PasquillStabilityClass stability)
        {
            _windVector = windVector;
            _stability = stability;

            for (int i = 0; i < _activePuffs.Count; i++)
            {
                var p = _activePuffs[i];
                p.WindSpeedAtHeight = _meteo.WindSpeedAtHeight(p.EffectiveReleaseHeight);
                p.WindVectorAtHeight = _meteo.WindVectorAtHeight(p.EffectiveReleaseHeight);
            }
        }

        /// <summary>
        /// Resets the engine to its initial state, clearing all active puffs and resetting the simulation time.
        /// </summary>
        public void Reset()
        {
            _activePuffs.Clear();
            _currentTimeS = 0;
            _nextPuffIndex = 0;
        }

        private double ComputeEffectiveHeight(ReleaseSource3D source)
        {
            double baseHeight = source.EffectivePosition.Z;

            double effDiam = source.EffectiveDiameterM;
            if (effDiam > 0 && (source.ExitVelocityMPerS > 0 || source.ExitTemperatureK > _meteo.AmbientTemperature))
            {
                double windAtStack = _meteo.WindSpeedAtHeight(baseHeight);
                double deltaH = BriggsPlumerise.ComputeDeltaH(
                    source.ExitVelocityMPerS,
                    effDiam,
                    source.ExitTemperatureK,
                    _meteo.AmbientTemperature,
                    windAtStack,
                    _stability);
                baseHeight += deltaH;
            }

            return Math.Min(baseHeight, _mixingHeight);
        }

        private double EvaluatePuff(Puff puff, double x, double y, double z)
        {
            if (puff.SigmaX < 0.5) return 0;

            double dx = x - puff.CenterX;
            double dy = y - puff.CenterY;

            double horizArg = -0.5 * (dx * dx * puff.InvSx2 + dy * dy * puff.InvSy2);
            if (horizArg < -18.0) return 0;

            double qEff = puff.Q * puff.DecayFactor;
            double sz = puff.SigmaZ;
            double cz = puff.CenterZ;

            double vertTerm = ComputeVerticalTerm(z, cz, sz);

            double c = qEff / (TwoPiPow15 * puff.SigmaX * puff.SigmaY) * Math.Exp(horizArg) * vertTerm;

            return c;
        }

        private double ComputeVerticalTerm(double z, double cz, double sz)
        {
            if (sz > 1.6 * _mixingHeight)
                return 1.0 / _mixingHeight;

            double invSz = 1.0 / sz;
            double total = 0;

            for (int n = -MixingReflections; n <= MixingReflections; n++)
            {
                double offset = 2.0 * n * _mixingHeight;
                double dz1 = (z - cz - offset) * invSz;
                double dz2 = (z + cz - offset) * invSz;
                total += Math.Exp(-0.5 * dz1 * dz1) + Math.Exp(-0.5 * dz2 * dz2);
            }

            return total * invSz / Sqrt2Pi;
        }

        private void UpdatePuffCache()
        {
            for (int i = 0; i < _activePuffs.Count; i++)
            {
                var p = _activePuffs[i];
                double elapsed = _currentTimeS - p.EmitTimeS;
                if (elapsed < 0.001)
                {
                    p.MinBound = p.Origin;
                    p.MaxBound = p.Origin;
                    p.CenterX = p.Origin.X;
                    p.CenterY = p.Origin.Y;
                    p.CenterZ = p.EffectiveReleaseHeight;
                    p.SigmaX = 0;
                    p.SigmaY = 0;
                    p.SigmaZ = 0;
                    p.DecayFactor = 1.0;
                    continue;
                }

                var wH = p.WindVectorAtHeight;

                double cx, cy, cz;
                double tau = p.JetTimeConstantS;

                if (_windField != null)
                {
                    double dt = _currentTimeS - p.LastUpdateTimeS;
                    if (dt > 0)
                    {
                        var localWind = _windField.Interpolate(p.CenterX, p.CenterY, Math.Max(0.1, p.CenterZ));
                        p.WindDispX += localWind.X * dt;
                        p.WindDispY += localWind.Y * dt;
                        p.WindDispZ += localWind.Z * dt;
                        p.LastUpdateTimeS = _currentTimeS;
                    }
                    cx = p.Origin.X + p.WindDispX;
                    cy = p.Origin.Y + p.WindDispY;
                    cz = p.EffectiveReleaseHeight + p.WindDispZ;
                }
                else if (p.HasJet)
                {
                    cx = p.Origin.X + wH.X * elapsed;
                    cy = p.Origin.Y + wH.Y * elapsed;
                    cz = p.EffectiveReleaseHeight + wH.Z * elapsed;
                }
                else
                {
                    cx = p.Origin.X + wH.X * elapsed;
                    cy = p.Origin.Y + wH.Y * elapsed;
                    cz = p.EffectiveReleaseHeight;
                }

                if (p.HasJet)
                {
                    double jx, jy, jz;
                    if (elapsed > 5.0 * tau)
                    {
                        jx = p.JetVelocity.X * tau;
                        jy = p.JetVelocity.Y * tau;
                        jz = p.JetVelocity.Z * tau;
                    }
                    else
                    {
                        double decay = 1.0 - Math.Exp(-elapsed / tau);
                        jx = p.JetVelocity.X * tau * decay;
                        jy = p.JetVelocity.Y * tau * decay;
                        jz = p.JetVelocity.Z * tau * decay;
                    }
                    cx += jx;
                    cy += jy;
                    cz += jz;
                }
                cz = Math.Max(0, Math.Min(cz, _mixingHeight));
                p.CenterX = cx;
                p.CenterY = cy;
                p.CenterZ = cz;

                // Travel distance for sigma: actual displacement from origin
                // (includes both wind advection and jet momentum)
                double dx = cx - p.Origin.X;
                double dy = cy - p.Origin.Y;
                double downwind = Math.Sqrt(dx * dx + dy * dy);
                if (downwind < 1.0) downwind = p.WindSpeedAtHeight * elapsed;
                var sigma = PasquillGiffordCoefficients.ComputePuffSigma(downwind, _stability);
                p.SigmaX = sigma.sigmaX;
                p.SigmaY = sigma.sigmaY;
                p.SigmaZ = sigma.sigmaZ;
                p.InvSx2 = 1.0 / (sigma.sigmaX * sigma.sigmaX);
                p.InvSy2 = 1.0 / (sigma.sigmaY * sigma.sigmaY);
                p.InvSz2 = 1.0 / (sigma.sigmaZ * sigma.sigmaZ);

                // Decay factor: chemical half-life + dry deposition (Horst 1977)
                var gas = p.Source.Gas;
                double lambda = (gas != null && gas.HalfLifeS > 0) ? Math.Log(2) / gas.HalfLifeS : 0;
                double depositionRate = (gas != null && gas.DryDepositionVelocityMPerS > 0)
                    ? 2.0 * gas.DryDepositionVelocityMPerS / (Sqrt2Pi * sigma.sigmaZ)
                    : 0;
                double totalDecay = lambda + depositionRate;
                p.DecayFactor = totalDecay > 0 ? Math.Exp(-totalDecay * elapsed) : 1.0;

                // Bounding box
                double extX = SigmaCutoff * sigma.sigmaX;
                double extY = SigmaCutoff * sigma.sigmaY;
                double extZ = SigmaCutoff * sigma.sigmaZ;

                double minZ = Math.Max(0, cz - extZ);
                double maxZ = Math.Min(cz + extZ, _mixingHeight);

                p.MinBound = new Point3D(cx - extX, cy - extY, minZ);
                p.MaxBound = new Point3D(cx + extX, cy + extY, maxZ);
            }
        }

        private bool IsPuffNegligible(Puff puff)
        {
            double elapsed = _currentTimeS - puff.EmitTimeS;
            if (elapsed < 0.001) return false;

            double qEff = puff.Q * puff.DecayFactor;

            double peakC = qEff / (TwoPiPow15 * puff.SigmaX * puff.SigmaY * puff.SigmaZ);
            if (peakC < NegligibleConcentration) return true;

            double domain = _scenario.DomainSizeM;
            if (Math.Abs(puff.CenterX) > domain * 5 || Math.Abs(puff.CenterY) > domain * 5) return true;

            return false;
        }

        private struct PuffScheduleEntry
        {
            public double EmitTimeS;
            public ReleaseSource3D Source;
            public double Q;
            public double EffectiveReleaseHeight;
        }
    }
}
