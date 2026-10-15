---
layout: default
title: Unified integral model
nav_order: 7
---

# Unified integral dispersion model (`CfdSolverType.UnifiedDispersion`)

Steady, continuous release; no grid. One set of ordinary differential equations is integrated along the cloud
centreline from the source through the momentum-jet, touchdown, heavy-gas (grounded, gravity-spreading) and
passive regimes. The formulation follows the DNV UDM (Witlox & Holt; *UDM Theory*, Phast 8.5, DNV public
documentation) with the additions listed under "Beyond the UDM". Implementation:
`DisperSim3D/Core/UnifiedDispersionEngine.cs`; aerosol thermodynamics: `Core/AerosolCloudThermo.cs`.

## State, profiles and geometry (UDM)

Integrated along the arc length `s`: air mass flow `ma`, horizontal and vertical momentum flux `Ix`, `Iz`,
enthalpy flux `Hf`, centre-line position `(x, zcld)`, cross-wind radius `Ry` (and the component mass flow `mc`
over a pool). Concentration profile

`c(x,y,z) = c0(x) · exp(-(|y|/Ry)^m) · exp(-(|z - zcld|/Rz)^n)`

with `m = 2 + 12 r / (2/r + 0.25 r - 0.5)`, `r = (ρ - ρa)/ρa` (2 = Gaussian, 50 = sharp-edged) and
`n = max(n_base - 4.5 ξ/(1 + 9 ξ), 1)`, `ξ = Heff/|L|`, `n_base` = 2 (A-D), 2.25 (E), 2.5 (F).
Effective height `Heff = Γ(1+1/n) Rz`, half-width `Weff = Γ(1+1/m) Ry`, cross-section
`A = 2 Weff Heff (1 + hd)` where `hd = P(1/n, (zcld/Rz)^n)` is the above-ground fraction of the lower half of the
ellipse (1 aloft, 0 grounded; no ground reflection, the truncated ellipse carries the mass). `A = mcld/(ρ u_cld)`
from continuity; the section is circular (`Ry = Rz`) in the jet phase. Ground-contact half-width
`Wgnd = Ry (1 - (zcld/Rz)^n)^(1/m)`, perimeter of the e^-1 contour above ground from the elliptic integral.

## Closures

| Term | Form | Value |
|---|---|---|
| Jet entrainment | `e_jet ρa P |u_cld - u_a cosθ|`, `e_jet = 0.5 α1/√π` | α1 = 0.17 (Gaussian basis; gives the 5.4 d/x centreline decay) |
| Cross-wind entrainment | `α2 ρa P |u_a sinθ|`, suppressed over `L_core = 6.4 D/(1 + 4.6 u_a/u_R)` and restored by `L_supp = (1 + k R) L_core` (Morton / DNV); near-field air drag `Cd ρa P (u_a sinθ)²` phased out over `3 L_supp` | α2 = 0.35, k = 0.3, Cd = 0.39 (k fitted so that vertical jets follow the Pratte-Baines rise) |
| Near-field passive (elevated) | Disselhorst: `ρa (1 - Wgnd/Ry) ε^(1/3) (ly^(4/3) + lz^(4/3))`, `ε = E(Z) u*³/(κ (zc + z0))`, eddy scales `min(R, 0.88 (zc+z0) L(Z))` | e_pas = 1 |
| Heavy-gas top entrainment | `ρa (Wgnd/Ry) u_top 2 Weff`, `u_top = κ u_T/Φ(Ri*)`, `Ri* = g (ρ-ρa) Heff (1+hd)/(ρa u_T²)` | κ = 0.4; Φ = 1 (Ri* < 2.36), √(1+0.8Ri*)/1.7, Ri*/7 (> 14.72) |
| Combination | aloft: jet + cross + near-field passive; grounded: near-field passive·(1-Wgnd/Ry) + max(jet + cross, heavy) | |
| Far-field passive | `ρa u_a A (σy'/σy + σz'/σz + d ln(1+hd)/dx)` | McMullen σy (averaging time (t/600)^0.2, floor 18.75 s), Hosker σz with the roughness function |
| Transition to passive | all of: |u_cld/u_a - 1| < 0.1, |ρ/ρa - 1| < 0.015, non-passive share < 0.3, Ri* < 15 (grounded); linear blend from x_tr to 2 x_tr | |
| Gravity spreading | `dRy/dx = C_E/(Cm u_x) √(g (ρ-ρa) Heff (1+hd)/ρ)`, max of heavy, circular and passive rates; passive `√2 dσy/dx` after transition | C_E = 1.15 |
| Momentum | entrained air brings `u_a(zc)`; buoyancy `-g (ρ-ρa) A`; ground drag `2 Wgnd ρa u*² (1 - u_cld²/u_a²)`; ground impact force while touching down | |
| Energy | entrained air enthalpy; ground heat `max(McAdams natural, Holman forced) · 2 Wgnd` | |
| Ambient | Monin-Obukhov length from class and z0 (Havens & Spicer), log wind profile with Businger/Paulson ψ (β = 2 stable), u* from the reference wind, cut-offs 1 m / 200 m | |

Sources: jets with elevation, pools / low-momentum area sources (vapour added while the cloud crosses the pool,
the cloud is kept at least as wide as the pool chord, initial air `(1.5 - 1)·vapour`), neutral point sources
(passive Gaussian from 1 m).

