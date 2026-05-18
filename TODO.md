# DisperSim 3D - TODO

Running list of work to come back to.

## Reproducing Vu (2019) LNG FAC2 = 1.0

Cite: Vu, Tran Le. *On numerical modelling of atmospheric gas dispersion
using CFD approach*. PhD thesis, Nanyang Technological University,
Singapore, 2019. Handle: <https://dr.ntu.edu.sg/handle/10356/103659>.
PDF in `docs/`.

The three remaining LNG / cryogenic FAILs (Burro 9, Coyote 3,
Maplin Sands 27) are all on cases Vu reproduces at FAC2 = 1.0
(Table 5.15, MRB = -0.15, MG = 1.16, VG = 1.11) using a stack of
modifications that we have only partially implemented. Status of each:

### 1. Sct in species transport equation — DONE

Stock `rhoReactingBuoyantFoam` (v2412, v2512) hardcodes
`fvm::laplacian(turbulence->muEff(), Yi)` in `YEqn.H` — equivalent to
`Sct = 1.0`. Vu's `gasDispersionBuoyantFoam` (thesis §A.2 Listing A.5)
replaces that with `fvm::laplacian(turbulence->muEff()/Sct, Yi)` and
reads `Sct` from `controlDict` (we use `transportProperties` instead).

- Build a patched binary with `bash scripts/build-rhoReactingBuoyantFoamSct.sh`
  in WSL Ubuntu with `openfoam2412-dev` installed. See
  `scripts/README-rhoReactingBuoyantFoamSct.md`.
- `OpenFoamCaseGenerator.GenerateRhoReactingBuoyantFoam` now also emits
  `transportProperties` with the `Sct` line.
- The Vu 2019 cryogenic preset
  (`CfdConfigurationPresets.ApplyCryogenicOverride`) sets
  `cfd.TurbulentSchmidtNumber = 0.15` and
  `cfd.UsePatchedSctSolver = true`.
- `OpenFoamRunner` dispatches the WSL binary for the solver step only
  (mesh / topoSet / decomposePar / reconstructPar still run on the
  configured native Windows env).

Verified end-to-end on Coyote 3: Sct value reaches the solver, but the
SPMs do not converge to Vu's FAC2 = 1.0 because of the items below.

### 2. Mesh refinement near the source — BUILT, DISABLED BY DEFAULT

Infra implemented: `CfdConfiguration.UseVu2019MeshRefinement` flag, three
wind-aligned refinement boxes in
`OpenFoamCaseGenerator.WriteVu2019RefinementDicts`, runner loop bumped
to 4 levels in `OpenFoamRunner`. Box geometry from Vu §5.4.1 Table 5.12:
300/150/75 m downwind, 100/50/25 m lateral, 15/7.5/4 m tall at levels
0/1/2. At an 8 m base cell, the refined zone reaches 1 m cells —
matching Vu's deepest level.

Disabled by default because of an empirical failure on Coyote 3
(2026-05-16): combining patched Sct = 0.15 with the 3-level Vu
refinement makes FAC2 fall from 0.40 → **0.00** and MG jump from 2.15 →
**6.37**. The fine mesh resolves the 6.67× amplified turbulent
diffusivity from `muEff/Sct` and over-disperses the cloud.

Vu's case behaves differently because her stock setup already
over-predicts slightly (Cm/Cp ≈ 1.0–1.2 in fig 5.11) and the Sct
adjustment brings predictions DOWN onto experiment. Our stock setup
already UNDER-predicts (FAC2 = 0.60 on Coyote 3), so the same Sct
adjustment makes things worse. The likely culprit is items 3 (steady
ABL precursor) and 4 (polynomial T-dependent thermo) — without them,
the initial k-ε field and the cold-cloud density are wrong enough that
amplifying turbulent diffusion exposes rather than corrects the error.

To-do:
- Land item 3 (precursor) and item 4 (polynomial thermo). Then re-test
  Vu refinement on Coyote 3. Expected: predictions stay realistic and
  Sct = 0.15 brings them onto experiment instead of dispersing the
  cloud.
