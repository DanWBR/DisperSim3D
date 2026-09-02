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

                case CfdSolverType.RhoReactingBuoyantFoam:
                    // Heavy-gas / reactive / buoyant — full atmospheric treatment.
                    cfd.UseAtmosphericBL = true;
                    cfd.TurbulentSchmidtNumber = 0.7;
                    cfd.BuoyancyEpsCoefficient = -0.33; // Mack & Spruijt
                    cfd.KEpsilonSigmaEpsilon = 1.167;   // Vu HHTSL
                    cfd.GroundThermalBC = GroundThermalBoundary.Adiabatic;
                    break;

                // FluidX3D paths don't go through the OpenFOAM CfdConfiguration
                // block; their defaults are managed inside FluidX3DRunner.
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

            cfd.TurbulentSchmidtNumber = 0.15;  // Vu 2019 §5.4.3 (LNG Burro); for dense gas wind tunnel Vu §5.2.2 uses 0.3. Solver uses muEff/Sct form (textbook), so Sct<1 amplifies turbulent species diffusion.
            cfd.GroundThermalBC = GroundThermalBoundary.FixedTemperature;
            cfd.GroundTemperatureK = (meteo != null && meteo.AmbientTemperature > 0)
                ? meteo.AmbientTemperature
                : 293.15;

            // The patched binary is intentionally NOT dispatched here.
            //
            // Stock rhoReactingBuoyantFoam hard-codes Sct = 1.0 in YEqn.H, so the
            // 0.15 set above never reaches the species equation unless we run the
            // patched rhoReactingBuoyantFoamSct that reads it from
            // transportProperties (scripts/build-rhoReactingBuoyantFoamSct.sh).
            // Dispatching it was tried and moves every LNG bench AWAY from the
            // published FLACS numbers, which is what these benches are scored
            // against. Measured on burro3 against the Hansen 2010 unobstructed
            // cohort (FAC2 = 0.94, MG = 1.18):
            //
            //     stock,   Sct = 1.0    FAC2 0.667  MG 1.571   PASS
            //     patched, Sct = 0.15   FAC2 0.333  MG 3.271   FAIL
            //
            // Same direction as every other element of the Vu stack measured in
            // docs/benchmark-results.md: her recipe amplifies turbulent species
            // diffusion, and this baseline already under-predicts, so it pushes
            // the wrong way. Agreement with the paper is the criterion, not
            // whether Sct = 0.15 is honoured.
            //
            // Consequence to keep in mind: Sct = 0.15 is still written into the
            // generated case and is inert there. Set UsePatchedSctSolver = true
            // manually to make it take effect.
            cfd.UsePatchedSctSolver = false;

            // The cryogenic patch injection (cfd.UseCryogenicPatchInjection)
            // is intentionally NOT enabled here. Empirical test on Coyote 3
            // (2026-05-16, isolated with stock effective Sct=1.0): replacing
            // the fvOptions scalarSemiImplicitSource with the gasInlet patch
            // (cold T=Tbp + flowRateInletVelocity + Y_CH4=1 BCs via topoSet +
            // createPatch on the pool footprint) made FAC2 fall from 0.60 to
            // 0.00 and MG jump from 2.30 to 3.53. The cold dense pocket slumps
            // near the source as designed, but at our 8 m base cell the cold
            // mass is mixed with ambient air within 1-2 timesteps, so the
            // slumping layer never forms; the plume instead stays narrow and
            // far-field arcs receive much less mass than stock. Combined with
            // Sct=0.15 it gets even worse (FAC2 0.00, MG 4.32, ratios
            // 0.14-0.34). The infra is correct per Vu §5.3.1 but only useful
            // as part of her full stack (cryo patch + Vu mesh + ABL precursor
            // + polynomial thermo). Users can still set
            // cfd.UseCryogenicPatchInjection = true manually for experiments.

            // The ABL precursor (cfd.UseAblPrecursor) is intentionally NOT
            // enabled here. Empirical test on Coyote 3 (2026-05-16): adding the
            // precursor on top of the patched Sct = 0.15 solver pushed FAC2 from
            // 0.40 → 0.00 and MG from 2.15 → 17.75. Same pattern as Vu mesh:
            // converging k-epsilon (so mut is realistic instead of underestimated
            // by the uniform initial field) only makes the muEff/Sct amplification
            // disperse the cloud even more. Our case under-predicts at baseline
            // — Vu's amplifications go the wrong direction for us. See TODO.md
            // item 6 "Audit source/BC model vs Vu thesis" for the right next
            // step before re-enabling any Vu stack item.

            // The Vu mesh refinement (cfd.UseVu2019MeshRefinement) is intentionally
            // NOT enabled here. Empirical test on Coyote 3 (2026-05-16): combining
            // the patched Sct = 0.15 solver with the 3-level Vu refinement makes
            // FAC2 collapse from 0.40 to 0.00 and MG explode from 2.15 to 6.37.
            // The amplified turbulent diffusion (muEff/Sct = 6.67·muEff) finally
            // gets resolved by the fine mesh, dispersing the cloud too aggressively.
            // Vu's case reaches FAC2 = 1.0 only because the rest of her stack
            // (steady ABL precursor, polynomial T-dependent thermo, modified k-ε
            // constants — TODO.md items 3, 4, 5) holds the cloud together. Until
            // those land, enabling Vu mesh on its own degrades results. Users can
            // still set cfd.UseVu2019MeshRefinement = true manually for experiments.
        }
    }
}
