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

| Metric | Formula | Acceptable | Perfect |
|---|---|---|---|
| **MRB** Mean Relative Bias | $\,2\cdot\overline{(C_o - C_p)/(C_o + C_p)}\,$ | $[-0.4,\,0.4]$ | $0$ |
| **RMSE** (normalised) | $\,\sqrt{\overline{(C_o - C_p)^2}}\,/\,\overline{C_o}\,$ | $<2.3$ | $0$ |
| **NMSE** | $\,\overline{(C_o - C_p)^2}\,/\,(\overline{C_o}\cdot\overline{C_p})\,$ | — | $0$ |
| **FAC2** | fraction with $\,0.5 \le C_p / C_o \le 2.0\,$ | $[0.5,\,2.0]$ | $1$ |
| **MG** geometric mean bias | $\,\exp\!\overline{(\ln C_o - \ln C_p)}\,$ | $[0.67,\,1.5]$ | $1$ |
| **VG** geometric variance | $\,\exp\!\operatorname{var}(\ln C_o - \ln C_p)\,$ | $<3.3$ | $1$ |

Geometric (log-based) metrics floor zero values at $10^{-12}$ to avoid $-\infty$.

## `.dsbench` file format

JSON document defining a complete recipe + the observed values from the
citation. Schema version `dsbench/v1`. Top-level fields:

| Field | Purpose |
|---|---|
| `name`, `citation`, `description` | provenance |
| `source` | gas, position, release rate, pool/stack diameter, exit conditions |
| `meteo` | wind speed/direction, Pasquill class, ambient T/p, `roughnessLengthM` |
| `domain` | size, grid resolution, simulation duration, timestep |
| `solver` | string matching `CfdSolverType` (e.g. `RhoReactingBuoyantFoam`) |
| `concentrationKind` | `PeakOverTime` or `FinalSnapshot` |
| `unit` | `KgPerM3`, `MoleFraction`, `MassFraction` — must match the engine output |
| `sensors[]` | name, position `[x,y,z]`, `measuredKgM3` |
| `acceptance` | per-metric `{ "min": …, "max": … }`, either bound optional |

JavaScript-style `// comments` are accepted.

## Bundled benchmarks

Located under [`benchmarks/`](https://github.com/DanWBR/DisperSim3D/tree/main/benchmarks)
at the repo root. All 5 currently **PASS** as regression baselines:

| File | Solver | Role |
|---|---|---|
| `gauss-D-smoketest.dsbench` | GaussianPlume | Engine self-consistency for Pasquill-Gifford coefficients |
| `gauss-puff-smoketest.dsbench` | GaussianPuff | Engine self-consistency for the puff `StepTo` loop |
| `burro9.dsbench` | RhoReactingBuoyantFoam | LNG cryogenic, neutral ABL, 3 arcs (Koopman 1982 / Vu 2019 §5.4) |
| `burro8.dsbench` | RhoReactingBuoyantFoam | Same setup under stable ABL (Pasquill F, U = 1.8 m/s) |
| `dat632.dsbench` | RhoReactingBuoyantFoam | SF₆ over slope, Hamburg WT (Mack &amp; Spruijt 2013) |

### What these benchmarks lock in

The observed values for the CFD benches (Burro 8/9, DAT632) are
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

## Adding a new benchmark

1. Copy the closest existing `.dsbench` as a template.
2. Fill in `source` / `meteo` / `domain` / `solver` from the experiment's
   published parameters; cite the paper in `citation`.
3. List sensors with their measured values from the citation. Verify
   `unit` matches your chosen solver's output (`MoleFraction` for
   species-transport solvers, `KgPerM3` for Gaussian).
4. Set `acceptance` ranges. Default Hanna ranges are usually fine.
5. `DisperSim3D.CLI --validate path/to/your.dsbench` to exercise it.

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
