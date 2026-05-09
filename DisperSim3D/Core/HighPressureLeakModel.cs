using System;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Parameters for a high-pressure gas leak from a pressurized vessel through an orifice.
    /// </summary>
    public class HighPressureLeakParams
    {
        /// <summary>Gets or sets the vessel pressure in Pascals.</summary>
        public double VesselPressurePa { get; set; } = 1e6;

        /// <summary>Gets or sets the vessel temperature in Kelvin.</summary>
        public double VesselTemperatureK { get; set; } = 293.15;

        /// <summary>Gets or sets the orifice diameter in meters.</summary>
        public double OrificeDiameterM { get; set; } = 0.025;

        /// <summary>Gets or sets the vessel volume in cubic meters.</summary>
        public double VesselVolumeM3 { get; set; } = 10.0;

        /// <summary>Gets or sets the gas heat capacity ratio (Cp/Cv).</summary>
        public double GasGamma { get; set; } = 1.4;

        /// <summary>Gets or sets the gas molar mass in kg/mol.</summary>
        public double GasMolarMassKgMol { get; set; } = 0.016;

        /// <summary>Gets or sets the ambient pressure in Pascals.</summary>
        public double AmbientPressurePa { get; set; } = 101325.0;

        /// <summary>Gets or sets the discharge coefficient (0 to 1). Default is 0.65.</summary>
        public double DischargeCoefficient { get; set; } = 0.65;

        /// <summary>When true, the user specifies mass flow rate and orifice diameter is back-calculated.</summary>
        public bool SpecifyMassFlow { get; set; }

        /// <summary>User-specified mass flow rate in kg/s (used when SpecifyMassFlow is true).</summary>
        public double SpecifiedMassFlowKgPerS { get; set; } = 1.0;
    }

    /// <summary>
    /// Computes mass flow rates and blowdown profiles for high-pressure gas leaks.
    /// </summary>
    public static class HighPressureLeakModel
    {
        private const double R = 8.314;

        /// <summary>
        /// Determines whether the flow through the orifice is choked (sonic) by comparing
        /// the ambient-to-vessel pressure ratio against the critical pressure ratio.
        /// </summary>
        /// <param name="p">Leak parameters.</param>
        /// <returns><c>true</c> if the flow is choked; otherwise, <c>false</c>.</returns>
        public static bool IsChoked(HighPressureLeakParams p)
        {
            double gamma = p.GasGamma;
            double criticalRatio = Math.Pow(2.0 / (gamma + 1), gamma / (gamma - 1));
            return (p.AmbientPressurePa / p.VesselPressurePa) <= criticalRatio;
        }

        /// <summary>
        /// Calculates the mass flow rate through the orifice using choked or unchoked flow equations.
        /// </summary>
        /// <param name="p">Leak parameters.</param>
        /// <returns>Mass flow rate in kg/s.</returns>
        public static double MassFlowRate(HighPressureLeakParams p)
        {
            double gamma = p.GasGamma;
            double M = p.GasMolarMassKgMol;
            double T = p.VesselTemperatureK;
            double P0 = p.VesselPressurePa;
            double A = Math.PI * 0.25 * p.OrificeDiameterM * p.OrificeDiameterM;
            double Cd = p.DischargeCoefficient;

            if (IsChoked(p))
            {
                double factor = gamma * M / (R * T);
                double term = Math.Pow(2.0 / (gamma + 1), (gamma + 1) / (gamma - 1));
                return Cd * A * P0 * Math.Sqrt(factor * term);
            }
            else
            {
                double Pb = p.AmbientPressurePa;
                double pr = Pb / P0;
                double term1 = 2 * gamma / (gamma - 1);
                double term2 = Math.Pow(pr, 2.0 / gamma) - Math.Pow(pr, (gamma + 1) / gamma);
                double rho0 = P0 * M / (R * T);
                return Cd * A * Math.Sqrt(rho0 * P0 * term1 * term2);
            }
        }

        /// <summary>
        /// Calculates the orifice diameter required to produce a given mass flow rate.
        /// </summary>
        public static double OrificeDiameterFromMassFlow(HighPressureLeakParams p, double targetMdot)
        {
            if (targetMdot <= 0) return 0;

            double gamma = p.GasGamma;
            double M = p.GasMolarMassKgMol;
            double T = p.VesselTemperatureK;
            double P0 = p.VesselPressurePa;
            double Cd = p.DischargeCoefficient;

            double massFluxPerArea;
            if (IsChoked(p))
            {
                double factor = gamma * M / (R * T);
                double term = Math.Pow(2.0 / (gamma + 1), (gamma + 1) / (gamma - 1));
                massFluxPerArea = Cd * P0 * Math.Sqrt(factor * term);
            }
            else
            {
                double Pb = p.AmbientPressurePa;
                double pr = Pb / P0;
                double term1 = 2 * gamma / (gamma - 1);
                double term2 = Math.Pow(pr, 2.0 / gamma) - Math.Pow(pr, (gamma + 1) / gamma);
                double rho0 = P0 * M / (R * T);
                massFluxPerArea = Cd * Math.Sqrt(rho0 * P0 * term1 * term2);
            }

            if (massFluxPerArea <= 0) return 0;
            double A = targetMdot / massFluxPerArea;
            return Math.Sqrt(4.0 * A / Math.PI);
        }

        /// <summary>
        /// Computes the Birch &amp; Schefer (1984) "expanded" pseudo-source for a sonic underexpanded jet.
        /// Instead of meshing the actual orifice (which would require sub-millimetre cells), CFD codes
        /// represent the leak as a fictitious larger orifice with subsonic velocity at atmospheric
        /// conditions, conserving the mass flow. This is the standard approach in industrial dispersion
        /// modelling per Fiates &amp; Vianna (2016) and Benintendi (2010).
        /// </summary>
        /// <param name="p">Leak parameters.</param>
        /// <param name="targetExpandedVelocityMS">Desired velocity at the pseudo-source
        ///   (typical: 100 m/s — keeps Mach ≪ 1 and avoids the need for compressible refinement).</param>
        /// <param name="ambientTemperatureK">Atmospheric temperature for the expanded jet.</param>
        public static (double DiameterM, double VelocityMS, double TemperatureK) ComputeExpandedSource(
            HighPressureLeakParams p,
            double targetExpandedVelocityMS = 100.0,
            double ambientTemperatureK = 293.15)
        {
            // Mass flow at the real (sonic if choked) orifice
            double mdot = MassFlowRate(p);

            // Density at the pseudo-source (fully expanded to ambient)
            double rhoAmbient = p.AmbientPressurePa * p.GasMolarMassKgMol / (R * ambientTemperatureK);

            // Required cross-section: A = mdot / (rho * V)
            if (rhoAmbient <= 0 || targetExpandedVelocityMS <= 0)
                return (p.OrificeDiameterM, 0, ambientTemperatureK);

            double areaPseudo = mdot / (rhoAmbient * targetExpandedVelocityMS);
            double dPseudo = Math.Sqrt(4.0 * areaPseudo / Math.PI);

            return (dPseudo, targetExpandedVelocityMS, ambientTemperatureK);
        }

        /// <summary>
        /// Computes a time-dependent blowdown mass flow profile using isentropic expansion.
        /// The simulation steps forward in time, reducing vessel pressure and temperature
        /// until the gas is depleted or pressure drops to ambient.
        /// </summary>
        /// <param name="p">Initial leak parameters.</param>
        /// <param name="durationS">Total simulation duration in seconds.</param>
        /// <param name="dtS">Time step size in seconds.</param>
        /// <returns>Array of mass flow rates (kg/s) at each time step.</returns>
        public static double[] ComputeBlowdownProfile(HighPressureLeakParams p, double durationS, double dtS)
        {
            int steps = (int)(durationS / dtS);
            double[] massFlowProfile = new double[steps];

            double P = p.VesselPressurePa;
            double T = p.VesselTemperatureK;
            double V = p.VesselVolumeM3;
            double M = p.GasMolarMassKgMol;
            double gamma = p.GasGamma;

            double mass = P * V * M / (R * T);

            for (int i = 0; i < steps; i++)
            {
                var current = new HighPressureLeakParams
                {
                    VesselPressurePa = P,
                    VesselTemperatureK = T,
                    OrificeDiameterM = p.OrificeDiameterM,
                    VesselVolumeM3 = V,
                    GasGamma = gamma,
                    GasMolarMassKgMol = M,
                    AmbientPressurePa = p.AmbientPressurePa
                };

                double mdot = MassFlowRate(current);
                massFlowProfile[i] = mdot;

                mass -= mdot * dtS;
                if (mass <= 0) { mass = 0; break; }

                // Isentropic expansion
                double rho = mass / V;
                P = rho * R * T / M;
                T = T * Math.Pow(P / p.VesselPressurePa, (gamma - 1) / gamma);
                T = Math.Max(T, 200);

                if (P <= p.AmbientPressurePa) break;
            }

            return massFlowProfile;
        }
    }
}
