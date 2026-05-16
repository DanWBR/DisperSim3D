---
layout: default
title: Validation
nav_order: 10
---

# Validation
{: .no_toc }

1. TOC
{:toc}

## Reference experiments

### Gaussian plume (analytical)

The Gaussian plume engine is validated against the **Project Prairie Grass**
field experiment (Barad 1958) — 68 SO2 tracer releases over flat short-grass
prairie at O'Neill, Nebraska. Five runs spanning Pasquill classes B through E:

- **Run 7** (B) — Q = 89.9 g/s, U(2 m) = 4.44 m/s, unstable
- **Run 11** (C) — Q = 95.9 g/s, U(2 m) = 7.61 m/s, slightly unstable
- **Run 22** (D) — Q = 48.4 g/s, U(2 m) = 7.39 m/s, neutral
- **Run 29** (E) — Q = 41.5 g/s, U(2 m) = 3.94 m/s, slightly stable
- **Run 35** (E) — Q = 38.8 g/s, U(2 m) = 1.80 m/s, stable

All runs: source at 0.46 m, z0 = 0.006 m, arc-maximum SO2 concentrations
at 1.5 m height, arcs at 50, 100, 200, 400, 800 m downwind.
Acceptance criteria follow Chang & Hanna (2004): MRB within ±0.67,
MG within [0.5, 2.0], VG < 4.0, FAC2 ≥ 0.3.

Dispersion coefficients are Briggs (1973) open-country power-law fits.
The engine evaluates wind speed at `max(H, WindMeasurementHeightM)` to
remain consistent with the PGT calibration convention (sigma curves were
fit to data using wind at measurement height, not at source height).

