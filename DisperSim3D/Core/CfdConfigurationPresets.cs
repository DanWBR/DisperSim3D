using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Validated atmospheric-CFD defaults applied per solver type, derived from
    /// Mack &amp; Spruijt 2013 (TNO), Tran Le Vu 2019 (NTU/Burro LNG validation), and
    /// Schalau et al. 2021 (BAM). Call <see cref="ApplyForSolver"/> when a new
    /// Simulation/WindFieldScenario is created or when the user changes the solver type.
    /// </summary>
    public static class CfdConfigurationPresets
    {
        /// <summary>
        /// Mutates <paramref name="cfd"/> in place with the recommended atmospheric defaults
        /// for the given solver. If <paramref name="gas"/> is flagged cryogenic, applies the
        /// LNG overrides (Sc_t = 0.15, FixedTemperature ground BC at the ambient air temperature).
        /// </summary>
        public static void ApplyForSolver(CfdConfiguration cfd, CfdSolverType solver,
            GasLibraryItem gas = null, MeteorologicalConditions meteo = null)
        {
            if (cfd == null) return;

            switch (solver)
            {
                case CfdSolverType.GaussianPuff:
                case CfdSolverType.GaussianPlume:
                    // Analytical solvers — atmospheric block does not apply.
                    cfd.UseAtmosphericBL = false;
                    break;

                case CfdSolverType.ScalarTransportFoam:
                case CfdSolverType.ScalarTransportFoamSteady:
                    // Passive scalar on a frozen wind field. No buoyancy in solver itself.
                    cfd.UseAtmosphericBL = true;
                    cfd.TurbulentSchmidtNumber = 0.7;
                    cfd.BuoyancyEpsCoefficient = null;
                    cfd.KEpsilonSigmaEpsilon = 1.3;
                    cfd.GroundThermalBC = GroundThermalBoundary.Adiabatic;
                    break;

                case CfdSolverType.ScalarSimpleFoam:
                case CfdSolverType.PimpleFoam:
                case CfdSolverType.RhoSimpleFoam:
                    // Wind-field / momentum solvers without active buoyancy.
                    cfd.UseAtmosphericBL = true;
                    cfd.BuoyancyEpsCoefficient = null;
                    cfd.KEpsilonSigmaEpsilon = 1.167; // Vu HHTSL
                    cfd.GroundThermalBC = GroundThermalBoundary.Adiabatic;
                    break;

                case CfdSolverType.BuoyantPimpleFoam:
                case CfdSolverType.ReactingFoam:
                case CfdSolverType.RhoReactingBuoyantFoam:
                    // Heavy-gas / reactive / buoyant — full atmospheric treatment.
                    cfd.UseAtmosphericBL = true;
                    cfd.TurbulentSchmidtNumber = 0.7;
                    cfd.BuoyancyEpsCoefficient = -0.33; // Mack & Spruijt
                    cfd.KEpsilonSigmaEpsilon = 1.167;   // Vu HHTSL
                    cfd.GroundThermalBC = GroundThermalBoundary.Adiabatic;
                    break;
            }

            ApplyCryogenicOverride(cfd, gas, meteo);
        }

        /// <summary>
        /// If the gas is flagged cryogenic and the solver supports temperature, switch the
        /// preset to the LNG validation set from Vu 2019 §5.4: Sc_t = 0.15, FixedTemperature
        /// ground at the ambient air temperature.
        /// </summary>
        private static void ApplyCryogenicOverride(CfdConfiguration cfd,
            GasLibraryItem gas, MeteorologicalConditions meteo)
        {
            if (cfd == null || gas == null || !gas.IsCryogenic) return;
            if (!cfd.UseAtmosphericBL) return; // Gaussian solvers — skip

            cfd.TurbulentSchmidtNumber = 0.15;
            cfd.GroundThermalBC = GroundThermalBoundary.FixedTemperature;
            cfd.GroundTemperatureK = (meteo != null && meteo.AmbientTemperature > 0)
                ? meteo.AmbientTemperature
                : 293.15;
        }
    }
}