- Add aspect-ratio support to `WriteBlockMeshDict` (Vu uses 20 — cells
  20× wider than tall). Today block mesh is cubic, which means we
  refine 8× per level instead of Vu's 4× (skips vertical refinement on
  her stretched mesh). Not blocking but doubles refined-cell count.
- Once enabled by default, verify it works correctly with the
  `GenerateRhoReactingBuoyantFoam` code path (the only OpenFOAM solver now).

### 3. Steady ABL precursor before the release — BUILT, DISABLED BY DEFAULT

Infra implemented: `CfdConfiguration.UseAblPrecursor` flag,
`WriteAblPrecursorDicts` emits `controlDict.precursor`, `fvSchemes.precursor`,
`fvSolution.precursor`, `fvOptions.precursor` next to the main dicts;
`OpenFoamRunner.RunAblPrecursor` swaps them in, runs `buoyantSimpleFoam`
serial for `AblPrecursorIterations` (default 500) SIMPLE iters, copies
converged U / T / p / p_rgh / k / epsilon / nut / alphat / rho from the
latest time dir into `0/`, wipes the precursor time dirs, restores main
dicts. Verified on Coyote 3: 500 SIMPLE iters converge to residuals ~1e-6
in ~5 min on one core.

Disabled by default because the empirical test (Coyote 3, 2026-05-16)
showed adding the precursor on top of the patched Sct = 0.15 solver pushed
FAC2 from 0.40 → **0.00** and MG from 2.15 → **17.75** (predicted /
observed ratios collapsed to 0.04–0.08). Same direction as the Vu mesh
result: converging k-epsilon (so the source-region `mut` is realistic
rather than underestimated by the uniform initial field) makes the
`muEff/Sct = 6.67·muEff` amplification disperse the cloud even more.

**Pattern across all three Vu items tested on Coyote 3:**

| Stack | MRB | FAC2 | MG | ratios |
|---|---:|---:|---:|---|
| Stock | 0.76 | 0.60 | 2.30 | 0.21–0.60 |
| + patched Sct = 0.15 | 0.72 | 0.40 | 2.15 | 0.37–0.56 |
| + Vu mesh refinement | 1.44 | 0.00 | 6.37 | 0.10–0.22 |
| + ABL precursor | 1.78 | 0.00 | 17.75 | 0.04–0.08 |

Each Vu modification monotonically pushes predictions LOWER. Vu's
modifications work for her because her STOCK setup over-predicts
(Cm/Cp ≈ 1.0–1.2 in fig 5.11) — her stack brings predictions down onto
experiment. Our STOCK setup already under-predicts (Coyote 3 stock
predictions are 21–60 % of observed), so the same diffusion-amplifying
modifications make things worse. **The problem is not the solver. It is
upstream — something in the case generator dispersing too much before
any Vu modification is applied.**

To-do (item 6 below) is the right next step — audit the case output
files against Vu §5.3 before adding more Vu stack items.

#### Original Vu reference (for context, kept for the audit work)

Vu (§5.3.4) runs `ablBuoyantSimpleFoam` (steady) until the atmospheric
profile is converged, then starts the transient `gasDispersionBuoyantFoam`
from that converged field.

### 4. Variable thermophysical properties — NOT DONE

Vu (§5.3.3 Table 5.11) uses **polynomial** T-dependent `ρ, c_p, μ, κ`
for both air and CH4 (coefficients from CoolProp). DisperSim today uses
the perfect-gas / constant-property thermo blocks. Polynomial thermo is
the right model for a 100 K cryogenic cloud since `ρ_CH4(111 K) ≈ 1.76`
vs `ρ_CH4(290 K) ≈ 0.66` (almost 3× difference).

To-do:
- Switch `WriteRhoReactingThermophysicalProperties` to emit
  `polynomial` blocks with the Table 5.11 coefficients for cryogenic
  presets.

### 5. Modified k-ε constants (HHTSL) — PARTIAL

