---
layout: default
title: Validation
nav_order: 10
---

# Validation
{: .no_toc }

1. TOC
{:toc}

## What validation means here

A dispersion model is only useful if you can show it produces the right numbers
on cases where the answer is already known. DisperSim 3D validates itself
in two complementary ways:

1. **Self-consistency tests.** These run the engine against reference values
   that were captured from a previous, known-good build. If a code change
   makes the predictions drift, the test fails. This catches regressions early
   without saying anything about how the engine compares to reality.
2. **Experimental validation.** These run the engine against published
   measurements from real field trials (Prairie Grass, Burro, Falcon, Coyote,
   Jack Rabbit II, and others) and score how close the predictions are to
   what was actually observed.

The two kinds of test live side by side. A passing self-consistency test
proves the code did not change; a passing experimental test proves the
physics is right within the documented tolerances.

## Fire radiation benchmarks

Radiation benchmarks live in `benchmarks/fire/*.fbench` and run with:

```bash
DisperSim3D.CLI --validate-fire benchmarks/fire
```

A `.fbench` file describes a published fire test in two independent halves, and
the runner scores them separately:

1. **Flame geometry and emissive power** — flame length, flame diameter, SEP.
   These exercise the correlations and the energy balance.
2. **Incident flux at radiometers** — measured kW/m² at known positions. These
   exercise the view factor and the atmospheric transmissivity on top of the
   first half.

The split matters diagnostically: a model can reproduce the flame and still get
the flux wrong, and knowing which half failed is most of the fix.

Acceptance is a predicted/observed ratio band, defaulting to the same factor-of-two
convention (FAC2) the dispersion benches use.

### Unverified data is never a pass

Every `.fbench` declares `dataConfidence`:

| Value | Meaning |
|---|---|
| `High` | read from a table in the cited source |
| `Medium` | read off a figure, or from a secondary citation |
| `Unverified` | not checked against the source at all |

An `Unverified` bench is evaluated and printed, but reported as **NOT COUNTED**
rather than as a pass. A green tick against a number nobody confirmed is worse
than having no test.

### LNG pool fire results

Five LNG pool fire tests, spanning pool diameters from 6 to 36 m and measured
emissive powers from 92 to 265 kW/m², all sourced from Raj (2005) Table 1 and
the summary text on its page 5:

| Test | D (m) | SEP measured | SEP predicted | ratio |
|---|--:|--:|--:|--:|
| AGA San Clemente (1973) | 6.1 | 143–178 → 160 | 115 | 0.72 |
| China Lake on water (1974–76) | 15 | 220 ± 30 | 193 | 0.88 |
| Esso Libya trench (1969) | 18 | 92 | 79 | 0.86 |
| Maplin Sands (1980) | 20 | 150–220 → 185 | 159 | 0.86 |
| Montoir (1987) | 35.7 | 257–273 → 265 | 231 | 0.87 |

Every case is inside the factor-of-two band, and the flame length for Montoir —
the only test here with a published length — comes out at 68 m against 78 m
measured, a ratio of 0.87.

**The interesting result is the bias, not the pass.** Four of the five ratios sit
between 0.86 and 0.88: the model tracks how emissive power scales with pool
diameter across a factor of six in size, and then under-predicts the level by a
consistent 13%. That is not scatter, and it is worth naming rather than
celebrating five green ticks.

The most likely home for it is the radiative fraction. The model derives
`SEP = χ·Q/(π·D·L)`, and χ is not reported for Montoir or Maplin Sands; 0.25 was
assumed for both. A χ nearer 0.29 would put those two on the nose. Deliberately
not tuned: fitting a constant to five points is not validation, and the
remaining evidence needed to separate χ from the flame-length correlation is the
radiometer data none of these benches have yet.

The 6.1 m AGA case is the outlier at 0.72, and it is also the smallest by far.
Thomas's correlation is a large-fire correlation, so this is the expected place
for it to lose accuracy.

### What the first benchmark found


The seeded Montoir 35 m LNG bench immediately exposed a real defect. The model
was applying Mudan's soot-blend cap to every pool fire, and Mudan is calibrated
on sooty hydrocarbons — kerosene, gasoline, crude. For a 35 m pool it caps the
emissive power at about 22 kW/m². LNG burns clean and stays radiant at that
diameter; the trials report 165–265 kW/m².

The energy balance on its own gave 211 kW/m², right inside the reported range.
The fix was `FireSource.IsSootyFuel`, which routes clean fuels past the soot
blend. No self-consistency test could have caught this: the model was
internally coherent and physically wrong, which is exactly the failure mode
experimental validation exists for.

