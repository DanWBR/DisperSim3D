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
- Once enabled by default, also wire it into `GenerateBuoyantPimpleFoam`
  and `GenerateReactingFoam` for the other heavy-gas solvers.

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

The buoyant scalar tracer is currently a C# CPU semi-Lagrangian solver
(`DisperSim3D/Core/BuoyantTracerEngine.cs`) with BFECC advection,
density-based buoyancy, and gravity-current lateral spreading. It is the
slow stage of every `FluidX3DDispersion` run: the GPU LBM wind field
takes 1-2 min, the CPU tracer takes 25-35 min on a 240 cubed grid.
During the tracer step the RTX 5070 idles at ~3% utilisation.

Goal: move the tracer kernel into the FluidX3D native DLL so it runs on
the same OpenCL device as the wind field. Reuses the existing buffer
layout (no CPU/GPU copies per snapshot) and brings the whole dispersion
runtime down by roughly an order of magnitude.

What needs porting (current C# logic in `BuoyantTracerEngine`):

- Semi-Lagrangian advection with BFECC error correction (3 passes).
- Density-based vertical buoyancy from mass-fraction Y and temperature T.
- Gravity-current lateral spreading (front speed model, Cgc = 0.5).
- Species + temperature diffusion (constant D, k).
- Mass injection source (point, sphere, pool) at runtime.
- Obstacle handling (boolean blocked array from voxelised AABBs).

API: extend `disp_bridge.cpp` with `fx3d_step_buoyant_tracer(handle, dt)`
and snapshot getters that read the tracer field back to host memory only
when DisperSim asks for it. Keep the C# `BuoyantTracerEngine` interface
unchanged so callers (`FluidX3DRunner`, validation harness) do not have
to change.

Estimated effort: 1-2 weeks. Wait until current validation campaign is
done so the regression baselines stay stable while the port lands.

