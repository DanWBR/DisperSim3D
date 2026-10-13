# Unified integral dispersion model (`CfdSolverType.UnifiedDispersion`)

Steady, continuous release; no grid. One set of ordinary differential equations is integrated along
the cloud centreline from the source through the momentum-jet, heavy-gas (grounded, gravity-spreading)
and passive regimes, in the spirit of DNV's UDM (Witlox & Holt). Implementation:
`DisperSim3D/Core/UnifiedDispersionEngine.cs`; aerosol thermodynamics: `Core/AerosolCloudThermo.cs`.

## State and profiles

Along the arc length `s`: gas (component) mass flow `mg` (constant), entrained air `ma`, horizontal and
vertical momentum flux `Mx`, `Mz`, enthalpy flux `Hf`, centroid `(x, z)`, crosswind half-width `Ry`.
The vertical half-depth `Rz` follows from continuity `A_eff = m / (rho U)`.

Concentration: `c(y,z) = c_c · exp(-(|y|/Ry)^n) · exp(-(|z-zc|/Rz)^m)` with ground reflection while airborne
and a one-sided profile once grounded. `n` goes from 2 (Gaussian) to 50 (top-hat) with the relative density
excess (fully flat at 10 %); `m` depends on the stability class (A 2.5 ... D 2.0 ... F 1.2).

## Closures

| Term | Form | Default |
|---|---|---|
| Jet entrainment | `alpha1 · sqrt(max(u-ua,0)^2 + w^2) · perimeter` | alpha1 = 0.057 (top-hat; reproduces 5.4 d/x of Chen & Rodi) |
| Cross-wind entrainment | `alpha2 · ua · |sin theta| · perimeter` | alpha2 = 0.35 (UDM) |
| Passive (ambient) | `KAMB · ua · d(A_eff)/dx` from the growth of the Pasquill-Gifford-Turner sigmas (Martin 1976 fit) at the equivalent distance; sigma_z scaled by (z0/0.03)^0.2 | KAMB = 0.7 |
| Heavy-gas top entrainment | `KTOP · u* / Phi(Ri*) · 2 Ry_eff` with the UDM Phi(Ri*) | KTOP = 4.0 (UDM: kappa = 0.41; see note) |
| Gravity spreading | `dRy/dt = CE · sqrt(g' H_eff)`, `g' = g (rho - rho_a)/rho` | CE = 0.6 (UDM 1.15) |
| Combination | `max(jet + cross, top) + HPASS · passive` in the heavy regime; passive only after transition | HPASS = 1 |
| Transition to passive | needs `Ri* < 15` **and** relative density excess `< 1.5 %` (UDM) | |
| Pool source | vapour leaves the pool already mixed with air: `(vapour + air)/vapour = POOLAIR` | 1.5 (UDM parameter) |
| Momentum | entrained air brings `ua`; buoyancy `g (rho_a - rho) A` in `Mz`; ground friction `Cf rho u^2 2Ry · min(1, Ri*)` | Cf = 0.004 |
| Energy | entrained air enthalpy; ground heat `h_g (T_a - T) 2Ry`, `h_g = 5 + 3.8 ua` W/m2K | |

Note on KTOP: the UDM adds a near-field passive term (HGSYSTEM / Disselhorst) that is not documented in the
PHMSA submission; here that dilution is represented by keeping the PG passive term in the heavy regime
(HPASS = 1) and a larger top-entrainment constant, calibrated on the benchmarks below.

All constants can be overridden for calibration with environment variables (`UDM_ALPHA1`, `UDM_ALPHA2`,
`UDM_CE`, `UDM_KTOP`, `UDM_KAMB`, `UDM_HPASS`, `UDM_POOLAIR`, `UDM_RHOCRIT`, `UDM_RITRANS`, `UDM_Z0CORR`,
`UDM_STABEXP`, `UDM_SIGMA=PG|Briggs`, `UDM_CF`); `UDM_DUMP=<csv>` writes the centreline and the entrainment
budget. `scripts/udm_calibrate.py` scores the continuous-release benchmarks.

## Two-phase (aerosol) releases with DWSIM

When the source carries `HighPressureLeak` (vessel P, T) and `CarryAerosol = true` (a `.dsbench` with a
`twoPhase` block does this automatically for this solver), the cloud thermodynamics are solved with the
DWSIM core (`DWSIMCore.Foundation`, property package from the settings, PR78 by default):

- source enthalpy = enthalpy of the pure component at the vessel state (isenthalpic flash to ambient
  pressure gives the exit temperature, liquid fraction and two-phase density);
- at every integration step a PH flash of {component, N2, O2} with the current air mass and enthalpy
  flux gives the cloud temperature, the liquid (aerosol) fraction and the mixture density
  (vapour as ideal gas with the flashed vapour molar mass, liquid density from the DWSIM correlation);
- the entrained air carries its DWSIM-basis enthalpy; once the aerosol has evaporated the model switches to
  the cp-based ideal-gas enthalpy (no further flashes).

No rainout, no pool re-evaporation and no humidity / water condensation yet. Ammonia-water aerosol
chemistry is not modelled.

## Validation (benchmarks with field data, `--validate ... -s UnifiedDispersion`)

| Benchmark | Type | MG | FAC2 |
|---|---|---|---|
| Burro 3 / 5 / 6 / 7 | LNG pool, C-D | 0.93 / 0.58 / 0.79 / 0.73 | 1.0 |
| Coyote 3 / 5 | LNG pool, C-D | 2.0 / 0.98 | 0.6 / 1.0 |
| Falcon 1 / 3 / 4 | LNG pool, F-D | 1.04 / 1.01 / 0.97 | 0.67-1.0 |
| Maplin Sands 27 | LNG on water, D | 2.8 | 0 |
| Kit Fox U5-2 | CO2 ground source, D | 2.9 | 0 |
| Prairie Grass 11-C / 22-D / 35-E | passive, SO2 | 1.02 / 0.98 / 1.45 | 1.0 |
| Prairie Grass 07-B / 29-E | passive, SO2 | 0.19 / 0.41 | 0-0.2 |
| Desert Tortoise 4 | NH3 two-phase jet (aerosol path) | 3.2 (1.3x at 100 m, 6x under at 0.8-2.8 km) | 0.33 |

MG = geometric mean of observed/predicted (MG > 1 under-prediction). Small dense sources (Maplin, Kit Fox)
are still under-predicted by about 3x and the Desert Tortoise far field is under-predicted (the cold
ammonia-water aerosol that keeps the real cloud grounded is not modelled).

## Not yet implemented

Finite-duration releases and puffs (observer method), rainout and pool re-evaporation, humidity,
ammonia-water aerosol, the HGSYSTEM near-field passive term, GUI exposure of the centreline results.