## Beyond the UDM (defaults)

- **Convective velocity scale from ground heating** (HEGADAS-type `u_T`): a cold dense cloud over a warmer surface is
  heated from below; `u_T² = u*² + w*²`, `w* = (g Q Heff (1+hd)/(ρ cp T))^(1/3)` with the ground heat flux `Q`,
  used in `Ri*` and in `u_top` (`ConvectiveFactor` = 1; 0 restores the UDM).
- **Far-field passive growth at the equivalent distance**: `dσ/dx` is taken where the ambient σ equals the cloud σ
  (the UDM takes it at the downwind distance x, which makes wide clouds grow independently of the local turbulence).
  `PassiveAtActualDistance = true` restores the UDM.
- **Cryogenic pool area from the vaporisation flux**: a boiling pool is at least `rate/0.167 kg/m²/s` wide (PHMSA / FERC
  base value for LNG on water); the given diameter is used when larger.
- **Smooth surfaces**: the Hosker roughness function is extrapolated below z0 = 1 cm with `(z0/0.01)^0.2`.
- **Fractional stability classes** ("D-E"): the class-dependent coefficients are interpolated.
- **Two-phase releases**: the flashed liquid is carried as aerosol; at every step a DWSIM PH flash of
  {component, N2, O2, H2O} (humid air) gives temperature, liquid fraction and density. No rainout yet.
- **Releases against the wind** (`source.releaseAzimuthDeg` pointing upwind in the bench): the jet is integrated with a
  negative along-wind velocity; the entrained air, moving with the wind, decelerates it until it stalls (`ux = 0`),
  after which the cloud is carried downwind over the source. Past the stall the cross-section follows continuity with
  at least the ambient wind speed (`A = m/(ρ max(u_cld, u_a))`), since `u_cld` alone vanishes there. The path is split
  into monotonic-x segments and the sampler takes, at every point, the largest contribution of the outgoing and
  returning passes, measuring the offset as the distance to the local polyline (so points beyond the nozzle or the
  stall carry no concentration).

Calibration overrides (environment): `UDM_ALPHA1`, `UDM_ALPHA2`, `UDM_EPAS`, `UDM_KTOP` (κ), `UDM_CE`, `UDM_POOLAIR`,
`UDM_KSUPP`, `UDM_RITRANS`, `UDM_RHOCRIT`, `UDM_REPAS`, `UDM_RUPAS`, `UDM_RTR`, `UDM_TAV`, `UDM_PASSEQ=0`,
`UDM_WSTAR`, `UDM_POOLFLUX`, `UDM_CSIGZ` (optional convective σz for unstable classes, off), `UDM_SIGMA=PG`,
`UDM_BETA`; `UDM_DUMP=<csv>` writes the centreline and the entrainment budget. `scripts/udm_calibrate.py` scores
the continuous-release benchmarks.

Bench inputs: `meteo.averagingTimeS` (600 s default = basis of the σy correlations; 1-18.75 s = instantaneous),
`meteo.stability` may be "D-E", `meteo.relativeHumidity`, `source.releaseAzimuthDeg` (absolute, 0 = north; default
downwind; set to the wind "from" direction for a release against the wind).

## Validation (`--validate ... -s UnifiedDispersion`, acceptance relative to the FLACS cohort statistics)

| Benchmark | Type | MG | FAC2 | |
|---|---|---|---|---|
| Burro 3 / 7 | LNG on water, C / D, 1 s data (PHMSA MVD) | 1.02 / 0.94 | 1.0 | pass |
| Coyote 3 / 5 | LNG on water, C, 1 s data (PHMSA MVD) | 0.70 / 0.90 | 0.67 / 1.0 | pass |
| Falcon 1 / 3 / 4 | LNG in a fenced basin | 0.75 / 0.58 / 0.55 | 0.67 | pass |
| Maplin Sands 27 | LNG on the sea, 3 s data (PHMSA MVD) | 1.74 | 0.60 | fail (FAC2 0.64 needed; PHAST: MG 3.9) |
| Kit Fox U5-2 | CO2 area source, 20 s | 1.18 | 0.75 | pass |
| Desert Tortoise 4 | NH3 two-phase jet, E (aerosol path) | 1.83 | 0.33 | pass |
| Prairie Grass 11-C / 22-D / 29 (D-E) / 35-E | passive SO2, 10 min | 1.30 / 1.43 / 0.68 / 1.37 | 0.8-1.0 | pass |
| Prairie Grass 07-B | passive SO2, strongly convective run | 0.29 | 0 | fail (PHAST: MG 0.28) |
| Burro 5 / 6 | not in the PHMSA MVD; digitised values unverified | - | - | not counted |

MG = geometric mean of observed/predicted. The Burro 3/7, Coyote 3/5 and Maplin 27 observations were replaced by the
PHMSA LNG Model Validation Database maximum arc-wise concentrations (DNV report to PHMSA, Oct 2010, Table 9); the
Kit Fox sensors sit at 0.3 m (25 m row) and 0.5 m as in the experiment.

Known limitations: convective lofting of surface releases (Prairie Grass 7) is not modelled; the Maplin and Desert
Tortoise far fields (dense cloud persisting beyond the passive transition) are under-predicted by about 2x;
finite-duration releases and puffs, rainout and pool re-evaporation, ammonia-water aerosol, lift-off of buoyant
clouds and gravity-spreading collapse are not implemented.