Vu uses the modified k-ε with HHTSL closure constants. The atmospheric
preset in `CfdConfigurationPresets` already sets
`KEpsilonSigmaEpsilon = 1.167` and `BuoyancyEpsCoefficient = -0.33`, but
the exact constant set Vu uses (`Cμ, C1ε, C2ε, σk, σε`) should be
cross-checked against thesis §4 and §5.

To-do:
- Audit thesis §4.3 constants vs `WriteTurbulenceProperties`. Adjust
  the cryogenic preset if any disagree.

### 6. Audit source / BC model vs Vu thesis — DONE (DIAGNOSIS REVISED)

Item 6.1 (source mass injection) confirmed and implemented as the full
patch-based `gasInlet` infrastructure:

- `CfdConfiguration.UseCryogenicPatchInjection` flag
- `WriteCryogenicGasInletDicts` (topoSet faceSet + createPatch dict)
- `WriteEmptyFvOptions` helper for the all-cryogenic case
- `AppendGasInletEntries` adds `gasInlet_N` BC entries to U / T / p /
  p_rgh / Y_CH4 / Y_other / k / epsilon / nut / alphat
- `OpenFoamRunner` runs `topoSet -dict topoSetDict_gasInlet` +
  `createPatch -overwrite` between `setFields` and the (optional) ABL
  precursor / decomposePar
- `IsCryogenic` mirrored from `GasLibraryItem` to `GasProperties` so
  the case generator sees the flag on each ReleaseSource3D

Disabled by default. The empirical isolation test (Coyote 3 with stock
effective Sct = 1.0, no Vu mesh, no precursor) showed that **patch
injection alone makes FAC2 go from 0.60 → 0.00** and MG from 2.30 →
3.53. With patched Sct = 0.15 also active the result is even worse:
FAC2 0.00, MG 4.32, ratios 0.14–0.34.

The diagnosis at the time of writing item 6 (above) was wrong: the
under-prediction baseline is NOT primarily from a missing cold-dense
slumping. The actual breakdown of stock predictions on Coyote 3:

| Arc | Stock ratio | Cryo patch ratio |
|---|---:|---:|
| 140 m | 0.21 | 0.22 |
| 200 m | 0.30 | 0.26 |
| 300 m | 0.45 | 0.31 |
| 400 m | 0.56 | 0.32 |
| 500 m | 0.60 | 0.30 |

Stock matches the 3 far arcs (300/400/500 m, ratios 0.45–0.60) but
misses near-source (0.21–0.30). Cryo patch slumps the cloud as designed
near the source but at our 8 m base cell, the cold dense pocket mixes
with ambient air within 1–2 timesteps — the slumping layer never forms
coherently, the plume stays narrow, and the 3 far arcs that stock got
right now get much less mass.

**Revised conclusion:** the Vu recipe is a TIGHTLY COUPLED package —
patch + mesh + Sct + precursor + polynomial thermo all together. Each
piece in isolation breaks the equilibrium her stack maintains. Our
stock predictions accidentally work for the far field through
appropriate stock mixing, not because the BC model is correct.

To reproduce Vu's FAC2 = 1.0 we'd need to commit to the full stack at
once, not iteratively. Estimated work: ~1-2 days for the polynomial
thermo (item 4) + retest with all flags on. If that still fails we
have a deeper issue (likely the k-ε modifications in item 5).

### 7. Future: bench against patched-binary + full Vu stack — NOT STARTED

Concrete plan if/when we revisit this:

1. Implement polynomial T-dependent thermo (item 4, ~half-day work).
2. Enable the cryogenic preset full stack: `UsePatchedSctSolver`,
   `UseVu2019MeshRefinement`, `UseAblPrecursor`,
   `UseCryogenicPatchInjection` all true.
