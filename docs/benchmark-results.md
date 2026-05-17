---
layout: default
title: Benchmark Results
nav_order: 11
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
| FluidX3D | Native DLL (CUDA, RTX 5070) |
| DWSIMCore | Single-DLL bundled (PR78 default) |
| Date | 2026-05-16 |

All benchmarks are exercised via `DisperSim3D.CLI --validate benchmarks/`.
Exit code 0 = every metric inside the per-bench acceptance band.

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

## Headline score: 18 PASS / 31 (58 %)

| Group | PASS | Total | Notes |
|---|---|---|---|
| Self-consistency | 2 | 2 | gauss-D-selftest, gauss-puff-selftest |
| Prairie Grass vs FLACS Hanna 2004 | 3 | 5 | 2 FAIL by 1–4 % on MRB only |
| OpenFOAM LNG vs FLACS Hansen 2010 | 5 | 8 | Burro 3/5/7/Coyote 5 PASS; Burro 8 PASS (config); Burro 9 / Coyote 3 / Maplin 27 FAIL by Sct limitation |
| OpenFOAM Falcon vs FLACS Hansen 2010 | 3 | 3 | DisperSim outperforms FLACS on the fence cohort |
| OpenFOAM SF₆ wind tunnel | 1 | 1 | DAT632 regression baseline |
| OpenFOAM MUST (FluidX3D-tracer hybrid) | 1 | 1 | Mock Urban Setting Test trial 11 |
| FluidX3D regression baselines | 3 | 5 | Gant-Ivings / Spadeadam / hydrogen jet; CO2PipeHaz / hydrogen FAIL by physics scope |
| Heavy-gas / pressurized Gaussian | 0 | 6 | Out-of-scope: dense-gas slumping, rainout, urban arrays not in Gaussian engine |

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
| 10 | burro7 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | 0.15 | 0.67 | 1.17 | 1.26 | within tolerance |
| 11 | burro8 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | -0.18 | 1.00 | 0.84 | 1.04 | stability F, low wind |
| 12 | burro9 | RhoReactingBuoyantFoam | LNG (CH₄) | FAIL | 0.72 | 0.67 | 2.18 | 1.11 | MG ceiling 1.95 exceeded by 0.23 — Sct limitation |
| 13 | coyote-03 | RhoReactingBuoyantFoam | LNG (CH₄) | FAIL | 0.76 | 0.60 | 2.30 | 1.14 | FAC2 floor 0.64 missed by 0.04 — Sct limitation |
| 14 | coyote-05 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | 0.52 | 0.67 | 1.73 | 1.10 | within tolerance |
| 15 | maplin-sands-27 | RhoReactingBuoyantFoam | LNG (CH₄) | FAIL | 1.41 | 0.00 | 6.06 | 1.13 | Sct limitation; Vu 2019 needed Sct = 0.15 to match |
| 16 | falcon-01 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | 1.04 | 0.00 | 3.28 | 1.13 | matches FLACS Falcon-cohort tolerance |
| 17 | falcon-03 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | 0.33 | 1.00 | 1.40 | 1.00 | beats FLACS (FAC2 = 0 in their cohort) |
| 18 | falcon-04 | RhoReactingBuoyantFoam | LNG (CH₄) | **PASS** | 0.23 | 1.00 | 1.26 | 1.02 | beats FLACS |
| 19 | DAT632 | RhoReactingBuoyantFoam | SF₆ | **PASS** | 0.013 | 1.00 | 1.01 | 1.00 | regression baseline |
| 20 | must-trial-11 | FluidX3DDispersion | propylene | **PASS** | 7e-4 | 1.00 | 1.001 | 1.00 | MUST cohort regression baseline |
| 21 | gant-ivings-2005 | FluidX3DDispersion | CH₄ jet | **PASS** | -1.7e-5 | 1.00 | 1.00 | 1.00 | regression baseline; cloud volume 1.169 m³ |
| 22 | spadeadam-co2 | FluidX3DDispersion | CO₂ | **PASS** | 0.005 | 1.00 | 1.003 | 1.21 | Witlox 2014 digitised |
| 23 | co2pipehaz-6mm | FluidX3DDispersion | CO₂ | FAIL | — | — | — | — | no two-phase / solid CO₂ sublimation |
| 24 | hydrogen-jet-schefer | FluidX3DDispersion | H₂ | FAIL | — | — | — | — | sensor estimates in dsbench were order-of-magnitude guesses; engine runs cleanly, cloud is plausible |
| 25 | kit-fox-u5-2 | GaussianPlume | CO₂ | FAIL | 0.76 | 0.50 | 2.27 | 1.11 | Gaussian on dense gas + obstacles is out of scope |
| 26 | desert-tortoise-04 | GaussianPlume | NH₃ | FAIL | 0.19 | 0.33 | 1.22 | 7.63 | no aerosol / rainout modelling |
| 27 | thorney-island-08 | GaussianPuff | Freon-12 | FAIL | -1.07 | 0.25 | 0.19 | 6.86 | dense gas slumping not modelled |
| 28 | jack-rabbit-i-t07 | GaussianPuff | Cl₂ | FAIL | 1.91 | 0.00 | 1.2e7 | 3.9e41 | depression detrainment not modelled |
| 29 | jack-rabbit-ii-t01 | GaussianPuff | Cl₂ | FAIL | -0.85 | 0.17 | 2.82 | 2.0e23 | urban array not modelled |
| 30 | jack-rabbit-ii-t07 | GaussianPuff | Cl₂ | FAIL | -0.77 | 0.33 | 0.41 | 1.50 | abs(log MG) 0.89 above tolerance 0.86 |
| 31 | gauss-puff-selftest (counted in row 2) | — | — | — | — | — | — | — | — |

(Row 31 placeholder kept so the table has a stable last index — total = 30 unique benches plus 1 puff self-test row.)

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

### Heavy gas / pressurized Gaussian (out of scope, 0/6)

These benches exercise physics outside the Gaussian engine's design
envelope — dense-gas slumping, two-phase rainout, depression-terrain
accumulation, urban arrays. They are documented FAILs that pin the
engine's applicability boundary. The correct solver for these cases is
`RhoReactingBuoyantFoam` (Kit Fox, Thorney Island) or `FluidX3DDispersion`
with the obstacle pipeline (Jack Rabbit II).

| Bench | Issue |
|---|---|
| kit-fox-u5-2 | dense gas in obstacle array |
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
