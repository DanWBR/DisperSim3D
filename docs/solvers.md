---
layout: default
title: Solvers
nav_order: 4
---

# Solvers
{: .no_toc }

1. TOC
{:toc}

DisperSim 3D ships three solver families: analytical, OpenFOAM CFD and
FluidX3D GPU LBM. Each has trade-offs in fidelity, runtime and the kinds of
release physics it can handle.

## Decision matrix

| Need | Best fit |
|---|---|
| Screening / preliminary sizing | **GaussianPuff** or **GaussianPlume**  -  seconds, no GPU/CFD |
| Fast iteration on a steady design | **FluidX3DWind + FluidX3DDispersionSteady**  -  converges in tens of seconds on GPU |
| Transient release with simple geometry | **FluidX3DDispersion** (LES on GPU) |
| Fire plume / buoyant hot release | **FluidX3DFire** (Boussinesq tracer) or **rhoReactingBuoyantFoam** |
| Heavy / cryogenic gas (SF₆, LNG) | **rhoReactingBuoyantFoam** with the cryogenic preset |
| Validated production CFD | **rhoReactingBuoyantFoam** (Fiates &amp; Vianna 2016) |

## Solver list

The full `CfdSolverType` enum:

| Enum value | Family | Implementation file |
|---|---|---|
| `GaussianPlume` | Analytical, steady | [`GaussianPlumeEngine.cs`](https://github.com/DanWBR/DisperSim3D/blob/main/DisperSim3D/Core/GaussianPlumeEngine.cs) |
| `GaussianPuff` | Analytical, transient | [`GaussianPuffEngine.cs`](https://github.com/DanWBR/DisperSim3D/blob/main/DisperSim3D/Core/GaussianPuffEngine.cs) |
| **`RhoReactingBuoyantFoam`** | **Universal CFD**  -  compressible + buoyant + multi-species | `OpenFoamCaseGenerator.cs` |
| `FluidX3DWind` | GPU LBM wind field | [`FluidX3DWindFieldRunner.cs`](https://github.com/DanWBR/DisperSim3D/blob/main/DisperSim3D/Core/FluidX3DWindFieldRunner.cs) |
| `FluidX3DDispersion` | GPU LBM wind + CPU tracer | [`FluidX3DRunner.cs`](https://github.com/DanWBR/DisperSim3D/blob/main/DisperSim3D/Core/FluidX3DRunner.cs) |
| `FluidX3DDispersionSteady` | GPU LBM + CPU tracer to convergence | [`FluidX3DSteadyDispersionRunner.cs`](https://github.com/DanWBR/DisperSim3D/blob/main/DisperSim3D/Core/FluidX3DSteadyDispersionRunner.cs) |
| `FluidX3DFire` | GPU LBM wind + Boussinesq buoyant tracer | [`FluidX3DFireRunner.cs`](https://github.com/DanWBR/DisperSim3D/blob/main/DisperSim3D/Core/FluidX3DFireRunner.cs) |

The six-character `SolverCode` for headless / CLI / file-format consumers:

| Code | Solver |
|---|---|
| `GAUSSP` / `GAUSSPL` | Gaussian Puff / Plume |
| `RHRBFM` | rhoReactingBuoyantFoam |
| `FX3DWN` | FluidX3D wind field |
| `FX3DDP` | FluidX3D dispersion (transient) |
| `FX3DDS` | FluidX3D dispersion (steady-state) |
| `FX3DFR` | FluidX3D fire (Boussinesq) |

## Analytical solvers

### Gaussian Puff (transient)

Each `PuffIntervalS` releases a puff of mass `Q · ΔT`. Centre is advected by
the meteo wind vector or interpolated from a bound `WindField3D`. σ grows
via Pasquill-Gifford open-country coefficients. Ground reflection and the
mixing-height lid are included. Briggs plume rise is applied for buoyant or
momentum-jet releases. HP leaks supply a time-varying mass-flow profile via
`HighPressureLeakModel.ComputeBlowdownProfile`.

### Gaussian Plume (steady-state, bent)

Centerline starts in `ReleaseDirection` and transitions exponentially to the
wind direction over a momentum-based bend length
`BendLength = max(R²·D·π/4, 10·D)`, clamped to 80 % of the domain. σ_y and
σ_z are evaluated along the curved centerline. When a `WindField3D` is bound,
wind direction and speed at the source position are interpolated from the
field instead of the uniform meteo.

### High-pressure leak  -  Birch &amp; Schefer expanded source

For an underexpanded sonic jet, modelling the real orifice in CFD requires
sub-millimetre cells and sub-microsecond timesteps. Birch &amp; Schefer (1984)
replaces the real orifice with a fictitious larger one at atmospheric
pressure and subsonic velocity:

$$
\dot m \;=\; C_d \cdot A_{\mathrm{orifice}} \cdot P_0 \cdot
  \sqrt{\frac{\gamma M}{RT}\!
    \left(\frac{2}{\gamma+1}\right)^{\!\frac{\gamma+1}{\gamma-1}}}
\quad\text{(choked)}
$$

$$
\rho_{\mathrm{amb}}
  \;=\; \frac{P_{\mathrm{atm}}\,M}{R\,T_{\mathrm{amb}}},
\qquad
A_{\mathrm{pseudo}}
  \;=\; \frac{\dot m}{\rho_{\mathrm{amb}}\,V_{\mathrm{target}}},
\qquad
d_{\mathrm{pseudo}} \;=\; \sqrt{\frac{4\,A_{\mathrm{pseudo}}}{\pi}}
$$

with $V_{\mathrm{target}} \approx 100\ \mathrm{m/s}$.

`HighPressureLeakModel.ComputeExpandedSource(p, 100, 293.15)` returns
`(d_pseudo, V_target, T_amb)`. `ReleaseSource3D.ExpandedDiameterForCfdM` and
`ExpandedVelocityForCfdMS` are the CFD-facing accessors  -  they return the
physical orifice values for non-choked flow and fall back to Birch only when
the leak is sonic.

## OpenFOAM solvers

See the [OpenFOAM section](#openfoam-pipeline) below for case generation,
boundary conditions and the Atmospheric Boundary Layer treatment.

The recommended universal CFD solver is **`rhoReactingBuoyantFoam`**  -  it
covers compressible flow, buoyancy, multi-species transport, sonic and
subsonic releases, with combustion switched off. Recipe follows Fiates &amp;
Vianna 2016.

### Atmospheric Boundary Layer treatment

When `CfdConfiguration.UseAtmosphericBL = true` (default for every CFD
solver), the case writer emits a validated atmospheric configuration based
on three published references:

- **Mack &amp; Spruijt 2013**  -  heavy gas, recommends `C_ε3 = -0.33` constant
  in the ε-equation buoyancy term and `Sc_t = 0.7`.
- **Tran Le Vu 2019**  -  LNG vapor, validates HHTSL k-ε constants with
  `σ_ε = 1.167`, `Sc_t = 0.3` for dense gas, `Sc_t = 0.15` for cryogenic,
  fixed-temperature ground BC for cryogenic releases.
- **Schalau et al. 2021**  -  wind around obstacles, stock OpenFOAM
  `atmBoundaryLayerInlet*` BCs, ground roughness via
  `nutkAtmRoughWallFunction(z₀)`.

Boundary-condition table:

| Field | Inlet (atmospheric on) | Ground (atmospheric on) |
|---|---|---|
| U | `atmBoundaryLayerInletVelocity` | `noSlip` (compressible) / `fixedValue (0 0 0)` (incompressible) |
| k | `atmBoundaryLayerInletK` | `kqRWallFunction` |
| ε | `atmBoundaryLayerInletEpsilon` | `epsilonWallFunction` |
| ν_t | `calculated` | `nutkAtmRoughWallFunction(z₀)` |
| T | `inletOutlet` / `fixedValue` | configurable: `Adiabatic`, `FixedTemperature` (LNG), `FixedFlux` |

Per-solver presets, configurable in code via `CfdConfigurationPresets`:

| Solver | UseAtmBL | Sc_t | C_ε3 | σ_ε | Ground T BC |
|---|---|---|---|---|---|
| Gaussian Plume / Puff | n/a | n/a | n/a | n/a | n/a |
| **RhoReactingBuoyantFoam** | true | 0.7 | -0.33 | 1.167 | Adiabatic |

When the source gas has `IsCryogenic = true`, the preset bumps Sc_t to 0.15
and switches the ground BC to `FixedTemperature` at ambient air temperature
(Vu 2019 §5.4).

## OpenFOAM pipeline

Cases are written to
`%TEMP%/DisperSim_OpenFOAM/<solver>_case_<scenarioId>` (configurable via
`CfdConfiguration.WorkingDirectory`). The runner pipeline for transient runs:

1. `blockMesh`  -  generate the base hex grid
2. Optional `topoSet` + `refineMesh` for source/obstacle refinement
3. `topoSet`  -  create source/obstacle cellSets
4. `setFields`  -  initialise U, species, T
5. `decomposePar` (when parallel)
6. `mpiexec -np N <solver> -parallel` (or single-process)
7. `reconstructPar`
8. `OpenFoamResultReader.ReadResults` → `OpenFoamResult`

Adjustable Courant number (`CfdConfiguration.MaxCourantNumber`, default 10.0
per Fiates &amp; Vianna 2016) lets the solver auto-size `deltaT`.

The OpenFOAM environment dialog supports Native Windows, WSL2, Docker and
BlueCFD backends. Native Windows is recommended and validated end-to-end with
`rhoReactingBuoyantFoam`.

## FluidX3D solvers

See the [FluidX3D page](solvers-fluidx3d) for details on the GPU LBM
implementation, the C# / C++ bridge and what each runner produces.