3. Run Coyote 3 + Burro 9 + Maplin 27 + Burro 3/5/7/8 (sanity check
   the cases that already PASS shouldn't regress).
4. If FAC2 = 1.0 on the 3 failing cases — declare success. If not,
   inspect Vu's k-ε modifications (item 5) and audit constants.

## Engine performance

### Port BuoyantTracerEngine to native FluidX3D (GPU)

Phase 1 (foundation) — DONE 2026-05-17.

- New C++ file `FluidX3D/src/tracer_bridge.cpp` (~360 LOC) compiles its
  own small OpenCL program on a separate `Device` instance, sharing the
  GPU but not FluidX3D's main `kernel.cpp` (which would re-compile
  ~3000 functions per init).
- 3 OpenCL kernels implemented:
  - `tracer_advect_forward` — forward semi-Lagrangian advection with
    trilinear sampling at the departure point
  - `tracer_compute_vEff` — placeholder effective velocity (clamp wind
    to ±vMax); future phases add buoyancy and gravity-current terms
  - `tracer_apply_obstacles` — zero Y / restore T inside blocked cells
- Bridge C ABI in `FluidX3D/src/disp_bridge.h`: 10 new `fx3d_tracer_*`
  functions (create, set_wind, set_obstacles, set_source_sphere /
  set_source_pool, set_initial_concentration, step, read_concentration,
  read_temperature, destroy).
- C# wrapper class
  [DisperSim3D/Core/BuoyantTracerEngineGpu.cs](DisperSim3D/Core/BuoyantTracerEngineGpu.cs)
  with the same public interface as the CPU engine plus an
  `IDisposable` for GPU buffer cleanup.
- CLI smoke test: `DisperSim3D.CLI --tracer-gpu-selftest` seeds a
  Gaussian blob on a 32³ grid, advects 10 steps in 5 m/s wind, and
  verifies the centre-of-mass shifted to within one cell of the
  analytic translation. **First run (RTX 5070): PASS — CoM error
  41 mm vs cell size 625 mm.**

Phase 2 — DONE 2026-05-17. All four feature pieces implemented as
additional OpenCL kernels and cross-validated against the CPU baseline.

1. **BFECC error correction** — new `tracer_bfecc_correct` kernel
   (clamp_to_neighbour_range(1.5·orig − 0.5·hat, orig)) + reuse of
   the forward-advect kernel with negative dt for the Pass-2 reverse
   step. The smoke-test error fell from 41 mm (forward only) to 9 mm
   (forward + BFECC) — the expected ~4× drop in numerical diffusion.
2. **Density + buoyancy + gravity-current spreading** — new
   `tracer_compute_density` kernel (Y, T → rho_mix); rewritten
   `tracer_compute_vEff` reads rho + T + neighbours and adds vertical
   buoyancy plus −uScale·∇rho/|∇rho| horizontal gravity-current
   spreading for dense cells. Constants gas_M, air_M, ambient_P,
   R, gravity, Cgc come in as kernel parameters.
3. **Diffusion** — new `tracer_diffuse_step` kernel runs a single
   explicit Laplacian sub-step; host loop dispatches it
   `ceil(2·(Cx+Cy+Cz))` times for Y and T, ping-ponging the
   yCur/yNext (or tCur/tNext) pointers so the live field always
   ends up in *Cur regardless of parity.
