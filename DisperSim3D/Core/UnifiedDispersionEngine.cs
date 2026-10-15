using System;
using System.Collections.Generic;
using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Unified integral dispersion model for a continuous release, following the formulation of the
    /// DNV UDM (Witlox &amp; Holt; UDM theory manual v8.5): one set of ordinary differential equations along
    /// the cloud centreline carries the cloud from a momentum jet through touchdown and the heavy-gas
    /// (grounded, gravity-spreading) phase into passive dispersion. No grid: the state is integrated once at
    /// <see cref="Initialize"/> and the concentration at a point is reconstructed from the stored centreline
    /// with the similarity profile  c(x,y,z) = c0(x) · exp(-(|y|/Ry)^m) · exp(-(|z - zcld|/Rz)^n).
    ///
    /// Closures (UDM): jet entrainment e_jet·ρa·P·|u_cld - u_a cosθ| (α1 = 0.17 on a Gaussian basis),
    /// cross-wind entrainment α2·ρa·P·|u_a sinθ| (0.35, near-field suppression), near-field passive
    /// entrainment (Disselhorst eddy scales), heavy-gas top entrainment κ·u*/Φ(Ri*) over the effective width,
    /// far-field passive entrainment from the growth of the McMullen σy (with averaging time) and Hosker σz
    /// (with roughness), gravity spreading dWeff/dt = C_E √(g' Heff) with C_E = 1.15, ground drag and impact
    /// forces, ground heat transfer (natural and forced convection), transition to passive on the four UDM
    /// criteria with a linear blending zone. Profile exponents: m(ρ excess) (2 → 50), n(Heff/|L|, stability).
    /// Two-phase sources carry the flashed liquid as aerosol with DWSIM PH flashes (see AerosolCloudThermo).
    /// </summary>
    public class UnifiedDispersionEngine : IConcentrationField
    {
        // ---- UDM model coefficients ----
        public double Alpha1 { get; set; } = 0.17;    // jet entrainment (Gaussian basis); e_jet = 0.5 α1/√π = 0.048 on the e^-1 perimeter
        public double Alpha2 { get; set; } = 0.35;    // cross-wind entrainment
        public double EpasCoefficient { get; set; } = 1.0;   // near-field passive entrainment (Disselhorst / HGSYSTEM)
        public double KappaTop { get; set; } = 0.4;   // heavy-gas top entrainment u_top = κ u*/Φ(Ri*)
        public double CE { get; set; } = 1.15;        // gravity spreading (Van Ulden)
        public double PoolAirFactor { get; set; } = 1.5;     // (vapour + air)/vapour mass leaving a pool / area source (pool vaporisation entrainment parameter)
        public double SuppressionK { get; set; } = 0.3;      // k in L_supp = (1 + k R) L_core for the cross-wind entrainment suppression (Morton / DNV); 0.3 with the near-field drag reproduces the Pratte-Baines rise of vertical jets
        public double NearFieldDragCd { get; set; } = 0.39;  // initial air-drag coefficient of the bent-over jet, phased out over 3 L_supp (Morton / DNV)
        public double RuPas { get; set; } = 0.1;      // transition: |u_cld/u_a - 1| < RuPas
        public double RrhoPas { get; set; } = 0.015;  // transition: |ρ/ρa - 1| < RrhoPas
        public double REPas { get; set; } = 0.3;      // transition: [1 - (Epas_nf + Ehvy)/Etot] < REPas
        public double RiPas { get; set; } = 15.0;     // transition (grounded): Ri* < RiPas
        public double RtrPas { get; set; } = 2.0;     // end of the transition zone at RtrPas·x_tr
        public double AveragingTimeS { get; set; } = 0;      // 0 = from the meteo (default 600 s, the basis of the McMullen σy)
        public bool PassiveAtActualDistance { get; set; } = false;  // true: UDM, dσ/dx of the ambient σ's at the downwind distance x; false (default): at the equivalent distance where the ambient σ equals the cloud σ (growth then depends on the actual turbulence / roughness)
        public bool UseHoskerSigmas { get; set; } = true;          // true: McMullen σy / Hosker σz (UDM); false: Pasquill-Gifford-Turner (Martin fit)
        public double StableBeta { get; set; } = 2.0;              // ψ = -β z/L for stable conditions (UDM/Irwin: 2.0)
        public double ConvectiveFactor { get; set; } = 1.0;        // weight of the convective velocity w* = (g Q Heff/(ρ cp T))^(1/3) from ground heating in u_T² = u*² + (ConvectiveFactor·w*)² (HEGADAS-type; 0 = off)
        public double PoolVaporisationFluxKgM2S { get; set; } = 0.167;   // cryogenic (boiling) pool: the pool area is at least rate / flux (LNG on water 0.085-0.195 kg/m2/s; PHMSA/FERC base case 0.167); 0 = use the given diameter only
        public double ConvectiveSigmaZ { get; set; } = 0;            // > 0: unstable classes add a convective vertical spread c·w*·t (w* from the class surface heat flux and mixing height) to the Hosker sigma_z in quadrature
        public double MaxDownwindFactor { get; set; } = 1.5;

        private const double G = 9.80665, R = 8.314462, Kappa = 0.4, MwAir = 0.028964, CpAir = 1005.0;
        private const double ZMinWind = 1.0, ZMaxWind = 200.0;

        private struct Node
        {
            public double x, z, s, Ry, Rz, m, n, U, rho, Yc, cc, T, Ri, fL, fW, hd;
            public bool grounded;
        }

        private readonly List<Node> _path = new List<Node>();
        private double _rhoAir, _tAir, _pAir, _ustar, _z0, _L, _tav, _tGnd;
        private MeteorologicalConditions _meteo;
        private double _windDirX, _windDirY, _originX, _originY;
        private PasquillStabilityClass _cls = PasquillStabilityClass.D;
        private double _clsIndex = 0;   // fractional class (1 = A ... 6 = F), 0 = integer class
        private double _nBase = 2.0;
        public IReadOnlyList<(double x, double z, double Ry, double Rz, double cc, double rho, bool grounded)> Centreline
        {
            get { var l = new List<(double, double, double, double, double, double, bool)>(); foreach (var p in _path) l.Add((p.x, p.z, p.Ry, p.Rz, p.cc, p.rho, p.grounded)); return l; }
        }

        private enum SpreadMode { Circular, Heavy, Passive }

        private class State
        {
            public double mc, ma, Ix, Iz, Hf, x, z, Ry, t;
            public State Clone() => (State)MemberwiseClone();
        }

        /// <summary>Derived cloud properties for a state.</summary>
        private struct Props
        {
            public double mcld, rho, T, Yc, ux, uz, ucld, theta, A, Ry, Rz, m, n, Cm, Cn, hd, Heff, Weff, zc, Wgnd, Pabove, Ri, uA, co, uT, Qgnd;
            public bool grounded;
        }

        private double _mwGas, _cpGas, _T0gas;
        // source description
        private bool _pool; private double _poolR, _poolRate;
        private bool _pointPassive;
        private double _jetD, _jetU, _jetU0wind, _rho0jet;
        // regime bookkeeping
        private SpreadMode _spread = SpreadMode.Circular;
        private double _xTr = double.NaN, _prevRyCirc = double.NaN, _prevS = double.NaN;
        private bool _upwind;
        // aerosol / DWSIM thermodynamics
        private AerosolCloudThermo _thermo;
        private bool _aerosol, _dry;
        private double _hAirJkg;
        private AerosolCloudThermo.CloudState _tc;
        private int _dryCount;
        public string ThermoNote { get; private set; } = "";

        private static bool TryEnv(string name, out double v)
        {
            v = 0; string e = Environment.GetEnvironmentVariable(name);
            return e != null && double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        /// <summary>Environment overrides for calibration runs (UDM_ALPHA1, UDM_ALPHA2, UDM_EPAS, UDM_KTOP, UDM_CE, UDM_POOLAIR,
        /// UDM_KSUPP, UDM_RITRANS, UDM_RHOCRIT, UDM_REPAS, UDM_RUPAS, UDM_RTR, UDM_TAV, UDM_PASSEQ=1, UDM_SIGMA=PG, UDM_BETA).</summary>
        private void ApplyEnvironmentOverrides()
        {
            double v;
            if (TryEnv("UDM_ALPHA1", out v)) Alpha1 = v;
            if (TryEnv("UDM_ALPHA2", out v)) Alpha2 = v;
            if (TryEnv("UDM_EPAS", out v)) EpasCoefficient = v;
            if (TryEnv("UDM_KTOP", out v)) KappaTop = v;
            if (TryEnv("UDM_CE", out v)) CE = v;
            if (TryEnv("UDM_POOLAIR", out v)) PoolAirFactor = v;
            if (TryEnv("UDM_KSUPP", out v)) SuppressionK = v;
            if (TryEnv("UDM_CDRAG", out v)) NearFieldDragCd = v;
            if (TryEnv("UDM_RITRANS", out v)) RiPas = v;
            if (TryEnv("UDM_RHOCRIT", out v)) RrhoPas = v;
            if (TryEnv("UDM_REPAS", out v)) REPas = v;
            if (TryEnv("UDM_RUPAS", out v)) RuPas = v;
            if (TryEnv("UDM_RTR", out v)) RtrPas = v;
            if (TryEnv("UDM_TAV", out v)) AveragingTimeS = v;
            if (TryEnv("UDM_PASSEQ", out v)) PassiveAtActualDistance = v <= 0;
            if (TryEnv("UDM_BETA", out v)) StableBeta = v;
            if (TryEnv("UDM_WSTAR", out v)) ConvectiveFactor = v;
            if (TryEnv("UDM_POOLFLUX", out v)) PoolVaporisationFluxKgM2S = v;
            if (TryEnv("UDM_CSIGZ", out v)) ConvectiveSigmaZ = v;
            string e = Environment.GetEnvironmentVariable("UDM_SIGMA");
            if (e != null) UseHoskerSigmas = !e.StartsWith("P", StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================ initialisation
        public void Initialize(DispersionScenario scenario)
        {
            ApplyEnvironmentOverrides();
            _path.Clear();
            _spread = SpreadMode.Circular; _xTr = double.NaN; _prevRyCirc = double.NaN; _prevS = double.NaN; _segments = null; _upwind = false;
            _pool = false; _pointPassive = false;
            _meteo = scenario.Meteo;
            _tAir = _meteo.AmbientTemperature > 0 ? _meteo.AmbientTemperature : 293.15;
            _pAir = _meteo.AmbientPressure > 0 ? _meteo.AmbientPressure : 101325.0;
            _rhoAir = _pAir * MwAir / (R * _tAir);
            _tGnd = _tAir;
            _z0 = _meteo.RoughnessLengthM > 0 ? _meteo.RoughnessLengthM : 0.03;
            _cls = _meteo.StabilityClass;
            _clsIndex = _meteo.StabilityIndex >= 1 && _meteo.StabilityIndex <= 6 ? _meteo.StabilityIndex : 0;
            if (_clsIndex > 0 && Math.Abs(_clsIndex - Math.Round(_clsIndex)) < 1e-6) { _cls = (PasquillStabilityClass)((int)Math.Round(_clsIndex) - 1); _clsIndex = 0; }
            _L = _clsIndex > 0 ? MoninObukhovFractional(_clsIndex, _z0) : MoninObukhov(_cls, _z0);
            _nBase = _clsIndex > 0 ? Interp(_clsIndex, 2.0, 2.0, 2.0, 2.0, 2.25, 2.5)
                   : ((_cls == PasquillStabilityClass.E) ? 2.25 : (_cls == PasquillStabilityClass.F ? 2.5 : 2.0));
            double zref = _meteo.WindMeasurementHeightM > 0 ? _meteo.WindMeasurementHeightM : 10.0;
            double uref = Math.Max(_meteo.WindSpeed, 0.3);
            _ustar = Kappa * uref / (Math.Log((zref + _z0) / _z0) - Psi(zref));
            _tav = AveragingTimeS > 0 ? AveragingTimeS : (_meteo.AveragingTimeS > 0 ? _meteo.AveragingTimeS : 600.0);
            InitConvective();
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
            _T0gas = T0;
            double rho0 = _pAir * _mwGas / (R * T0);
            double zR = Math.Max(src.EffectivePosition.Z, 0.0);
            double d = src.EffectiveDiameterM;
            double uExit = src.ExitVelocityMPerS > 0 ? src.ExitVelocityMPerS : src.ComputedExitVelocity;
            var dir = src.ReleaseDirection;
            double along = dir.X * _windDirX + dir.Y * _windDirY;

            var st = new State { mc = mg, ma = 0, x = 0, z = zR, t = 0 };
            // ---- two-phase (aerosol) source: DWSIM PH flash of the vessel state at ambient pressure ----
            _aerosol = false; _dry = false; _thermo = null; _dryCount = 0;
            if (src.CarryAerosol && src.HighPressureLeak != null)
            {
                string comp = !string.IsNullOrWhiteSpace(src.AerosolCompound) ? src.AerosolCompound : (src.Gas != null ? src.Gas.Name : null);
                double rh = _meteo.RelativeHumidity > 0 ? _meteo.RelativeHumidity : 0.5;
                _thermo = AerosolCloudThermo.TryCreate(comp, _pAir, _tAir, rh, out string err);
                if (_thermo != null)
                {
                    double hv = _thermo.ComponentEnthalpyKJkg(src.HighPressureLeak.VesselTemperatureK, src.HighPressureLeak.VesselPressurePa);
                    if (!double.IsNaN(hv))
                    {
                        _hAirJkg = _thermo.AirEnthalpyKJkg * 1000.0;
                        st.Hf = mg * hv * 1000.0;
                        _tc = _thermo.Flash(mg, 0, st.Hf, src.HighPressureLeak.VesselTemperatureK);
                        if (_tc.Ok)
                        {
                            _aerosol = true; _mwGas = _thermo.ComponentMolarMass;
                            rho0 = _tc.RhoMix; T0 = _tc.T;
                            ThermoNote = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "aerosol: {0} flashed to {1:F1} K, liquid fraction {2:F3}, rho {3:F2} kg/m3; air RH {5:P0} (DWSIM {4})", _thermo.Compound, _tc.T, _tc.LiquidFraction, _tc.RhoMix, DwsimThermo.PropertyPackageName, rh);
                        }
                        else ThermoNote = "aerosol: initial flash failed, ideal-gas path used";
                    }
                    else ThermoNote = "aerosol: vessel enthalpy unavailable, ideal-gas path used";
                }
                else ThermoNote = "aerosol: " + err + ", ideal-gas path used";
            }

            double xMax = Math.Max(scenario.DomainSizeM, 50.0) * MaxDownwindFactor;
            _pool = uExit < 1.5 && d > 0.5 && zR <= Math.Max(1.0, 0.5 * d) && !_aerosol;
            bool cryoPool = _pool && (src.Gas != null && src.Gas.IsCryogenic || T0 < 200);
            if (cryoPool && PoolVaporisationFluxKgM2S > 0) d = Math.Max(d, 2 * Math.Sqrt(mg / (PoolVaporisationFluxKgM2S * Math.PI)));
            _pointPassive = !_pool && !_aerosol && uExit <= 0.05 && d <= 0.1;
            if (_pool)
            {
                // ground-level area source: the vapour is added while the cloud crosses the pool (upwind edge → downwind edge),
                // the cloud moves with the wind and is held at least as wide as the pool chord
                _poolR = 0.5 * d; _poolRate = mg;
                double x0 = -_poolR * 0.98;
                st.x = x0; st.z = 0;
                st.mc = mg * PoolAreaFraction(x0);
                st.ma = Math.Max(PoolAirFactor - 1.0, 0.0) * st.mc;
                st.Ry = Math.Max(HalfChord(x0), 0.05);
                st.Hf = st.mc * _cpGas * T0 + st.ma * CpAir * _tAir;
                double ua0 = Ua(0.5);
                st.Ix = (st.mc + st.ma) * ua0; st.Iz = 0;
                _spread = SpreadMode.Heavy;
            }
            else if (_pointPassive)
            {
                // neutral point source without momentum: passive Gaussian plume from x = 1 m
                st.x = 1.0; st.z = zR;
                st.Ry = Math.Sqrt(2.0) * SigmaY(1.0);
                double rz = Math.Sqrt(2.0) * SigmaZ(1.0);
                double ua0 = Ua(zR + rz * 0.5);
                double cg = Gam(1.5);
                double hd0 = zR >= rz ? 1.0 : GammaP(0.5, Math.Pow(zR / rz, 2));
                double A0 = 2 * cg * cg * st.Ry * rz * (1 + hd0);
                st.ma = Math.Max(_rhoAir * ua0 * A0 - mg, 0.0);
                st.Hf = mg * _cpGas * T0 + st.ma * CpAir * _tAir;
                st.Ix = (mg + st.ma) * ua0; st.Iz = 0;
                _spread = SpreadMode.Passive; _xTr = 0;
            }
            else
            {
                double U0 = uExit > 0.05 ? uExit : Math.Max(Ua(zR), 0.5);
                double u0 = uExit > 0.05 ? U0 * along : U0;                       // < 0: release against the wind
                _upwind = u0 < 0;
                double w0 = uExit > 0.05 ? U0 * dir.Z : 0.0;
                if (Math.Abs(u0) < 0.05 * Ua(zR) && Math.Abs(w0) < 0.05 * Ua(zR)) u0 = 0.05 * Ua(zR);
                _jetD = d > 0 ? d : Math.Sqrt(4 * mg / (rho0 * Math.Max(U0, 0.1) * Math.PI));
                _jetU = Math.Sqrt(u0 * u0 + w0 * w0); _jetU0wind = Ua(zR); _rho0jet = rho0;
                double A0 = mg / (rho0 * Math.Max(_jetU, 1e-3));
                st.Ry = Math.Sqrt(A0 / Math.PI);     // circular; refined algebraically at the first step
                st.Ix = mg * u0; st.Iz = mg * w0;
                if (!_aerosol) st.Hf = mg * _cpGas * T0;
                _spread = SpreadMode.Circular;
            }
            Integrate(st, xMax);
        }

        // ================================================================ ambient
        private static double MoninObukhov(PasquillStabilityClass c, double z0)
        {
            double a, b;
            switch (c)
            {
                case PasquillStabilityClass.A: a = -11.4; b = 0.1; break;
                case PasquillStabilityClass.B: a = -26.0; b = 0.17; break;
                case PasquillStabilityClass.C: a = -123.0; b = 0.3; break;
                case PasquillStabilityClass.E: a = 123.0; b = 0.3; break;
                case PasquillStabilityClass.F: a = 26.0; b = 0.17; break;
                default: return 1e5;
            }
            return a * Math.Pow(z0, b);
        }

        /// <summary>Linear interpolation of a per-class value on the fractional class index (1 = A ... 6 = F).</summary>
        private static double Interp(double idx, double a, double b, double c, double d, double e, double f)
        {
            double[] v = { a, b, c, d, e, f };
            double t = Math.Max(1, Math.Min(6, idx)) - 1;
            int i = Math.Min((int)Math.Floor(t), 4);
            return v[i] + (t - i) * (v[i + 1] - v[i]);
        }
        private static double MoninObukhovFractional(double idx, double z0)
        {
            // interpolate 1/L between the neighbouring classes (D: 1/L = 0)
            var cls = new[] { PasquillStabilityClass.A, PasquillStabilityClass.B, PasquillStabilityClass.C, PasquillStabilityClass.D, PasquillStabilityClass.E, PasquillStabilityClass.F };
            double[] inv = new double[6];
            for (int i = 0; i < 6; i++) { double L = MoninObukhov(cls[i], z0); inv[i] = Math.Abs(L) >= 1e4 ? 0 : 1 / L; }
            double v = Interp(idx, inv[0], inv[1], inv[2], inv[3], inv[4], inv[5]);
            return Math.Abs(v) < 1e-5 ? 1e5 : 1 / v;
        }

        /// <summary>Businger/Paulson stability function ψ(z/L) for the wind profile.</summary>
        private double Psi(double z)
        {
            if (Math.Abs(_L) >= 1e4) return 0;
            double zl = z / _L;
            if (zl > 0) return -StableBeta * zl;
            double a = Math.Pow(1 - 15 * zl, 0.25);
            return 2 * Math.Log((1 + a) / 2) + Math.Log((1 + a * a) / 2) - 2 * Math.Atan(a) + Math.PI / 2;
        }

        /// <summary>Ambient wind speed at height z (logarithmic profile with stability correction, UDM cut-offs 1 m / 200 m).</summary>
        private double Ua(double z)
        {
            z = Math.Max(ZMinWind, Math.Min(ZMaxWind, z));
            return Math.Max(_ustar / Kappa * (Math.Log((z + _z0) / _z0) - Psi(z)), 0.2);
        }

        // ---- passive dispersion coefficients ----
        private void McMullen(out double I, out double J, out double K, out double Lc)
        {
            if (_clsIndex > 0)
            {
                I = Interp(_clsIndex, 5.357, 5.058, 4.651, 4.230, 3.922, 3.533);
                J = Interp(_clsIndex, 0.8828, 0.9024, 0.9181, 0.9222, 0.9222, 0.9181);
                K = Interp(_clsIndex, -0.0076, -0.0096, -0.0076, -0.0087, -0.0064, -0.0070);
                Lc = Interp(_clsIndex, 0.4481, 6.1992, 4.5704, 11.433, 2.2925, 2.8799);
                return;
            }
            switch (_cls)
            {
                case PasquillStabilityClass.A: I = 5.357; J = 0.8828; K = -0.0076; Lc = 0.4481; break;
                case PasquillStabilityClass.B: I = 5.058; J = 0.9024; K = -0.0096; Lc = 6.1992; break;
                case PasquillStabilityClass.C: I = 4.651; J = 0.9181; K = -0.0076; Lc = 4.5704; break;
                case PasquillStabilityClass.E: I = 3.922; J = 0.9222; K = -0.0064; Lc = 2.2925; break;
                case PasquillStabilityClass.F: I = 3.533; J = 0.9181; K = -0.0070; Lc = 2.8799; break;
                default: I = 4.230; J = 0.9222; K = -0.0087; Lc = 11.433; break;
            }
        }
        private void Hosker(out double a1, out double b1, out double a2, out double b2)
        {
            if (_clsIndex > 0)
            {
                a1 = Interp(_clsIndex, 0.112, 0.130, 0.112, 0.098, 0.0609, 0.0638);
                b1 = Interp(_clsIndex, 1.06, 0.950, 0.920, 0.889, 0.895, 0.783);
                a2 = Interp(_clsIndex, 5.38e-4, 6.52e-4, 9.05e-4, 1.35e-3, 1.96e-3, 1.36e-3);
                b2 = Interp(_clsIndex, 0.815, 0.750, 0.718, 0.688, 0.684, 0.672);
                return;
            }
            switch (_cls)
            {
                case PasquillStabilityClass.A: a1 = 0.112; b1 = 1.06; a2 = 5.38e-4; b2 = 0.815; break;
                case PasquillStabilityClass.B: a1 = 0.130; b1 = 0.950; a2 = 6.52e-4; b2 = 0.750; break;
                case PasquillStabilityClass.C: a1 = 0.112; b1 = 0.920; a2 = 9.05e-4; b2 = 0.718; break;
                case PasquillStabilityClass.E: a1 = 0.0609; b1 = 0.895; a2 = 1.96e-3; b2 = 0.684; break;
                case PasquillStabilityClass.F: a1 = 0.0638; b1 = 0.783; a2 = 1.36e-3; b2 = 0.672; break;
                default: a1 = 0.098; b1 = 0.889; a2 = 1.35e-3; b2 = 0.688; break;
            }
        }
        private static readonly double[] RoughZ0 = { 0.01, 0.04, 0.1, 0.4, 1.0, 4.0 };
        private static readonly double[] RoughC1 = { 1.56, 2.02, Math.E, 5.16, 7.37, 11.7 };
        private static readonly double[] RoughD1 = { 0.0480, 0.0269, 0, -0.098, -0.0957, -0.128 };
        private static readonly double[] RoughC2 = { 6.25e-4, 7.76e-4, 0, 18.6, 4.29e3, 4.59e4 };
        private static readonly double[] RoughD2 = { 0.45, 0.37, 0, -0.225, -0.60, -0.78 };
        private static double RoughF(int i, double x)
        {
            if (i == 2) return 1.0;
            double t1 = RoughC1[i] * Math.Pow(x, RoughD1[i]), t2 = RoughC2[i] * Math.Pow(x, RoughD2[i]);
            return RoughZ0[i] < 0.1 ? Math.Log(t1 / (1 + t2)) : Math.Log(t1 * (1 + 1 / t2));
        }
        private double RoughnessFactor(double x)
        {
            // below the Hosker table (z0 < 0.01 m) extrapolate with the Pasquill-Smith dependence sigma_z ~ z0^0.2 (smooth surfaces, sea)
            double smooth = _z0 < 0.01 ? Math.Pow(Math.Max(_z0, 1e-4) / 0.01, 0.2) : 1.0;
            double z0 = Math.Max(0.01, Math.Min(4.0, _z0));
            int i = 0; while (i < RoughZ0.Length - 2 && z0 > RoughZ0[i + 1]) i++;
            double fa = RoughF(i, x), fb = RoughF(i + 1, x);
            double w = (Math.Log(z0) - Math.Log(RoughZ0[i])) / (Math.Log(RoughZ0[i + 1]) - Math.Log(RoughZ0[i]));
            return smooth * (fa + Math.Max(0, Math.Min(1, w)) * (fb - fa));
        }
        // PG-Turner (Martin 1976) alternative
        private void PgCoefficients(out double a, out double c, out double dd, out double f)
        {
            switch (_cls)
            {
                case PasquillStabilityClass.A: a = 213; c = 440.8; dd = 1.941; f = 9.27; break;
                case PasquillStabilityClass.B: a = 156; c = 106.6; dd = 1.149; f = 3.3; break;
                case PasquillStabilityClass.C: a = 104; c = 61.0; dd = 0.911; f = 0.0; break;
                case PasquillStabilityClass.E: a = 50.5; c = 22.8; dd = 0.678; f = -1.3; break;
                case PasquillStabilityClass.F: a = 34.0; c = 14.35; dd = 0.740; f = -0.35; break;
                default: a = 68.0; c = 34.3; dd = 0.725; f = -1.7; break;
            }
        }

        /// <summary>Ambient cross-wind dispersion coefficient σya(x) (m), averaging time tav.</summary>
        private double SigmaY(double x)
        {
            double tavFac = Math.Pow(Math.Max(_tav, 18.75) / 600.0, 0.2);
            if (!UseHoskerSigmas)
            {
                PgCoefficients(out double a, out _, out _, out _);
                return tavFac * a * Math.Pow(Math.Max(x, 1e-3) / 1000.0, 0.894);
            }
            McMullen(out double I, out double J, out double K, out double Lc);
            double xe = Math.Max(x, Lc);
            double lx = Math.Log(xe / 1000.0);
            double s = tavFac * Math.Exp(I + J * lx + K * lx * lx);
            return x < Lc ? s * x / Lc : s;
        }
        private double _wStarCbl = 0, _ziCbl = 0;
        private void InitConvective()
        {
            _wStarCbl = 0; _ziCbl = 0;
            if (ConvectiveSigmaZ <= 0) return;
            double idx = _clsIndex > 0 ? _clsIndex : ((int)_cls + 1);
            if (idx >= 4) return;   // neutral / stable
            double H0 = Interp(idx, 250, 150, 90, 0, 0, 0), zi = Interp(idx, 1300, 920, 840, 800, 400, 100);
            _ziCbl = zi;
            _wStarCbl = Math.Pow(G * H0 * zi / (_rhoAir * CpAir * _tAir), 1.0 / 3);
        }

        /// <summary>Ambient vertical dispersion coefficient σza(x) (m) with the roughness correction (and the optional convective spread).</summary>
        private double SigmaZ(double x)
        {
            double sz = SigmaZBase(x);
            if (_wStarCbl > 0)
            {
                double t = x / Ua(10.0);
                double sc = ConvectiveSigmaZ * _wStarCbl * t;
                sz = Math.Min(Math.Sqrt(sz * sz + sc * sc), 0.75 * _ziCbl);
            }
            return sz;
        }
        private double SigmaZBase(double x)
        {
            if (!UseHoskerSigmas)
            {
                PgCoefficients(out _, out double c, out double dd, out double f);
                double zf = Math.Max(0.5, Math.Min(2.0, Math.Pow(_z0 / 0.03, 0.2)));
                return Math.Max(zf * (c * Math.Pow(Math.Max(x, 1e-3) / 1000.0, dd) + f), 1e-3);
            }
            Hosker(out double a1, out double b1, out double a2, out double b2);
            double xe = Math.Max(x, 100.0);
            double g = a1 * Math.Pow(xe, b1) / (1 + a2 * Math.Pow(xe, b2));
            double s = Math.Max(RoughnessFactor(xe), 0.05) * g;
            return x < 100.0 ? s * x / 100.0 : s;
        }
        private double DSigmaY(double x) { double h = Math.Max(0.01 * x, 0.01); return (SigmaY(x + h) - SigmaY(Math.Max(x - h, 1e-3))) / (x + h - Math.Max(x - h, 1e-3)); }
        private double DSigmaZ(double x) { double h = Math.Max(0.01 * x, 0.01); return (SigmaZ(x + h) - SigmaZ(Math.Max(x - h, 1e-3))) / (x + h - Math.Max(x - h, 1e-3)); }
        /// <summary>Downwind distance at which the ambient σ equals the given value (bisection).</summary>
        private double InverseSigma(Func<double, double> sig, double value)
        {
            double lo = 0.01, hi = 1e5;
            if (sig(lo) >= value) return lo;
            if (sig(hi) <= value) return hi;
            for (int i = 0; i < 60; i++) { double mid = Math.Sqrt(lo * hi); if (sig(mid) < value) lo = mid; else hi = mid; }
            return Math.Sqrt(lo * hi);
        }

        // ================================================================ special functions
        private static double Gam(double x)
        {
            double[] g = { 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
            if (x < 0.5) return Math.PI / (Math.Sin(Math.PI * x) * Gam(1 - x));
            x -= 1; double a = 0.99999999999980993; double t = x + 7.5;
            for (int i = 0; i < 8; i++) a += g[i] / (x + i + 1);
            return Math.Sqrt(2 * Math.PI) * Math.Pow(t, x + 0.5) * Math.Exp(-t) * a;
        }
        private static double LogGam(double x) => Math.Log(Gam(x));
        /// <summary>Regularised lower incomplete gamma function P(a, x).</summary>
        private static double GammaP(double a, double x)
        {
            if (x <= 0) return 0;
            if (x < a + 1)
            {
                double ap = a, sum = 1.0 / a, del = sum;
                for (int n = 0; n < 500; n++) { ap += 1; del *= x / ap; sum += del; if (Math.Abs(del) < Math.Abs(sum) * 1e-12) break; }
                return Math.Min(1.0, sum * Math.Exp(-x + a * Math.Log(x) - LogGam(a)));
            }
            double b = x + 1 - a, c = 1e300, dd = 1 / b, h = dd;
            for (int i = 1; i < 500; i++)
            {
                double an = -i * (i - a); b += 2; dd = an * dd + b; if (Math.Abs(dd) < 1e-300) dd = 1e-300;
                c = b + an / c; if (Math.Abs(c) < 1e-300) c = 1e-300; dd = 1 / dd; double del = dd * c; h *= del;
                if (Math.Abs(del - 1) < 1e-12) break;
            }
            return Math.Max(0.0, 1 - Math.Exp(-x + a * Math.Log(x) - LogGam(a)) * h);
        }
        /// <summary>Complete elliptic integral of the second kind E(m), m = k² in [0, 1] (Abramowitz &amp; Stegun 17.3.36).</summary>
        private static double EllipticE(double m)
        {
            double m1 = Math.Max(1 - m, 1e-12);
            if (m1 >= 1) return Math.PI / 2;
            double a = 1 + m1 * (0.44325141463 + m1 * (0.06260601220 + m1 * (0.04757383546 + m1 * 0.01736506451)));
            double b = m1 * (0.24998368310 + m1 * (0.09200180037 + m1 * (0.04069697526 + m1 * 0.00526449639)));
            return a - b * Math.Log(m1);
        }

        // ================================================================ cloud geometry and properties
        private double ExponentM(double rel) => rel <= 0 ? 2.0 : Math.Min(50.0, 2.0 + 12.0 * rel / (2.0 / rel + 0.25 * rel - 0.5));
        private double ExponentN(double Heff) { double xi = Math.Abs(_L) >= 1e4 ? 0 : Heff / Math.Abs(_L); return Math.Max(_nBase - 4.5 * xi / (1 + 9 * xi), 1.0); }

        private void Thermo(State st, out double rho, out double T)
        {
            double m = st.mc + st.ma;
            if (_aerosol && !_dry && _tc.Ok) { T = _tc.T; rho = _tc.RhoMix; return; }
            double cpMix = (st.mc * _cpGas + st.ma * CpAir) / m;
            T = Math.Max(st.Hf / (m * cpMix), 60.0);
            double ng = st.mc / _mwGas, na = st.ma / MwAir;
            rho = _pAir * (m / (ng + na)) / (R * T);
        }

        private Props Evaluate(State st)
        {
            var p = new Props();
            p.mcld = st.mc + st.ma;
            Thermo(st, out p.rho, out p.T);
            p.Yc = st.mc / p.mcld;
            p.ux = st.Ix / p.mcld; p.uz = st.Iz / p.mcld;
            p.grounded = st.z <= 1e-9;
            if (p.grounded) p.uz = 0;
            p.ucld = Math.Max(Math.Sqrt(p.ux * p.ux + p.uz * p.uz), 0.1);
            p.theta = Math.Atan2(p.uz, Math.Abs(p.ux) < 1e-6 ? 1e-6 : p.ux);
            // release against the wind: once the jet has stalled the gas is swept back at the wind speed, so the
            // cross-section follows continuity with at least the ambient velocity (u_cld alone would blow up at the stall)
            double uAdv = _upwind ? Math.Max(p.ucld, Ua(Math.Max(st.z, 0.0))) : p.ucld;
            p.A = p.mcld / (p.rho * uAdv);
            double rel = Math.Max((p.rho - _rhoAir) / _rhoAir, 0.0);
            p.m = ExponentM(rel); p.Cm = Gam(1 + 1 / p.m);
            // vertical exponent needs Heff: iterate n, Rz, hd
            p.n = 2.0; p.Cn = Gam(1.5);
            p.Ry = Math.Max(st.Ry, 1e-3); p.hd = p.grounded ? 0 : 1; p.Rz = p.Ry;
            for (int it = 0; it < 12; it++)
            {
                double Rz;
                if (_spread == SpreadMode.Circular)
                {
                    Rz = Math.Sqrt(p.A / (2 * p.Cm * p.Cn * (1 + p.hd)));
                    p.Ry = Rz;
                }
                else Rz = p.A / (2 * p.Cm * p.Cn * p.Ry * (1 + p.hd));
                Rz = Math.Max(Rz, 1e-3);
                double hd = p.grounded ? 0 : (st.z >= Rz ? 1.0 : GammaP(1 / p.n, Math.Pow(st.z / Rz, p.n)));
                double Heff = p.Cn * Rz;
                double n = ExponentN(Heff);
                bool conv = Math.Abs(Rz - p.Rz) < 1e-4 * Rz && Math.Abs(hd - p.hd) < 1e-4 && Math.Abs(n - p.n) < 1e-4;
                p.Rz = Rz; p.hd = hd; p.n = n; p.Cn = Gam(1 + 1 / n);
                if (conv) break;
            }
            p.Heff = p.Cn * p.Rz; p.Weff = p.Cm * p.Ry;
            // centroid height above the ground
            double zr = st.z / p.Rz;
            double num = Math.Pow(zr, 0) * Gam(2 / p.n) / p.n * (1 - GammaP(2 / p.n, Math.Pow(zr, p.n)));
            double zetaC = p.Rz * num / (p.Cn * (1 + p.hd));
            p.zc = Math.Max(st.z + zetaC, 0.01);
            p.Wgnd = p.grounded ? p.Ry : (st.z >= p.Rz ? 0.0 : p.Ry * Math.Pow(1 - Math.Pow(zr, p.n), 1 / p.m));
            // perimeter of the e^-1 contour above the ground
            double a = Math.Max(p.Ry, p.Rz), b = Math.Min(p.Ry, p.Rz);
            p.Pabove = 2 * (1 + p.hd) * a * EllipticE(1 - (b / a) * (b / a));
            p.uA = Ua(p.zc);
            // ground heat flux and the convective velocity scale it drives inside the cloud (used with u* in the heavy-gas entrainment)
            p.Qgnd = 0; p.uT = _ustar;
            if (p.Wgnd > 0 && _tGnd > p.T)
            {
                double dT = _tGnd - p.T;
                double Qnat = 1.52 * Math.Pow(dT, 4.0 / 3);
                double Qfor = p.rho * CpAir * _ustar * _ustar * dT / Math.Max(p.ucld, Ua(10)) * Math.Pow(0.71, -2.0 / 3);
                p.Qgnd = Math.Max(Qnat, Qfor);
                if (ConvectiveFactor > 0)
                {
                    double wStar = Math.Pow(G * p.Qgnd * p.Heff * (1 + p.hd) / (p.rho * CpAir * p.T), 1.0 / 3);
                    p.uT = Math.Sqrt(_ustar * _ustar + Math.Pow(ConvectiveFactor * wStar, 2));
                }
            }
            p.Ri = (p.Wgnd > 0) ? G * (p.rho - _rhoAir) * p.Heff * (1 + p.hd) / (_rhoAir * p.uT * p.uT) : 0.0;
            p.co = p.rho * p.Yc;
            return p;
        }

        /// <summary>UDM top-entrainment stability function Φ(Ri*): u_top = κ u*/Φ.</summary>
        private static double PhiRi(double ri)
        {
            if (ri < 0) return Math.Pow(1.0 + 0.65 * Math.Abs(ri), -0.6);
            if (ri < 2.3625) return 1.0;
            if (ri < 14.72) return Math.Sqrt(1.0 + 0.8 * ri) / 1.7;
            return ri / 7.0;
        }

        private double PoolAreaFraction(double x)
        {
            // fraction of the circular pool (radius _poolR, centre x=0) upwind of x
            double r = _poolR; if (x <= -r) return 0; if (x >= r) return 1;
            double u = x / r;
            return 0.5 + (u * Math.Sqrt(1 - u * u) + Math.Asin(u)) / Math.PI;
        }
        private double HalfChord(double x) { double r = _poolR; return Math.Abs(x) >= r ? 0 : Math.Sqrt(r * r - x * x); }

        // ================================================================ derivatives
        private System.Text.StringBuilder _dump;
        private bool _dumpOk;
        private double[] Derivs(State st, double s)
        {
            var p = Evaluate(st);
            double uA = p.uA;
            double sinT = Math.Abs(Math.Sin(p.theta)), cosT = Math.Cos(p.theta);
            double gndFrac = p.Ry > 0 ? Math.Max(0, Math.Min(1, p.Wgnd / p.Ry)) : 0;   // 0 aloft, 1 grounded

            // ---- entrainment terms (kg/m/s) ----
            double eJet = 0.5 * Alpha1 / Math.Sqrt(Math.PI);
            double Ejet = eJet * _rhoAir * p.Pabove * Math.Abs(p.ucld - uA * cosT);
            double fsupp = 1.0, cdAir = 0.0;
            if (!_pool && !_pointPassive && _jetU > 0.05)
            {
                double Lcore = Math.Max(6.4 * _jetD / (1 + 4.6 * (_jetU0wind / _jetU)), 0.5 * _jetD);
                double Rv = Math.Sqrt(_rho0jet * _jetU * _jetU / (_rhoAir * Math.Max(_jetU0wind * _jetU0wind, 1e-3)));
                double Lsupp = (1 + SuppressionK * Rv) * Lcore;
                fsupp = s <= Lcore ? 0 : (s >= Lsupp ? 1 : (s - Lcore) / Math.Max(Lsupp - Lcore, 1e-6));
                cdAir = NearFieldDragCd * Math.Max(0.0, 1 - s / (3 * Lsupp));
            }
            double Ecross = fsupp * Alpha2 * _rhoAir * p.Pabove * uA * sinT * (1 - gndFrac);
            // near-field passive (Disselhorst): eddy scales from the centroid height and the Monin-Obukhov length
            double Epnf = 0;
            if (gndFrac < 1)
            {
                double zz = p.zc + _z0;
                double Z = Math.Abs(_L) >= 1e4 ? 0 : zz / _L;
                double Ly, Lz, Ez;
                if (Z < 0) { Ez = 1 - 5 * Z; Ly = Lz = (1 - 7.4 * Z) / Ez; }
                else if (Z == 0) { Ez = Ly = Lz = 1; }
                else { Ez = 1 + 4 * Z; Ly = 1 / (1 + 0.1 * Z); Lz = 1 / Ez; }
                double eps = Ez * Math.Pow(_ustar, 3) / (Kappa * zz);
                double ly = Math.Min(p.Ry, 0.88 * zz * Ly), lz = Math.Min(p.Rz, 0.88 * zz * Lz);
                Epnf = _rhoAir * (1 - gndFrac) * EpasCoefficient * Math.Pow(eps, 1.0 / 3) * (Math.Pow(ly, 4.0 / 3) + Math.Pow(lz, 4.0 / 3));
            }
            double utop = 0, Ehvy = 0;
            if (gndFrac > 0)
            {
                utop = KappaTop * p.uT / PhiRi(p.Ri);
                Ehvy = _rhoAir * gndFrac * utop * 2 * p.Weff;
            }
            double EtotNf = gndFrac > 0 ? Epnf + Math.Max(Ejet + Ecross, Ehvy) : Ejet + Ecross + Epnf;

            // far-field passive entrainment and spread rate
            double xs = Math.Max(st.x, 0.5);
            double sy, sz, dsy, dsz;
            if (PassiveAtActualDistance) { sy = SigmaY(xs); sz = SigmaZ(xs); dsy = DSigmaY(xs); dsz = DSigmaZ(xs); }
            else
            {
                sy = p.Ry / Math.Sqrt(2.0); sz = p.Rz / Math.Sqrt(2.0);
                double xy = InverseSigma(SigmaY, sy), xz = InverseSigma(SigmaZ, sz);
                dsy = DSigmaY(xy); dsz = DSigmaZ(xz);
            }
            double relGrowth = dsy / Math.Max(sy, 1e-6) + dsz / Math.Max(sz, 1e-6);
            if (!p.grounded && st.z > 0)
            {
                // d ln(1 + hd)/dx with hd = P(1/n, (zcld/Rz)^n) evaluated for the ambient Rz = √2 σz
                double rz1 = Math.Sqrt(2.0) * sz, rz2 = Math.Sqrt(2.0) * (sz + dsz * Math.Max(0.01 * xs, 0.01));
                double h1 = st.z >= rz1 ? 1.0 : GammaP(1 / p.n, Math.Pow(st.z / rz1, p.n));
                double h2 = st.z >= rz2 ? 1.0 : GammaP(1 / p.n, Math.Pow(st.z / rz2, p.n));
                relGrowth += (Math.Log(1 + h2) - Math.Log(1 + h1)) / Math.Max(0.01 * xs, 0.01);
            }
            double Epff = Math.Max(_rhoAir * uA * p.A * relGrowth, 0.0);
            double dRyPas = Math.Sqrt(2.0) * dsy;

            // ---- transition to passive ----
            if (double.IsNaN(_xTr) && st.x > 0)
            {
                bool okU = Math.Abs(p.ucld / uA - 1) < RuPas;
                bool okRho = Math.Abs(p.rho / _rhoAir - 1) < RrhoPas;
                double passivePart = gndFrac > 0 ? (Epnf + Ehvy) : Epnf;
                bool okE = EtotNf <= 0 || (1 - passivePart / EtotNf) < REPas;
                bool okRi = gndFrac <= 0 || p.Ri < RiPas;
                if (okU && okRho && okE && okRi) _xTr = st.x;
            }
            double f = 0;
            if (!double.IsNaN(_xTr))
            {
                if (_xTr <= 0) f = 1;
                else f = Math.Max(0, Math.Min(1, (st.x - _xTr) / Math.Max((RtrPas - 1) * _xTr, 1e-6)));
            }
            double Etot = (1 - f) * EtotNf + f * Epff;
            double dma = Etot;

            // ---- pool vapour addition ----
            double dmc = 0, dmaPool = 0;
            if (_pool && Math.Abs(st.x) < _poolR)
            {
                dmc = _poolRate * 2 * HalfChord(st.x) / (Math.PI * _poolR * _poolR);
                dmaPool = Math.Max(PoolAirFactor - 1.0, 0.0) * dmc;
            }

            // ---- momentum (fluxes, N/m) ----
            double dIx = (dma + dmaPool) * uA + dmc * uA;
            double dIz = 0;
            if (gndFrac > 0)
                dIx += 2 * p.Wgnd * _rhoAir * _ustar * _ustar * (1 - p.ucld * p.ucld / (uA * uA)) * cosT;   // UDM ground drag
            if (!p.grounded)
            {
                dIz += -G * (p.rho - _rhoAir) * p.A;                                                      // buoyancy
                if (cdAir > 0 && sinT > 0)
                {
                    // near-field air drag on the bent-over jet (Ooms form), normal to the axis with a downwind component
                    double Fd = cdAir * _rhoAir * p.Pabove * uA * uA * sinT * sinT;
                    double sgn = Math.Sign(p.theta);
                    dIx += Fd * sinT; dIz += -Fd * cosT * sgn;
                }
                if (gndFrac > 0 && p.uz < 0)
                {
                    // ground impact force: perpendicular to the centreline, magnitude 2 Wgnd ρ u² |tanθ|
                    double Fimp = 2 * p.Wgnd * p.rho * p.ucld * p.ucld * Math.Abs(Math.Tan(p.theta));
                    dIx += -Fimp * Math.Sin(p.theta); dIz += Fimp * Math.Cos(p.theta);
                }
            }

            // ---- energy (W/m) ----
            double dHf = (_aerosol && !_dry) ? dma * _hAirJkg : dma * CpAir * _tAir;
            dHf += dmaPool * CpAir * _tAir + dmc * _cpGas * _T0gas;
            if (gndFrac > 0 && p.Qgnd > 0) dHf += p.Qgnd * 2 * p.Wgnd;   // McAdams natural / Holman forced convection from the ground

            // ---- cross-wind spreading ----
            double dRy = 0;
            double gP = (p.rho - _rhoAir) / p.rho;
            double heavyRate = (gndFrac > 0 && gP > 0) ? CE / (p.Cm * Math.Max(Math.Abs(p.ux), 0.2)) * Math.Sqrt(G * gP * p.Heff * (1 + p.hd)) : 0;
            double chordRate = 0;
            if (_pool && st.x < 0 && Math.Abs(st.x) < _poolR && p.Ry < HalfChord(st.x)) chordRate = -st.x / HalfChord(st.x);
            switch (_spread)
            {
                case SpreadMode.Circular: dRy = 0; break;   // algebraic (Ry = Rz)
                case SpreadMode.Heavy: dRy = Math.Max(Math.Max(heavyRate, dRyPas), chordRate); break;
                default: dRy = dRyPas; break;
            }
            if (!double.IsNaN(_xTr) && _spread != SpreadMode.Passive) dRy = (1 - f) * dRy + f * dRyPas;

            double dx = cosT, dz = p.grounded ? 0 : Math.Sin(p.theta), dt = 1.0 / p.ucld;
            if (_dump != null && _dumpOk)
                _dump.AppendLine(string.Join(";", new[] { st.x, st.z, p.Ry, p.Rz, p.m, p.n, p.Ri, p.rho, p.Yc, p.T, p.ucld, p.ux, uA, Ejet, Ecross, Epnf, Ehvy, Epff, f, utop, dRy, p.hd, p.Wgnd, (double)(int)_spread }
                    .Select(v => v.ToString("G5", System.Globalization.CultureInfo.InvariantCulture))));
            return new[] { dma + dmaPool, dIx, dIz, dHf, dx, dz, dRy, dt, dmc };
        }

        private State Advance(State st, double[] k, double ds)
        {
            var n = st.Clone();
            n.ma = Math.Max(st.ma + k[0] * ds, 0); n.Ix = st.Ix + k[1] * ds; n.Iz = st.z <= 1e-9 ? 0 : st.Iz + k[2] * ds;
            n.Hf = st.Hf + k[3] * ds; n.x = st.x + k[4] * ds; n.z = st.z <= 1e-9 ? 0 : Math.Max(st.z + k[5] * ds, 0);
            n.Ry = Math.Max(st.Ry + k[6] * ds, 1e-3); n.t = st.t + k[7] * ds; n.mc = st.mc + k[8] * ds;
            return n;
        }

        // ================================================================ integration
        private void Integrate(State st, double xMax)
        {
            int steps = 0; double s = 0;
            string dumpPath = Environment.GetEnvironmentVariable("UDM_DUMP");
            if (!string.IsNullOrEmpty(dumpPath)) { _dump = new System.Text.StringBuilder(); _dump.AppendLine("x;z;Ry;Rz;m;n;Ri;rho;Yc;T;U;ux;uA;Ejet;Ecross;Epnf;Ehvy;Epff;f;utop;dRy;hd;Wgnd;mode"); }
            if (_spread == SpreadMode.Circular) { var p0 = Evaluate(st); st.Ry = p0.Ry; }
            Record(st, s);
            while (st.x < xMax && steps < 400000)
            {
                var pr = Evaluate(st);
                double scale = Math.Max(Math.Max(pr.Ry, pr.Rz), 0.05);
                double ds = Math.Min(Math.Max(0.02 * scale, 0.005), 5.0);
                if (_pool && Math.Abs(st.x) < 1.2 * _poolR) ds = Math.Min(ds, Math.Max(_poolR / 40.0, 0.005));
                if (!pr.grounded && st.z > 0 && pr.uz < 0) ds = Math.Min(ds, Math.Max(0.1 * st.z / Math.Max(-pr.uz / pr.ucld, 1e-3), 0.005));
                var k1 = Derivs(st, s); var s1 = Advance(st, k1, 0.5 * ds);
                var k2 = Derivs(s1, s + 0.5 * ds); var s2 = Advance(st, k2, 0.5 * ds);
                var k3 = Derivs(s2, s + 0.5 * ds); var s3 = Advance(st, k3, ds);
                var k4 = Derivs(s3, s + ds);
                var k = new double[k1.Length];
                for (int i = 0; i < k.Length; i++) k[i] = (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]) / 6.0;
                _dumpOk = true; Derivs(st, s); _dumpOk = false;
                st = Advance(st, k, ds);
                s += ds; steps++;
                UpdateThermo(st);
                PostStep(st, s, ds);
                if ((steps % 2) == 0 || st.z <= 1e-9) Record(st, s);
                if (double.IsNaN(st.x) || double.IsNaN(st.Hf) || (!_aerosol && st.Hf <= 0)) break;
                if (st.mc / (st.mc + st.ma) < 1e-7) break;
            }
            if (_dump != null) { try { System.IO.File.WriteAllText(dumpPath, _dump.ToString()); } catch { } _dump = null; }
        }

        /// <summary>Regime bookkeeping after a step: touchdown of the centre-line, circular → heavy spreading, passive.</summary>
        private void PostStep(State st, double s, double ds)
        {
            if (st.z <= 1e-9) { st.z = 0; st.Iz = 0; }
            var p = Evaluate(st);
            if (_spread == SpreadMode.Circular)
            {
                st.Ry = p.Ry;      // keep the section circular
                if (p.Wgnd > 0 && !double.IsNaN(_prevRyCirc))
                {
                    double circRate = (p.Ry - _prevRyCirc) / Math.Max(s - _prevS, 1e-9);
                    double gP = (p.rho - _rhoAir) / p.rho;
                    double heavyRate = gP > 0 ? CE / (p.Cm * Math.Max(Math.Abs(p.ux), 0.2)) * Math.Sqrt(G * gP * p.Heff * (1 + p.hd)) : 0;
                    if (heavyRate > circRate || (p.grounded && gP <= 0)) _spread = SpreadMode.Heavy;
                }
                _prevRyCirc = p.Ry; _prevS = s;
            }
            if (!double.IsNaN(_xTr) && _spread != SpreadMode.Passive && st.x >= RtrPas * Math.Max(_xTr, 1e-6)) _spread = SpreadMode.Passive;
        }

        /// <summary>Aerosol path: PH flash of the new state (once per step); switch to the ideal-gas enthalpy
        /// basis once the liquid has evaporated.</summary>
        private void UpdateThermo(State st)
        {
            if (!_aerosol || _dry) return;
            var tc = _thermo.Flash(st.mc, st.ma, st.Hf, _tc.T);
            if (tc.Ok) _tc = tc;
            if (_tc.TotalLiquidMassFraction <= 1e-6) _dryCount++; else _dryCount = 0;
            if (_dryCount >= 10)
            {
                double m = st.mc + st.ma;
                double cpMix = (st.mc * _cpGas + st.ma * CpAir) / m;
                st.Hf = m * cpMix * _tc.T;
                _dry = true;
            }
        }

        private void Record(State st, double s)
        {
            var p = Evaluate(st);
            _path.Add(new Node { x = st.x, z = st.z, s = s, Ry = p.Ry, Rz = p.Rz, m = p.m, n = p.n, U = p.ucld, rho = p.rho, Yc = p.Yc, cc = p.co, T = p.T, Ri = p.Ri, hd = p.hd,
                fL = (_aerosol && !_dry) ? _tc.LiquidFraction : 0, fW = (_aerosol && !_dry) ? _tc.WaterLiquidFraction : 0, grounded = p.grounded });
        }

        // ================================================================ evaluation
        private List<(int i0, int i1)> _segments;

        /// <summary>Splits the path into segments of monotonic x (one for a downwind release; two or more when the jet
        /// goes against the wind, stalls and comes back).</summary>
        private void EnsureSegments()
        {
            if (_segments != null && _segments.Count > 0 && _segments[_segments.Count - 1].i1 == _path.Count - 1) return;
            _segments = new List<(int, int)>();
            int n = _path.Count; if (n == 0) return;
            int start = 0; int dir = 0;
            for (int i = 1; i < n; i++)
            {
                double d = _path[i].x - _path[i - 1].x;
                int sd = d > 1e-9 ? 1 : (d < -1e-9 ? -1 : 0);
                if (sd == 0) continue;
                if (dir == 0) dir = sd;
                else if (sd != dir) { _segments.Add((start, i - 1)); start = i - 1; dir = sd; }
            }
            _segments.Add((start, n - 1));
        }

        private int NearestInSegment(int i0, int i1, double xLoc, double z)
        {
            bool inc = _path[i1].x >= _path[i0].x;
            int lo = i0, hi = i1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                bool less = inc ? _path[mid].x < xLoc : _path[mid].x > xLoc;
                if (less) lo = mid; else hi = mid;
            }
            int best = lo; double bd = double.MaxValue;
            for (int i = lo; i >= i0; i--)
            {
                double ex = _path[i].x - xLoc; if (ex * ex > bd) break;
                double ez = _path[i].z - z, d2 = ex * ex + ez * ez;
                if (d2 < bd) { bd = d2; best = i; }
            }
            for (int i = lo + 1; i <= i1; i++)
            {
                double ex = _path[i].x - xLoc; if (ex * ex > bd) break;
                double ez = _path[i].z - z, d2 = ex * ex + ez * ez;
                if (d2 < bd) { bd = d2; best = i; }
            }
            return best;
        }

        /// <summary>Finds, on every monotonic segment of the path, the centreline node nearest to the point (exact search pruned
        /// with the monotonic x) and keeps the node whose profile gives the largest mass fraction at the point; returns the
        /// crosswind offset and the offset perpendicular to the local trajectory in the (x, z) plane.</summary>
        private bool Locate(double x, double y, double z, out Node nd, out double yLoc, out double dPerp)
        {
            nd = default; yLoc = 0; dPerp = 0;
            if (_path.Count == 0) return false;
            double dx = x - _originX, dy = y - _originY;
            double xLoc = dx * _windDirX + dy * _windDirY;
            yLoc = dx * _windDirY - dy * _windDirX;
            EnsureSegments();
            int n = _path.Count; double bestVal = -1; bool found = false;
            foreach (var seg in _segments)
            {
                int best = NearestInSegment(seg.i0, seg.i1, xLoc, z);
                var cand = _path[best];
                int j0 = Math.Max(best - 1, seg.i0), j1 = Math.Min(best + 1, seg.i1);
                // distance to the local polyline (the two sub-segments around the nearest node): equals the perpendicular
                // offset inside the path and the distance to the end node beyond the nozzle or the stall point
                double dp = cand.grounded ? 0 : Math.Min(DistToSegment(j0, best, xLoc, z), DistToSegment(best, j1, xLoc, z));
                double val = cand.Yc * Profile(cand, yLoc, z, dp);
                if (val > bestVal) { bestVal = val; nd = cand; dPerp = dp; found = true; }
            }
            return found;
        }

        private double DistToSegment(int ia, int ib, double x, double z)
        {
            double ax = _path[ia].x, az = _path[ia].z, bx = _path[ib].x, bz = _path[ib].z;
            double vx = bx - ax, vz = bz - az, l2 = vx * vx + vz * vz;
            double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((x - ax) * vx + (z - az) * vz) / l2));
            double px = ax + t * vx - x, pz = az + t * vz - z;
            return Math.Sqrt(px * px + pz * pz);
        }

        private static double Profile(Node nd, double yLoc, double z, double dPerp)
        {
            if (z < 0) return 0;
            double fh = Math.Exp(-Math.Pow(Math.Abs(yLoc) / nd.Ry, nd.m));
            double fv = nd.grounded ? Math.Exp(-Math.Pow(Math.Max(z, 0) / nd.Rz, nd.n)) : Math.Exp(-Math.Pow(dPerp / nd.Rz, nd.n));
            return fh * fv;
        }

        /// <summary>Gas concentration, kg/m³.</summary>
        public double EvaluateConcentration(double x, double y, double z)
        {
            if (!Locate(x, y, z, out var nd, out double yLoc, out double dp)) return 0;
            return nd.cc * Profile(nd, yLoc, z, dp);
        }

        /// <summary>Gas mass fraction of the local mixture.</summary>
        public double EvaluateMassFraction(double x, double y, double z)
        {
            if (!Locate(x, y, z, out var nd, out double yLoc, out double dp)) return 0;
            return Math.Min(nd.Yc * Profile(nd, yLoc, z, dp), 1.0);
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
