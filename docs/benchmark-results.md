---
layout: default
title: Benchmark Results
nav_order: 12
---

# Benchmark Results
{: .no_toc }

Consolidated snapshot of the DisperSim 3D validation harness against
published commercial-code (FLACS, PHAST) Statistical Performance Measures
on the same field / wind-tunnel experiments.

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
| OpenFOAM | ESI-OpenCFD v2512 (native Windows) + v2412 (WSL2 Ubuntu, for the patched `rhoReactingBuoyantFoamSct`) |
| FluidX3D | Native DLL (OpenCL, RTX 5070) |
| DWSIMCore | Single-DLL bundled (PR78 default) |
| Date | 2026-05-16 |

All benchmarks are exercised via `DisperSim3D.CLI --validate benchmarks/`.
Exit code 0 = every metric inside the per-bench acceptance band.

> **The GPU changed after this run.** The RTX 5070 and RTX 3060 above were
> both sold and replaced with a single RTX 5070 Ti (16 GB), so the table
> describes hardware the machine no longer has. Everything dated 2026-05-16 and
> 2026-05-17 was produced on the two-card setup; anything dated 2026-08-31 ran
> on the 5070 Ti.
>
> **The v2512 *native Windows* install above is gone from the development
> machine** (checked 2026-08-31: no install directory, no registry entry, no
> `blockMesh.exe`). OpenFOAM v2512 was reinstalled the same day **under WSL2**
> instead, so the version matches again and the suite is reproducible; the host
> differs. See
> [Three benches moved onto the CFD solver](#three-benches-moved-onto-the-cfd-solver-2026-08-31)
> for a same-model cross-check between the two.

## Validation philosophy

Each bench compares DisperSim 3D's SPMs against the SPMs that a published
commercial code (FLACS or PHAST) achieved on the same experiment, as
reported in the cited paper. **PASS means DisperSim is no worse than the
reference within a documented tolerance** (typically MRB ± 0.5, FAC2
± 0.2–0.3, MG ± 0.5, VG ± 1.0–1.5 depending on the bench cohort).

Two exceptions:
- **Self-consistency** benches compare the engine against its own
  captured output. PASS = no regression in the numerical code.
- **Regression baselines** (a handful of FluidX3D and OpenFOAM cases)
  compare against the engine's last-known-good output for the same
  pipeline. These guard against silent drift while a peer-reviewed
  reference SPM set is being assembled.

## Headline score: 17 PASS / 31 (55 %)

| Group | PASS | Total | Notes |
|---|---|---|---|
| Self-consistency | 2 | 2 | gauss-D-selftest, gauss-puff-selftest |
| Prairie Grass vs FLACS Hanna 2004 | 3 | 5 | 2 FAIL by 1–4 % on MRB only |
| OpenFOAM LNG vs FLACS Hansen 2010 | 5 | 9 | Burro 3/5/7/Coyote 5 PASS; Burro 8 PASS (config); Burro 6 / Burro 9 / Coyote 3 / Maplin 27 FAIL by Sct limitation |
| OpenFOAM Falcon vs FLACS Hansen 2010 | 3 | 3 | DisperSim outperforms FLACS on the fence cohort |
| OpenFOAM SF₆ wind tunnel | 1 | 1 | DAT632 regression baseline |
| OpenFOAM MUST (FluidX3D-tracer hybrid) | 1 | 1 | Mock Urban Setting Test trial 11 |
| FluidX3D regression baselines | 2 | 4 | Gant-Ivings / Spadeadam PASS; CO2PipeHaz / hydrogen FAIL by physics scope |
| OpenFOAM dense gas vs FLACS | 0 | 2 | Thorney Island 8 by Sct limitation; Kit Fox U5-2 by its missing billboard array |
| Heavy-gas / pressurized Gaussian | 0 | 4 | Out-of-scope: rainout, depression terrain, urban arrays not in Gaussian engine |
| **Total** | **17** | **31** | |

> The score read 18 / 31 until 2026-08-31. `must-trial-11` was counted
> twice, once under its own group and again under the FluidX3D baselines,
> which inflated both columns by one; the placeholder row the summary table
> used to carry existed to pad its length to the inflated total. Counting
> the table rows directly gives 17 PASS and 14 FAIL over 31 benches. No
> bench changed its result — only the arithmetic did.

## Summary table

| # | Benchmark | Solver | Gas | Status | MRB | FAC2 | MG | VG | Reason |
|---|---|---|---|:-:|---:|---:|---:|---:|---|
| 1 | gauss-D-selftest | GaussianPlume | tracer | **PASS** | 5e-5 | 1.00 | 1.00 | 1.00 | self-consistency |
| 2 | gauss-puff-selftest | GaussianPuff | tracer | **PASS** | -3e-5 | 1.00 | 1.00 | 1.00 | self-consistency |
| 3 | prairie-grass-07-B | GaussianPlume | SO₂ | FAIL | -0.69 | 0.60 | 0.46 | 1.31 | abs(MRB) above tolerance by 0.01 |
| 4 | prairie-grass-11-C | GaussianPlume | SO₂ | FAIL | 0.72 | 0.40 | 2.13 | 1.03 | abs(MRB) above tolerance by 0.04 |
| 5 | prairie-grass-22-D | GaussianPlume | SO₂ | **PASS** | 0.67 | 0.40 | 2.03 | 1.03 | within FLACS-cohort tolerance |
| 6 | prairie-grass-29-E | GaussianPlume | SO₂ | **PASS** | -0.48 | 1.00 | 0.61 | 1.02 | within tolerance |
| 7 | prairie-grass-35-E | GaussianPlume | SO₂ | **PASS** | 0.52 | 0.60 | 1.75 | 1.27 | within tolerance |
| 8 | burro3 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | 0.43 | 0.67 | 1.57 | 1.14 | within FLACS unobstructed cohort |
| 9 | burro5 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | 0.37 | 0.67 | 1.47 | 1.13 | within tolerance |
| 10 | burro6 | RhoReactingBuoyantFoam | LNG (CH₄) | FAIL | 0.99 | 0.33 | 3.07 | 1.15 | Sct limitation; ~3× low at every arc |
| 11 | burro7 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | 0.15 | 0.67 | 1.17 | 1.26 | within tolerance |
| 12 | burro8 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | -0.18 | 1.00 | 0.84 | 1.04 | stability F, low wind |
| 13 | burro9 | RhoReactingBuoyantFoam | LNG (CH₄) | FAIL | 0.72 | 0.67 | 2.18 | 1.11 | MG ceiling 1.95 exceeded by 0.23 — Sct limitation |
| 14 | coyote-03 | RhoReactingBuoyantFoam | LNG (CH₄) | FAIL | 0.76 | 0.60 | 2.30 | 1.14 | FAC2 floor 0.64 missed by 0.04 — Sct limitation |
| 15 | coyote-05 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | 0.52 | 0.67 | 1.73 | 1.10 | within tolerance |
| 16 | maplin-sands-27 | RhoReactingBuoyantFoam | LNG (CH₄) | FAIL | 1.41 | 0.00 | 6.06 | 1.13 | Sct limitation; Vu 2019 needed Sct = 0.15 to match |
| 17 | falcon-01 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | 1.04 | 0.00 | 3.28 | 1.13 | matches FLACS Falcon-cohort tolerance |
| 18 | falcon-03 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | 0.33 | 1.00 | 1.40 | 1.00 | beats FLACS (FAC2 = 0 in their cohort) |
| 19 | falcon-04 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | 0.23 | 1.00 | 1.26 | 1.02 | beats FLACS |
| 20 | DAT632 | RhoReactingBuoyantFoam | SF₆ | **PASS** | 0.013 | 1.00 | 1.01 | 1.00 | regression baseline |
| 21 | must-trial-11 | FluidX3DDispersion | propylene | **PASS** | 7e-4 | 1.00 | 1.001 | 1.00 | MUST cohort regression baseline |
| 22 | gant-ivings-2005 | FluidX3DDispersion | CH₄ jet | **PASS** | -1.7e-5 | 1.00 | 1.00 | 1.00 | regression baseline; cloud volume 1.169 m³ |
| 23 | spadeadam-co2 | FluidX3DDispersion | CO₂ | **PASS** | 0.005 | 1.00 | 1.003 | 1.21 | Witlox 2014 digitised |
| 24 | co2pipehaz-6mm | FluidX3DDispersion | CO₂ | FAIL | — | — | — | — | no two-phase / solid CO₂ sublimation |
| 25 | hydrogen-jet-schefer | FluidX3DDispersion | H₂ | FAIL | — | — | — | — | sensor estimates in dsbench were order-of-magnitude guesses; engine runs cleanly, cloud is plausible |
| 26 | kit-fox-u5-2 | RhoReactingBuoyantFoam | CO₂ | FAIL | 1.46 | 0.00 | 7.18 | 1.41 | billboard roughness array not encoded; near field 16× low, recovering downwind |
| 27 | desert-tortoise-04 | GaussianPlume | NH₃ | FAIL | 0.19 | 0.33 | 1.22 | 7.63 | no aerosol / rainout modelling |
| 28 | thorney-island-08 | RhoReactingBuoyantFoam | Freon-12 | FAIL | 1.03 | 0.00 | 3.15 | 1.02 | Sct limitation; ~3× low at every arc |
| 29 | jack-rabbit-i-t07 | GaussianPuff | Cl₂ | FAIL | 1.91 | 0.00 | 1.2e7 | 3.9e41 | depression detrainment not modelled |
| 30 | jack-rabbit-ii-t01 | GaussianPuff | Cl₂ | FAIL | -0.85 | 0.17 | 2.82 | 2.0e23 | urban array not modelled |
| 31 | jack-rabbit-ii-t07 | GaussianPuff | Cl₂ | FAIL | -0.77 | 0.33 | 0.41 | 1.50 | abs(log MG) 0.89 above tolerance 0.86 |

All 31 benches are now listed; the placeholder row the table used to carry is gone, since burro6 took the missing slot.

Rows 10, 26 and 28 were rerun on 2026-08-31 under OpenFOAM v2512 (WSL2, 4 procs) rather than in the 2026-05-16 batch — see
[Three benches moved onto the CFD solver](#three-benches-moved-onto-the-cfd-solver-2026-08-31).

> **The LNG rows do not reproduce from a default checkout.** burro3, burro5,
> burro7 and coyote-05 pass here but fail when re-run today, because the
> cryogenic preset now dispatches the patched `Sct = 0.15` solver that these
> figures were not measured with. See
> [The LNG cohort does not reproduce under the current defaults](#the-lng-cohort-does-not-reproduce-under-the-current-defaults-2026-08-31).

## Detailed sections

### Gaussian batch (FLACS reference: Hanna 2004)

FLACS reference for the Prairie Grass 43-trial cohort: FAC2 = 0.49,
MG = 1.53, VG = 2.75. Per-bench tolerance: MRB 0.5, FAC2 0.2, MG 0.5,
VG 1.0.

DisperSim Gaussian engine uses Briggs (1973) open-country power-law fits
for σ_y / σ_z and the Pasquill-Gifford-Turner stability classes. Wind is
evaluated at `max(H, WindMeasurementHeightM)` per PGT convention.

3 of 5 Prairie Grass benches PASS. The 2 failures (Run 7 B, Run 11 C)
miss the MRB tolerance by 0.01–0.04 — same order as the FLACS cohort's
own residual error.

### OpenFOAM LNG (FLACS reference: Hansen 2010 unobstructed cohort)

FLACS reference for unobstructed LNG: FAC2 = 0.94, MG = 1.18, VG = 1.14.
Per-bench tolerance: MRB 0.5, FAC2 0.3, MG 0.5, VG 1.5. Pass thresholds:
FAC2 ≥ 0.64, MG ∈ [0.51, 1.95], VG ≤ 2.64.

5 of 8 PASS. The 3 failures (Burro 9, Coyote 3, Maplin 27) are all
attributable to the stock `rhoReactingBuoyantFoam` Sct limitation — see
[Vu (2019) reproduction attempts](#vu-2019-reproduction-attempts).

### OpenFOAM Falcon (FLACS reference: Hansen 2010 Falcon cohort)

FLACS reference for Falcon with vapour fence: FAC2 = 0.00, MG = 5.56,
VG = 23.65 (FLACS itself struggled). Per-bench tolerance: MRB 1.0,
FAC2 0.3, MG 2.0, VG 10.0.

3 of 3 PASS. DisperSim outperforms FLACS on Falcon 3 and 4 (FAC2 = 1.0
vs the cohort's FLACS FAC2 = 0.0). Falcon 1 also reaches the
low-wind-stable failure mode FLACS exhibited (FAC2 = 0.0).

### OpenFOAM SF₆ wind tunnel

`DAT632` — semi-circular SF₆ release on an 8.6° slope at 1 m/s wind.
Mack & Spruijt 2013 did not tabulate Hanna SPMs; this is the engine's
regression baseline for `rhoReactingBuoyantFoam` with the C_eps3 = -0.33
buoyancy treatment and the SF₆ species path.

PASS, regression baseline (MRB 0.013, FAC2 1.00, MG 1.01, VG 1.00).

### MUST (Mock Urban Setting Test) Trial 11

Propylene tracer at 200 g/s in neutral stability through a 12 × 10 ISO
container array (120 obstacles voxelised through the FluidX3D obstacle
pipeline). Per-trial experimental data is in the restricted DPG report
(Biltoft 2001), so the bench uses regression baselines from a
known-good FluidX3D run; the FLACS Hanna 2004 MUST aggregate is in the
dsbench file for context (FAC2 = 0.64, MG = 1.57, VG = 1.69).

PASS as regression baseline. Captures both the LBM wind field (480³ on
a 240³ tracer grid) and the buoyant tracer obstacle handling.

### FluidX3D regression baselines

| Bench | Status | Notes |
|---|:-:|---|
| gant-ivings-2005 | **PASS** | CH₄ sonic jet through 10.5 mm orifice at 5.0 bar / 250 K. Birch-Schefer expanded source. Cloud volume 1.169 m³ (LFL = 0.028, UFL = 0.089 mass frac CH₄). |
| spadeadam-co2 | **PASS** | Cold liquid CO₂ release through 25.62 mm orifice (BP CO2PIPETRANS Test 5). Cleanest SPM match in the FluidX3D family. |
| must-trial-11 | **PASS** | (see MUST section above) |
| co2pipehaz-6mm | FAIL | Supercritical CO₂ release through 6 mm orifice (INERIS CO2PipeHaz Test 2). Engine has no two-phase / solid CO₂ sublimation model. |
| hydrogen-jet-schefer | FAIL | 207 bar H₂ release through 1.91 mm (Schefer 2008). Tests positive-buoyancy handling — engine runs cleanly and cloud is plausible, but sensor estimates in the dsbench were order-of-magnitude guesses (no published per-sensor H₂ data), so the SPM comparison is not authoritative. |

### Heavy gas / pressurized Gaussian (out of scope, 0/4)

These benches exercise physics outside the Gaussian engine's design
envelope — two-phase rainout, depression-terrain accumulation, urban
arrays. They are documented FAILs that pin the engine's applicability
boundary. The correct solver is `FluidX3DDispersion` with the obstacle
pipeline (Jack Rabbit II).

Kit Fox and Thorney Island used to sit here. Both were moved onto
`RhoReactingBuoyantFoam` on 2026-08-31 and now fail for reasons that are
about the model rather than about running dense gas through a Gaussian
engine.

| Bench | Issue |
|---|---|
| desert-tortoise-04 | aerosol / rainout |
| thorney-island-08 | dense-gas slumping |
| jack-rabbit-i-t07 | depression detrainment |
| jack-rabbit-ii-t01 | urban array |
| jack-rabbit-ii-t07 | far-field decay |

## Vu (2019) reproduction attempts

The three OpenFOAM LNG failures (Burro 9, Coyote 3, Maplin Sands 27) are
all cases Vu (2019) reaches FAC2 = 1.0 on using a custom
`gasDispersionBuoyantFoam` solver with Sct = 0.15. This session
implemented and tested the major elements of her recipe on Coyote 3.
**Each modification monotonically degraded predictions** because our
baseline already under-predicts where Vu's over-predicts, so her
diffusion-amplifying modifications go in the wrong direction for us.

### Results on Coyote 3 (FLACS pass: FAC2 ≥ 0.64, MG ∈ [0.51, 1.95])

| Stack | MRB | FAC2 | MG | predicted/observed ratios |
|---|---:|---:|---:|---|
| Stock | 0.76 | **0.60** | 2.30 | 0.21–0.60 |
| + patched Sct = 0.15 solver (Vu §A.2 formula `muEff/Sct`) | 0.72 | 0.40 | 2.15 | 0.37–0.56 |
| + Vu §5.4.1 Mesh2 refinement (3 nested wind-aligned boxes) | 1.44 | 0.00 | 6.37 | 0.10–0.22 |
| + Vu §5.3.4 steady ABL precursor (`buoyantSimpleFoam`, 500 SIMPLE iters) | 1.78 | 0.00 | 17.75 | 0.04–0.08 |

### Reference citation

Vu, Tran Le. *On numerical modelling of atmospheric gas dispersion using
CFD approach*. PhD thesis, Nanyang Technological University, Singapore,
2019. Handle: <https://dr.ntu.edu.sg/handle/10356/103659>. PDF in
`docs/`.

Vu Burro test result (Table 5.15): MRB = -0.15, RMSE = 0.10, FAC2 = 1.00,
MG = 1.16, VG = 1.11. Beats FLACS (FAC2 = 0.94).

### Infrastructure built (all disabled by default)

- **`CfdConfiguration.UsePatchedSctSolver`** — auto-enabled by the
  cryogenic preset. Dispatches the WSL-built `rhoReactingBuoyantFoamSct`
  binary (see `scripts/build-rhoReactingBuoyantFoamSct.sh`) for the
  solver step only; mesh, decomposition, reconstruction stay on the
  native Windows env. Patched binary reads `Sct` from
  `transportProperties` and uses Vu's exact form
  `fvm::laplacian(turbulence->muEff()/Sct, Yi)`.
- **`CfdConfiguration.UseVu2019MeshRefinement`** — flag exists, infra
  in `OpenFoamCaseGenerator.WriteVu2019RefinementDicts` and
  `OpenFoamRunner` (4-level refinement loop). Disabled by default per
  the empirical degradation above.
- **`CfdConfiguration.UseAblPrecursor`** — flag exists, infra in
  `OpenFoamCaseGenerator.WriteAblPrecursorDicts` and
  `OpenFoamRunner.RunAblPrecursor` (in-place dict swap, run steady,
  copy converged fields back). Disabled by default per the empirical
  degradation above.

### Next step

See `TODO.md` item 6 — "Audit source / BC model vs Vu thesis". The
baseline under-prediction must come from upstream of the solver (most
likely the fvOptions cellSet source vs Vu's flowRateInletVelocity face
patch, or the atmospheric inlet profile parameters). That audit needs
to happen before re-enabling any Vu stack item.

## GPU buoyant tracer port — first production benchmark (2026-05-17)

The C# CPU semi-Lagrangian `BuoyantTracerEngine` has been ported to a
native OpenCL pipeline that runs on the same RTX 5070 as the LBM wind
field — 7 kernels covering forward / reverse advection, BFECC
correction, density / buoyancy / gravity-current effective velocity,
explicit Laplacian diffusion, obstacle mask, and sphere / pool source
injection. Enabled via the `--gpu-tracer` CLI flag or
`cfd.UseGpuBuoyantTracer = true`. Single-precision floats throughout
(CPU engine is double).

Full FluidX3D-family batch result, RTX 5070:

| Bench | Wallclock | MRB | FAC2 | MG | VG | Status | Notes |
|---|---:|---:|---:|---:|---:|:---:|---|
| gant-ivings-2005 | 56 s | 0.19 | 1.00 | 1.21 | 1.00 | FAIL | FP32 error 15–25 % on sonic-jet centreline (PASSES Hanna SPMs, FAILS the tighter regression baseline tolerance) |
| must-trial-11 | 334 s | 7e-4 | 1.00 | 1.001 | 1.00 | **PASS** | identical to CPU baseline to 3–4 significant digits |
| spadeadam-co2 | 159 s | -0.40 | 0.75 | 0.65 | 1.27 | **PASS** | far-field over-predict by 2-3×, within reference tolerance |
| co2pipehaz-6mm | 142 s | 1.02 | 0.25 | 3.24 | 1.21 | FAIL | model limitation (no two-phase / sublimation); CPU also FAILs |
| hydrogen-jet-schefer | 125 s | 0.74 | 0.50 | 2.37 | 1.56 | FAIL | dsbench obs values are order-of-magnitude guesses (no published per-sensor H₂ data); CPU also FAILs |

**Aggregate: 2 / 5 PASS** (CPU was 3 / 5; GPU regression on gant-ivings
is the only difference, attributed to FP32 in a high-pressure sonic jet
with Y-gradients from 1.0 → 0.0 across 2–3 cells × 120 timesteps).

**Wallclock summary:**

| Metric | CPU baseline (2026-05-16) | GPU (2026-05-17) | Speedup |
|---|---:|---:|---:|
| gant-ivings (representative) | ~25–35 min | 56 s | ~30× |
| Full 5-bench batch | ~125–175 min | 16.3 min | ~8–10× |

The 8-10× aggregate is bounded by LBM wind-field setup time which
doesn't change between CPU and GPU tracer (the LBM was already GPU).
The tracer step itself is ~30× faster.

## Three benches moved onto the CFD solver (2026-08-31)

**These numbers are not folded into the headline score above**, which is a
snapshot of the 2026-05-16 run. They are reported separately so the two runs
stay distinguishable.

They were produced on OpenFOAM **v2512 under WSL2, 4 processes** — the same
OpenFOAM version the table above declares, on a different host, because the
native Windows v2512 install is gone from the machine (checked 2026-08-31: no
install directory, no registry entry, no `blockMesh.exe`, no `OpenFoamPath` in
the app's saved settings). v2512 was reinstalled under WSL2 the same day.

### What changed

Three benches were declared against an engine that could not represent their
physics, so they could only ever fail:

| Bench | Was | Now | Why the reference was already valid |
|---|---|---|---|
| `burro6` | absolute thresholds | Hansen 2010 cohort | Same series, paper and solver as burro3/5/7/8/9; it was the one bench never migrated off the older acceptance scheme, which is why the LNG cohort listed eight benches and not nine |
| `thorney-island-08` | `GaussianPuff` | `RhoReactingBuoyantFoam` | Thorney Island is named in the Hansen 2010 unobstructed cohort this bench already scored against |
| `kit-fox-u5-2` | `GaussianPlume` | `RhoReactingBuoyantFoam` | Hanna 2004 Table 2, aggregate over 52 Kit Fox trials |

### Results

Environment: OpenFOAM v2512 (WSL2, Ubuntu-24.04), `--nprocs 4`, .NET 10,
NVIDIA RTX 5070 Ti 16 GB (driver 596.36). The two cards listed in the test
environment above were sold before these runs.

| Bench | MRB | RMSE | FAC2 | MG | VG | Status |
|---|---:|---:|---:|---:|---:|:-:|
| burro6 | 0.9925 | 0.9325 | 0.3333 | 3.074 | 1.152 | FAIL |
| thorney-island-08 | 1.034 | 0.8493 | 0.00 | 3.154 | 1.016 | FAIL |
| kit-fox-u5-2 | 1.457 | 1.185 | 0.00 | 7.18 | 1.407 | FAIL |

#### Cross-check: v2412 serial vs v2512 parallel

All three were first run on v2412 with `--nprocs 1`, before v2512 was
reinstalled. The two runs agree to the fourth significant figure:

| Bench | v2412, serial | v2512, 4 procs |
|---|---|---|
| burro6 | MRB 0.9925, MG 3.074 | MRB 0.9925, MG 3.074 |
| thorney-island-08 | MRB 1.033, MG 3.15 | MRB 1.034, MG 3.154 |
| kit-fox-u5-2 | MRB 1.457, MG 7.18 | MRB 1.457, MG 7.18 |

The residual differences are decomposition round-off. Worth recording for two
reasons: it says the OpenFOAM version and the process count are not what these
benches are sensitive to, and it means the failures below are the model's, not
the environment's.

Predicted / observed by arc:

| Bench | Arcs | Ratios |
|---|---|---|
| burro6 | 57, 140, 400 m | 0.224, 0.283, 0.544 |
| thorney-island-08 | 50, 100, 200, 400 m | 0.392, 0.299, 0.306, 0.283 |
| kit-fox-u5-2 | 25, 50, 100, 225 m | 0.062, 0.107, 0.199, 0.286 |

### Reading them

All three fail, but they now fail with a shape that says something, which
the Gaussian runs could not.

**burro6 and thorney-island-08 carry the same signature**: a roughly uniform
3× under-prediction across every arc, `MG` 3.07 and 3.15. That is the
documented consequence of stock `rhoReactingBuoyantFoam` pinning
`Sc_t = 1.0` — see [Vu (2019) reproduction attempts](#vu-2019-reproduction-attempts),
where the same ~3× gap appears on the LNG arcs. Two more cases pointing at a
limitation already on the record, and Thorney Island is a Freon-12 dense gas
rather than LNG, so the signature is not confined to cryogenic methane.

**kit-fox-u5-2 fails differently and worse**: 16× low at 25 m, recovering
monotonically to 3.5× low at 225 m. That is not the flat `Sc_t` bias. It is a
collapsed near field that recovers downwind, which is what a missing obstacle
array produces — the billboard roughness array that Kit Fox exists to test is
still absent from the bench, because `BenchmarkObstacleArray` only knows the
MUST container grid. The bench description says so. Until that array is
encoded, Kit Fox measures the dense-gas path and not the thing the experiment
was run to measure.

### One environment finding that these runs depended on

None of the three could run at all until `wsl -d <distro> -- bash -c` was
replaced with `wsl -e`. The outer login shell that `--` invokes expanded
`$VAR` against its own empty environment before `bash -c` saw the script, so
the bashrc discovery loop tested `[ -f "" ]`, never matched, and **no WSL run
had ever sourced an OpenFOAM environment** — every one silently used whatever
binaries sat on `PATH`. On a machine carrying both an Ubuntu `openfoam`
package (v1912) and an ESI v2412 install, that meant v1912, which rejects the
generated case on a wall function it does not have.

This matters for anything above that was produced through WSL2: the install
that actually ran was whatever `PATH` resolved to, not necessarily the one
configured.

## The LNG cohort does not reproduce under the current defaults (2026-08-31)

Re-running the suite on 2026-08-31 reproduced the Gaussian, FluidX3D and
DAT632 benches to the fourth significant figure, and every OpenFOAM LNG bench
came out far worse than the table above. Four of them turned from PASS to
FAIL:

| Bench | MG recorded | MG on 2026-08-31 | |
|---|--:|--:|---|
| burro3 | 1.57 | 3.27 | PASS &rarr; FAIL |
| burro5 | 1.47 | 5.99 | PASS &rarr; FAIL |
| burro7 | 1.17 | 4.21 | PASS &rarr; FAIL |
| coyote-05 | 1.73 | 3.97 | PASS &rarr; FAIL |
| burro9 | 2.18 | 3.69 | FAIL both |
| coyote-03 | 2.30 | 5.13 | FAIL both |

### The cause

**The recorded LNG numbers were produced by the stock solver. The current
defaults dispatch the patched one.**

`CfdConfigurationPresets.ApplyCryogenicOverride` sets
`UsePatchedSctSolver = true` whenever the source gas is flagged cryogenic,
which every LNG bench is. That dispatches `rhoReactingBuoyantFoamSct`, which
reads `Sct = 0.15` from `transportProperties` and applies it. The solver log
confirms it plainly:

```
Exec   : rhoReactingBuoyantFoamSct -parallel        (OpenFOAM 2412)
Selecting RAS turbulence model buoyantKEpsilon
    Ceps3 -0.33;  sigmaEps 1.167;  Prt 0.85;  Sct 0.15;
Reading turbulent Schmidt number (Sct) from transportProperties
    Sct = 0.15
```

Disabling `UsePatchedSctSolver` in the preset and re-running burro3 returns
the recorded value exactly:

| burro3 | MRB | RMSE | FAC2 | MG | VG | |
|---|--:|--:|--:|--:|--:|---|
| Recorded 2026-05-16 | 0.43 | — | 0.67 | 1.57 | 1.14 | PASS |
| Patched solver, `Sct = 0.15` | 1.032 | 0.9216 | 0.3333 | 3.271 | 1.188 | FAIL |
| Stock solver, `Sct = 1.0` | 0.4321 | 0.6327 | 0.6667 | **1.571** | 1.134 | PASS |

Nothing is broken. Not the environment, not the mesh, not the WSL dispatch.
The OpenFOAM path works; it is running a different configuration from the one
that produced the table.

### The contradiction this exposes

Two decisions in this project disagree, and the disagreement was silent until
the suite was re-run.

[The Vu reproduction section](#vu-2019-reproduction-attempts) concludes that
every element of the Vu stack degraded predictions, and states that the three
flags are **disabled by default per the empirical degradation**.

`CfdConfigurationPresets` enables one of them anyway, with its own reasoning
in the code:

> Stock `rhoReactingBuoyantFoam` hard-codes `Sct = 1.0` in `YEqn.H`, so the
> 0.15 we just set above is ignored unless we dispatch the patched binary
> that actually reads `Sct` from `transportProperties`.

Both are defensible. Writing `Sct = 0.15` into a case and then running a
binary that ignores it is genuinely misleading, which is what the preset set
out to fix. And the measurements genuinely say that applying it makes the LNG
predictions worse. The code is what runs, so the patched solver wins, and the
table above stopped matching.

### What has to be decided

This is a methodology question, not a bug to be quietly patched:

- **Turn the preset off.** Restores the five LNG passes and matches the
  documented decision. Accepts that `Sct = 0.15` sits in the generated case
  with no effect, which anyone reading the case will misread.
- **Leave it on and re-measure the table.** The honest route if `Sct = 0.15`
  is held to be the physically correct closure. Costs five passes and owes an
  explanation of why Vu's recipe degrades results in this implementation.

Until it is settled, **five PASS rows in the LNG cohort are not reproducible
from a default checkout**, and that is the more important fact than either
number.

## Methodology notes

### SPM definitions (Chang & Hanna 2004; Vu 2019 §1.4.2)

| Metric | Description | Acceptable | Perfect |
|---|---|---:|---:|
| MRB | Mean Relative Bias | [-0.4, 0.4] | 0 |
| RMSE | Root Mean Square Error (normalised) | < 2.3 | 0 |
| NMSE | Normalised Mean Square Error | — | 0 |
| FAC2 | Fraction within factor of 2 | > 0.5 | 1 |
| MG | Geometric Mean Bias | [0.67, 1.5] | 1 |
| VG | Geometric Variance | < 3.3 | 1 |

Each bench overrides these with a per-cohort tolerance derived from the
cited reference paper (see `acceptance.ReferenceMatchTolerance` in each
`.dsbench` file).

### What a PASS means

PASS means DisperSim's SPMs are within the per-bench tolerance band
**relative to the published reference**, NOT that DisperSim's predictions
exactly match the experiment. For example, the Falcon 1 PASS reports
FAC2 = 0.00 — same as FLACS's published Falcon FAC2 = 0.00. Both codes
struggle on Falcon-with-fence; the bench validates that DisperSim is no
worse than FLACS at this case.

### Known limitations

1. **Stock `rhoReactingBuoyantFoam` Sct = 1.0 hard-coded.** Affects 3 LNG
   benches. The patched WSL binary is built but disabled by default
   pending the upstream audit (see Vu reproduction section above).

2. **BuoyantTracerEngine runs CPU-only.** FluidX3D benches take 25–35
   min per run because the buoyant scalar tracer is a C# semi-Lagrangian
   solver with BFECC. GPU port is in `TODO.md`.

3. **No two-phase / aerosol model.** CO2PipeHaz, Desert Tortoise, and
   Jack Rabbit II near-field fail because the engine has no rainout,
   pool re-evaporation, or solid CO₂ sublimation. The
   `TwoPhaseSourceCalculator` infrastructure exists for jet-source
   thermodynamics but does not yet feed the dispersion step.

4. **Gaussian engine out of scope for dense gas / urban arrays.** The
   6 heavy-gas Gaussian benches document this limit explicitly.

## References

| Code | Source |
|---|---|
| FLACS unobstructed LNG cohort | Hansen, O.R.; Gavelli, F.; Ichard, M.; Davis, S.G. (2010). J. Loss Prev. Process Ind. 23, 857–877. |
| FLACS Prairie Grass cohort | Hanna, S.R.; Chang, J.C. (2004). Atmos. Environ. 38, 2233–2249. |
| FLACS Falcon cohort | Hansen et al. 2010 (above). |
| PHAST Jack Rabbit II | Mazzola, T.; Hanna, S. (2021). Atmos. Environ. 244, 117905. |
| Burro experiments | Koopman, R.P. et al. (1982). Lawrence Livermore Natl. Lab. UCRL-53186. |
| Vu thesis (LNG OpenFOAM) | Vu, T.L. (2019). NTU Singapore. <https://dr.ntu.edu.sg/handle/10356/103659> |
| Briggs σ-coefficients | Briggs, G.A. (1973). Diffusion Estimation for Small Emissions. ATDL No. 79, NOAA. |
| Spadeadam CO₂ | Witlox, H.W.M. et al. (2014). J. Loss Prev. Process Ind. 30, 243–255. |
| Gant & Ivings CH₄ jet | Gant, S.E.; Ivings, M.J. (2005). HSL/2005/13. |
| Schefer H₂ jet | Schefer, R.W. et al. (2008). Int. J. Hydrogen Energy 33, 8035–8042. |
| MUST | Biltoft, C.A. (2001). DPG WDTC-FR-01-121 (restricted). |
| DAT632 | Mack, A.; Spruijt, M.P.N. (2013). J. Hazard. Mater. 250–251, 1–14. |

Full bibliography in [references.md](references.md).