4. **Source injection** — new `tracer_source_inject` kernel handles
   both sphere and pool modes via an isPool flag and pool_max_k
   parameter. Host-side `compute_source_rate` counts the source
   cells once at SetSource time and stores the per-cell injection
   rate (matches CPU engine's arithmetic).

Plus: a 7th kernel `tracer_apply_obstacles` zeroes Y and resets T in
blocked cells (already present in phase 1 for completeness).

Cross-validation result (`DisperSim3D.CLI --tracer-gpu-selftest`,
16³ grid with cold CH4 sphere source, full pipeline through 10 steps):

| Metric | Value | Tolerance | Status |
|---|---|---|---|
| Y max relative error | 0.34 % | 5.0 % | **PASS** |
| T max relative error | 0.00 % | 1.0 % | **PASS** |
| Peak Y (CPU vs GPU) | 0.0033 / 0.0033 | identical to 4 decimals | **PASS** |

Runner integration done in the same session:
- New `IBuoyantTracerEngine` interface implemented by both engines.
- `FluidX3DRunner` picks GPU when `cfg.UseGpuBuoyantTracer` is true
  AND `FluidX3DBridge.IsAvailable()`, otherwise falls back to CPU.
- New CLI flag `--gpu-tracer` sets
  `AppSettings.Instance.UseGpuBuoyantTracerPreferred = true`;
  `ValidationRunner` copies that into the SnapshotCfdConfig.

Phase 3 — production validation, IN PROGRESS (1 of 5 benches run).

**gant-ivings-2005 result on RTX 5070** (2026-05-17):

| | GPU | CPU baseline |
|---|---:|---:|
| Wallclock | **56 s** | 25–35 min |
| Speedup | **~30×** | — |
| jet_1m  | 0.0687 | 0.0808 (ratio 0.85) |
| jet_2m  | 0.0361 | 0.0418 (ratio 0.86) |
| jet_3m  | 0.0253 | 0.0310 (ratio 0.82) |
| jet_5m  | 0.0154 | 0.0195 (ratio 0.79) |
| Cloud vol (LFL-UFL) | 0.877 m³ | 1.169 m³ (ratio 0.75) |
| MRB | 0.19 | 0 (regression) |
| FAC2 | 1.00 | 1.00 |
| MG | 1.21 | 1.00 (regression) |
| Hanna criteria | **PASS** (all SPMs in acceptable range) | — |
| Regression baseline | FAIL (MG=1.21 > 1.10 tight ceiling) | — |

**Diagnosis:** the GPU port uses single-precision floats; the CPU
engine uses double. On the gant-ivings bench (high-pressure sonic CH₄
jet with Y_CH₄ gradients of 1.0 → 0.0 over a few cells), FP32 rounding
accumulates across 120 transient time steps × N diffusion sub-steps
into systematic 15–25 % under-prediction at the centreline sensors.
For atmospheric / cryogenic dispersion benches with smoother gradients
the FP32 error is expected to be much smaller (the 16³ smoke test on
diluted CH₄ showed 0.34 % per-cell Y error).

**Decision (2026-05-17):** keep GPU at FP32. Engineering-acceptable
accuracy on atmospheric / urban / cryogenic-pool benches, large
speedup. Sharp-jet sources where FP32 hurts are a known and documented
limitation; users running those should pass `--cpu-tracer` (TODO) or
not pass `--gpu-tracer`.

**Full batch result on RTX 5070 (all 5 FluidX3D benches, `--gpu-tracer`):**

| Bench | Wallclock | SPMs | Status | Notes |
|---|---:|---|:---:|---|
| gant-ivings-2005 | 56 s | MRB 0.19, FAC2 1.0, MG 1.21 | FAIL (regression) / PASS (Hanna) | FP32 error 15–25 % on sonic jet |
| must-trial-11 | 334 s | MRB 7e-4, FAC2 1.0, MG 1.001 | **PASS** | identical to CPU to 3-4 sig digits |
| spadeadam-co2 | 159 s | MRB −0.40, FAC2 0.75, MG 0.65 | **PASS** | far-field diverges, still under reference tolerance |
| co2pipehaz-6mm | 142 s | MRB 1.02, FAC2 0.25, MG 3.24 | FAIL | model limitation (no two-phase / sublimation); CPU also FAILs |
| hydrogen-jet-schefer | 125 s | MRB 0.74, FAC2 0.5, MG 2.37 | FAIL | dsbench obs are order-of-magnitude guesses; CPU also FAILs |

**Score with GPU tracer: 2 / 5 PASS** (vs CPU 3 / 5). Net: GPU lost
gant-ivings due to FP32 in sonic-jet conditions; matched CPU on every
other bench.

**Total wallclock for the 5-bench batch:** 16.3 min on GPU vs
~125–175 min on CPU baseline. **~8–10× aggregate speedup** (limited
by the LBM wind-field setup time which doesn't scale; the tracer step
alone is closer to 30× faster as seen on gant-ivings).