## Reference experiments

### Gaussian plume against Prairie Grass

The Gaussian plume engine is validated against the Project Prairie Grass
field experiment (Barad 1958). Prairie Grass is the classic reference
dataset for ground-level continuous releases: 68 separate SO2 tracer runs
over flat short-grass prairie at O'Neill, Nebraska, each one a few minutes
long at well-characterised wind and stability. Five Prairie Grass runs are
bundled, one per Pasquill stability class:

- Run 7 (class B). Q = 89.9 g/s, U at 2 m = 4.44 m/s, unstable atmosphere.
- Run 11 (class C). Q = 95.9 g/s, U at 2 m = 7.61 m/s, slightly unstable.
- Run 22 (class D). Q = 48.4 g/s, U at 2 m = 7.39 m/s, neutral.
- Run 29 (class E). Q = 41.5 g/s, U at 2 m = 3.94 m/s, slightly stable.
- Run 35 (class E). Q = 38.8 g/s, U at 2 m = 1.80 m/s, stable.

In every run the source sits at 0.46 m above ground, the roughness length
is 0.006 m, and the measured concentrations are the peak (arc-maximum) over
a sensor arc at 1.5 m height. Sensor arcs are at 50, 100, 200, 400, and
800 m downwind.

Acceptance criteria follow Chang and Hanna (2004), the standard reference
for atmospheric model performance evaluation:

- MRB within plus or minus 0.67
- MG within [0.5, 2.0]
- VG less than 4.0
- FAC2 at least 0.3

The Gaussian engine uses Briggs (1973) open-country power-law fits for the
sigma_y and sigma_z dispersion coefficients, and evaluates wind speed at
`max(H, WindMeasurementHeightM)`. The reason for the second choice: the
PGT sigma curves were originally fit against wind measured at the
measurement height (typically 2 m for Prairie Grass, 10 m for industrial
sites), so the model has to query the wind profile at that same height to
stay self-consistent.

For per-sensor predicted-versus-measured tables see the
[benchmark results page](benchmark-results.md).

### CFD against jet experiments

For the CFD pipeline (OpenFOAM and FluidX3D) we lean on the dispersion
literature:

- Birch et al. (1984, 1987). Methane sonic jets at 2.0 and 3.5 bar
  upstream pressure. Centreline mole fraction decay along the jet axis.
- Chuech et al. (1989). Air sonic jet velocity decay.
- Gant and Ivings (2005). Methane jet from a 10.5 mm orifice at 5.0 bar
  and 250 K. Used here as a flammable cloud volume reference.
- Fiates and Vianna (2016). Full 416 by 488 by 77 m offshore platform with
  five leak directions crossed with four wind directions, compared against
  ANSYS-CFX.

Expected agreement per Fiates and Vianna (2016): within 10% on jet
centreline against experimental data, within 20% against commercial CFD
on cloud volume, and 7% difference on the largest cloud case in their
comparison.

### CFD against heavy-gas and cryogenic releases

For atmospheric dispersion of heavy gases and cryogenic spills:

- Mack and Spruijt (2013). Run `reactingFoam` with turbulent Schmidt
  number `Sc_t = 0.7` and buoyancy correction `C_eps3 = -0.33`. Reproduces
  the Hamburg wind tunnel SF6 case DAT632 over an 8.6 degree slope in all
  8 sensors at FAC2, and matches Fluent on a separate CO2 release over
  hilly terrain (8821 kg/s).
- Vu (2019). Custom solver `gasDispersionBuoyantFoam` with HHTSL k-epsilon
  constants, `Sc_t = 0.15`, and a fixed-temperature ground. Reproduces the
  Burro 3, 7, 8 and 9 LNG arc-maximum concentrations within published
  Hanna SPM bounds (FAC2 = 1.0, MRB = -0.15, RMSE = 0.10), outperforming
  FLACS on every metric.
- Schalau et al. (2021). `rhoReactingBuoyantFoam` with z0-based
  atmospheric boundary conditions reaches VDI 3783/9 hit ratio q above
  66% on the cube and 7 by 3 building array tests.

### FluidX3D buoyant tracer

The GPU LBM dispersion path (`FluidX3DDispersion`) ships with its own
benchmarks:

