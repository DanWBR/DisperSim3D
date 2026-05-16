---
layout: default
title: Benchmark Results
nav_order: 11
---

# Benchmark Results
{: .no_toc }

Results from the DisperSim 3D validation harness on the development machine.

1. TOC
{:toc}

## Test environment

| Item | Value |
|---|---|
| CPU | Intel Core i9-13900KF (24C / 32T) |
| RAM | 64 GB DDR5 |
| GPU | NVIDIA RTX 5070 + RTX 3060 |
| OS | Windows 11 Pro 10.0.26200 |
| Runtime | .NET 10.0.300 |
| OpenFOAM | v2512 (WSL2 Ubuntu) |
| FluidX3D | Native DLL (CUDA, RTX 5070) |
| Date | 2026-05-15 |

All benchmarks are exercised via `DisperSim3D.CLI --validate benchmarks/`.
Exit code 0 = every metric inside its acceptance range.

## Summary

| # | Benchmark | Solver | Gas | Status | Notes |
|---|---|---|---|---|---|
| 1 | Gauss-D-selftest | GaussianPlume | tracer | **PASS** | Self-consistency |
| 2 | Gauss-Puff-selftest | GaussianPuff | tracer | **PASS** | Self-consistency |
| 3 | Prairie Grass Run 7 (B) | GaussianPlume | SO2 | FAIL | B-class σ_z long-range overshoot |
| 4 | Prairie Grass Run 11 (C) | GaussianPlume | SO2 | FAIL | PGT underprediction |
| 5 | Prairie Grass Run 22 (D) | GaussianPlume | SO2 | FAIL | Marginal (MG=2.025) |
| 6 | Prairie Grass Run 29 (E) | GaussianPlume | SO2 | **PASS** | Hanna external |
| 7 | Prairie Grass Run 35 (E) | GaussianPlume | SO2 | **PASS** | Hanna external |
| 8 | Burro 3 | RhoReactingBuoyantFoam | LNG (CH4) | **PASS** | Stability C |
| 9 | Burro 7 | RhoReactingBuoyantFoam | LNG (CH4) | **PASS** | High wind |
| 10 | Burro 8 | RhoReactingBuoyantFoam | LNG (CH4) | FAIL | Regression baseline drift |
| 11 | Burro 9 | RhoReactingBuoyantFoam | LNG (CH4) | FAIL | Regression baseline drift |
| 12 | DAT632 | RhoReactingBuoyantFoam | SF6 | **PASS** | Regression baseline |
| 13 | Falcon 1 | RhoReactingBuoyantFoam | LNG (CH4) | FAIL | Stable F, low-wind underpredict |
| 14 | Falcon 4 | RhoReactingBuoyantFoam | LNG (CH4) | **PASS** | Chan 1990 primary data |
| 15 | Maplin Sands 27 | RhoReactingBuoyantFoam | LNG (CH4) | FAIL | Engine underpredict 6× |
| 16 | Coyote 3 | RhoReactingBuoyantFoam | LNG (CH4) | FAIL | Engine underpredict 2× |
| 17 | Coyote 5 | RhoReactingBuoyantFoam | LNG (CH4) | **PASS** | Stability D high wind |
| 18 | Desert Tortoise 4 | GaussianPlume | NH3 | FAIL | No aerosol/rainout modelling |
| 19 | Jack Rabbit I T7 | GaussianPuff | Cl2 | FAIL | Depression detrainment |
| 20 | Jack Rabbit II T1 | GaussianPuff | Cl2 | FAIL | Urban array not modelled |
| 21 | Jack Rabbit II T7 | GaussianPuff | Cl2 | FAIL | Far-field decay underestimated |
| 22 | Thorney Island 8 | GaussianPuff | Freon-12 | FAIL | No dense-gas slumping |
| 23 | Kit Fox U5-2 | GaussianPlume | CO2 | FAIL | Dense gas in obstacle array |
| 24 | Gant &amp; Ivings 2005 | FluidX3DDispersion | CH4 | **PASS** | Regression baseline |
| 25 | CO2PipeHaz 6 mm | FluidX3DDispersion | CO2 | FAIL | No two-phase modelling |
| 26 | Spadeadam DF1 Test 5 | FluidX3DDispersion | CO2 | **PASS** | Witlox 2014 digitised |

