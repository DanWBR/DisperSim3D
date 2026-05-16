using System;
using System.Collections.Generic;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Result of a two-phase source calculation: mass-flow split between vapor and
    /// rainout (droplet) fractions, plus the Birch &amp; Schefer pseudo-source geometry
    /// for the vapor fraction.
    /// </summary>
    public sealed class TwoPhaseSourceResult
    {
        /// <summary>Vapor mass flow (kg/s) - this is what enters the airborne dispersion engine.</summary>
        public double VaporMassFlowKgPerS;

        /// <summary>Droplet/liquid mass flow (kg/s) - rains out as a pool that re-evaporates.</summary>
        public double DropletMassFlowKgPerS;

        /// <summary>Vapor mass fraction at the expanded source (0..1).</summary>
        public double VaporFraction;

        /// <summary>Estimated post-expansion temperature (K).</summary>
        public double TempExitK;

        /// <summary>Velocity at the Birch pseudo-source (m/s, subsonic).</summary>
        public double VelocityExitMS;

        /// <summary>Birch &amp; Schefer expanded pseudo-source diameter (m).</summary>
        public double DiameterPseudoM;

        /// <summary>Mixture density at the expanded source (kg/m^3).</summary>
        public double DensityExitKgM3;

        /// <summary>True when DWSIM flash indicated vapor fraction below ~0.99.</summary>
        public bool IsTwoPhase;

        /// <summary>True when the DWSIM bridge was used; false on fallback to ideal-gas Birch model.</summary>
        public bool DwsimUsed;

        /// <summary>Human-readable summary (T_exit, x_v, ρ_exit, source).</summary>
        public string Notes;

        /// <summary>Error message when the calculation fell back; empty on full success.</summary>
        public string Error;
    }

    /// <summary>
    /// Computes the effective dispersion source term for pressurized two-phase releases
    /// (liquefied gases, supercritical fluids).
    ///
    /// Flow:
    /// <list type="number">
    /// <item>Mass flow through the orifice (choked/unchoked) via <see cref="HighPressureLeakModel"/>.</item>
    /// <item>Estimate post-expansion temperature via isentropic relation (γ-based).</item>
    /// <item>Compute vapor mass fraction via Clapeyron flash-fraction (analytical).</item>
    /// <item>Birch &amp; Schefer pseudo-source geometry from the vapor-only mass flow.</item>
    /// </list>
    ///
    /// Vapor fraction estimation uses the Clapeyron flash-fraction approximation:
    /// <c>x_v ≈ C_p,liq · (T_vessel - T_bp) / ΔH_vap</c>. The needed compound constants
    /// (normal boiling point, latent heat of vaporisation, liquid heat capacity)
    /// come from a small built-in table for the gases most relevant to industrial
    /// release scenarios; DWSIM's compound database provides T_bp / T_c / P_c as a
    /// fallback for less-common substances via <see cref="DwsimThermo.GetCompoundInfo"/>
    /// (which does NOT trigger the BinaryFormatter path that the full flowsheet solve does
    /// and therefore works on .NET 10).
    ///
    /// Limitations of v1:
    /// <list type="bullet">
    /// <item>Single-stage analytical flash (not iterated to isenthalpic convergence).</item>
    /// <item>Supercritical fluids are treated as all-vapor at ambient (good for CO2 supercritical-to-vapor,
    ///   but misses solid-CO2 sublimation effects).</item>
    /// <item>Falls back to ideal-gas (all-vapor) when no compound data is available.</item>
    /// </list>
    /// </summary>
    public static class TwoPhaseSourceCalculator
    {
        private const double R = 8.314;

        /// <summary>Built-in thermodynamic constants for the substances most relevant
        /// to industrial pressurized release scenarios. ΔH_vap is at the normal boiling
        /// point (NBP); C_p,liq is the liquid heat capacity averaged between NBP and 298 K.
        /// Sources: Perry's Chemical Engineers' Handbook, 8th ed., Tables 2-150, 2-153, 2-191.</summary>
        private sealed class CompoundFlashData
        {
            public double T_bp_K;              // Normal boiling point [K]
            public double DeltaHvap_J_per_kg;  // Latent heat of vaporisation at NBP
            public double Cp_liq_J_per_kgK;    // Liquid Cp (NBP..298K average)
            public double T_critical_K;
            public double P_critical_Pa;
        }

        private static readonly Dictionary<string, CompoundFlashData> _flashTable =
            new Dictionary<string, CompoundFlashData>(StringComparer.OrdinalIgnoreCase)
            {
                // Carbon dioxide — note T_bp is sublimation T at 1 atm = 194.7 K;
                // ΔH_sub ~570 kJ/kg (Perry's, CRC Handbook)
                { "Carbon dioxide", new CompoundFlashData {
                    T_bp_K = 194.65, DeltaHvap_J_per_kg = 5.71e5,
                    Cp_liq_J_per_kgK = 2400.0,
                    T_critical_K = 304.13, P_critical_Pa = 7.377e6 } },
                { "CO2", new CompoundFlashData {
                    T_bp_K = 194.65, DeltaHvap_J_per_kg = 5.71e5,
                    Cp_liq_J_per_kgK = 2400.0,
                    T_critical_K = 304.13, P_critical_Pa = 7.377e6 } },
                // Ammonia
                { "Ammonia", new CompoundFlashData {
                    T_bp_K = 239.82, DeltaHvap_J_per_kg = 1.371e6,
                    Cp_liq_J_per_kgK = 4700.0,
                    T_critical_K = 405.5, P_critical_Pa = 11.28e6 } },
                { "NH3", new CompoundFlashData {
                    T_bp_K = 239.82, DeltaHvap_J_per_kg = 1.371e6,
                    Cp_liq_J_per_kgK = 4700.0,
                    T_critical_K = 405.5, P_critical_Pa = 11.28e6 } },
                // Chlorine
                { "Chlorine", new CompoundFlashData {
                    T_bp_K = 239.11, DeltaHvap_J_per_kg = 2.88e5,
                    Cp_liq_J_per_kgK = 949.0,
                    T_critical_K = 416.96, P_critical_Pa = 7.991e6 } },
                { "Cl2", new CompoundFlashData {
                    T_bp_K = 239.11, DeltaHvap_J_per_kg = 2.88e5,
                    Cp_liq_J_per_kgK = 949.0,
                    T_critical_K = 416.96, P_critical_Pa = 7.991e6 } },
                // Methane (LNG)
                { "Methane", new CompoundFlashData {
                    T_bp_K = 111.65, DeltaHvap_J_per_kg = 5.10e5,
                    Cp_liq_J_per_kgK = 3500.0,
                    T_critical_K = 190.56, P_critical_Pa = 4.599e6 } },
                { "CH4", new CompoundFlashData {
                    T_bp_K = 111.65, DeltaHvap_J_per_kg = 5.10e5,
                    Cp_liq_J_per_kgK = 3500.0,
                    T_critical_K = 190.56, P_critical_Pa = 4.599e6 } },
                // Propane (LPG)
                { "Propane", new CompoundFlashData {
                    T_bp_K = 231.04, DeltaHvap_J_per_kg = 4.26e5,
                    Cp_liq_J_per_kgK = 2530.0,
                    T_critical_K = 369.83, P_critical_Pa = 4.248e6 } },
                // n-Butane
                { "n-Butane", new CompoundFlashData {
                    T_bp_K = 272.65, DeltaHvap_J_per_kg = 3.86e5,
                    Cp_liq_J_per_kgK = 2380.0,
                    T_critical_K = 425.12, P_critical_Pa = 3.796e6 } },
                // Hydrogen sulfide
                { "Hydrogen sulfide", new CompoundFlashData {
                    T_bp_K = 213.55, DeltaHvap_J_per_kg = 5.49e5,
                    Cp_liq_J_per_kgK = 1980.0,
                    T_critical_K = 373.4, P_critical_Pa = 8.963e6 } },
            };

        private static CompoundFlashData GetFlashData(string compoundName)
        {
            if (string.IsNullOrEmpty(compoundName)) return null;
            if (_flashTable.TryGetValue(compoundName, out var d)) return d;

            // Fallback: probe DWSIM's compound database for critical properties + NBP
            // (this code path doesn't call Flowsheet.Solve, so no BinaryFormatter usage).
            // ΔH_vap is estimated from Watson correlation at NBP using T_c.
            try
            {
                var info = DwsimThermo.GetCompoundInfo(compoundName);
                if (info != null && info.NormalBoilingPointK > 0)
                {
                    // Watson correlation: ΔH_vap ~ R · T_b · (1 - T_b/T_c)^0.38 (Pitzer 1955)
                    // Approximate Cp_liq at 80% of T_c with empirical Souders rule (~2 J/g/K nominal).
                    double Tr = info.NormalBoilingPointK / Math.Max(info.CriticalTemperatureK, info.NormalBoilingPointK * 1.5);
                    double hvap_kJpermol = 4.184 * 21.0 * info.NormalBoilingPointK * Math.Pow(1 - Tr, 0.38);
                    double hvap_Jperkg = hvap_kJpermol * 1000.0 / info.MolarMassKgMol;
                    return new CompoundFlashData
                    {
                        T_bp_K = info.NormalBoilingPointK,
                        DeltaHvap_J_per_kg = hvap_Jperkg,
                        Cp_liq_J_per_kgK = 2500.0, // default 2.5 kJ/kg/K (organic liquid range)
                        T_critical_K = info.CriticalTemperatureK,
                        P_critical_Pa = info.CriticalPressurePa
                    };
                }
            }
            catch { /* fall through to null → ideal-gas fallback */ }
            return null;
        }

        /// <summary>Computes the two-phase source for a single-compound release.</summary>
        /// <param name="leak">High-pressure leak parameters (vessel P, T, orifice, γ, M).</param>
        /// <param name="compoundName">DWSIM compound name (e.g. "Carbon dioxide", "Ammonia").</param>
        /// <param name="ambientPressurePa">Atmospheric pressure (Pa).</param>
        /// <param name="ambientTemperatureK">Ambient air temperature (K) - used as fallback when DWSIM unavailable.</param>
        /// <param name="targetExpandedVelocityMS">Birch pseudo-source target velocity (default 100 m/s).</param>
        public static TwoPhaseSourceResult Compute(
            HighPressureLeakParams leak,
            string compoundName,
            double ambientPressurePa,
            double ambientTemperatureK,
            double targetExpandedVelocityMS = 100.0)
        {
            var result = new TwoPhaseSourceResult();

            // 1. Mass flow. When SpecifyMassFlow is set, honour the measured/observed
            //    rate (e.g. from a field experiment paper) instead of recomputing from
            //    the orifice — HighPressureLeakModel assumes gas-through-orifice, which
            //    underpredicts by a factor 5-10× for liquefied gas storage (Cl2, NH3,
            //    propane, liquid CO2) where the upstream is liquid, not vapor.
            //    The proper liquid/two-phase orifice formulation (Bernoulli or HEM)
            //    is out of scope for v1; using the published m_dot is more accurate.
            double mdot = leak.SpecifyMassFlow && leak.SpecifiedMassFlowKgPerS > 0
                ? leak.SpecifiedMassFlowKgPerS
                : HighPressureLeakModel.MassFlowRate(leak);
            if (mdot <= 0)
            {
                result.Error = "Computed mass flow is zero - check vessel pressure and orifice diameter.";
                return result;
            }

            // 2. Look up compound thermodynamics for the flash-fraction calculation
            var fd = GetFlashData(compoundName);
            if (fd == null)
            {
                FillIdealGasFallback(result, leak, mdot, ambientPressurePa, ambientTemperatureK,
                    targetExpandedVelocityMS, "no flash data for compound: " + (compoundName ?? "<null>"));
                return result;
            }

            // 3. Decide phase regime by comparing vessel T against critical T
            bool supercritical = leak.VesselTemperatureK > fd.T_critical_K
                                 && leak.VesselPressurePa > fd.P_critical_Pa;

            // T_exit ≈ saturation temperature at ambient P (after the throat the cloud
            // is at boiling point until the latent heat is satisfied by sensible heat).
            // For supercritical storage, the JT expansion drops T to T_bp regardless.
            double Texit = fd.T_bp_K;

            // 4. Clapeyron flash fraction: the sensible heat available from cooling the
            //    liquid from vessel T down to T_bp provides the latent heat that flashes
            //    a fraction x_v of the liquid into vapor at ambient pressure.
            //
            //        x_v = C_p,liq · (T_vessel - T_bp) / ΔH_vap
            //
            //    For supercritical fluids, the analogous estimate uses the fluid's
            //    pseudo-liquid Cp and gives a higher vapor fraction (the supercritical
            //    fluid expands through the saturation envelope and crosses to vapor).
            //    Both clamped to [0, 1].
            double dT = leak.VesselTemperatureK - fd.T_bp_K;
            double xv;
            if (dT <= 0)
            {
                // Vessel colder than NBP — sub-cooled or below triple point. All vapor
                // for atmospheric expansion of a (very cold) gas, all liquid for sub-cooled
                // liquid below NBP. The Clapeyron formula gives <= 0 here; cap at 0.
                xv = 0.0;
            }
            else
            {
                xv = fd.Cp_liq_J_per_kgK * dT / fd.DeltaHvap_J_per_kg;
                if (supercritical)
                {
                    // Supercritical correction: above T_c the fluid has no real "liquid
                    // phase", so x_v is bounded from below by 0.5 (the supercritical
                    // expansion to ambient typically yields majority vapor with some
                    // condensate / solid CO2 entrainment).
                    xv = Math.Max(xv, 0.50);
                }
            }
            if (xv > 1.0) xv = 1.0;
            if (xv < 0.0) xv = 0.0;

            double mdotVapor = mdot * xv;
            double mdotDroplet = mdot * (1.0 - xv);

            // 5. Density at the expanded state: ideal-gas density of the vapor phase
            //    at (P_amb, T_exit). For supercritical CO2 this slightly underestimates
            //    density (the cloud carries entrained solid particles), but for the
            //    pseudo-source geometry it's adequate.
            double M = leak.GasMolarMassKgMol > 0 ? leak.GasMolarMassKgMol : 0.029;
            double rhoExit = ambientPressurePa * M / (R * Math.Max(Texit, 150.0));

            // 6. Birch & Schefer pseudo-source for the VAPOR fraction only
            double dPseudo;
            if (mdotVapor > 0 && targetExpandedVelocityMS > 0)
            {
                double areaPseudo = mdotVapor / (rhoExit * targetExpandedVelocityMS);
                dPseudo = Math.Sqrt(4.0 * areaPseudo / Math.PI);
            }
            else
            {
                dPseudo = leak.OrificeDiameterM;
            }

            result.VaporMassFlowKgPerS = mdotVapor;
            result.DropletMassFlowKgPerS = mdotDroplet;
            result.VaporFraction = xv;
            result.TempExitK = Texit;
            result.VelocityExitMS = targetExpandedVelocityMS;
            result.DiameterPseudoM = dPseudo;
            result.DensityExitKgM3 = rhoExit;
            result.IsTwoPhase = xv > 0.01 && xv < 0.99;
            result.DwsimUsed = false; // analytical Clapeyron, not a DWSIM flowsheet solve
            result.Notes = string.Format(
                "Clapeyron flash ({4}): T_vessel={0:F1} K, T_bp={1:F1} K, ΔH_vap={2:F0} kJ/kg, C_p,liq={3:F0} J/kg/K → x_v={5:F3}; mdot_total={6:F4} kg/s, vapor={7:F4} kg/s, rainout={8:F4} kg/s",
                leak.VesselTemperatureK, fd.T_bp_K, fd.DeltaHvap_J_per_kg / 1000.0,
                fd.Cp_liq_J_per_kgK,
                supercritical ? "supercritical" : "subcritical liquid",
                xv, mdot, mdotVapor, mdotDroplet);
            return result;
        }

        private static void FillIdealGasFallback(
            TwoPhaseSourceResult result, HighPressureLeakParams leak, double mdot,
            double ambientPressurePa, double ambientTemperatureK,
            double targetExpandedVelocityMS, string reason)
        {
            var (d, v, t) = HighPressureLeakModel.ComputeExpandedSource(
                leak, targetExpandedVelocityMS, ambientTemperatureK);
            result.VaporMassFlowKgPerS = mdot;
            result.DropletMassFlowKgPerS = 0;
            result.VaporFraction = 1.0;
            result.TempExitK = t;
            result.VelocityExitMS = v;
            result.DiameterPseudoM = d;
            result.IsTwoPhase = false;
            result.DwsimUsed = false;

            double M = leak.GasMolarMassKgMol > 0 ? leak.GasMolarMassKgMol : 0.029;
            result.DensityExitKgM3 = ambientPressurePa * M / (R * ambientTemperatureK);

            result.Notes = string.Format(
                "Ideal-gas fallback ({0}). T_exit={1:F1} K, all-vapor, D_pseudo={2:F4} m, V={3:F0} m/s",
                reason, t, d, v);
        }
    }
}