- DAT632 (Hamburg wind tunnel). FluidX3D with mass-injection source and
  Smagorinsky subgrid diffusivity (Cs = 0.092, Sct = 0.7) reproduces the
  SF6 concentrations at all 5 sensors within 16% (MRB = -0.098, VG = 1.003,
  FAC2 = 1.0, all Hanna SPMs pass). The wind field runs on the GPU at
  480 cubed; the tracer is a CPU semi-Lagrangian solver at 120 cubed.
- Burro 9 (LNG cryogenic spill). FluidX3D with the buoyant tracer engine
  reproduces CH4 concentrations at the 140, 400 and 800 m arcs (MRB =
  0.044, MG = 1.046, FAC2 = 1.0, VG = 1.051, all Hanna SPMs pass). The
  buoyant tracer adds density-based vertical buoyancy, gravity-current
  lateral spreading with front speed coefficient Cgc = 0.5, and BFECC
  (Back and Forth Error Compensation and Correction) advection that
  reduces numerical diffusion from first to second order. The tracer
  grid is 3 times finer than the scenario grid (300 cubed on a 100-cell
  base, giving 6.7 m cells) to resolve the near-field pool evaporation
  source (32 m diameter LNG pool, Q = 109.5 kg/s, T_exit = 111 K).
- Gant and Ivings (2005). Methane jet from a 10.5 mm orifice at 5.0 bar
  and 250 K. Birch and Schefer expanded source at 32 mm diameter and
  100 m/s gives choked flow Q = 0.054 kg/s (Cd = 0.65). Against the
  initial Birch 1/x centreline-decay estimates the FluidX3D tracer
  achieves FAC2 = 1.0, MG = 1.10, MRB = 0.096, with all four sensors
  inside 20% of the analytical jet model. Flammable cloud volume in the
  LFL to UFL mass-fraction envelope is 1.17 m^3. This was the first
  benchmark to validate cloud volume directly through the
  `expectedCloudVolumeM3` field.

## Validation harness

DisperSim 3D ships an integrated harness that runs `.dsbench` files end to
end and scores them with the Hanna Statistical Performance Measures. Three
ways to invoke it:

- CLI: `DisperSim3D.CLI --validate <file-or-dir>`. Pass a single bench
  file or a directory of them. Exit code is 0 when every benchmark passes
  its acceptance ranges and 2 otherwise.
- UI: `Dispersion` then `Validate against Benchmarks...` opens a dialog
  with a multi-select file list, a Run-all button, a colour-coded SPM
  table (green = pass, red = fail) and an `Export Markdown...` button.
- API: `DisperSim3D.Validation.ValidationRunner.Run(spec, envCfg, log)`
  returns a `ValidationReport` for custom harnesses.

## Hanna Statistical Performance Measures