**Summary: 11 PASS / 26 (42 %)**. By category:
- Self-consistency: 2/2 (100 %)
- Gaussian Prairie Grass field validation: 2/5 (40 %)
- OpenFOAM LNG dispersion (Burro/Coyote/Falcon/Maplin): 4/8 (50 %)
- OpenFOAM SF₆ wind tunnel (DAT632): 1/1 (100 %)
- FluidX3D GPU LBM: 2/3 (67 %)
- Heavy-gas / pressurized two-phase (Gaussian): 0/7 (engines lack rainout + slumping)

The Prairie Grass failures (B, C, D) are within the documented limitations
of the Gaussian plume model for near-ground releases. See
[Prairie Grass discussion](#prairie-grass-discussion) for analysis.

The heavy-gas / pressurized failures (DT4, JR I/II, Thorney Island,
Kit Fox, CO2PipeHaz) are out-of-scope for the current Gaussian and
FluidX3D-single-phase engines. The `TwoPhaseSourceCalculator`
infrastructure has been built but, used alone, removes rainout mass
without pool re-evaporation and makes predictions worse  -  see the
[two-phase discussion](validation.md#two-phase-pressurized-source-opt-in)
in the validation guide.

Additionally, **FluidX3DDispersion** has been cross-validated against the
DAT632 and Burro 9 experimental data (see [GPU LBM cross-validation](#gpu-lbm-cross-validation-against-experimental-data)).

---

## 1. Gaussian Plume self-consistency test

Self-consistency test for `GaussianPlumeEngine` with
Pasquill-Gifford-Turner sigma coefficients.

### Configuration

| Parameter | Value |
|---|---|
| Source | Ground-level point, Q = 1.0 kg/s, t = 600 s |
| Gas | Tracer (M = 29 g/mol) |
| Wind | 5.0 m/s from west, stability D |
| Domain | 2500 m half-size, 80 cells |
| Concentration | FinalSnapshot, kg/m3 |

### Sensor results (regression baselines)

| Sensor | Distance | Observed (kg/m3) | Cp/Co |
|---|---:|---:|---:|
| x100 | 100 m | 1.430e-3 | 1.000 |
| x500 | 500 m | 7.374e-5 | 1.000 |
| x1000 | 1000 m | 2.054e-5 | 1.000 |
| x2000 | 2000 m | 5.720e-6 | 1.000 |

Reference values updated to Briggs (1973) coefficients with wind evaluated
at measurement height (10 m).

### SPM results

| Metric | Value | Acceptance | Pass |
|---|---:|---|:-:|
| MRB | 0.0 | [-0.05, 0.05] | Y |
| RMSE | 0.0 | < 0.10 | Y |
| NMSE | 0.0 | -- | -- |
| FAC2 | 1.0 | [0.95, 2.0] | Y |
| MG | 1.0 | [0.95, 1.05] | Y |
| VG | 1.0 | < 1.10 | Y |

---

## 2. Gaussian Puff self-consistency test

Self-consistency test for `GaussianPuffEngine` with Slade (1968)
puff coefficients and the `StepTo` transient loop.

### Configuration

| Parameter | Value |
|---|---|
| Source | Ground-level point, Q = 1.0 kg/s, t = 300 s |
| Gas | Tracer (M = 29 g/mol) |
| Wind | 5.0 m/s from west, stability D |
| Domain | 2500 m half-size, 80 cells, dt = 5 s |
| Concentration | PeakOverTime, kg/m3 |

### Sensor results (regression baselines)

| Sensor | Distance | Observed (kg/m3) | Cp/Co |
|---|---:|---:|---:|
| x100 | 100 m | 1.227e-3 | 1.000 |
| x500 | 500 m | 1.258e-4 | 1.000 |
| x1000 | 1000 m | 2.175e-6 | 1.000 |

### SPM results

| Metric | Value | Acceptance | Pass |
|---|---:|---|:-:|
| MRB | 0.0 | [-0.05, 0.05] | Y |
| RMSE | 0.0 | < 0.10 | Y |
| NMSE | 0.0 | -- | -- |
| FAC2 | 1.0 | [0.95, 2.0] | Y |
| MG | 1.0 | [0.95, 1.05] | Y |
| VG | 1.0 | < 1.10 | Y |

---

## Prairie Grass (Barad 1958) {#prairie-grass-prairie-grass-runs}

Validation of `GaussianPlumeEngine` against the Project Prairie Grass field
experiment (Barad 1958). SO2 tracer released at 0.46 m over flat short-grass
prairie (z0 = 0.006 m) at O'Neill, Nebraska. Arc-maximum concentrations
measured at 1.5 m height on arcs at 50, 100, 200, 400, 800 m downwind.

Dispersion coefficients: Briggs (1973) open-country power-law fits
(sigma_y = ay * x^by, sigma_z = az * x^bz). Wind evaluated at
`max(H, WindMeasurementHeightM)` per PGT calibration convention.

Acceptance criteria: Chang & Hanna (2004) for external validation  - 
MRB within +/-0.67, MG within [0.5, 2.0], VG < 4.0, FAC2 >= 0.3.

### 3. Run 7  -  Stability B (unstable)

| Parameter | Value |
|---|---|
| Source | Q = 89.9 g/s SO2 at 0.46 m |
| Wind | U(2 m) = 4.44 m/s, dT = +2.83 C |
| Stability | B (unstable) |

| Sensor | Distance | Observed (kg/m3) | Predicted (kg/m3) | Cp/Co |
|---|---:|---:|---:|---:|
| arc-50m | 50 m | 9.78e-5 | 1.298e-4 | 1.33 |
| arc-100m | 100 m | 2.33e-5 | 3.354e-5 | 1.44 |
| arc-200m | 200 m | 5.10e-6 | 8.502e-6 | 1.67 |
| arc-400m | 400 m | 8.00e-7 | 2.145e-6 | 2.68 |
| arc-800m | 800 m | 1.00e-7 | 5.403e-7 | 5.40 |

| Metric | Value | Acceptance | Pass |
|---|---:|---|:-:|
| MRB | -0.686 | [-0.67, 0.67] | N |
| RMSE | 0.595 | < 3.0 | Y |
| FAC2 | 0.6 | >= 0.3 | Y |
| MG | 0.465 | [0.5, 2.0] | N |
| VG | 1.311 | < 4.0 | Y |

**Result: FAIL.** B-class sigma_z grows linearly with distance (bz = 1.0),
causing systematic overprediction at long range (800 m: 5.4x). This is a
known limitation of the Briggs B-class parameterisation.

### 4. Run 11  -  Stability C (slightly unstable)

| Parameter | Value |
|---|---|
| Source | Q = 95.9 g/s SO2 at 0.46 m |
| Wind | U(2 m) = 7.61 m/s, dT = +1.33 C |
| Stability | C (slightly unstable) |

| Sensor | Distance | Observed (kg/m3) | Predicted (kg/m3) | Cp/Co |
|---|---:|---:|---:|---:|
| arc-50m | 50 m | 2.73e-4 | 1.685e-4 | 0.617 |
| arc-100m | 100 m | 8.93e-5 | 4.553e-5 | 0.510 |
| arc-200m | 200 m | 2.95e-5 | 1.180e-5 | 0.400 |
| arc-400m | 400 m | 7.60e-6 | 3.028e-6 | 0.398 |
| arc-800m | 800 m | 1.70e-6 | 7.745e-7 | 0.456 |

| Metric | Value | Acceptance | Pass |
|---|---:|---|:-:|
| MRB | 0.718 | [-0.67, 0.67] | N |
| RMSE | 0.640 | < 3.0 | Y |
| FAC2 | 0.4 | >= 0.3 | Y |
| MG | 2.129 | [0.5, 2.0] | N |
| VG | 1.027 | < 4.0 | Y |

**Result: FAIL.** Systematic underprediction by factor ~2.2x. The PGT
power-law sigmas spread too rapidly for this specific wind speed / stability
combination, diluting the plume more than observed.

### 5. Run 22  -  Stability D (neutral)

| Parameter | Value |
|---|---|
| Source | Q = 48.4 g/s SO2 at 0.46 m |
| Wind | U(2 m) = 7.39 m/s, dT = -0.37 C |
| Stability | D (neutral) |

| Sensor | Distance | Observed (kg/m3) | Predicted (kg/m3) | Cp/Co |
|---|---:|---:|---:|---:|
| arc-50m | 50 m | 2.24e-4 | 1.488e-4 | 0.664 |
| arc-100m | 100 m | 8.18e-5 | 4.520e-5 | 0.553 |
| arc-200m | 200 m | 2.77e-5 | 1.293e-5 | 0.467 |
| arc-400m | 400 m | 8.60e-6 | 3.632e-6 | 0.422 |
| arc-800m | 800 m | 2.50e-6 | 1.014e-6 | 0.406 |

| Metric | Value | Acceptance | Pass |
|---|---:|---|:-:|
| MRB | 0.673 | [-0.67, 0.67] | N |
| RMSE | 0.552 | < 3.0 | Y |
| FAC2 | 0.4 | >= 0.3 | Y |
| MG | 2.025 | [0.5, 2.0] | N |
| VG | 1.034 | < 4.0 | Y |

**Result: FAIL** (marginal). MG = 2.025 barely exceeds the 2.0 limit.
Same pattern as C-class: systematic underprediction increasing with distance.

### 6. Run 29  -  Stability E (slightly stable)

| Parameter | Value |
|---|---|
| Source | Q = 41.5 g/s SO2 at 0.46 m |
| Wind | U(2 m) = 3.94 m/s, dT = -0.78 C |
| Stability | E (slightly stable) |

| Sensor | Distance | Observed (kg/m3) | Predicted (kg/m3) | Cp/Co |
|---|---:|---:|---:|---:|
| arc-50m | 50 m | 2.48e-4 | 4.515e-4 | 1.82 |
| arc-100m | 100 m | 8.78e-5 | 1.681e-4 | 1.91 |
| arc-200m | 200 m | 2.76e-5 | 4.893e-5 | 1.77 |
| arc-400m | 400 m | 9.20e-6 | 1.322e-5 | 1.44 |
| arc-800m | 800 m | 2.60e-6 | 3.498e-6 | 1.35 |

| Metric | Value | Acceptance | Pass |
|---|---:|---|:-:|
| MRB | -0.484 | [-0.67, 0.67] | Y |
| RMSE | 1.310 | < 3.0 | Y |
| FAC2 | 1.0 | >= 0.3 | Y |
| MG | 0.609 | [0.5, 2.0] | Y |
| VG | 1.020 | < 4.0 | Y |

**Result: PASS.** All 5 sensors within FAC2. Slight overprediction
(1.4x-1.9x) typical of Gaussian models in stable conditions.

### 7. Run 35  -  Stability E (stable, low wind)

| Parameter | Value |
|---|---|
| Source | Q = 38.8 g/s SO2 at 0.46 m |
| Wind | U(2 m) = 1.80 m/s, dT = -1.39 C |
| Stability | E (stable) |

| Sensor | Distance | Observed (kg/m3) | Predicted (kg/m3) | Cp/Co |
|---|---:|---:|---:|---:|
| arc-50m | 50 m | 6.60e-4 | 9.239e-4 | 1.40 |
| arc-100m | 100 m | 5.75e-4 | 3.441e-4 | 0.598 |
| arc-200m | 200 m | 2.53e-4 | 1.001e-4 | 0.396 |
| arc-400m | 400 m | 7.62e-5 | 2.706e-5 | 0.355 |
| arc-800m | 800 m | 1.38e-5 | 7.159e-6 | 0.519 |

| Metric | Value | Acceptance | Pass |
|---|---:|---|:-:|
| MRB | 0.524 | [-0.67, 0.67] | Y |
| RMSE | 0.547 | < 3.0 | Y |
| FAC2 | 0.6 | >= 0.3 | Y |
| MG | 1.749 | [0.5, 2.0] | Y |
| VG | 1.265 | < 4.0 | Y |

**Result: PASS.** Low-wind stable case. Near-field overprediction (50 m:
1.4x) transitions to underprediction at 200-400 m  -  typical for Gaussian
models where plume meander is not captured.

### Prairie Grass discussion

#### Aggregate performance

Across all 25 sensors (5 runs x 5 arcs):

| Metric | Value |
|---|---|
| Sensor-level FAC2 | 15/25 = 60% |
| Runs passing Hanna criteria | 2/5 (both E stability) |
| Runs within MG [0.5, 2.0] | 4/5 (all except B) |

The 60% sensor-level FAC2 is consistent with published Gaussian plume
validation studies against Prairie Grass (Hanna et al. 2004).

#### Known limitations

1. **B-class sigma_z**: Briggs (1973) B-class has bz = 1.0 (linear growth),
   producing unrealistically large sigma_z at long range (sigma_z(800 m) ~
   96 m). This causes systematic overprediction beyond 400 m.

2. **C/D-class underprediction**: The PGT power-law sigmas are approximate
   fits. For neutral-to-slightly-unstable conditions at high wind speeds,
   the model disperses the plume too rapidly, underpredicting by a factor
   of ~2x. Run 22 (D) is marginal (MG = 2.025 vs limit 2.0).

3. **Near-ground source geometry**: Prairie Grass releases at 0.46 m are a
   challenging case. The PGT sigma curves were originally calibrated for
   elevated releases; near-ground releases are at the edge of the model's
   applicability.

4. **Arc-maximum vs centerline**: Prairie Grass reports arc-maximum
   concentrations, not centerline. For a well-aligned wind this is
   equivalent, but instantaneous wind meander causes the arc-max to exceed
   the steady-state centerline prediction.

#### Briggs (1973) coefficients

The plume engine uses Briggs (1973) open-country power-law fits for sigma_y
and sigma_z. These were recalibrated from the original engine coefficients
to match the published Briggs curves:

| Class | ay | by | az | bz |
|---|---:|---:|---:|---:|
| A | 0.2293 | 0.9894 | 0.2000 | 1.0000 |
| B | 0.1667 | 0.9894 | 0.1200 | 1.0000 |
| C | 0.1146 | 0.9894 | 0.0872 | 0.9790 |
| D | 0.0833 | 0.9894 | 0.1090 | 0.8550 |
| E | 0.0625 | 0.9894 | 0.0383 | 0.9400 |
| F | 0.0417 | 0.9894 | 0.0202 | 0.9430 |

Reference: Briggs, G.A. (1973). Diffusion Estimation for Small Emissions.
ATDL Contribution File No. 79, NOAA.

---

## 8. Burro 9  -  OpenFOAM

LNG cryogenic spill on water, neutrally stratified ABL. Koopman et al. 1982;
Vu 2019 section 5.4. Regression baselines from `rhoReactingBuoyantFoam`
(stock OpenFOAM v2512, implicit Sct = 1.0) at grid resolution 100.

### Configuration

| Parameter | Value |
|---|---|
| Source | LNG pool, D = 32.2 m, Q = 109.5 kg/s, t = 79 s |
| Gas | CH4 at 111 K (cryogenic), M = 16.04 g/mol |
| Wind | 5.7 m/s from west (z_ref = 2 m), stability D |
| Ambient | T = 308.55 K, p = 101325 Pa, z0 = 0.0002 m |
| Domain | 1000 m half-size, 100 cells, dt = 1.0 s, 200 s |
| Solver | RhoReactingBuoyantFoam |
| Concentration | PeakOverTime, mass fraction |

### Sensor results (regression baselines)

| Sensor | Distance | Observed (mass frac.) | Cp/Co |
|---|---:|---:|---:|
| arc140_centerline | 140 m | 0.02270 | 1.000 |
| arc400_centerline | 400 m | 0.00774 | 1.000 |
| arc800_centerline | 800 m | 0.00460 | 1.000 |

### SPM results

| Metric | Value | Acceptance | Pass |
|---|---:|---|:-:|
| MRB | 0.0 | [-0.10, 0.10] | Y |
| RMSE | 0.0 | < 0.40 | Y |
| NMSE | 0.0 | -- | -- |
| FAC2 | 1.0 | [0.95, 2.0] | Y |
| MG | 1.0 | [0.90, 1.10] | Y |
| VG | 1.0 | < 1.10 | Y |

### Note on stock OpenFOAM vs. experiment

The stock `rhoReactingBuoyantFoam` uses `turbulence->muEff()` for species
transport (equivalent to Sct = 1.0). Vu 2019 reached experimental FAC2 = 1.0
using a custom solver `gasDispersionBuoyantFoam` with Sct = 0.15 for LNG.
Without the custom code, predictions are systematically lower than Vu's at
the LNG arcs. The regression baselines lock the current solver pipeline
against future changes; they are not a quantitative match to the original
Koopman experiments.

---

## 9. Burro 8  -  OpenFOAM

Most stable ABL of the Burro series (Pasquill F, U = 1.8 m/s). Confirms
`buoyantKEpsilon` survives stable stratification + low wind. Same Sct = 1.0
limitation as Burro 9.

### Configuration

| Parameter | Value |
|---|---|
| Source | LNG pool, D = 29.9 m, Q = 116.4 kg/s, t = 107 s |
| Gas | CH4 at 111 K (cryogenic), M = 16.04 g/mol |
| Wind | 1.8 m/s from west (z_ref = 2 m), stability F |
| Ambient | T = 306.25 K, p = 101325 Pa, z0 = 0.0002 m |
| Domain | 1000 m half-size, 100 cells, dt = 1.0 s, 300 s |
| Solver | RhoReactingBuoyantFoam |
| Concentration | PeakOverTime, mass fraction |

### Sensor results (regression baselines)

| Sensor | Distance | Observed (mass frac.) | Cp/Co |
|---|---:|---:|---:|
| arc057_centerline | 57 m | 0.01965 | 1.000 |
| arc140_centerline | 140 m | 0.00747 | 1.000 |
| arc400_centerline | 400 m | 0.00383 | 1.000 |

### SPM results

| Metric | Value | Acceptance | Pass |
|---|---:|---|:-:|
| MRB | 0.0 | [-0.10, 0.10] | Y |
| RMSE | 0.0 | < 0.20 | Y |
| NMSE | 0.0 | -- | -- |
| FAC2 | 1.0 | [0.95, 2.0] | Y |
| MG | 1.0 | [0.90, 1.10] | Y |
| VG | 1.0 | < 1.10 | Y |

---

## 10. DAT632  -  Hamburg wind tunnel (OpenFOAM)

SF6 release on 8.6-degree slope. Mack & Spruijt 2013. Quasi-laminar
(Re_l ~ 15 000). Exercises the C_eps3 = -0.33 buoyancy treatment and the
SF6 species path.

### Configuration

| Parameter | Value |
|---|---|
| Source | Semi-circular release, D = 70 mm, Q = 8.715e-5 kg/s, t = 600 s |
| Gas | SF6, M = 146.05 g/mol (non-cryogenic) |
| Wind | 1.0 m/s from west (z_ref = 1 m), stability D |
| Ambient | T = 300 K, p = 101325 Pa, z0 = 0.0001 m |
| Domain | 4 m half-size, 120 cells, dt = 0.05 s, 60 s |
| Solver | RhoReactingBuoyantFoam |
| Concentration | FinalSnapshot, mass fraction |

### Sensor results (regression baselines)

| Sensor | Position x (m) | Observed (mass frac.) | Cp/Co |
|---|---:|---:|---:|
| sensor100_x0.6 | 0.60 | 0.006335 | 1.000 |
| sensor300_x1.23 | 1.23 | 0.004585 | 1.000 |
| sensor400_x1.5 | 1.50 | 0.004048 | 1.000 |
| sensor700_x2.0 | 2.00 | 0.003279 | 1.000 |
| sensor800_x2.5 | 2.50 | 0.002721 | 1.000 |

### SPM results

| Metric | Value | Acceptance | Pass |
|---|---:|---|:-:|
| MRB | 0.0 | [-0.10, 0.10] | Y |
| RMSE | 0.0 | < 0.30 | Y |
| NMSE | 0.0 | -- | -- |
| FAC2 | 1.0 | [0.95, 2.0] | Y |
| MG | 1.0 | [0.90, 1.10] | Y |
| VG | 1.0 | < 1.30 | Y |

---

## 11. Gant & Ivings 2005  -  FluidX3D

High-pressure CH4 sonic jet from 10.5 mm orifice at 5.0 bar / 250 K.
Buoyant tracer engine with 3x grid resolution (180x180x90), BFECC
anti-diffusion advection. Primary validation target: flammable cloud volume.

### Configuration

| Parameter | Value |
|---|---|
| Source | CH4 jet, D = 10.5 mm, Q = 0.054 kg/s (Cd = 0.65), t = 60 s |
| Birch expansion | D_eff = 32 mm, v_exit = 100 m/s, T_exit = 250 K |
| Gas | CH4, M = 16.04 g/mol, LFL = 0.028, UFL = 0.089 (mass frac.) |
| Wind | 2.0 m/s from west, stability D |
| Ambient | T = 293.15 K, p = 101325 Pa, z0 = 0.03 m |
| Domain | 10 m half-size, base grid 60, tracer grid 180x180x90 (3x) |
| LBM wind field | 240x240x120 (4x base) |
| Solver | FluidX3DDispersion (buoyant tracer) |
| Concentration | PeakOverTime, mass fraction |

### Sensor results (regression baselines)

| Sensor | Distance | Observed (mass frac.) | Cp/Co |
|---|---:|---:|---:|
| jet_1m | 1 m | 0.08081 | 1.000 |
| jet_2m | 2 m | 0.04181 | 1.000 |
| jet_3m | 3 m | 0.03098 | 1.000 |
| jet_5m | 5 m | 0.01949 | 1.000 |

### SPM results

| Metric | Value | Acceptance | Pass |
|---|---:|---|:-:|
| MRB | 0.0 | [-0.10, 0.10] | Y |
| RMSE | 0.0 | < 0.40 | Y |
| NMSE | 0.0 | -- | -- |
| FAC2 | 1.0 | [0.95, 2.0] | Y |
| MG | 1.0 | [0.90, 1.10] | Y |
| VG | 1.0 | < 1.10 | Y |

### Cloud volume

| Metric | Value | Acceptance | Pass |
|---|---:|---|:-:|
| Predicted | 1.169 m3 | -- | -- |
| Expected | 1.169 m3 | -- | -- |
| Ratio (P/E) | 1.000 | [0.80, 1.20] | Y |

LFL-UFL envelope computed via `FlammableCloudCalculator` over the
mass-fraction concentration field (LFL = 0.028, UFL = 0.089).

---

## GPU LBM cross-validation against experimental data

`FluidX3DDispersion` has been independently validated against the same
experimental datasets used by the OpenFOAM benchmarks. These results compare
the GPU LBM buoyant tracer against the original cited measurements (not the
OpenFOAM regression baselines).

### DAT632 (Hamburg wind tunnel, SF6 on slope)

FluidX3D with mass-injection source and Smagorinsky subgrid diffusivity
(Cs = 0.092, Sct = 0.7). GPU LBM wind field at 480-cubed, CPU
semi-Lagrangian tracer at 120-cubed.

| Metric | Value | Hanna acceptable | Pass |
|---|---:|---|:-:|
| MRB | -0.098 | [-0.4, 0.4] | Y |
| FAC2 | 1.0 | > 0.5 | Y |
| VG | 1.003 | < 3.3 | Y |

All 5 sensors reproduced within 16% of the Hamburg wind-tunnel measurements
(Mack & Spruijt 2013).

### Burro 9 (LNG cryogenic spill)

FluidX3D with the buoyant tracer engine: density-based vertical buoyancy,
gravity-current lateral spreading (Cgc = 0.5), BFECC anti-diffusion. Tracer
at 3x resolution (300-cubed on 100-base grid, 6.7 m cells). LNG pool
source: D = 32 m, Q = 109.5 kg/s, T_exit = 111 K.

| Metric | Value | Hanna acceptable | Pass |
|---|---:|---|:-:|
| MRB | 0.044 | [-0.4, 0.4] | Y |
| MG | 1.046 | [0.67, 1.5] | Y |
| FAC2 | 1.0 | > 0.5 | Y |
| VG | 1.051 | < 3.3 | Y |

CH4 concentrations at the 140 / 400 / 800 m arcs reproduced against
Koopman et al. 1982 field data.

### Gant & Ivings 2005 (CH4 sonic jet)

FluidX3D buoyant tracer (3x grid, 180x180x90 on 60-base, 0.11 m cells).
CH4 jet from 10.5 mm orifice at 5.0 bar / 250 K (choked flow Q = 0.054 kg/s,
Cd = 0.65). Against initial Birch 1/x centreline-decay estimates:

| Metric | Value | Hanna acceptable | Pass |
|---|---:|---|:-:|
| MRB | 0.096 | [-0.4, 0.4] | Y |
| MG | 1.10 | [0.67, 1.5] | Y |
| FAC2 | 1.0 | > 0.5 | Y |

All four sensors within 20% of the analytical Birch jet model. Flammable
cloud volume (LFL-UFL envelope): **1.17 m3**.

---

## Methodology notes

### Regression baselines vs. experimental validation

The eleven `.dsbench` files bundled with DisperSim 3D serve three distinct roles:

1. **Self-consistency regression** (benchmarks 1-2): the "observed" values are
   captured from the solver's own output. When the same solver re-runs, all
   SPMs are perfect (MRB = 0, FAC2 = 1). Catches any change in the engine's
   numerical code.

2. **Experimental validation  -  Gaussian plume** (benchmarks 3-7): the
   GaussianPlumeEngine is compared against Project Prairie Grass field data
   (Barad 1958). Acceptance per Chang & Hanna (2004). E-stability runs pass;
   B/C/D runs fail within documented Gaussian model limitations.

3. **Regression baselines  -  CFD** (benchmarks 8-11): observed values from
   the current OpenFOAM / FluidX3D pipeline. Any change in the case writer,
   mesh, or solver that alters predictions will break these tests.

4. **Experimental cross-validation  -  GPU LBM** (below): FluidX3D is compared
   against original cited measurements. These SPM values are non-trivial and
   demonstrate the engine's physical accuracy.

### Stock OpenFOAM Sct limitation

`rhoReactingBuoyantFoam` (OpenFOAM v2512) does not expose a turbulent Schmidt
number. Its species transport equation reads `fvm::laplacian(turbulence->muEff(), Yi)`,
equivalent to Sct = 1.0 implicit. Vu 2019 reached experimental FAC2 = 1.0 by
writing a custom solver `gasDispersionBuoyantFoam` with Sct = 0.15 for LNG.
Without the custom code, the OpenFOAM predictions are systematically ~3x lower
than Vu's at the LNG arcs. The regression baselines capture the current solver
pipeline; they are not a quantitative match to the original experiments.

### Hanna SPM definitions

| Metric | Description | Acceptable | Perfect |
|---|---|---|---|
| MRB | Mean Relative Bias | [-0.4, 0.4] | 0 |
| RMSE | Root Mean Square Error (normalised) | < 2.3 | 0 |
| NMSE | Normalised Mean Square Error | -- | 0 |
| FAC2 | Fraction within factor of 2 | > 0.5 | 1 |
| MG | Geometric Mean Bias | [0.67, 1.5] | 1 |
| VG | Geometric Variance | < 3.3 | 1 |

Reference: Chang & Hanna 2004; Vu 2019 section 1.4.2.
