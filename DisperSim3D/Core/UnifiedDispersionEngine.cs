using System;
using System.Collections.Generic;
using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Unified integral dispersion model for a continuous release: one set of ordinary differential
    /// equations along the cloud centreline carries the cloud from a momentum jet through the heavy-gas
    /// (grounded, gravity-spreading) phase into passive Gaussian dispersion, in the spirit of the UDM
    /// (Witlox &amp; Holt). No grid: the state is integrated once at <see cref="Initialize"/> and the
    /// concentration at a point is reconstructed from the stored centreline with prescribed cross-section
    /// profiles  c(y,z) = c_c · exp(-(|y|/Ry)^n) · exp(-(|z-zc|/Rz)^m).
    ///
    /// State along the arc length s: gas mass flow (constant), entrained air mass flow, horizontal and
    /// vertical momentum flux, enthalpy flux, centroid (x, z) and crosswind half-width Ry. The vertical
    /// half-depth Rz follows from continuity (A_eff = m/(rho·U)). Closures:
    ///  - jet entrainment      alpha1 · |U_rel| · perimeter
    ///  - bent-over entrainment alpha2 · u_a · |sin theta| · perimeter
    ///  - ambient turbulence   growth of the Pasquill-Gifford sigmas at the equivalent distance, vertical
    ///                          part damped by 1/(1 + 0.8 Ri*) when the cloud is heavier than air
    ///  - gravity spreading    dRy/dt = C_E · sqrt(g' · H) on the ground, with edge entrainment
    ///  - ground heat transfer and reflection, buoyancy in the vertical momentum, mixture density from
    ///    the ideal-gas law with the gas / air mole fractions.
    /// Profile exponents relax from flat-topped (heavy) to Gaussian (passive) with Ri*.
    /// </summary>
    public class UnifiedDispersionEngine : IConcentrationField
    {
        // ---- closure constants (calibrated against the repository benchmarks) ----
        public double Alpha1 { get; set; } = 0.057;   // jet (top-hat) entrainment
        public double Alpha2 { get; set; } = 0.35;    // bent-over / crosswind entrainment (UDM: 0.35)
        public double CE { get; set; } = 0.6;         // gravity front constant (UDM 1.15; 0.6 calibrated on Burro/Coyote/Falcon/Maplin/Kit Fox)
        public double GammaEdge { get; set; } = 0.0;  // edge entrainment fraction of the front advance (UDM: 0 for continuous releases)
        public double RiDamping { get; set; } = 0.0;  // optional damping of the passive vertical growth, 1/(1 + RiDamping·Ri*) (UDM: none)
        public double DensityProfileScale { get; set; } = 0.1; // relative density excess at which the horizontal profile is fully flat-topped
        public double PoolAirFactor { get; set; } = 1.5;       // (vapour + air)/vapour mass leaving a pool source (UDM pool vaporisation entrainment parameter)
        public double RoughnessCorrection { get; set; } = 1.0;  // 1 = scale sigma_z by (z0/0.03)^0.2 (PG curves are for z0 = 3 cm)
        public double StabilityExponent { get; set; } = 1.0;    // 1 = vertical profile exponent by stability class (A 2.5 ... F 1.2), 0 = always 2
        public double RiTransition { get; set; } = 15.0;       // Ri* below which the grounded cloud may be treated as passive (UDM: 15)
        public double DensityTransition { get; set; } = 0.015; // relative density excess below which the cloud may be treated as passive (UDM: 0.015)
        public double HeavyPassiveFactor { get; set; } = 1.0;  // fraction of the passive (turbulent) entrainment kept in the heavy regime (calibrated: 1)
        public bool UsePasquillGiffordSigmas { get; set; } = true; // true = Pasquill-Gifford-Turner curves (Martin 1976 fit); false = Briggs rural power laws
        public double GroundFrictionCf { get; set; } = 0.004;
        public double MaxDownwindFactor { get; set; } = 1.5;

        private const double G = 9.80665, R = 8.314462, Kappa = 0.41, MwAir = 0.028964, CpAir = 1005.0;

        private struct Node
        {
            public double x, z, s, Ry, Rz, n, m, U, rho, Yc, cc, T, Ri;
            public bool grounded;
        }

        private readonly List<Node> _path = new List<Node>();
        private double _rhoAir, _tAir, _pAir, _ustar, _z0;
        private MeteorologicalConditions _meteo;
        private double _windDirX, _windDirY, _originX, _originY;
        private double[] _pg = new double[4];
        private double _z0Factor = 1.0;
        private PasquillStabilityClass _cls = PasquillStabilityClass.D;
        public IReadOnlyList<(double x, double z, double Ry, double Rz, double cc, double rho, bool grounded)> Centreline
        {
            get { var l = new List<(double, double, double, double, double, double, bool)>(); foreach (var p in _path) l.Add((p.x, p.z, p.Ry, p.Rz, p.cc, p.rho, p.grounded)); return l; }
        }

        private class State
        {
            public double mg, ma, Mx, Mz, Hf, x, z, Ry, t;
            public bool grounded;
            public State Clone() => (State)MemberwiseClone();
        }

        private double _mwGas, _cpGas;

        /// <summary>Environment overrides (UDM_ALPHA1, UDM_ALPHA2, UDM_CE, UDM_GEDGE, UDM_RIDAMP, UDM_RIPROF, UDM_CF, UDM_KAMB, UDM_KTOP) for calibration runs.</summary>
        private void ApplyEnvironmentOverrides()
        {
            double v;
            string e;
            if ((e = Environment.GetEnvironmentVariable("UDM_ALPHA1")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) Alpha1 = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_ALPHA2")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) Alpha2 = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_CE")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) CE = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_GEDGE")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) GammaEdge = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_RIDAMP")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) RiDamping = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_DPROF")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) DensityProfileScale = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_POOLAIR")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) PoolAirFactor = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_Z0CORR")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) RoughnessCorrection = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_STABEXP")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) StabilityExponent = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_RITRANS")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) RiTransition = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_RHOCRIT")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) DensityTransition = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_HPASS")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) HeavyPassiveFactor = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_SIGMA")) != null) UsePasquillGiffordSigmas = !e.StartsWith("B", StringComparison.OrdinalIgnoreCase);
            if ((e = Environment.GetEnvironmentVariable("UDM_CF")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) GroundFrictionCf = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_KAMB")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) AmbientFactor = v;
            if ((e = Environment.GetEnvironmentVariable("UDM_KTOP")) != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) TopEntrainment = v;
        }

        /// <summary>Multiplier on the ambient (Pasquill-Gifford) entrainment.</summary>
        public double AmbientFactor { get; set; } = 0.7;
        /// <summary>Heavy-gas top entrainment u_top = TopEntrainment · u* / Φ(Ri*) (UDM form of Φ), applied over the cloud width (0 = off).</summary>
        public double TopEntrainment { get; set; } = 4.0;   // calibrated: ~10x kappa (UDM uses kappa = 0.41 with its own near-field passive term)

        public void Initialize(DispersionScenario scenario)
        {
            ApplyEnvironmentOverrides();
            _path.Clear();
            _meteo = scenario.Meteo;
            _tAir = _meteo.AmbientTemperature > 0 ? _meteo.AmbientTemperature : 293.15;
            _pAir = _meteo.AmbientPressure > 0 ? _meteo.AmbientPressure : 101325.0;
            _rhoAir = _pAir * MwAir / (R * _tAir);
            _z0 = _meteo.RoughnessLengthM > 0 ? _meteo.RoughnessLengthM : 0.03;
            double zref = _meteo.WindMeasurementHeightM > 0 ? _meteo.WindMeasurementHeightM : 10.0;
            _ustar = Kappa * Math.Max(_meteo.WindSpeed, 0.3) / Math.Log(Math.Max(zref, 2 * _z0) / _z0);
            double ay, by, az, bz; PasquillGiffordCoefficients.PowerLaw(_meteo.StabilityClass, out ay, out by, out az, out bz);
            _z0Factor = RoughnessCorrection > 0 ? Math.Max(0.5, Math.Min(2.0, Math.Pow(_z0 / 0.03, 0.2))) : 1.0;
            az *= _z0Factor;
            _pg = new[] { ay, by, az, bz };
            _cls = _meteo.StabilityClass;
            _mVert = StabilityExponent > 0 ? VerticalExponent(_meteo.StabilityClass) : 2.0;
            double wd = _meteo.WindDirectionDeg * Math.PI / 180.0;
            _windDirX = -Math.Sin(wd); _windDirY = -Math.Cos(wd);

            if (scenario.Sources == null || scenario.Sources.Count == 0) return;
            var src = scenario.Sources[0];
            _originX = src.EffectivePosition.X; _originY = src.EffectivePosition.Y;
            _mwGas = src.Gas != null && src.Gas.MolarMass > 0 ? src.Gas.MolarMass : 0.029;
            _cpGas = src.Gas != null ? src.Gas.EffectiveSpecificHeatJPerKgK : 1000.0;

            double mg = src.EffectiveReleaseRateKgPerS;
            if (mg <= 0) return;
            double T0 = src.ExitTemperatureK > 0 ? src.ExitTemperatureK : _tAir;
            double rho0 = _pAir * _mwGas / (R * T0);
            double z0 = Math.Max(src.EffectivePosition.Z, 0.0);
            double d = src.EffectiveDiameterM;
            double uExit = src.ExitVelocityMPerS > 0 ? src.ExitVelocityMPerS : src.ComputedExitVelocity;
            var dir = src.ReleaseDirection;
            double along = dir.X * _windDirX + dir.Y * _windDirY;     // horizontal component along the wind

            var st = new State { mg = mg, ma = 0, x = 0, z = z0, t = 0 };
            double uA0 = WindAt(Math.Max(z0, 0.5));
            bool pool = uExit < 0.5 && d > 0.5 && z0 <= 0.5 * d;   // low-velocity area source on the ground
            if (pool)
            {
                st.grounded = true; st.z = 0; st.Ry = 0.5 * d;
                double u = uA0; double Rz = 1.0;
                for (int it = 0; it < 6; it++)
                {
                    double A = mg / (rho0 * u);
                    Rz = A / (2 * st.Ry * Gam(1 + 1.0 / 2) * Gam(1 + 1.0 / 1.0));
                    u = WindAt(Math.Max(0.5 * Rz, 0.3));
                }
                st.Mx = mg * u; st.Mz = 0;
                st.ma = Math.Max(PoolAirFactor - 1.0, 0.0) * mg;   // vapour leaves the pool already mixed with air
            }
            else
            {
                double U0 = uExit > 0.05 ? uExit : Math.Max(uA0, 0.5);
                double u0 = uExit > 0.05 ? Math.Max(U0 * along, 0.0) : U0;
                double w0 = uExit > 0.05 ? U0 * dir.Z : 0.0;
                if (u0 < 0.05 * uA0 && Math.Abs(w0) < 0.05 * uA0) u0 = 0.05 * uA0;
                double A0 = mg / (rho0 * Math.Max(Math.Sqrt(u0 * u0 + w0 * w0), 1e-3));
                st.Ry = Math.Sqrt(A0 / Math.PI);
                st.Mx = mg * u0; st.Mz = mg * w0; st.grounded = false;
            }
            st.Hf = mg * _cpGas * T0 + st.ma * CpAir * _tAir;

            double xMax = Math.Max(scenario.DomainSizeM, 50.0) * MaxDownwindFactor;
            Integrate(st, xMax);
        }

        // ---------------------------------------------------------------- integration
        private bool _dumpOk;
        private void Integrate(State st, double xMax)
        {
            int steps = 0; double s = 0;
            string dumpPath = Environment.GetEnvironmentVariable("UDM_DUMP");
            if (!string.IsNullOrEmpty(dumpPath)) { _dump = new System.Text.StringBuilder(); _dump.AppendLine("x;z;Ry;Rz;n;m;Ri;rho;Yc;T;U;u;uA;Ejet;Ecross;Eamb;Etop;Eedge;uFront;dRy;grounded"); }
            Record(st, s);
            while (st.x < xMax && steps < 400000)
            {
                var d0 = Derivs(st, out var rz0);
                double scale = Math.Max(Math.Max(st.Ry, rz0), 0.05);
                double ds = Math.Min(Math.Max(0.02 * scale, 0.005), 5.0);
                var k1 = d0; var s1 = Advance(st, k1, 0.5 * ds);
                var k2 = Derivs(s1, out _); var s2 = Advance(st, k2, 0.5 * ds);
                var k3 = Derivs(s2, out _); var s3 = Advance(st, k3, ds);
                var k4 = Derivs(s3, out _);
                var k = new double[k1.Length];
                for (int i = 0; i < k.Length; i++) k[i] = (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]) / 6.0;
                _dumpOk = true; Derivs(st, out _); _dumpOk = false;
                st = Advance(st, k, ds);
                s += ds; steps++;
                Touchdown(st);
                if ((steps % 2) == 0 || st.grounded) Record(st, s);
                if (st.Hf <= 0 || double.IsNaN(st.x)) break;
            }
            if (_dump != null) { try { System.IO.File.WriteAllText(dumpPath, _dump.ToString()); } catch { } _dump = null; }
        }

        private State Advance(State st, double[] k, double ds)
        {
            var n = st.Clone();
            n.ma = Math.Max(st.ma + k[0] * ds, 0); n.Mx = st.Mx + k[1] * ds; n.Mz = st.grounded ? 0 : st.Mz + k[2] * ds;
            n.Hf = st.Hf + k[3] * ds; n.x = st.x + k[4] * ds; n.z = st.grounded ? 0 : Math.Max(st.z + k[5] * ds, 0);
            n.Ry = Math.Max(st.Ry + k[6] * ds, 1e-3); n.t = st.t + k[7] * ds;
            return n;
        }

        /// <summary>Switch to the grounded regime when the heavy cloud reaches the ground.</summary>
        private void Touchdown(State st)
        {
            if (st.grounded) return;
            Props(st, out double m, out double U, out double u, out double w, out double rho, out _, out _, out double Ry, out double Rz, out _, out _);
            bool heavy = rho > _rhoAir * 1.001;
            if (st.z - Rz <= 0.0 && heavy && w <= 0.05 * U)
            {
                st.grounded = true; st.z = 0; st.Mz = 0;
                // conserve the effective area when folding the elevated section onto the ground
                double A = m / (rho * U);
                st.Ry = Math.Max(st.Ry, Math.Sqrt(A / (Math.PI / 2.0)));
            }
        }

        private double Gam(double v) => SpecialGamma(v);

        private static double SpecialGamma(double x)
        {
            // Lanczos approximation (x > 0)
            double[] g = { 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
            if (x < 0.5) return Math.PI / (Math.Sin(Math.PI * x) * SpecialGamma(1 - x));
            x -= 1; double a = 0.99999999999980993; double t = x + 7.5;
            for (int i = 0; i < 8; i++) a += g[i] / (x + i + 1);
            return Math.Sqrt(2 * Math.PI) * Math.Pow(t, x + 0.5) * Math.Exp(-t) * a;
        }

        private double WindAt(double z) => Math.Max(_meteo.WindSpeedAtHeight(Math.Max(z, 0.5)), 0.2);

        private double _mVert = 2.0;
        private void Exponents(double relDensityExcess, out double n, out double m)
        {
            double f = Math.Max(0, Math.Min(1, relDensityExcess / DensityProfileScale));
            n = 2.0 + 48.0 * f;     // horizontal: Gaussian (2) -> top-hat (50) with the relative density excess (UDM)
            m = _mVert;             // vertical: by stability class (UDM: 2.5 unstable ... 1.0 very stable; 2 neutral)
        }
        private static double VerticalExponent(PasquillStabilityClass c)
        {
            switch (c)
            {
                case PasquillStabilityClass.A: return 2.5;
                case PasquillStabilityClass.B: return 2.3;
                case PasquillStabilityClass.C: return 2.1;
                case PasquillStabilityClass.E: return 1.6;
                case PasquillStabilityClass.F: return 1.2;
                default: return 2.0;
            }
        }
        /// <summary>UDM top-entrainment stability function Φ(Ri*): u_top = κ·u*/Φ.</summary>
        private static double PhiRi(double ri)
        {
            if (ri < 0) return 1.0 / (1.0 + 0.65 * Math.Abs(ri));
            if (ri < 2.3625) return 1.0;
            if (ri < 14.72) return (1.0 + 0.8 * Math.Sqrt(ri)) / 1.7;
            return ri / 7.0;
        }

        private double AreaFactor(double n, double m, bool grounded) =>
            grounded ? 2 * Gam(1 + 1 / n) * Gam(1 + 1 / m) : 4 * Gam(1 + 1 / n) * Gam(1 + 1 / m);

        /// <summary>Derived cloud properties from the state.</summary>
        private void Props(State st, out double m, out double U, out double u, out double w, out double rho, out double T, out double Yc,
            out double Ry, out double Rz, out double Ri, out double A)
        {
            m = st.mg + st.ma;
            double cpMix = (st.mg * _cpGas + st.ma * CpAir) / m;
            T = Math.Max(st.Hf / (m * cpMix), 60.0);
            double ng = st.mg / _mwGas, na = st.ma / MwAir;
            double mwMix = m / (ng + na);
            rho = _pAir * mwMix / (R * T);
            u = Math.Max(st.Mx / m, 0.0); w = st.grounded ? 0.0 : st.Mz / m;
            double uA = WindAt(st.grounded ? 0.5 : st.z);
            if (u < 0.05 * uA) u = 0.05 * uA;
            U = Math.Max(Math.Sqrt(u * u + w * w), 1e-3);
            A = m / (rho * U);
            Ry = st.Ry;
            // Richardson number of the grounded cloud (0 while airborne or lighter than air)
            double rel = Math.Max((rho - _rhoAir) / rho, 0.0);
            double gPrime = G * (rho - _rhoAir) / rho;
            Ri = 0;
            double n, mm;
            Exponents(rel, out n, out mm);
            if (st.grounded && gPrime > 0)
            {
                double RzGuess = A / (Ry * AreaFactor(n, mm, true));
                Ri = gPrime * RzGuess * Gam(1 + 1 / mm) / (_ustar * _ustar);
            }
            Rz = A / (Ry * AreaFactor(n, mm, st.grounded));
            if (!st.grounded && Rz < 1e-3) Rz = 1e-3;
            Yc = st.mg / m;
        }

        private System.Text.StringBuilder _dump;
        private double[] Derivs(State st, out double RzOut)
        {
            Props(st, out double m, out double U, out double u, out double w, out double rho, out double T, out double Yc, out double Ry, out double Rz, out double Ri, out double A);
            RzOut = Rz;
            Exponents(Math.Max((rho - _rhoAir) / rho, 0.0), out double n, out double mm);
            double zc = st.grounded ? 0.5 * Rz * Gam(1 + 1 / mm) : st.z;
            double uA = WindAt(zc);
            double gPrime = G * (rho - _rhoAir) / rho;

            // geometry
            double RyEff = Ry * Gam(1 + 1 / n);                    // mean half-width
            double H = Rz * Gam(1 + 1 / mm);                       // mean (half-)depth
            double perimeter = st.grounded ? 2 * RyEff + 2 * H : Math.PI * (RyEff + H);

            // ---- entrainment (volume flow of air per unit arc length, m2/s) ----
            double uEx = Math.Max(u - uA, 0.0);
            double uRel = Math.Sqrt(uEx * uEx + w * w);
            double Ejet = Alpha1 * uRel * perimeter;
            double sinTheta = Math.Abs(w) / U;
            double Ecross = Alpha2 * uA * sinTheta * perimeter;
            // ambient turbulence: growth of the PG sigmas at the equivalent distances
            double sigY = Ry / Math.Sqrt(2.0), sigZ = Rz / Math.Sqrt(2.0);
            double dSy, dSz;
            if (UsePasquillGiffordSigmas) { dSy = PgSigmaYRate(sigY); dSz = PgSigmaZRate(sigZ); }
            else { dSy = SigmaRate(_pg[0], _pg[1], sigY); dSz = SigmaRate(_pg[2], _pg[3], sigZ); }
            // UDM transition to passive needs ALL of: Ri* < 15, relative density excess < 1.5 % (speed and
            // entrainment criteria are implicit here). Until then the grounded cloud is in the heavy regime:
            // no passive turbulent diffusion, dilution by the top entrainment kappa·u*/Phi(Ri*).
            double relExcess = Math.Max((rho - _rhoAir) / rho, 0.0);
            bool heavyRegime = st.grounded && gPrime > 0 && (Ri > RiTransition || relExcess > DensityTransition);
            double damp = 1.0;
            if (RiDamping > 0) damp = 1.0 / (1.0 + RiDamping * Math.Max(Ri, 0));
            double dRyPas = Math.Sqrt(2.0) * dSy, dRzPas = Math.Sqrt(2.0) * dSz * damp;
            double areaFac = AreaFactor(n, mm, st.grounded);
            double Eamb = AmbientFactor * uA * areaFac * (dRyPas * Rz + Ry * dRzPas);
            if (heavyRegime) { Eamb *= HeavyPassiveFactor; dRyPas *= HeavyPassiveFactor; }
            // gravity spreading, edge entrainment and shear-driven top entrainment (grounded heavy cloud)
            double uFront = 0, Eedge = 0, Etop = 0;
            if (st.grounded && gPrime > 0)
            {
                uFront = CE * Math.Sqrt(gPrime * H);
                Eedge = GammaEdge * 2 * H * uFront;
                if (heavyRegime) Etop = TopEntrainment * _ustar / PhiRi(Ri) * 2 * RyEff;
            }
            // UDM: max(jet + cross-wind, heavy-gas top entrainment) + passive (+ edge, zero for continuous releases)
            double Etot = Math.Max(Ejet + Ecross, Etop) + Eamb + Eedge;
            double dma = _rhoAir * Etot;

            // ---- momentum ----
            double dMx = dma * uA;
            if (st.grounded) dMx -= GroundFrictionCf * Math.Min(1.0, Ri) * rho * u * u * 2 * RyEff;   // friction only matters while the cloud is heavy
            double dMz = st.grounded ? 0 : G * (_rhoAir - rho) * A;

            // ---- energy ----
            double dHf = dma * CpAir * _tAir;
            if (st.grounded)
            {
                double hg = 5.0 + 3.8 * uA;                        // W/m2K, forced convection over ground
                dHf += hg * (_tAir - T) * 2 * RyEff / U;
            }

            // ---- geometry evolution ----
            double dRy;
            if (st.grounded) dRy = uFront / U + dRyPas;
            else
            {
                // airborne: keep the section circular while jet-dominated, let ambient growth widen it
                double Rcirc = Math.Sqrt(A / Math.PI);
                double relax = (Rcirc - Ry) / Math.Max(10 * Ry, 1.0);
                dRy = Math.Max(relax, 0) + dRyPas * 0.5 + (Ejet + Ecross) / Math.Max(U * perimeter, 1e-6) * Ry * 0.5;
                if (Ry > Rcirc) dRy = Math.Min(dRy, dRyPas);
            }

            double dx = u / U, dz = w / U, dt = 1.0 / U;
            if (_dump != null && _dumpOk)
                _dump.AppendLine(string.Join(";", new[] { st.x, st.z, Ry, Rz, n, mm, Ri, rho, Yc, T, U, u, uA, Ejet, Ecross, Eamb, Etop, Eedge, uFront, dRy, st.grounded ? 1 : 0 }
                    .Select(v => v.ToString("G5", System.Globalization.CultureInfo.InvariantCulture))));
            return new[] { dma, dMx, dMz, dHf, dx, dz, dRy, dt };
        }

        // Pasquill-Gifford-Turner curves, Martin (1976) fit: sigma_y = a·x^0.894, sigma_z = c·x^d + f  (x in km, sigmas in m)
        private void PgCoefficients(out double a, out double c, out double d, out double f)
        {
            switch (_cls)
            {
                case PasquillStabilityClass.A: a = 213; c = 440.8; d = 1.941; f = 9.27; break;
                case PasquillStabilityClass.B: a = 156; c = 106.6; d = 1.149; f = 3.3; break;
                case PasquillStabilityClass.C: a = 104; c = 61.0; d = 0.911; f = 0.0; break;
                case PasquillStabilityClass.E: a = 50.5; c = 22.8; d = 0.678; f = -1.3; break;
                case PasquillStabilityClass.F: a = 34.0; c = 14.35; d = 0.740; f = -0.35; break;
                default: a = 68.0; c = 34.3; d = 0.725; f = -1.7; break;
            }
        }
        private double PgSigmaYRate(double sigma)
        {
            PgCoefficients(out double a, out _, out _, out _);
            double xkm = Math.Pow(Math.Max(sigma, 1e-3) / a, 1.0 / 0.894);
            if (xkm < 0.001) xkm = 0.001;
            return a * 0.894 * Math.Pow(xkm, 0.894 - 1.0) / 1000.0;     // d sigma / dx, per metre
        }
        private double PgSigmaZRate(double sigma)
        {
            PgCoefficients(out _, out double c, out double d, out double f);
            c *= _z0Factor; f *= _z0Factor;
            double sigma1m = c * Math.Pow(0.001, d) + f;              // fit value at x = 1 m
            double rate1m = c * d * Math.Pow(0.001, d - 1.0) / 1000.0;
            if (sigma < sigma1m) return rate1m + (sigma1m - sigma);  // reach the fit within ~1 m
            double xkm = Math.Pow(Math.Max(sigma - f, 1e-3) / c, 1.0 / d);
            if (xkm < 0.001) xkm = 0.001;
            return c * d * Math.Pow(xkm, d - 1.0) / 1000.0;
        }

        private static double SigmaRate(double a, double b, double sigma)
        {
            // dsigma/dx of sigma = a·x^b at the equivalent distance x_eq = (sigma/a)^(1/b); floored at x = 1 m
            double xeq = Math.Pow(Math.Max(sigma, 1e-3) / a, 1.0 / b);
            if (xeq < 1.0) xeq = 1.0;
            return a * b * Math.Pow(xeq, b - 1.0);
        }

        private void Record(State st, double s)
        {
            Props(st, out double m, out double U, out double u, out double w, out double rho, out double T, out double Yc, out double Ry, out double Rz, out double Ri, out double A);
            Exponents(Math.Max((rho - _rhoAir) / rho, 0.0), out double n, out double mm);
            _path.Add(new Node { x = st.x, z = st.z, s = s, Ry = Ry, Rz = Rz, n = n, m = mm, U = U, rho = rho, Yc = Yc, cc = rho * Yc, T = T, Ri = Ri, grounded = st.grounded });
        }

        // ---------------------------------------------------------------- evaluation
        private bool Locate(double x, double y, double z, out Node nd, out double yLoc, out double xLoc)
        {
            nd = default; yLoc = 0; xLoc = 0;
            if (_path.Count == 0) return false;
            double dx = x - _originX, dy = y - _originY;
            xLoc = dx * _windDirX + dy * _windDirY;
            yLoc = dx * _windDirY - dy * _windDirX;
            if (xLoc < -_path[0].Ry) return false;
            // nearest centreline point in the (x, z) plane
            int lo = 0, hi = _path.Count - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (_path[mid].x < xLoc) lo = mid; else hi = mid; }
            int best = lo; double bd = double.MaxValue;
            for (int i = Math.Max(0, lo - 200); i <= Math.Min(_path.Count - 1, hi + 200); i++)
            {
                double ex = _path[i].x - xLoc, ez = _path[i].z - z;
                double d2 = ex * ex + 0.25 * ez * ez;
                if (d2 < bd) { bd = d2; best = i; }
            }
            nd = _path[best];
            return true;
        }

        private double Profile(Node nd, double yLoc, double z)
        {
            double fh = Math.Exp(-Math.Pow(Math.Abs(yLoc) / nd.Ry, nd.n));
            double fv;
            if (nd.grounded) fv = Math.Exp(-Math.Pow(Math.Max(z, 0) / nd.Rz, nd.m));
            else fv = Math.Exp(-Math.Pow(Math.Abs(z - nd.z) / nd.Rz, nd.m)) + Math.Exp(-Math.Pow(Math.Abs(z + nd.z) / nd.Rz, nd.m));
            return fh * Math.Min(fv, 1.0);
        }

        /// <summary>Gas concentration, kg/m³.</summary>
        public double EvaluateConcentration(double x, double y, double z)
        {
            if (!Locate(x, y, z, out var nd, out double yLoc, out _)) return 0;
            return nd.cc * Profile(nd, yLoc, z);
        }

        /// <summary>Gas mass fraction of the local mixture.</summary>
        public double EvaluateMassFraction(double x, double y, double z)
        {
            if (!Locate(x, y, z, out var nd, out double yLoc, out _)) return 0;
            double f = Profile(nd, yLoc, z);
            double Y = nd.Yc * f;
            return Math.Min(Y, 1.0);
        }

        /// <summary>Gas volume (mole) fraction of the local mixture.</summary>
        public double EvaluateVolumeFraction(double x, double y, double z)
        {
            double Y = EvaluateMassFraction(x, y, z);
            if (Y <= 0) return 0;
            double ng = Y / _mwGas, na = (1 - Y) / MwAir;
            return ng / (ng + na);
        }
    }
}
