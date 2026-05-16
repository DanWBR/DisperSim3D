using System;
using System.Collections.Generic;
using System.Linq;
using DWSIMCore.Foundation.CalculatorInterface;
using DWSIMCore.Foundation.BaseClasses;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Thin wrapper around DWSIMCore.Foundation.CalculatorInterface.Calculator —
    /// the .NET 10 native rewrite of DWSIM's thermodynamic core.
    ///
    /// Replaces the old reflection-based wrapper around DWSIM.Automation.FluentAPI
    /// which had a hard dependency on BinaryFormatter (removed in .NET 10) and
    /// pulled the full 200+-DLL DWSIM install via runtime probing. DWSIMCore is
    /// a single project reference, builds cleanly on net10.0, and exposes the
    /// PT/PH/PS flash routines we need directly.
    ///
    /// Public surface is kept compatible with the old wrapper so callers
    /// (TwoPhaseSourceCalculator, DwsimMixtureBuilderDialog, etc.) need no
    /// changes beyond a rebuild.
    /// </summary>
    public static class DwsimThermo
    {
        private static Calculator _calc;
        private static List<string> _availableCompounds;
        private static string _propPackName = "Peng-Robinson 1978 (PR78)";
        private static readonly Dictionary<string, CompoundInfo> _compoundCache =
            new Dictionary<string, CompoundInfo>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True once <see cref="Initialize"/> has loaded the DWSIMCore
        /// compound databases (chemsep, dwsim, biodiesel, electrolyte, chedl).</summary>
        public static bool IsAvailable => _calc != null;

        /// <summary>Last error message from a failed Initialize/Compute call.</summary>
        public static string LastError { get; private set; } = "";

        /// <summary>Loads the DWSIMCore Calculator and primes the compound catalog.
        /// The <paramref name="dwsimInstallPath"/> parameter is accepted for backward
        /// compatibility with the old reflection wrapper but is ignored — DWSIMCore
        /// is a direct project reference, so no install path is needed.</summary>
        public static bool Initialize(string dwsimInstallPath = null)
        {
            if (_calc != null) return true;
            LastError = "";
            try
            {
                _calc = new Calculator();
                _calc.Initialize();
                _availableCompounds = null; // force re-enumeration
                return true;
            }
            catch (Exception ex)
            {
                var root = ex;
                while (root.InnerException != null) root = root.InnerException;
                LastError = "DWSIMCore.Calculator.Initialize: " + root.GetType().Name + " — " + root.Message;
                _calc = null;
                return false;
            }
        }

        /// <summary>Returns the list of all compounds loaded by DWSIMCore
        /// (typically ~700-1000 from ChemSep + DWSIM + biodiesel + electrolyte
        /// + ChEDL databases).</summary>
        public static IReadOnlyList<string> AvailableCompounds()
        {
            if (!IsAvailable) return new List<string>();
            if (_availableCompounds != null) return _availableCompounds;
            try
            {
                _availableCompounds = _calc.AvailableCompounds.Keys
                    .Select(k => k.ToString())
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return _availableCompounds;
            }
            catch (Exception ex)
            {
                LastError = "AvailableCompounds: " + ex.Message;
                return new List<string>();
            }
        }

        /// <summary>Drops the cached compound list and property cache. Call when
        /// the underlying Calculator state needs to be rebuilt.</summary>
        public static void ResetFlowsheetCache()
        {
            _availableCompounds = null;
            _compoundCache.Clear();
        }

        /// <summary>Sets the property package used for subsequent flash calls.
        /// Common values: "Peng-Robinson 1978 (PR78)", "Peng-Robinson (PR)",
        /// "Soave-Redlich-Kwong (SRK)", "Raoult's Law". Use <see cref="GetPropertyPackageList"/>
        /// to enumerate the installed packages.</summary>
        public static void SetPropertyPackage(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) _propPackName = name;
        }

        /// <summary>Returns the list of available property package names from DWSIMCore.</summary>
        public static IReadOnlyList<string> GetPropertyPackageList()
        {
            if (!IsAvailable) return new List<string>();
            try { return _calc.GetPropPackList(); }
            catch (Exception ex)
            {
                LastError = "GetPropPackList: " + ex.Message;
                return new List<string>();
            }
        }

        /// <summary>Constant per-compound properties pulled from DWSIM's database.
        /// All units are SI: M [kg/mol], Tc [K], Pc [Pa], Tb [K]. ω is dimensionless.</summary>
        public sealed class CompoundInfo
        {
            public string Name;
            public double MolarMassKgMol;
            public double CriticalTemperatureK;
            public double CriticalPressurePa;
            public double AcentricFactor;
            public double NormalBoilingPointK;
            public double CriticalVolumeM3Mol;
        }

        /// <summary>Looks up constant properties of a single compound. Cached.
        /// Returns null when the compound is unknown.</summary>
        public static CompoundInfo GetCompoundInfo(string compoundName)
        {
            if (string.IsNullOrEmpty(compoundName) || !IsAvailable) return null;
            if (_compoundCache.TryGetValue(compoundName, out var cached)) return cached;
            try
            {
                if (!_calc.AvailableCompounds.TryGetValue(compoundName, out var cp)) return null;
                var info = new CompoundInfo
                {
                    Name = compoundName,
                    // DWSIM stores Molar_Weight in g/mol; convert to kg/mol.
                    MolarMassKgMol      = cp.Molar_Weight / 1000.0,
                    CriticalTemperatureK = cp.Critical_Temperature,
                    CriticalPressurePa   = cp.Critical_Pressure,
                    AcentricFactor       = cp.Acentric_Factor,
                    NormalBoilingPointK  = cp.Normal_Boiling_Point,
                    // DWSIM stores Critical_Volume in L/mol; convert to m³/mol.
                    CriticalVolumeM3Mol  = cp.Critical_Volume / 1000.0,
                };
                _compoundCache[compoundName] = info;
                return info;
            }
            catch (Exception ex)
            {
                LastError = "GetCompoundInfo: " + ex.Message;
                return null;
            }
        }

        /// <summary>Container returned by <see cref="ComputeMixtureProperties"/>.</summary>
        public sealed class MixtureProperties
        {
            public double MolarMassKgMol;     // mixture molar mass
            public double DensityKgM3;        // mass density at T, P
            public double ViscosityPaS;       // dynamic viscosity
            public double CpJPerKgK;          // specific heat capacity
            public double VaporFraction;      // 0 = all liquid, 1 = all vapor
            public double GammaCpCv;          // ratio Cp/Cv
            public string Error;
        }

        /// <summary>Runs a PT flash on the supplied mole-fraction mixture and
        /// returns aggregate mixture properties (M, ρ, μ, Cp, γ, vapor fraction).
        /// Composition is normalised before sending to DWSIMCore.</summary>
        public static MixtureProperties ComputeMixtureProperties(
            IDictionary<string, double> moleFractions,
            double temperatureK,
            double pressurePa)
        {
            var result = new MixtureProperties();
            if (!IsAvailable) { result.Error = "DWSIMCore not initialised."; return result; }
            if (moleFractions == null || moleFractions.Count == 0)
            { result.Error = "Empty mixture composition."; return result; }
            try
            {
                double sum = 0;
                foreach (var v in moleFractions.Values) sum += v;
                if (sum <= 0) { result.Error = "All mole fractions are zero."; return result; }

                var compounds = moleFractions.Keys.ToArray();
                var fracs = compounds.Select(k => moleFractions[k] / sum).ToArray();

                // PT flash returns Object(,) matrix:
                //   row 0: phase labels (string)
                //   row 1: phase fractions (mole)
                //   rows 2..N+1: compound mole fractions per phase
                var flashMatrix = _calc.PTFlash(_propPackName, 2, pressurePa, temperatureK,
                    compounds, fracs);

                // Sum vapor phase fraction (DWSIM may report it under "Vapor" label).
                double xv = 0;
                int nPhases = flashMatrix.GetLength(1);
                for (int p = 0; p < nPhases; p++)
                {
                    var phaseLbl = flashMatrix[0, p] as string ?? "";
                    if (phaseLbl.Equals("Vapor", StringComparison.OrdinalIgnoreCase))
                    {
                        if (flashMatrix[1, p] is double f) xv += f;
                        else if (flashMatrix[1, p] != null) xv += Convert.ToDouble(flashMatrix[1, p]);
                    }
                }
                result.VaporFraction = xv;

                // Ask DWSIMCore for properties of the dominant phase (the one with
                // the larger fraction). For single-phase mixtures CalcProp under the
                // "Overall" label doesn't work — DWSIM requires the actual present
                // phase label ("Vapor" or "Liquid"). Mass-basis where applicable.
                string label = xv >= 0.5 ? "Vapor" : "Liquid";

                // Molar mass is composition-only, doesn't depend on phase.
                result.MolarMassKgMol = SumMolarMass(compounds, fracs);

                result.DensityKgM3 = CalcProp("density", label, "Mass",
                    compounds, fracs, temperatureK, pressurePa);
                if (double.IsNaN(result.DensityKgM3) || result.DensityKgM3 <= 0)
                {
                    // Fall back to ideal-gas density for vapor / Rackett-ish estimate for liquid.
                    if (xv >= 0.5)
                        result.DensityKgM3 = pressurePa * result.MolarMassKgMol / (8.314 * Math.Max(temperatureK, 100.0));
                    else
                        result.DensityKgM3 = 1000.0; // crude liquid default
                }
                result.ViscosityPaS = CalcProp("viscosity", label, "Mass",
                    compounds, fracs, temperatureK, pressurePa);
                if (double.IsNaN(result.ViscosityPaS)) result.ViscosityPaS = 1e-5;

                result.CpJPerKgK = CalcProp("heatCapacityCp", label, "Mass",
                    compounds, fracs, temperatureK, pressurePa) * 1000.0;
                if (double.IsNaN(result.CpJPerKgK) || result.CpJPerKgK <= 0) result.CpJPerKgK = 1000.0;

                double cv = CalcProp("heatCapacityCv", label, "Mass",
                    compounds, fracs, temperatureK, pressurePa) * 1000.0;
                result.GammaCpCv = (cv > 0 && !double.IsNaN(cv)) ? result.CpJPerKgK / cv : 1.3;

                return result;
            }
            catch (Exception ex)
            {
                var root = ex;
                while (root.InnerException != null) root = root.InnerException;
                result.Error = root.GetType().Name + ": " + root.Message;
                return result;
            }
        }

        /// <summary>Result of an isenthalpic (PH) flash: post-expansion state.</summary>
        public sealed class PHFlashResult
        {
            public double TemperatureK;        // resulting temperature
            public double VaporFraction;       // mass fraction in vapor phase (0..1)
            public double DensityKgM3;         // density at the flash state (dominant phase)
            public string Error;
        }

        /// <summary>Performs an isenthalpic (Joule-Thomson) flash at a given pressure.
        /// Use to model adiabatic free expansion of a pressurised fluid through an orifice
        /// or valve: from vessel state (P_v, T_v) compute h_vessel via <see cref="GetMassEnthalpyKJperKg"/>,
        /// then call this with (P_ambient, h_vessel) to obtain the post-expansion T and vapor fraction.
        /// Returns NaN values + Error string on failure.</summary>
        public static PHFlashResult PHFlash(
            IDictionary<string, double> moleFractions,
            double pressurePa,
            double massEnthalpyKJPerKg,
            double initialTKEstimate = 300.0)
        {
            var res = new PHFlashResult();
            if (!IsAvailable) { res.Error = "DWSIMCore not initialised."; return res; }
            try
            {
                double sum = 0;
                foreach (var v in moleFractions.Values) sum += v;
                if (sum <= 0) { res.Error = "All mole fractions are zero."; return res; }

                var compounds = moleFractions.Keys.ToArray();
                var fracs = compounds.Select(k => moleFractions[k] / sum).ToArray();

                // PH flash matrix layout (DWSIMCore Calculator.PHFlash):
                //   row 0:        phase labels (string)
                //   row 1:        phase mole fractions
                //   rows 2..N+1:  compound mole fractions per phase
                //   row N+2 @ col 0: final temperature in K
                var flash = _calc.PHFlash(_propPackName, 2, pressurePa, massEnthalpyKJPerKg,
                    compounds, fracs, null, null, null, null, initialTKEstimate);

                int nPhases = flash.GetLength(1);
                double xvMole = 0;
                for (int p = 0; p < nPhases; p++)
                {
                    var lbl = flash[0, p] as string ?? "";
                    if (lbl.Equals("Vapor", StringComparison.OrdinalIgnoreCase))
                    {
                        if (flash[1, p] is double f) xvMole += f;
                        else if (flash[1, p] != null) xvMole += Convert.ToDouble(flash[1, p]);
                    }
                }

                // Final temperature from the last row.
                int tRow = compounds.Length + 2;
                double T;
                var tObj = flash[tRow, 0];
                if (tObj is double td) T = td;
                else T = Convert.ToDouble(tObj);

                res.TemperatureK = T;

                // For mass-based vapor fraction we need the vapor and liquid molar masses;
                // for a single-compound flash, mass fraction == mole fraction.
                if (compounds.Length == 1)
                {
                    res.VaporFraction = xvMole;
                }
                else
                {
                    // Approximate: mass fraction ≈ mole fraction when mol weights are
                    // close. For accurate multi-component we'd need the per-phase
                    // molar mass which requires extra CalcProp calls. Single-compound
                    // covers all current use cases (pure Cl2/NH3/CO2 releases).
                    res.VaporFraction = xvMole;
                }

                // Density of the dominant phase at the flash state.
                string label = xvMole >= 0.5 ? "Vapor" : "Liquid";
                res.DensityKgM3 = CalcProp("density", label, "Mass",
                    compounds, fracs, T, pressurePa);
                if (double.IsNaN(res.DensityKgM3) || res.DensityKgM3 <= 0)
                {
                    // Ideal-gas fallback for vapor; rough water-like default for liquid.
                    double M = SumMolarMass(compounds, fracs);
                    res.DensityKgM3 = xvMole >= 0.5
                        ? pressurePa * M / (8.314 * Math.Max(T, 100.0))
                        : 1000.0;
                }
                return res;
            }
            catch (Exception ex)
            {
                var root = ex;
                while (root.InnerException != null) root = root.InnerException;
                res.Error = root.GetType().Name + ": " + root.Message;
                return res;
            }
        }

        /// <summary>Returns the mass enthalpy (kJ/kg) of a mixture at given T, P.
        /// Runs a PT flash internally to determine which phase is present then
        /// queries the property package for h_mass on that phase. Falls back to
        /// NaN on failure.</summary>
        public static double GetMassEnthalpyKJperKg(
            IDictionary<string, double> moleFractions,
            double temperatureK,
            double pressurePa)
        {
            if (!IsAvailable) return double.NaN;
            try
            {
                double sum = 0;
                foreach (var v in moleFractions.Values) sum += v;
                if (sum <= 0) return double.NaN;

                var compounds = moleFractions.Keys.ToArray();
                var fracs = compounds.Select(k => moleFractions[k] / sum).ToArray();

                // Probe vapor fraction via PT flash to pick the right phase label.
                var props = ComputeMixtureProperties(moleFractions, temperatureK, pressurePa);
                if (!string.IsNullOrEmpty(props.Error)) return double.NaN;
                string label = props.VaporFraction >= 0.5 ? "Vapor" : "Liquid";

                // CalcProp returns enthalpy in J/kg (SI mass basis) when basis="Mass".
                // PHFlash takes kJ/kg, so divide by 1000.
                double hJPerKg = CalcProp("enthalpy", label, "Mass",
                    compounds, fracs, temperatureK, pressurePa);
                if (double.IsNaN(hJPerKg)) return double.NaN;
                return hJPerKg / 1000.0;
            }
            catch
            {
                return double.NaN;
            }
        }

        /// <summary>Returns the composition-weighted molar mass in kg/mol.</summary>
        private static double SumMolarMass(string[] compounds, double[] moleFractions)
        {
            double sumMW = 0;
            for (int i = 0; i < compounds.Length; i++)
            {
                var info = GetCompoundInfo(compounds[i]);
                if (info != null) sumMW += moleFractions[i] * info.MolarMassKgMol;
            }
            return sumMW > 0 ? sumMW : 0.029; // default to air MW
        }

        /// <summary>Computes any single-phase property at a given (T, P, composition).
        /// Returns NaN on failure. Property names: "density", "enthalpy", "entropy",
        /// "viscosity", "heatCapacityCp", "heatCapacityCv", "molecularweight", etc.</summary>
        public static double CalcProp(string prop, string phaseLabel, string basis,
            string[] compounds, double[] moleFractions,
            double temperatureK, double pressurePa)
        {
            if (!IsAvailable) return double.NaN;
            try
            {
                var arr = _calc.CalcProp(_propPackName, prop, basis, phaseLabel,
                    compounds, temperatureK, pressurePa, moleFractions);
                if (arr == null || arr.Length == 0) return double.NaN;
                if (arr[0] is double d) return d;
                return Convert.ToDouble(arr[0]);
            }
            catch
            {
                return double.NaN;
            }
        }
    }
}
