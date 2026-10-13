using System;
using System.Globalization;
using DWSIMCore.Foundation.CalculatorInterface;
using DWSIMCore.Foundation.PropertyPackages;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Equilibrium thermodynamics of a dispersing cloud made of one released component (vapour and
    /// liquid aerosol) mixed with dry air, computed with the DWSIM core: a PH flash of
    /// {component, N2, O2, H2O} with the property package selected in the settings (PR78 by default).
    /// The entrained air is humid (relative humidity of the meteo), so water condenses in a cold cloud
    /// and releases its latent heat; ice is not modelled (water below 273 K stays liquid).
    /// One property-package instance is kept for the whole integration; each call builds a small
    /// material stream, so a flash costs a few milliseconds.
    /// </summary>
    public sealed class AerosolCloudThermo
    {
        private const double R = 8.314462;
        private readonly Calculator _calc;
        private readonly PropertyPackage _pp;
        private readonly string[] _comps;
        private readonly double[] _mw;
        private readonly double _P;
        private readonly double _wH2O;          // kg water vapour per kg of (moist) entrained air
        private double _lastRhoL = 0;
        private const double RhoWater = 1000.0;

        public string Compound => _comps[0];
        public double AirEnthalpyKJkg { get; }
        public double ComponentMolarMass => _mw[0];
        /// <summary>Water mass fraction of the entrained (moist) air.</summary>
        public double AirWaterMassFraction => _wH2O;

        public struct CloudState
        {
            public bool Ok;
            public double T, RhoMix, LiquidFraction, VaporDensity, LiquidDensity, VaporMolarMass;
            /// <summary>Condensed water mass flow / total water mass flow.</summary>
            public double WaterLiquidFraction;
            /// <summary>All liquid (component + water) / total cloud mass.</summary>
            public double TotalLiquidMassFraction;
        }

        private AerosolCloudThermo(Calculator calc, PropertyPackage pp, string compound, double P, double Tair, double rh)
        {
            _calc = calc; _pp = pp; _P = P;
            _comps = new[] { compound, "Nitrogen", "Oxygen", "Water" };
            _mw = new double[_comps.Length];
            for (int i = 0; i < _comps.Length; i++) _mw[i] = calc.AvailableCompounds[_comps[i]].Molar_Weight / 1000.0;
            // humidity ratio (kg water / kg dry air) from the Magnus saturation pressure, then per kg of moist air
            rh = Math.Max(0, Math.Min(1, rh));
            double tc = Tair - 273.15;
            double pSat = 610.94 * Math.Exp(17.625 * tc / (tc + 243.04));
            double pw = Math.Min(rh * pSat, 0.99 * P);
            double x = 0.622 * pw / (P - pw);
            _wH2O = x / (1 + x);
            var air = AirMoles(1.0);
            AirEnthalpyKJkg = DwsimThermo.GetMassEnthalpyKJperKg(new System.Collections.Generic.Dictionary<string, double> { { "Nitrogen", air[0] }, { "Oxygen", air[1] }, { "Water", air[2] } }, Tair, P);
        }

        /// <summary>Moles of N2, O2 and H2O in <paramref name="ma"/> kg of moist air.</summary>
        private double[] AirMoles(double ma)
        {
            double mDry = ma * (1 - _wH2O), mW = ma * _wH2O;
            return new[] { 0.767 * mDry / _mw[1], 0.233 * mDry / _mw[2], mW / _mw[3] };
        }

        /// <summary>Creates the helper, or returns null (with a reason) when DWSIM or the compound is unavailable.</summary>
        public static AerosolCloudThermo TryCreate(string compound, double pressurePa, double airTempK, double relativeHumidity, out string error)
        {
            error = null;
            if (!DwsimThermo.Initialize()) { error = "DWSIMCore not available: " + DwsimThermo.LastError; return null; }
            var calc = DwsimThermo.CoreCalculator;
            string resolved = ResolveName(calc, compound);
            if (resolved == null) { error = "compound not found in the DWSIM database: " + compound; return null; }
            foreach (var air in new[] { "Nitrogen", "Oxygen", "Water" })
                if (!calc.AvailableCompounds.ContainsKey(air)) { error = air + " not found in the DWSIM database"; return null; }
            var pp = DwsimThermo.CreatePropertyPackageInstance();
            if (pp == null) { error = "could not create the property package"; return null; }
            try { return new AerosolCloudThermo(calc, pp, resolved, pressurePa, airTempK, relativeHumidity); }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        private static string ResolveName(Calculator calc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (calc.AvailableCompounds.ContainsKey(name)) return name;
            foreach (var k in calc.AvailableCompounds.Keys)
                if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) return k;
            var alias = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "NH3", "Ammonia" }, { "CO2", "Carbon dioxide" }, { "Cl2", "Chlorine" }, { "CH4", "Methane" }, { "LNG", "Methane" },
                { "H2S", "Hydrogen sulfide" }, { "SO2", "Sulfur dioxide" }, { "C3H8", "Propane" }, { "LPG", "Propane" }, { "H2", "Hydrogen" }
            };
            if (alias.TryGetValue(name, out var a) && calc.AvailableCompounds.ContainsKey(a)) return a;
            return null;
        }

        /// <summary>Mass enthalpy (kJ/kg, DWSIM basis) of the pure component at T, P (any phase state).</summary>
        public double ComponentEnthalpyKJkg(double T, double P)
            => DwsimThermo.GetMassEnthalpyKJperKg(new System.Collections.Generic.Dictionary<string, double> { { _comps[0], 1.0 } }, T, P);

        /// <summary>PH flash of the cloud: mc, ma in kg/s, Hf in W (DWSIM enthalpy basis).</summary>
        public CloudState Flash(double mc, double ma, double HfWatt, double Tguess)
        {
            var st = new CloudState { Ok = false };
            double mTot = mc + ma;
            if (mTot <= 0) return st;
            double nC = mc / _mw[0];
            var nAir = AirMoles(ma);
            double nTot = nC + nAir[0] + nAir[1] + nAir[2];
            var z = new[] { nC / nTot, nAir[0] / nTot, nAir[1] / nTot, nAir[2] / nTot };
            double mWater = ma * _wH2O;
            double h = HfWatt / mTot / 1000.0;   // kJ/kg
            object[,] m;
            try { m = _calc.PHFlash(_pp, 2, _P, h, _comps, z, null, null, null, null, Tguess > 0 ? Tguess : 280.0); }
            catch { return st; }
            int nPh = m.GetLength(1);
            double T = ToD(m[_comps.Length + 2, 0]);
            if (double.IsNaN(T) || T < 50 || T > 1500) return st;
            double mVap = 0, mLiq = 0, mLiqComp = 0, mLiqWater = 0, MvSum = 0, fvSum = 0;
            for (int p = 0; p < nPh; p++)
            {
                string lbl = m[0, p] as string ?? "";
                double fp = ToD(m[1, p]);
                if (double.IsNaN(fp) || fp <= 0) continue;
                double[] x = new double[_comps.Length];
                double massP = 0;
                for (int i = 0; i < _comps.Length; i++) { x[i] = ToD(m[2 + i, p]); if (double.IsNaN(x[i])) x[i] = 0; massP += fp * nTot * x[i] * _mw[i]; }
                if (lbl.Equals("Vapor", StringComparison.OrdinalIgnoreCase))
                {
                    mVap += massP; fvSum += fp;
                    for (int i = 0; i < _comps.Length; i++) MvSum += fp * x[i] * _mw[i];
                }
                else if (lbl.StartsWith("Liquid", StringComparison.OrdinalIgnoreCase) || lbl.Equals("Aqueous", StringComparison.OrdinalIgnoreCase))
                {
                    mLiq += massP; mLiqComp += fp * nTot * x[0] * _mw[0]; mLiqWater += fp * nTot * x[3] * _mw[3];
                }
            }
            double Mv = fvSum > 0 ? MvSum / fvSum : _mw[1];
            double rhoV = _P * Mv / (R * T);
            double rhoLc = LiquidDensity(T);
            double rhoL = mLiq > 0 ? mLiq / (mLiqComp / Math.Max(rhoLc, 1) + mLiqWater / RhoWater + Math.Max(0, mLiq - mLiqComp - mLiqWater) / RhoWater) : rhoLc;
            double vol = mVap / rhoV + (rhoL > 0 ? mLiq / rhoL : 0);
            st.Ok = true; st.T = T; st.VaporDensity = rhoV; st.LiquidDensity = rhoL; st.VaporMolarMass = Mv;
            st.LiquidFraction = mc > 0 ? Math.Max(0, Math.Min(1, mLiqComp / mc)) : 0;
            st.WaterLiquidFraction = mWater > 0 ? Math.Max(0, Math.Min(1, mLiqWater / mWater)) : 0;
            st.TotalLiquidMassFraction = Math.Max(0, mLiq / mTot);
            st.RhoMix = vol > 0 ? mTot / vol : rhoV;
            return st;
        }

        private double LiquidDensity(double T)
        {
            try
            {
                string s = _calc.GetCompoundTDepProp(_comps[0], "densityOfLiquid", T);
                double v;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 1 && v < 5000) { _lastRhoL = v; return v; }
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v) && v > 1 && v < 5000) { _lastRhoL = v; return v; }
            }
            catch { }
            return _lastRhoL > 0 ? _lastRhoL : 600.0;
        }

        private static double ToD(object o)
        {
            if (o == null) return double.NaN;
            if (o is double d) return d;
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return double.NaN; }
        }
    }
}
