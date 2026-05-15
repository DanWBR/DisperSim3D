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

| # | Benchmark | Solver | Gas | Status |
|---|---|---|---|---|
| 1 | Gauss-D-smoketest | GaussianPlume | tracer | PASS |
| 2 | Gauss-Puff-smoketest | GaussianPuff | tracer | PASS |
| 3 | Burro 9 | RhoReactingBuoyantFoam | LNG (CH4) | PASS |
| 4 | Burro 8 | RhoReactingBuoyantFoam | LNG (CH4) | PASS |
| 5 | DAT632 | RhoReactingBuoyantFoam | SF6 | PASS |
| 6 | Gant & Ivings 2005 | FluidX3DDispersion | CH4 | PASS |

Additionally, **FluidX3DDispersion** has been cross-validated against the
DAT632 and Burro 9 experimental data (see [GPU LBM cross-validation](#gpu-lbm-cross-validation-against-experimental-data)).

---

## 1. Gaussian Plume smoketest

Self-consistency regression test for `GaussianPlumeEngine` with
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
| x100 | 100 m | 2.287e-3 | 1.000 |
| x500 | 500 m | 1.298e-4 | 1.000 |
| x1000 | 1000 m | 3.765e-5 | 1.000 |
| x2000 | 2000 m | 1.092e-5 | 1.000 |

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

## 2. Gaussian Puff smoketest

Self-consistency regression test for `GaussianPuffEngine` with Slade (1968)
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

## 3. Burro 9 (OpenFOAM)

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

## 4. Burro 8 (OpenFOAM)

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

## 5. DAT632 Hamburg wind tunnel (OpenFOAM)

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

## 6. Gant & Ivings 2005 (FluidX3D)

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

The six `.dsbench` files bundled with DisperSim 3D serve two distinct roles:

1. **Regression baselines** (benchmarks 1-6): the "observed" values are
   captured from the solver's own output on a known reference build. When the
   same solver re-runs the same case, all SPMs are perfect (MRB = 0, FAC2 = 1,
   etc.). Any change in the solver, case writer, or numerical pipeline that
   alters predictions will break these tests.

2. **Experimental cross-validation** (GPU LBM section): the FluidX3D buoyant
   tracer is compared against the original cited measurements from field
   experiments and wind-tunnel data. These SPM values are non-trivial and
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