The Hanna SPMs are implemented in
[`SpmCalculator.cs`](https://github.com/DanWBR/DisperSim3D/blob/main/DisperSim3D/Validation/SpmCalculator.cs)
as a pure function over `IList<SensorPair>` following Vu (2019) Section
1.4.2 and Chang and Hanna (2004).

The table has two acceptance columns. The "Regression" column has tight
bounds and is used for self-consistency and CFD baseline tests where any
real drift means the code changed. The "External" column has the looser
bounds from Chang and Hanna (2004) and is the one used when comparing
predictions against real experimental data, where bigger spread is
expected because the physics is genuinely uncertain.

| Metric | Formula | Regression | External | Perfect |
|---|---|---|---|---|
| MRB Mean Relative Bias | $\,2\cdot\overline{(C_o - C_p)/(C_o + C_p)}\,$ | $[-0.4,\,0.4]$ | $[-0.67,\,0.67]$ | $0$ |
| RMSE (normalised) | $\,\sqrt{\overline{(C_o - C_p)^2}}\,/\,\overline{C_o}\,$ | $<2.3$ | $<3.0$ | $0$ |
| NMSE | $\,\overline{(C_o - C_p)^2}\,/\,(\overline{C_o}\cdot\overline{C_p})\,$ | - | - | $0$ |
| FAC2 | fraction with $\,0.5 \le C_p / C_o \le 2.0\,$ | $\ge 0.5$ | $\ge 0.3$ | $1$ |
| MG geometric mean bias | $\,\exp\!\overline{(\ln C_o - \ln C_p)}\,$ | $[0.67,\,1.5]$ | $[0.5,\,2.0]$ | $1$ |
| VG geometric variance | $\,\exp\!\operatorname{var}(\ln C_o - \ln C_p)\,$ | $<3.3$ | $<4.0$ | $1$ |

A short reading guide to the metrics:

- MRB is a symmetric bias indicator. Zero means no bias; positive means
  the engine underpredicts on average; negative means it overpredicts.
- FAC2 is the fraction of sensors where the prediction is within a factor
  of 2 of the observation. It is the single most popular indicator in
  the dispersion literature because it has a clear physical reading: a
  FAC2 of 0.5 means half your sensors are within a factor of 2.
- MG (geometric mean bias) and VG (geometric variance) work in log space
  and are appropriate when concentrations span orders of magnitude, which
  they always do in dispersion validation.

Geometric metrics floor zero values at $10^{-12}$ so the log does not blow
up to minus infinity when the engine predicts zero at some sensor.

## Two-phase pressurized source

Pressurized liquefied gases (Cl2, NH3, propane, cold liquid CO2) and
supercritical fluids (CO2 above its critical pressure) flash partially
to vapor the moment they expand to atmospheric pressure. The thermodynamic
vapor fraction at the expanded state can be estimated in two ways:

The first way is the simple Clapeyron flash relation. It is the energy
balance "sensible cooling pays for latent heat":

$$\;x_v \;=\; \frac{C_{p,liq}\,(T_{vessel} - T_{bp})}{\Delta H_{vap}}\;$$

The second way is a real-fluid isenthalpic (PH) flash with a proper
equation of state. DisperSim 3D bundles DWSIMCore as a single
self-contained DLL (see [Cross-platform notes](cross-platform.md)) and
uses Peng-Robinson 1978 by default. The PH flash gives the thermodynamic
vapor fraction directly from the EoS and is much more accurate near the
critical region.

`TwoPhaseSourceCalculator` (in `DisperSim3D.Core`) tries the DWSIMCore PH
flash first and falls back to the Clapeyron formula if DWSIMCore is not
available or the flash fails to converge. The fallback uses a small
built-in compound table (CO2, NH3, Cl2, CH4, propane, n-butane, H2S) plus
a Watson-correlation fallback for less common substances. The result
carries the following information:

- `VaporMassFlowKgPerS`. What enters an airborne dispersion engine.
- `DropletMassFlowKgPerS`. What forms a re-evaporating pool.
- `TempExitK`, `VelocityExitMS`, `DiameterPseudoM`. Birch and Schefer
  pseudo-source geometry for the vapor portion.

Bench files can opt into this pre-processing through an optional
`twoPhase` block inside `source`:

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

When `enabled` is true the `ValidationRunner` replaces the source's
`releaseRateKgPerS`, `stackDiameterM`, `exitTemperatureK` and
`exitVelocityMPerS` with the calculator's vapor-only output before the
dispersion engine sees the source. The bench's user-supplied
`releaseRateKgPerS` is honoured as the total measured mass flow. The
gas-orifice formula in `HighPressureLeakModel` is bypassed because it
underpredicts liquid-storage releases by a factor of 5 to 10.

### When NOT to use the two-phase split

The Clapeyron and PH flash both give the THERMODYNAMIC vapor fraction:
the fraction of mass that would be vapor at equilibrium with ambient
pressure. For high-momentum pressurized jets (Jack Rabbit I, Jack Rabbit
II, Desert Tortoise) the fine droplets formed by the flash re-evaporate
in seconds inside the cold expanding jet, so the actual AIRBORNE fraction
is close to one. Applying the thermodynamic x_v alone removes the rainout
mass without modelling the pool re-evaporation that would put it back
into the cloud, and predictions get worse.

Bench files for those scenarios keep `twoPhase` defined with
`enabled: false` as documentation of the recipe, and the dispersion engine
sees the full mass flow. Two-phase is currently enabled for no bundled
bench in this revision. The calculator and the pipeline integration are in
place for future work that adds a coupled airborne + pool re-evaporation
source.

## Cloud volume validation

When a `.dsbench` file declares `expectedCloudVolumeM3`, the runner
computes the flammable cloud volume (cells where LFL is less than or equal
to c which is less than or equal to UFL) through `FlammableCloudCalculator`
and reports the ratio predicted-over-expected. The
`acceptance.CloudVolumeRatio` field accepts or rejects the ratio. This is
the standard metric for jet and plume consequence assessment per Gant and
Ivings (2005) and Fiates and Vianna (2016). The LFL and UFL in the
`.dsbench` must be in the same unit as the engine's concentration field
(for example, mass-fraction LFL = 0.028 for 5% v/v CH4).

## `.dsbench` file format

A `.dsbench` is a JSON document that describes a complete simulation
recipe plus the observed values from the cited paper. Schema version is
`dsbench/v1`. Top-level fields:

| Field | Purpose |
|---|---|
| `name`, `citation`, `description` | Provenance. |
| `source` | Gas, position, release rate, pool or stack diameter, exit conditions. |
| `source.twoPhase` | Optional. Clapeyron or PH flash recipe (see [Two-phase pressurized source](#two-phase-pressurized-source)). |
| `meteo` | Wind speed and direction, Pasquill class, ambient T and P, `roughnessLengthM`. |
| `domain` | Size, grid resolution, simulation duration, time step. |
| `solver` | String matching `CfdSolverType` (for example, `RhoReactingBuoyantFoam`). |
| `concentrationKind` | `PeakOverTime` or `FinalSnapshot`. |
| `unit` | `KgPerM3`, `MoleFraction`, or `MassFraction`. Must match the engine output. |
| `sensors[]` | Name, position `[x,y,z]`, `measuredKgM3`. |
| `expectedCloudVolumeM3` | Optional. Expected flammable cloud volume in m^3. Triggers volume validation. |
| `acceptance` | Per-metric `{ "min": ..., "max": ... }`. Either bound is optional. |
| `acceptance.CloudVolumeRatio` | Optional. Range for the predicted-over-expected cloud volume ratio. |

JavaScript-style `// comments` are accepted in the JSON parser.

## Bundled benchmarks

The bundled `.dsbench` files live under
[`benchmarks/`](https://github.com/DanWBR/DisperSim3D/tree/main/benchmarks)
at the repo root. The collection covers three categories:

1. Self-consistency tests against the engine's own output. These catch
   any change in numerical behaviour that would shift the predictions.
2. Experimental validation against published field-trial data.
3. Regression baselines for CFD solvers, where the reference values were
   captured from the current OpenFOAM or FluidX3D pipeline rather than
   from the original experiment.

| File | Solver | Role |
|---|---|---|
| `Gauss-D-selftest.dsbench` | GaussianPlume | Self-consistency. Briggs (1973) sigma coefficients at neutral stability. |
| `Gauss-Puff-selftest.dsbench` | GaussianPuff | Self-consistency. Puff `StepTo` time loop. |
| `prairie-grass-07-B.dsbench` | GaussianPlume | Experimental. Prairie Grass Run 7, stability B (Barad 1958). |
| `prairie-grass-11-C.dsbench` | GaussianPlume | Experimental. Prairie Grass Run 11, stability C (Barad 1958). |
| `prairie-grass-22-D.dsbench` | GaussianPlume | Experimental. Prairie Grass Run 22, stability D (Barad 1958). |
| `prairie-grass-29-E.dsbench` | GaussianPlume | Experimental. Prairie Grass Run 29, stability E (Barad 1958). |
| `prairie-grass-35-E.dsbench` | GaussianPlume | Experimental. Prairie Grass Run 35, stability E (Barad 1958). |
| `burro3.dsbench`, `burro7.dsbench`, `burro8.dsbench`, `burro9.dsbench` | RhoReactingBuoyantFoam | LNG dispersion against the LLNL Burro series (Koopman 1982). Stabilities range from F (Burro 8) through D and C. |
| `coyote-03.dsbench`, `coyote-05.dsbench` | RhoReactingBuoyantFoam | LNG dispersion against the Coyote series (Goldwire 1983). |
| `falcon-01.dsbench`, `falcon-04.dsbench` | RhoReactingBuoyantFoam | LNG dispersion inside a vapour fence (Brown 1990). |
| `maplin-sands-27.dsbench` | RhoReactingBuoyantFoam | LNG spilled onto water (Puttock 1982). |
| `dat632.dsbench` | RhoReactingBuoyantFoam | SF6 wind-tunnel release over a slope (Mack and Spruijt 2013). Also runs with `FluidX3DDispersion`. |
| `thorney-island-08.dsbench` | GaussianPuff | Instantaneous heavy-gas release of a Freon-12/N2 mixture (McQuaid 1985). |
| `desert-tortoise-04.dsbench` | GaussianPlume | Pressurized liquefied ammonia jet (Goldwire 1985). |
| `kit-fox-u5-2.dsbench` | GaussianPlume | Continuous CO2 area source in an obstacle array (Hanna 2004). |
| `jack-rabbit-i-t07.dsbench` | GaussianPuff | Liquefied chlorine release into a depression (Hanna 2012). |
| `jack-rabbit-ii-t01.dsbench`, `jack-rabbit-ii-t07.dsbench` | GaussianPuff | Pressurized chlorine release inside a mock urban array (Hanna 2021). |
| `gant-ivings-2005.dsbench` | FluidX3DDispersion | CH4 sonic jet at 10.5 mm, 5 bar, 250 K (Gant and Ivings 2005). First benchmark to use `expectedCloudVolumeM3` and `CloudVolumeRatio`. |
| `co2pipehaz-6mm.dsbench` | FluidX3DDispersion | Supercritical CO2 through a 6 mm orifice (Gant 2014). In DisperSim's target range. |
| `spadeadam-co2.dsbench` | FluidX3DDispersion | BP cold liquid CO2 through 25.62 mm orifice (Witlox 2014). |
| `hydrogen-jet-schefer.dsbench` | FluidX3DDispersion | High-pressure H2 release at 207 bar through 1.91 mm orifice (Schefer 2008). Tests positive-buoyancy handling. |

### What these benchmarks actually validate

The Prairie Grass benchmarks validate the Gaussian plume engine against
real field measurements (Barad 1958). Acceptance follows Chang and Hanna
(2004) published criteria. The E stability runs pass; the B, C and D
runs fail by small margins. Those failures are inside the documented
limitations of the Gaussian plume model for near-ground continuous
releases at those particular wind and stability combinations, not a code
bug. See the [Prairie Grass discussion](benchmark-results.md#prairie-grass-discussion)
on the results page.

The observed values for the CFD benches (Burro 8 and 9, DAT632 in some
configurations) are regression baselines captured from the current solver
pipeline at the current grid resolution. They are not a quantitative match
to the experimental ground truth from the cited papers. There are two
reasons:

1. The stock `rhoReactingBuoyantFoam` solver in OpenFOAM v2512 does not
   expose the turbulent Schmidt number. Its species transport equation
   reads `fvm::laplacian(turbulence->muEff(), Yi)`, which is equivalent
   to `Sc_t = 1.0` implicit. Vu (2019) reached experimental FAC2 = 1.0
   by writing a custom solver `gasDispersionBuoyantFoam` with
   `Sc_t = 0.15` for LNG. Without that custom code, the stock predictions
   are systematically about three times lower than Vu's at the LNG arcs.
2. Mesh resolution. Vu used 897 thousand cells; the bundled benches use
   100 cubed divided by 2 (about 500 thousand cells) plus refinement.

So the CFD benches catch any change in the case writer or the solver
pipeline that would alter the predicted concentrations. They are a
regression net, not a quantitative match against the original
experiments.

The Gant and Ivings (2005) bench also locks the flammable cloud volume
at 1.169 m^3, computed by `FlammableCloudCalculator` over the LFL to UFL
mass-fraction range. Any change to the source injection, tracer
advection, diffusivity, or BFECC limiter that shifts the cloud volume
by more than 20% will break the `CloudVolumeRatio` acceptance.

## Adding a new benchmark

1. Copy the closest existing `.dsbench` as a template.
2. Fill in `source`, `meteo`, `domain` and `solver` from the experiment's
   published parameters. Cite the paper in the `citation` field.
3. List the sensors with their measured values from the citation. Verify
   that `unit` matches your chosen solver's output (`MoleFraction` for
   species-transport solvers, `KgPerM3` for Gaussian).
4. Set the `acceptance` ranges. The default Hanna ranges (External
   column above) are usually fine.
5. For cloud-volume benchmarks, optionally set `expectedCloudVolumeM3`
   and add `acceptance.CloudVolumeRatio` (for example,
   `{ "min": 0.8, "max": 1.2 }`). Make sure `source.gas.lfl` and `ufl`
   are in the same unit as the engine's concentration field
   (`MassFraction` for species-transport solvers).
6. Exercise the new bench with
   `DisperSim3D.CLI --validate path/to/your.dsbench`.

## Detector optimisation validation

`SetCoveringSolver.SolveExact` is verified against:

- A trivial 4-variable problem (Vianna 2019 Section 5.1) with expected
  Z = 52 and X = [1, 0, 0, 1].
- A p-median test on 10 facilities (Vianna 2019 Section 5.2 Table 3),
  giving results identical to CPLEX.
- Nine covering instances ranging from 25 to 14400 cells (Vianna 2019
  Section 5.3), giving the same optimal cardinality as the reference.

For greedy-only mode, expect at most one column over the optimum on
structured (axis-aligned cubic) instances.