See [Benchmark Results](benchmark-results.md#prairie-grass-prairie-grass-runs) for
per-sensor tables.

### CFD pipeline

The CFD pipeline reproduces the test cases of:

- **Birch et al. (1984, 1987)** — methane sonic jets at 2.0 and 3.5 bar
  upstream pressure — molar fraction decay along the centreline.
- **Chuech et al. (1989)** — air sonic jet velocity decay.
- **Gant &amp; Ivings (2005)** — jet simulation from a 10.5 mm orifice at
  5.0 bar / 250 K (cloud volume comparison).
- **Fiates &amp; Vianna (2016)** — full 416 × 488 × 77 m offshore platform
  with 5 leak directions × 4 wind directions, comparison against ANSYS-CFX.

Expected agreement (per Fiates &amp; Vianna 2016): within 10 % of
experimental data on jet centreline; within 20 % of commercial-CFD results
on cloud volume; 7 % difference on the largest cloud case.

For atmospheric / heavy-gas / cryogenic releases:

- **Mack &amp; Spruijt 2013** — `reactingFoam` with `Sc_t = 0.7` and
  `C_ε3 = -0.33` reproduces Hamburg WT dataset DAT632 (SF₆ over 8.6° slope)
  within 8/8 sensors at FAC2 and matches Fluent's solution on a CO₂ release
  in an atmospheric boundary layer over hilly terrain (8821 kg/s).
- **Vu 2019** — `gasDispersionBuoyantFoam` with HHTSL k-ε constants,
  `Sc_t = 0.15`, `FixedTemperature` ground reproduces Burro 3/7/8/9 LNG
  vapour dispersion peak concentrations within Hanna SPM ranges
  (FAC2 = 1.0, MRB = -0.15, RMSE = 0.10) — outperforming FLACS on every
  metric.
- **Schalau et al. 2021** — `rhoReactingBuoyantFoam` with z₀-based
  atmospheric BCs achieves VDI 3783/9 hit ratio q > 66 % on cube and 7 × 3
  building array test cases.

For GPU LBM dispersion (`FluidX3DDispersion`):

- **DAT632 (Hamburg wind tunnel)** — `FluidX3DDispersion` with mass-injection
  source and Smagorinsky subgrid diffusivity (Cs = 0.092, Sct = 0.7)
  reproduces SF₆ concentrations at all 5 sensors within 16 %
  (MRB = −0.098, VG = 1.003, FAC2 = 1.0 — all Hanna SPMs pass).
  The GPU LBM wind field at 480³ feeds a CPU semi-Lagrangian tracer at 120³.
- **Burro 9 (LNG cryogenic spill)** — `FluidX3DDispersion` with the buoyant
  tracer engine reproduces CH₄ concentrations at 140 / 400 / 800 m arcs
  (MRB = 0.044, MG = 1.046, FAC2 = 1.0, VG = 1.051 — all Hanna SPMs pass).
  The buoyant tracer adds density-based vertical buoyancy, gravity-current
  lateral spreading (front speed model with Cgc = 0.5), and BFECC
  (Back and Forth Error Compensation and Correction) anti-diffusion advection
  to reduce numerical diffusion to second order. The tracer runs at 3×
  the scenario grid resolution (300³ on a 100-base grid, 6.7 m cells) to
  resolve the near-field pool evaporation source (32 m diameter LNG pool,
  Q = 109.5 kg/s, T_exit = 111 K).
- **Gant &amp; Ivings 2005 (CH₄ sonic jet)** — `FluidX3DDispersion` with the
  buoyant tracer engine (3× grid, 180×180×90 on a 60-base grid, 0.11 m
  cells) reproduces a methane jet from a 10.5 mm orifice at 5.0 bar / 250 K
  (choked flow Q = 0.054 kg/s, Cd = 0.65). Against initial Birch 1/x
  centreline-decay estimates the tracer achieves FAC2 = 1.0, MG = 1.10,
  MRB = 0.096 — all four sensors within 20 % of the analytical jet model.
  Flammable cloud volume (LFL–UFL envelope in mass-fraction units) =
  1.17 m³. First benchmark to validate cloud volume via
  `expectedCloudVolumeM3`; the `.dsbench` now carries calibrated regression
  baselines from the FluidX3D prediction itself.

## Validation harness

DisperSim 3D ships an integrated harness that runs benchmarks end-to-end
and scores them with Hanna Statistical Performance Measures. Live entry
points:

- **CLI** — `DisperSim3D.CLI --validate <file-or-dir>`. Single file or
  directory of `.dsbench` files. Exit code 0 when every benchmark passes
  its acceptance ranges; 2 otherwise.
- **UI** — `Dispersion → Validate against Benchmarks…` opens a dialog
  with multi-select file list, Run-all button, colour-coded SPM table
  (green = pass, red = fail) and an `Export Markdown…` button.
- **API** — `DisperSim3D.Validation.ValidationRunner.Run(spec, envCfg, log)`
  returns a `ValidationReport` for custom harnesses.

## Hanna SPM definitions

Implemented in
[`SpmCalculator.cs`](https://github.com/DanWBR/DisperSim3D/blob/main/DisperSim3D/Validation/SpmCalculator.cs)
as a pure function over `IList<SensorPair>` (Vu 2019 §1.4.2,
Chang &amp; Hanna 2004):

| Metric | Formula | Regression | External | Perfect |
|---|---|---|---|---|
| **MRB** Mean Relative Bias | $\,2\cdot\overline{(C_o - C_p)/(C_o + C_p)}\,$ | $[-0.4,\,0.4]$ | $[-0.67,\,0.67]$ | $0$ |
| **RMSE** (normalised) | $\,\sqrt{\overline{(C_o - C_p)^2}}\,/\,\overline{C_o}\,$ | $<2.3$ | $<3.0$ | $0$ |
| **NMSE** | $\,\overline{(C_o - C_p)^2}\,/\,(\overline{C_o}\cdot\overline{C_p})\,$ | — | — | $0$ |
| **FAC2** | fraction with $\,0.5 \le C_p / C_o \le 2.0\,$ | $\ge 0.5$ | $\ge 0.3$ | $1$ |
| **MG** geometric mean bias | $\,\exp\!\overline{(\ln C_o - \ln C_p)}\,$ | $[0.67,\,1.5]$ | $[0.5,\,2.0]$ | $1$ |
| **VG** geometric variance | $\,\exp\!\operatorname{var}(\ln C_o - \ln C_p)\,$ | $<3.3$ | $<4.0$ | $1$ |

The "Regression" column is used for self-consistency and CFD baselines
(tight tolerances catch solver changes). The "External" column is per
Chang &amp; Hanna (2004) for comparison against field experiments.

Geometric (log-based) metrics floor zero values at $10^{-12}$ to avoid $-\infty$.

## Two-phase pressurized source (opt-in)

Pressurized liquefied gases (Cl₂, NH₃, propane, cold liquid CO₂) and
supercritical fluids (CO₂ at &gt; P_c) flash partially to vapor when expanded
to atmospheric pressure. The thermodynamic vapor fraction at the expanded
state can be estimated analytically via the Clapeyron flash relation:

$$\;x_v \;=\; \frac{C_{p,liq}\,(T_{vessel} - T_{bp})}{\Delta H_{vap}}\;$$

DisperSim 3D ships a `TwoPhaseSourceCalculator` (in `DisperSim3D.Core`) that
computes this split using a built-in compound table (CO₂, NH₃, Cl₂, CH₄,
propane, n-butane, H₂S) plus a Watson-correlation fallback for less common
substances via `DwsimThermo.GetCompoundInfo`. It returns:

- `VaporMassFlowKgPerS` — what would enter an airborne dispersion engine
- `DropletMassFlowKgPerS` — what would form a re-evaporating pool
- `TempExitK`, `VelocityExitMS`, `DiameterPseudoM` — Birch &amp; Schefer
  pseudo-source geometry for the vapor portion

Bench files can opt into this pre-processing via an optional `twoPhase`
block in the `source`:

```json
"twoPhase": {
  "enabled": true,
  "compoundName": "Carbon dioxide",
  "vesselPressurePa": 15869325,
  "vesselTemperatureK": 278.15,
  "orificeDiameterM": 0.02562,
  "dischargeCoefficient": 0.65,
  "targetExpandedVelocityMS": 100.0
}
```

When enabled, the `ValidationRunner` replaces the source's
`releaseRateKgPerS` / `stackDiameterM` / `exitTemperatureK` / `exitVelocityMPerS`
with the calculator's vapor-only output before the dispersion engine sees
the source. The bench's user-supplied `releaseRateKgPerS` is honoured as
the **total** measured/observed mass flow (the gas-orifice formula in
`HighPressureLeakModel` underpredicts liquid-storage releases by 5-10× and
is bypassed).

### Important caveat — when NOT to enable

The Clapeyron $x_v$ is the **thermodynamic** vapor fraction at equilibrium
with ambient pressure. For high-momentum pressurized jets (Jack Rabbit I/II,
Desert Tortoise) the fine droplets formed by the flash **re-evaporate in
seconds** in the cold expanding jet — the actual airborne fraction is much
higher than $x_v$. Applying $x_v$ alone removes the rainout mass without
modelling the pool re-evaporation that would add it back, and predictions
get substantially worse.

The bench files for these scenarios keep `twoPhase` defined with
`enabled: false` as documentation of the recipe, and the dispersion engine
sees the full mass flow.

Two-phase is currently enabled (true) for **no bundled bench** as of this
revision. The calculator and infrastructure are in place for future work
that adds a coupled airborne + pool re-evaporation source.

## Cloud volume validation

When a `.dsbench` file declares `expectedCloudVolumeM3`, the runner
computes the flammable cloud volume (cells where LFL ≤ c ≤ UFL) via
`FlammableCloudCalculator` and reports the ratio predicted/expected.
The `acceptance.CloudVolumeRatio` field accepts/rejects the ratio.
This is the standard metric for jet/plume consequence assessment per
Gant &amp; Ivings (2005) and Fiates &amp; Vianna (2016). LFL/UFL in the
`.dsbench` must be in the same unit as the engine's concentration
field (e.g. mass-fraction LFL = 0.028 for 5 % v/v CH₄).

## `.dsbench` file format

JSON document defining a complete recipe + the observed values from the
citation. Schema version `dsbench/v1`. Top-level fields:

| Field | Purpose |
|---|---|
| `name`, `citation`, `description` | provenance |
| `source` | gas, position, release rate, pool/stack diameter, exit conditions |
| `source.twoPhase` | *(optional)* Clapeyron flash recipe — see [Two-phase pressurized source](#two-phase-pressurized-source-opt-in) |
| `meteo` | wind speed/direction, Pasquill class, ambient T/p, `roughnessLengthM` |
| `domain` | size, grid resolution, simulation duration, timestep |
| `solver` | string matching `CfdSolverType` (e.g. `RhoReactingBuoyantFoam`) |
| `concentrationKind` | `PeakOverTime` or `FinalSnapshot` |
| `unit` | `KgPerM3`, `MoleFraction`, `MassFraction` — must match the engine output |
| `sensors[]` | name, position `[x,y,z]`, `measuredKgM3` |
| `expectedCloudVolumeM3` | *(optional)* expected flammable cloud volume (m³); triggers volume validation |
| `acceptance` | per-metric `{ "min": …, "max": … }`, either bound optional |
| `acceptance.CloudVolumeRatio` | *(optional)* range for predicted/expected cloud volume ratio |

JavaScript-style `// comments` are accepted.

## Bundled benchmarks

Located under [`benchmarks/`](https://github.com/DanWBR/DisperSim3D/tree/main/benchmarks)
at the repo root. 11 benchmarks: 2 self-consistency, 5 Prairie Grass
experimental validation, 4 CFD regression baselines:

| File | Solver | Role |
|---|---|---|
| `gauss-D-smoketest.dsbench` | GaussianPlume | Engine self-consistency for Briggs (1973) sigma coefficients |
| `gauss-puff-smoketest.dsbench` | GaussianPuff | Engine self-consistency for the puff `StepTo` loop |
| `prairie-grass-07-B.dsbench` | GaussianPlume | Prairie Grass Run 7, stability B (Barad 1958) |
| `prairie-grass-11-C.dsbench` | GaussianPlume | Prairie Grass Run 11, stability C (Barad 1958) |
| `prairie-grass-22-D.dsbench` | GaussianPlume | Prairie Grass Run 22, stability D (Barad 1958) |
| `prairie-grass-29-E.dsbench` | GaussianPlume | Prairie Grass Run 29, stability E (Barad 1958) |
| `prairie-grass-35-E.dsbench` | GaussianPlume | Prairie Grass Run 35, stability E (Barad 1958) |
| `burro9.dsbench` | RhoReactingBuoyantFoam | LNG cryogenic, neutral ABL, 3 arcs (Koopman 1982 / Vu 2019 §5.4). Also validated with `FluidX3DDispersion` (buoyant tracer, gravity-current spreading, BFECC, 3× grid) — all Hanna SPMs pass |
| `burro8.dsbench` | RhoReactingBuoyantFoam | Same setup under stable ABL (Pasquill F, U = 1.8 m/s) |
| `dat632.dsbench` | RhoReactingBuoyantFoam | SF₆ over slope, Hamburg WT (Mack &amp; Spruijt 2013). Also validated with `FluidX3DDispersion` (mass-injection source, Smagorinsky D) — all SPMs pass |
| `gant-ivings-2005.dsbench` | FluidX3DDispersion | CH₄ sonic jet, 10.5 mm @ 5 bar / 250 K (Gant &amp; Ivings 2005). Buoyant tracer 3× grid (180³), BFECC, cloud volume = 1.17 m³. First benchmark to use `expectedCloudVolumeM3` + `CloudVolumeRatio` acceptance |

### What these benchmarks lock in

The **Prairie Grass** benchmarks validate the Gaussian plume engine against
real field measurements (Barad 1958). Acceptance follows Chang &amp; Hanna
(2004) published criteria. Runs at E stability pass; B/C/D runs fail within
documented limitations of the Gaussian plume model (see
[benchmark results](benchmark-results.md#prairie-grass-discussion)).

The observed values for the **CFD benches** (Burro 8/9, DAT632) are
**regression baselines captured from the current solver pipeline at the
current grid resolution**, not the experimental ground truth from the
cited papers. Two reasons:

1. **`rhoReactingBuoyantFoam` (stock OpenFOAM v2512) does not expose Sc_t.**
   Its species transport equation reads `fvm::laplacian(turbulence->muEff(), Yi)`
   — equivalent to `Sc_t = 1.0` implicit. Vu 2019 reached experimental
   FAC2 = 1.0 by writing a custom solver `gasDispersionBuoyantFoam` with
   `Sc_t = 0.15` for LNG. Without that custom code, our predictions are
   systematically ~3× lower than Vu's at the LNG arcs.

2. **Mesh resolution.** Vu used 897 k cells; we use 100³ / 2 = 500 k base +
   refinement.

So the CFD benches **catch any change in the case-writer or solver pipeline**
that would alter the predicted concentrations — they're a regression net,
not a quantitative match against the original experiments.

The Gant &amp; Ivings 2005 bench additionally locks the **flammable cloud
volume** (1.169 m³) computed by `FlammableCloudCalculator` over the
LFL–UFL mass-fraction range. Any change to the source injection, tracer
advection, diffusivity, or BFECC limiter that shifts the cloud volume
by more than ±20 % will break the `CloudVolumeRatio` acceptance.

## Adding a new benchmark

1. Copy the closest existing `.dsbench` as a template.
2. Fill in `source` / `meteo` / `domain` / `solver` from the experiment's
   published parameters; cite the paper in `citation`.
3. List sensors with their measured values from the citation. Verify
   `unit` matches your chosen solver's output (`MoleFraction` for
   species-transport solvers, `KgPerM3` for Gaussian).
4. Set `acceptance` ranges. Default Hanna ranges are usually fine.
5. *(Optional)* For cloud-volume benchmarks: set `expectedCloudVolumeM3`
   and add `acceptance.CloudVolumeRatio` (e.g. `{ "min": 0.8, "max": 1.2 }`).
   Ensure `source.gas.lfl` / `ufl` are in the same unit as the engine's
   concentration field (`MassFraction` for species-transport solvers).
6. `DisperSim3D.CLI --validate path/to/your.dsbench` to exercise it.

## Detector optimisation validation

`SetCoveringSolver.SolveExact` is verified against:

- Trivial 4-variable problem (Vianna 2019 §5.1): expected `Z = 52`,
  `X = [1, 0, 0, 1]`.
- p-median test (10 facilities, Vianna 2019 §5.2 Table 3): identical
  results to CPLEX.
- 9 covering instances ranging 25–14 400 cells (Vianna 2019 §5.3): same
  optimal cardinality.

For greedy-only mode, expect ≤ 1 column over the optimum on structured
(axis-aligned cubic) instances.
