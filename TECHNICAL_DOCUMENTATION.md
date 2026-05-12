# DisperSim 3D — Technical Documentation

**Version**: 1.0  
**License**: GPL v3  
**Target framework**: .NET 10 (Windows, WPF + WinForms)  
**Author**: Daniel Wagner Oliveira de Medeiros

---

## 1. Overview

DisperSim 3D is an open-source desktop application for simulating accidental gas releases and their atmospheric dispersion in industrial environments. It combines fast analytical models (Gaussian puff/plume) with full CFD (OpenFOAM) so that engineers can iterate quickly on screening calculations and then refine critical scenarios on a high-fidelity mesh — all from the same project file.

**Target users**: process safety engineers, HSE consultants, plant designers performing dispersion modelling, area classification, and gas detector siting studies.

**Key capabilities**:

- 3D scene editor with imported CAD geometry (obstacles, equipment, buildings)
- Project model centred on reusable Gas Library, Sources, Wind Fields, Simulations
- Four solver families:
  - **Gaussian Puff** (transient analytical)
  - **Gaussian Plume** (steady-state analytical with bent-plume trajectory)
  - **CFD** (OpenFOAM v2512+): scalarTransportFoam, simpleFoam, pimpleFoam, buoyantPimpleFoam, reactingFoam, rhoSimpleFoam, **rhoReactingBuoyantFoam** (recommended universal, after Fiates & Vianna 2016)
  - **GPU LBM** (FluidX3D): `FluidX3DWind`, `FluidX3DDispersion`, `FluidX3DDispersionSteady`, `FluidX3DFire` — invoked in-process via `FluidX3D.dll` and a C-ABI bridge, no temp-file round-trip
- High-pressure leak modelling with **Birch & Schefer expanded-diameter** for sonic releases
- Pre-computed wind fields shared across multiple dispersion runs
- Snapshot-based simulations: history is immutable; editing a source after a run does not change past results
- Animated wind-field visualization with user-tunable arrow appearance
- **Detector placement** in two flavours:
  - Set Covering Problem (Vianna 2019) — exact and greedy, minimum-cardinality cover
  - **Dispersion Studies + Detector Allocation** — curated multi-simulation collection and greedy maximum-coverage placement against a detector budget
- **GPU device selection** with on-demand VRAM/RAM/disk estimation (`Settings → GPU & Memory…`)
- Flammable cloud volume integration in the LFL/UFL band

---

## 2. Architecture

### 2.1 Solution layout

| Project | Type | Purpose |
|---|---|---|
| `DisperSim3D` | Library (net10.0-windows) | Models, solvers, viewport, dialogs |
| `DisperSim3D.CLI` | Console exe | Headless batch runner — reads XML, runs solver, prints summary |
| `TestApp` | WinForms exe | Standalone UI host that embeds the editor panel |

### 2.2 UI stack

- **WinForms** for the host shell, menus, dialogs and dock layout (DockPanelSuite + VS2015 theme).
- **WPF** via `ElementHost` for components that benefit from richer controls:
  - `HelixToolkit.Wpf` 3D viewport (`Scene3DEditorControl`)
  - WPF `TreeView`-based project tree (`ProjectTreeWpfPanel`) with per-node checkboxes, status badges, context menus
- **HandyControl** (MIT) for the modern WPF property grid (`PropertyGridWpfPanel`)

### 2.3 Logical architecture

```
                      ┌────────────────────┐
                      │       TestApp      │  ← entry point
                      └──────────┬─────────┘
                                 │
                      ┌──────────▼─────────┐
                      │ Scene3DEditorPanel │  ← menu bar, dock layout, action handlers
                      └──┬─────────────┬───┘
            ┌────────────┘             └─────────────┐
            ▼                                         ▼
  ┌────────────────────┐                ┌────────────────────┐
  │ ProjectTreeWpfPanel│                │ Scene3DEditorControl│
  │  (left dock)       │                │  (3D viewport, WPF)│
  └─────────┬──────────┘                └─────────┬──────────┘
            │                                     │
            ▼                                     ▼
       ┌──────────┐                          ┌──────────┐
       │  Scene3D │ ◄─── snapshot/migrate ───┤  XML I/O │
       │ (Project)│                          │ Save/Load│
       └─────┬────┘                          └──────────┘
             │
   ┌─────────┴────────────────────────────────┐
   ▼                                          ▼
Solvers (Core/)                          External pipeline
- GaussianPuffEngine                     - OpenFoamCaseGenerator
- GaussianPlumeEngine                    - OpenFoamRunner
- WindFieldRunner                        - OpenFoamResultReader
- SimulationRunner                       - SimulationManager (queue)
- DetectorOptimizer
```

### 2.4 Key design decisions

1. **Snapshot semantics**: when a `Simulation` runs, it deep-clones the source, gas, meteo and CFD config into `Snapshot*` fields. Result history is immutable.
2. **Dock framework via DockPanelSuite**: panels are full WeifenLuo `DockContent`s; viewport stays as `DockState.Document` and never closes.
3. **Tree action separation**: `ProjectTreeWpfPanel` raises high-level events (`ActionRequested`, `VisibilityChanged`, `SelectionChanged`); the panel doesn't know about solvers or dialogs.
4. **CFD pipeline is fully external**: case directories are written to disk, OpenFOAM is invoked via `OpenFoamEnvironment` (WSL2/Docker/Native/BlueCFD), results read from disk. The C# runtime never links to OpenFOAM.

---

## 3. Physical models

### 3.1 Gaussian Puff (transient)

**File**: [`Core/GaussianPuffEngine.cs`](DisperSim3D/Core/GaussianPuffEngine.cs)

- Each emission interval `PuffIntervalS` releases a puff of mass `Q · ΔT`
- Puff centre advected by `WindVector` (or sampled from `WindField3D` if present)
- Sigma growth via Pasquill-Gifford open-country coefficients
- Wind speed power-law profile `u(z) = u_ref · (z/z_ref)^p` based on stability and terrain
- Optional Briggs plume rise for buoyant or momentum-jet releases (`BriggsPlumerise.ComputeDeltaH`)
- High-pressure leaks supply a time-varying mass-flow profile via `HighPressureLeakModel.ComputeBlowdownProfile`

### 3.2 Gaussian Plume (steady-state, bent)

**File**: [`Core/GaussianPlumeEngine.cs`](DisperSim3D/Core/GaussianPlumeEngine.cs)

- Centerline starts in `ReleaseDirection`, transitions exponentially to the wind direction over a momentum-based bend length
- For each source, `BendLength = max(R²·D·π/4, 10·D)` clamped to 80 % of domain
- σ_y, σ_z evaluated along the curved centerline using Pasquill-Gifford
- When a `WindField3D` is bound, wind direction and speed at the source position are interpolated from the field instead of using the uniform meteo

### 3.3 CFD — solver gallery

| Enum | OpenFOAM / native solver | Use case |
|---|---|---|
| `ScalarTransportFoam` / `ScalarTransportFoamSteady` | `scalarTransportFoam` | Passive scalar in a frozen velocity field |
| `ScalarSimpleFoam` | `simpleFoam` + scalar | Steady-state RANS |
| `RhoSimpleFoam` | `rhoSimpleFoam` + scalar | Compressible steady |
| `PimpleFoam` | `pimpleFoam` + fvOptions scalar | Transient incompressible |
| `BuoyantPimpleFoam` | `buoyantPimpleFoam` | Transient with buoyancy (heavy/light gas) |
| `ReactingFoam` | `reactingFoam` | Multi-species, combustion off |
| **`RhoReactingBuoyantFoam`** | `rhoReactingBuoyantFoam` | **Recommended CFD**: compressible + buoyant + multi-species, subsonic & sonic, combustion off |
| `FluidX3DWind` | FluidX3D LBM (D3Q19 FP32 + SUBGRID) | GPU steady wind field, seconds-to-minutes |
| `FluidX3DDispersion` | FluidX3D wind + CPU `DispersionTracerEngine` | GPU wind + semi-Lagrangian transient tracer |
| `FluidX3DDispersionSteady` | FluidX3D wind + CPU tracer, convergence-driven | Single converged frame, L2-delta tolerance |
| `FluidX3DFire` | FluidX3D wind + CPU `FireTracerEngine` | Dual tracer (smoke + T) with Boussinesq buoyancy |

The `RhoReactingBuoyantFoam` recipe follows Fiates & Vianna (2016): `chemistryThermo rho`, `chemistry off`, `combustionModel none`, `heRhoThermo` with `reactingMixture`. Reactions are declared but the species block is stripped of all kinetic data.

### 3.3.1 Atmospheric Boundary Layer treatment

**Files**: [`Core/CfdConfigurationPresets.cs`](DisperSim3D/Core/CfdConfigurationPresets.cs), [`Core/OpenFoamCaseGenerator.cs`](DisperSim3D/Core/OpenFoamCaseGenerator.cs), [`Models/CfdConfiguration.cs`](DisperSim3D/Models/CfdConfiguration.cs).

When `CfdConfiguration.UseAtmosphericBL = true` (default for every CFD solver via `CfdConfigurationPresets.ApplyForSolver`), the case writer emits a validated atmospheric configuration based on three published references:

- **Mack & Spruijt 2013** — heavy gas in `reactingFoam`, recommends `C_ε3 = -0.33` constant in the ε-equation buoyancy term (instead of OpenFOAM's default `tanh|u·g/...|`) and `Sc_t = 0.7`.
- **Tran Le Vu 2019** — LNG vapor in custom `gasDispersionBuoyantFoam`, validates HHTSL k-ε constants with `σ_ε = 1.167` (vs default 1.3), `Sc_t = 0.3` for dense gas, `Sc_t = 0.15` for cryogenic LNG, fixed-temperature ground BC for cryogenic releases.
- **Schalau et al. 2021** — wind around obstacles in `rhoReactingBuoyantFoam`, atmospheric inlet via stock OpenFOAM `atmBoundaryLayerInlet*` BCs, ground roughness via `nutkAtmRoughWallFunction(z₀)`.

DisperSim ships the **stock OpenFOAM v2512 BCs** subset of these recipes (no custom C++):

| Field | Inlet patch (atmospheric on) | Ground patch (atmospheric on) |
|---|---|---|
| U | `atmBoundaryLayerInletVelocity` (Uref / Zref / z₀ from MeteorologicalConditions) | `noSlip` (compressible) / `fixedValue (0 0 0)` (incompressible) |
| k | `atmBoundaryLayerInletK` | `kqRWallFunction` |
| ε | `atmBoundaryLayerInletEpsilon` | `epsilonWallFunction` |
| ν_t | `calculated` | `nutkAtmRoughWallFunction(z₀)` |
| T | `inletOutlet` / `fixedValue` | configurable: `Adiabatic` (default), `FixedTemperature` (LNG), `FixedFlux` |

`constant/turbulenceProperties` gets a `kEpsilonCoeffs { sigmaEps 1.167; ... }` block when atmospheric mode is on. When `BuoyancyEpsCoefficient` is non-null and the solver is buoyant, the RAS model is switched to `buoyantKEpsilon` and the coeffs block carries `Ceps3 -0.33`. `transportProperties` gets `Sct` and `Prt` keywords.

Per-solver presets (configurable in code via `CfdConfigurationPresets`):

| Solver | UseAtmBL | Sc_t | C_ε3 | σ_ε | Ground T BC |
|---|---|---|---|---|---|
| Gaussian Plume / Puff | n/a | n/a | n/a | n/a | n/a |
| ScalarTransportFoam(Steady) | true | 0.7 | – | 1.3 | Adiabatic |
| ScalarSimpleFoam / PimpleFoam / RhoSimpleFoam | true | – | – | 1.167 | Adiabatic |
| BuoyantPimpleFoam / ReactingFoam / **RhoReactingBuoyantFoam** | true | 0.7 | -0.33 | 1.167 | Adiabatic |

When the source's gas (`GasLibraryItem.IsCryogenic = true`) flags cryogenic LNG behaviour, the preset further bumps Sc_t to 0.15 and switches the ground BC to `FixedTemperature` at the ambient air temperature (Vu 2019 §5.4).

The case writer also emits a `LOG_atmospheric.txt` advisory inside the case directory when the ground-adjacent cell size is smaller than (or close to) `z₀` — a regime where `nutkAtmRoughWallFunction` is ill-conditioned. This is non-fatal; recommended minimum cell ≈ `2·z₀`.

### 3.4 High-pressure leak — Birch & Schefer expanded source

**File**: [`Core/HighPressureLeakModel.cs`](DisperSim3D/Core/HighPressureLeakModel.cs)

For an underexpanded sonic jet, modelling the real orifice in CFD requires sub-millimetre cells and sub-microsecond timesteps. The Birch & Schefer (1984) approach replaces the real orifice with a fictitious larger one at atmospheric pressure and subsonic velocity:

```
mdot     = Cd · A_orifice · P0 · √(γM/RT · (2/(γ+1))^((γ+1)/(γ−1)))   (choked)
ρ_amb    = P_atm · M / (R · T_amb)
A_pseudo = mdot / (ρ_amb · V_target)                  V_target ≈ 100 m/s
d_pseudo = √(4 · A_pseudo / π)
```

`HighPressureLeakModel.ComputeExpandedSource(p, 100, 293.15)` returns `(d_pseudo, V_target, T_amb)`.

`ReleaseSource3D.ExpandedDiameterForCfdM` and `ExpandedVelocityForCfdMS` are the CFD-facing accessors. They return the physical orifice diameter and `ComputedExitVelocity` for non-choked flow, falling back to the Birch values only when the leak is sonic. Used inside `OpenFoamCaseGenerator.WriteSetFieldsDict`, `WriteJetSetFieldsDict`, `WriteReactingSetFieldsDict`, and the `dt` sizing inside `Generate`.

### 3.5 Flammable cloud volume

**File**: [`Core/FlammableCloudCalculator.cs`](DisperSim3D/Core/FlammableCloudCalculator.cs)

```
V_flam = Σ (cell_volume) for cells where LFL ≤ c ≤ UFL
```

Returns total volume, lean half (LFL → ½(LFL+UFL)), rich half, peak concentration and cell count. This is the standard input to vapour-cloud-explosion models.

### 3.6 Detector placement optimisation

**File**: [`Core/DetectorOptimizer.cs`](DisperSim3D/Core/DetectorOptimizer.cs)

Implementation of Vianna (2019), *The set covering problem applied to optimisation of gas detectors in chemical process plants*. Pipeline:

1. Load concentration field from each completed `Simulation` (cached on `ResultTag`, falls back to disk via `OpenFoamResultReader.ReadResults`)
2. Mesh size `L = ∛(min flammable cloud volume)` (Eq. 23 of the paper) unless the user overrides
3. Discretise the user-defined protected region into cubic cells of side `L`
4. Mark every cell that lies inside any flammable cloud — these are the SCP rows
5. Build dominance adjacency: each candidate detector cell `j` covers a configurable neighbourhood (`Cardinal` 6-face or `Moore` 26-surrounding) within radius
6. Solve via [`SetCoveringSolver.SolveExact`](DisperSim3D/Core/SetCoveringSolver.cs) (Balas-style implicit enumeration, greedy upper bound, branch-and-bound with most-constrained-row branching) or fast greedy
7. Convert column indices back to world coordinates, return the optimal detector positions

### 3.7 FluidX3D GPU LBM solvers

**Files**:
[`Core/FluidX3DBridge.cs`](DisperSim3D/Core/FluidX3DBridge.cs),
[`Core/FluidX3DUnits.cs`](DisperSim3D/Core/FluidX3DUnits.cs),
[`Core/FluidX3DObstacleVoxelizer.cs`](DisperSim3D/Core/FluidX3DObstacleVoxelizer.cs),
[`Core/FluidX3DWindFieldRunner.cs`](DisperSim3D/Core/FluidX3DWindFieldRunner.cs),
[`Core/FluidX3DRunner.cs`](DisperSim3D/Core/FluidX3DRunner.cs),
[`Core/FluidX3DSteadyDispersionRunner.cs`](DisperSim3D/Core/FluidX3DSteadyDispersionRunner.cs),
[`Core/FluidX3DFireRunner.cs`](DisperSim3D/Core/FluidX3DFireRunner.cs),
[`Core/DispersionTracerEngine.cs`](DisperSim3D/Core/DispersionTracerEngine.cs),
[`Core/FireTracerEngine.cs`](DisperSim3D/Core/FireTracerEngine.cs),
[`FluidX3D/src/disp_bridge.h`](FluidX3D/src/disp_bridge.h),
[`FluidX3D/src/disp_bridge.cpp`](FluidX3D/src/disp_bridge.cpp).

FluidX3D is a GPU lattice Boltzmann solver from [ProjectPhysX](https://github.com/ProjectPhysX/FluidX3D) compiled as a sibling C++ project to `FluidX3D.dll` and invoked via P/Invoke through a thin C-ABI bridge. Runs on any OpenCL 1.2+ device. A 64³ wind field that takes minutes in `simpleFoam` finishes in 5–10 s on a mid-range GPU.

**Feature flags compiled into the DLL** (`FluidX3D/src/defines.hpp`):

| Flag | Why |
|---|---|
| `VOLUME_FORCE` | gravity + Boussinesq force injection |
| `EQUILIBRIUM_BOUNDARIES` | inlet/outlet cells as `TYPE_E` with fixed U, ρ |
| `SUBGRID` | Smagorinsky-Lilly LES — required at atmospheric Re ≥ 10⁵ |
| `D3Q19`, `FP32` | velocity set / precision (default; FP16 optional later) |
| **off:** `INTERACTIVE_GRAPHICS` | headless DLL, no SDL/window |
| **off:** `TEMPERATURE` | replaced by the CPU tracer engines to keep velocity clean |

**C-ABI surface** (`FluidX3D/src/disp_bridge.h`):

```cpp
extern "C" {
  uint64_t fx3d_create(const Fx3dConfig* cfg);
  uint64_t fx3d_create_on_device(const Fx3dConfig* cfg, int device_id);
  void     fx3d_list_devices(char* buf, uint32_t max_bytes);  // returns JSON
  void     fx3d_add_box_obstacle(uint64_t h, float xmin, ymin, zmin, xmax, ymax, zmax);
  void     fx3d_set_inlet_wind(uint64_t h, float ux, float uy, float uz);
  void     fx3d_add_release_source(uint64_t h, float x, float y, float z,
                                    float radius_m, float concentration);
  int      fx3d_run(uint64_t h, uint32_t steps, void(*cb)(uint32_t, uint32_t));
  void     fx3d_read_velocity(uint64_t h, float* ux, float* uy, float* uz);
  void     fx3d_read_concentration(uint64_t h, float* c);
  void     fx3d_destroy(uint64_t h);
}
```

Handles are opaque 64-bit integers backed by a `std::unordered_map<uint64_t, std::unique_ptr<LBM>>` on the native side so many simulations can run sequentially without state leak. `FluidX3DBridge.cs` mirrors these with `[DllImport]` declarations.

**Unit conversion** (`FluidX3DUnits`):

```
Δx           = (2 · domainHalfM) / Nx
Δt           = Δx · C_si / C_lattice              C_lattice = 1/√3
nu_lattice   = nu_physical    · Δt / Δx²
g_lattice    = g_physical     · Δt² / Δx
alpha_latt.  = alpha_physical · Δt / Δx²
```

Inverse maps are used when reading velocities and concentrations back to SI.

#### 3.7.1 `FluidX3DWind` — wind field

Pipeline:

1. Convert SI inputs (domain size, wind speed, kinematic viscosity) to lattice units.
2. `fx3d_create_on_device(cfg, device_id)` allocates an LBM grid on the user-selected GPU.
3. `FluidX3DObstacleVoxelizer.Voxelize` rasterises every `Decoration3D` AABB to `TYPE_S` solid cells.
4. `fx3d_set_inlet_wind` installs an equilibrium-boundary inlet on the -X face with the requested wind vector.
5. `fx3d_run(steady_steps)` — default 3000 steps; LBM stabilises in ~3 domain crossings.
6. `fx3d_read_velocity` copies `u.x`, `u.y`, `u.z` back to the host as `float[Nx·Ny·Nz]` arrays.
7. Convert lattice → SI, populate `wf.WindField` (`WindField3D`), mark the scenario **Ready**.

#### 3.7.2 `FluidX3DDispersion` — transient dispersion

Concentration is **not** carried by FluidX3D itself (the upstream `TEMPERATURE` extension perturbs velocities when used for passive scalars). Instead the runner builds a `DispersionTracerEngine` — a CPU semi-Lagrangian advection-diffusion solver that reads the frozen LBM velocity at every cell:

```
c_new(x) = c_old(x − u(x)·Δt)                                    semi-Lagrangian advection
        + Δt · diff · ∇²c
        − Δt · decay · c                                          first-order decay (gas half-life)
```

Source treatment:

- Position / radius from `Simulation.SnapshotSource.Position`.
- Magnitude from `ReleaseRateKgPerS` converted via molar mass and voxel volume.
- HP-leak Birch & Schefer expanded source applied identically to the OpenFOAM path.

#### 3.7.3 `FluidX3DDispersionSteady` — convergence-driven steady run

Identical mechanics to the transient runner, except the loop iterates in chunks (`ConvergenceChecks` chunks over `SnapshotDurationS`) and compares each snapshot to the previous via L2 relative delta:

```
‖c_n − c_{n−1}‖₂ / max(‖c_n‖₂, ε)  <  ConvergenceTolerance         default 1e-3
```

When the tolerance is met, the runner writes a **single** converged binary frame and sets `OpenFoamResult.IsSteadyState = true`. The viewport reads that flag and hides the playback bar entirely — there is only one frame to display.

#### 3.7.4 `FluidX3DFire` — buoyant fire plume

A dual-tracer extension of `DispersionTracerEngine` — `FireTracerEngine` advects two scalars simultaneously: a smoke mass fraction `Y_smoke` and a temperature `T` (Kelvin). The Boussinesq buoyancy term is injected into the vertical velocity sampled from the LBM field:

```
u_z_eff(x) = u_z_lbm(x) + β · g · (T(x) − T_amb) · Δt              capped at 0.5 · dx / Δt
```

Default exit temperature is **1500 K** (`ExitTemperatureK` override on the source). Output is one `<time>.bin` for smoke plus one `<time>_T.bin` for temperature per write interval.

#### 3.7.5 GPU device selection

If `AppSettings.PreferredComputeDeviceId ≥ 0`, all four runners call `fx3d_create_on_device(cfg, device_id)` to pin the LBM context to that OpenCL device. Device IDs come from `fx3d_list_devices(buf, max_bytes)`, which returns a JSON manifest of every detected OpenCL device with name, type, memory, max work-group size and compute units. A two-call protocol probes the required buffer size first, then fetches the JSON; failures are captured in `FluidX3DBridge.LastListDevicesError` and surfaced in the Compute GPU tab of `GpuPerformanceSettingsDialog`.

### 3.8 Dispersion Studies and Detector Allocation

**Files**:
[`Models/DispersionStudy.cs`](DisperSim3D/Models/DispersionStudy.cs),
[`Models/DetectorAllocation.cs`](DisperSim3D/Models/DetectorAllocation.cs),
[`Core/DispersionStudyEngine.cs`](DisperSim3D/Core/DispersionStudyEngine.cs),
[`Core/DetectorAllocator.cs`](DisperSim3D/Core/DetectorAllocator.cs),
[`Core/StudyAllocationRenderer.cs`](DisperSim3D/Core/StudyAllocationRenderer.cs),
[`Dialogs/DispersionStudyDialog.cs`](DisperSim3D/Dialogs/DispersionStudyDialog.cs),
[`Dialogs/DetectorAllocationDialog.cs`](DisperSim3D/Dialogs/DetectorAllocationDialog.cs).

Companion workflow to §3.6 — instead of solving an exact set-cover against one simulation, the user curates a multi-simulation **study** and lets a greedy **maximum-coverage** allocator place `K` detectors to cover as many clouds as possible.

#### 3.8.1 `DispersionStudy`

Project-level object with:

| Property | Purpose |
|---|---|
| `SimulationIds[]` | references to existing `Simulation` entries |
| `DetectionQuantity` | `ViewFieldProperty` enum — `PercentLfl`, `Ppm`, `MoleFraction`, `MassFraction`, `Temperature`, `ThermalRadiation` |
| `DetectionThreshold` | threshold in the units defined by `DetectionQuantity` |

`DispersionStudyEngine.LoadClouds(study, scene)` reads the **last** concentration timestep of each simulation (or the steady-state frame for `FluidX3DDispersionSteady`) and builds one `CloudSnapshot` per simulation — a flagged-cell list with an axis-aligned bounding box used to short-cut the radius test (`CellWithinRadius` skips clouds whose bbox does not intersect the detector sphere).

#### 3.8.2 `DetectorAllocation`

Configuration:

| Property | Purpose |
|---|---|
| `DispersionStudyId` | study to allocate against |
| `Objective` | `CoverAll` (stop only when every cloud has ≥ 1 detector) or `CoverPercentage` (stop at a target %) |
| `MaxDetectors` | hard cap on detector count |
| `DetectionRadiusM` | sphere radius around each candidate that counts as "covered" |
| `MinZ`, `MaxZ` | vertical band detectors may occupy (typically 1.5 – 3 m) |
| `CandidateNx, Ny, Nz` | candidate grid resolution |
| `UseExistingDetectors` | when true, project `GasDetector3D`s pin as already-placed and the allocator fills the remaining gap |

`DetectorAllocator.Allocate`:

1. Build a Cartesian candidate grid `Nx · Ny · Nz`, clipped to `[MinZ, MaxZ]`.
2. Cull candidates inside any `Decoration3D` AABB.
3. Pre-compute `cover[c_i] = { cloud j : any flagged cell in cloud j lies within DetectionRadiusM of c_i }`.
4. Greedy loop: while not done and `|placed| < MaxDetectors`, pick the candidate covering the most still-uncovered clouds; remove the newly-covered clouds from consideration; repeat.
5. Pinned existing detectors (when `UseExistingDetectors = true`) are accounted for before the greedy loop runs.

Results populate `AllocatedPositions[]`, `AchievedCoveragePercent` and `PerCloudCovered[]` (boolean per simulation).

`StudyAllocationRenderer.BuildStudyVisual` produces marching-cubes isosurfaces per cloud (palette-cycled colours) and `BuildAllocationVisual` overlays orange detector spheres with translucent radius shells so coverage gaps are visible at a glance.

---

## 4. Workflow

### 4.1 Typical user session

1. **File → New Project**
2. Right-click **Gases → Add Pure Gas...** → enter Methane / Custom
3. Right-click **Sources → Add Source...** → click on the map → fill the source dialog (gas, release rate, direction, optional HP leak). Once placed, positions can only be changed via the property panel — **drag-to-reposition is intentionally disabled** because moving an object after a simulation has run would silently invalidate the snapshot's geometry.
4. Right-click **Wind Fields → Add Wind Field...** in the Manager → set wind speed/direction/stability → **Run**. Pick **FluidX3DWind** for a sub-30-second GPU run, or **simpleFoam** for the OpenFOAM path; status changes to `Ready` when done.
5. Right-click **Simulations → New Simulation...** → pick source × wind field × solver → OK creates a `Configured` simulation. The solver picker includes the four `FluidX3D*` runners alongside the OpenFOAM gallery.
6. Right-click the simulation → **Run** → `Configured → Queued → Running → Completed`. Snapshot of source/gas/meteo/cfd-config is taken at this moment.
7. Check the simulation's checkbox in the tree → 3D playback in the viewport, controls in the bottom playback bar. `FluidX3DDispersionSteady` results hide the bar (single converged frame).
8. **Dispersion → Optimize Detector Placement...** → pick simulations + protected region → outputs minimum detector set (Vianna 2019 SCP), adds them to the project as `OptDet N`.
9. **Dispersion Studies → Add Study...** to bundle multiple simulations under one detection criterion, then **Detector Allocation → Add Allocation...** for greedy maximum-coverage placement against a detector budget (see §3.8).

### 4.2 Project tree sections

```
[Project Name]
├── General Settings           (defaults: wind, domain, grid)
├── Gases (n)                  (project Gas Library — pure + mixtures)
├── Geometry (n)               (3D models / decorations / obstacles)
├── Sources (n)                (release sources, top-level)
├── Wind Fields (n)            (pre-computed simpleFoam / FluidX3DWind runs)
├── Simulations (n)            (Source × WindField × Solver runs)
├── Monitors (n)               (passive concentration probes)
├── Detectors (n)              (alarm-threshold detectors)
├── Dispersion Studies (n)     (curated collections + detection criterion)
└── Detector Allocations (n)   (greedy max-coverage placement against a Study)
```

Each leaf node:
- **Checkbox** controls 3D visibility (where applicable)
- **Status badge** colour-coded (Ready/Completed = green, Failed = red, Running = orange)
- **Right-click** opens context menu (Add/Edit/Delete/Run/Open Case Folder/...)
- **Double-click** opens the editor dialog (or focuses the property grid)
- **Left-click** sets the WPF property grid to that object

---

## 5. XML project format

Root element: `<Scene3D Version="1">` (legacy) or `<Project Version="2">` (new). Both are accepted — `LegacyProjectMigrator.MigrateInPlace` extracts inline gases/sources from old files into the new top-level sections.

### 5.1 Top-level structure

```xml
<Scene3D Version="1" Name="..." Description="...">
  <GridSettings Spacing="5" SnapToGrid="True"/>
  <WorkPlanes>...</WorkPlanes>
  <Decorations>
    <Decoration Id="..." Name="..." FilePath="..." PosX="..." .../>
  </Decorations>

  <GeneralSettings Name="..." Description="..." Author="..." CreatedAt="..."
                   DefaultDomainSize="200" DefaultGridRes="40">
    <DefaultMeteo WindSpeed="5" WindDir="270" Stability="D" Temp="293.15" Pressure="101325"/>
  </GeneralSettings>

  <GasLibrary>
    <Gas Id="..." Name="Methane" Kind="Pure"
         MolarMass="0.01604" LFL="0.033" IDLH="0" ERPG1="0" ERPG2="0" ERPG3="0"/>
    <Gas Id="..." Name="Sour Gas" Kind="Mixture">
      <Mixture>
        <Component Name="CH4" MolarMass="0.016" MoleFrac="0.8" LFL="0.033" IDLH="0"/>
        <Component Name="H2S" MolarMass="0.034" MoleFrac="0.2" LFL="0.043" IDLH="0.0696"/>
      </Mixture>
    </Gas>
  </GasLibrary>

  <TopLevelSources>
    <Source Id="..." Name="..." GasRefId="..." PosX="..." PosY="..." PosZ="..."
            ReleaseRate="0.5" PuffInterval="1" HeightOffset="2" Azimuth="0" Elevation="0">
      <HPLeak VesselP="..." VesselT="..." Orifice="..." Volume="..." Gamma="1.4" MolarMass="0.016" Cd="0.65"/>
      <Gas Name="Methane" MolarMass="0.016" LFL="0.033" IDLH="0"/>
    </Source>
  </TopLevelSources>

  <WindFieldScenarios>
    <WindFieldScenario Id="..." Name="..." DomainSize="200" DomainHeight="100" GridRes="40"
                       Status="Ready" CasePath="C:\...\wind_<id>">
      <Meteo WindSpeed="5" WindDir="270" Stability="D" Temp="293.15" Pressure="101325"/>
    </WindFieldScenario>
  </WindFieldScenarios>

  <Simulations>
    <Simulation Id="..." Name="..." CreatedAt="..." CompletedAt="..."
                SourceId="..." WindFieldId="..." SolverType="GaussianPuff"
                Status="Completed" StatusMessage=""
                DomainSize="200" GridRes="40" Duration="300" TimeStep="0.5"
                CasePath="..." MaxC="...">
      <SnapshotSource .../>
      <SnapshotMeteo WindSpeed="..." .../>
    </Simulation>
  </Simulations>

  <DispersionScenarios>...</DispersionScenarios>  <!-- legacy, kept for back-compat reads -->
  <MonitorPoints>...</MonitorPoints>
  <FireScenario>...</FireScenario>
  <GasDetectors>...</GasDetectors>
  <CfdSimulations>...</CfdSimulations>
</Scene3D>
```

Numeric values use `InvariantCulture` (decimal point, no thousands separator).

### 5.2 Migration rules

`LegacyProjectMigrator.MigrateInPlace`:

- Sources inline in any `DispersionScenario` are hoisted to `TopLevelSources` (same instance, not cloned).
- Each unique inline `Gas` becomes a `GasLibraryItem` (Pure) and the source's `GasRefId` is set.
- Each existing `CfdSimulationEntry` becomes a stub `Simulation` with `Status = Completed` (or `Failed`).
- Migration is idempotent — safe to run on every load.

---

## 6. OpenFOAM cases generated

Cases are written to `%TEMP%/DisperSim_OpenFOAM/<solver>_case_<scenarioId>` (configurable via `CfdConfiguration.WorkingDirectory`).

### 6.1 Folder layout (rhoReactingBuoyantFoam example)

```
rhoreact_case_<id>/
├── 0/
│   ├── U                  initial velocity (uniform wind)
│   ├── p                  pressure (101325)
│   ├── p_rgh              hydrostatic-corrected pressure
│   ├── T                  temperature
│   ├── CH4, O2, N2        species mass fractions
│   ├── k, epsilon         turbulence
│   └── alphat, mut/nut    wall function fields
├── constant/
│   ├── thermophysicalProperties     heRhoThermo + reactingMixture + sutherland/janaf
│   ├── thermo.compressibleGas       N2/O2/CH4 thermodynamic data (janaf 7-coeff)
│   ├── chemistryProperties          chemistryThermo rho, chemistry off
│   ├── combustionProperties         combustionModel none
│   ├── reactions                    species list, empty reactions block
│   ├── turbulenceProperties         RAS / RASModel kEpsilon (or kOmegaSST)
│   └── g                            (0 0 -9.81)
└── system/
    ├── controlDict                  application=rhoReactingBuoyantFoam, adjustTimeStep yes, maxCo
    ├── fvSchemes                    Euler ddt, Gauss linear/upwind, wallDist meshWave
    ├── fvSolution                   PIMPLE, GAMG p_rgh, PBiCG U/h/k/eps/Yi
    ├── blockMeshDict                hex domain
    ├── topoSetDict                  source cell-set definitions
    ├── topoSetDict_refine0/_refine1 mesh refinement zones around sources/obstacles
    ├── refineMeshDict
    ├── setFieldsDict                initial gas/velocity at source cells (uses Birch expanded)
    └── decomposeParDict             scotch decomposition (when nProcs > 1)
```

### 6.2 Boundary condition groups

Per the recipe in Fiates & Vianna 2016 §2 (`OpenFoamCaseGenerator.Write*BoundaryFields`):

| Patch | U | p | p_rgh | T | Species | k | ε / ω |
|---|---|---|---|---|---|---|---|
| Wall | (0 0 0) | calculated | fixedFluxPressure | zeroGradient | zeroGradient | kqRWallFunction | epsilon/omegaWallFunction |
| Open | pressureInletOutletVelocity | calculated | totalPressure | inletOutlet | inletOutlet | inletOutlet | inletOutlet |
| Wind (inlet) | fixedValue (uniform wind) | calculated | zeroGradient | fixedValue | fixedValue (air composition) | fixedValue | fixedValue |
| Wind (atmospheric) | atmBoundaryLayerInletVelocity | calculated | zeroGradient | fixedValue | fixedValue | atmBoundaryLayerInletK | atmBoundaryLayerInletEpsilon |
| Ground (atmospheric) | noSlip | – | fixedFluxPressure | per `GroundThermalBC` | zeroGradient | kqRWallFunction | epsilon/`nutkAtmRoughWallFunction(z₀)` |
| Leak | fixedValue (jet vector) | calculated | zeroGradient | fixedValue | fixedValue (released species = 1) | fixedValue | fixedValue |

When `CfdConfiguration.UseAtmosphericBL` is true (default for every CFD solver per `CfdConfigurationPresets`), the atmospheric rows above replace the corresponding plain "Wind/Wall" rows. `constant/turbulenceProperties` then carries either `kEpsilonCoeffs { sigmaEps 1.167 ... }` or, when buoyancy treatment is requested, `RAS { RASModel buoyantKEpsilon; buoyantKEpsilonCoeffs { Ceps3 -0.33 ... } }`. `constant/transportProperties` carries `Sct` and `Prt` keywords.

### 6.3 Solver pipeline

`OpenFoamRunner.RunAsync` (transient) executes:

1. `blockMesh` — generate base hex grid
2. Optional `topoSet` + `refineMesh` for source/obstacle refinement (one or two levels)
3. `topoSet` (source/obstacle cellSet creation)
4. `setFields` (initialise U, species, T)
5. `decomposePar` (when parallel)
6. `mpiexec -np N <solver> -parallel` (or single-process)
7. `reconstructPar`
8. `OpenFoamResultReader.ReadResults` → `OpenFoamResult`

Adjustable Courant number (`CfdConfiguration.MaxCourantNumber`, default 10.0 per Fiates & Vianna 2016) lets the solver auto-size `deltaT`.

### 6.4 Wind field generation

`WindFieldRunner.Run` writes a steady-state `simpleFoam` case with the user's meteorology as the inlet, no scalar transport. `OpenFoamResultReader.ReadWindField` reads the converged `U` field and constructs a `WindField3D` for in-memory interpolation.

For GPU wind fields, `FluidX3DWindFieldRunner.RunAsync` performs the same job entirely in VRAM: no temp directory, no OpenFOAM dictionary writing, just an LBM allocation, voxelisation, equilibrium-inlet seeding, `fx3d_run(steady_steps)`, and a copy back to host memory. Result is the same `WindField3D` interface, so downstream consumers (`Scene3DEditorControl`, `WindFieldVisualiser`, the dispersion runners) need not know which back end ran.

### 6.5 FluidX3D case layout

FluidX3D runners do not generate an OpenFOAM-style case tree. Instead they own an in-memory LBM instance (handle table on the C++ side, see §3.7) and write **only the result snapshots** to disk, under `%TEMP%/DisperSim3D_<solver>_sim_<id>/`:

```
DisperSim3D_fx3ddp_sim_<simId>/        FluidX3DDispersion
├── 0.000.bin                          concentration field at t=0
├── 1.500.bin                          at t=1.5s
├── 3.000.bin
└── ...

DisperSim3D_fx3dds_sim_<simId>/        FluidX3DDispersionSteady
└── 18.420.bin                         single converged snapshot only

DisperSim3D_fx3dfr_sim_<simId>/        FluidX3DFire (dual tracer)
├── 0.000.bin       0.000_T.bin
├── 1.500.bin       1.500_T.bin
└── ...
```

Each `.bin` is the raw row-major (X, Y, Z) `double[Nx,Ny,Nz]` array written by `OpenFoamResult.SaveBinaryField`. The runner pre-loads the in-memory cache with the latest snapshot via `OpenFoamResult.PreloadField` so the UI sees the new frame without a disk round-trip; older frames are lazily loaded on demand by `OpenFoamResult.GetField`.

The `<time>` prefix uses `F3` invariant-culture formatting (e.g. `12.500.bin`). Reloading a project locates the case via `entry.CasePath`, scans for `*.bin` files, and reconstructs `OpenFoamResult.TimeStepPaths`. A single `.bin` (or a solver code containing `FX3DDS`) triggers `IsSteadyState = true` so the playback bar stays hidden.

---

## 7. API and extensibility

### 7.1 Adding a new CFD solver

1. Add an entry to `CfdSolverType` enum.
2. Create `OpenFoamCaseGenerator.GenerateMyFoam(scenario, config)` that writes the required dicts.
3. Route the new enum in:
   - `OpenFoamRunner.RunAsync` switch (solver command + case generator)
   - `OpenFoamRunner.RunAsync` field-name resolution (which scalar to read)
   - `SimulationManager.RunJobAsync` switch (steady vs transient)
   - `SimulationManager.Enqueue` solver label
   - `Scene3DEditorPanel._solverCombo` items + index↔enum map
   - `SimulationEditorDialog._cmbSolver` items + index↔enum map
   - `HeadlessRunner.RunFromFile` CLI string parser

### 7.2 Adding a new dispersion engine (analytical)

Implement `IConcentrationField` (`EvaluateConcentration(x, y, z)`). Hook into `Scene3DEditorControl.StartDispersion` for transient or `StartSteadyStateDispersion` for steady.

### 7.3 Adding a tree-section and editor

1. Add the model collection on `Scene3D` (and serialise it in `Scene3DEditorControl.SaveToFile` + `LoadFromFile`).
2. Add the section to `ProjectTreeWpfPanel.RefreshTree` and `BuildContextMenu`.
3. Add the action enum values to `ProjectTreeAction`.
4. Wire `Scene3DEditorPanel.ProjectTree_ActionRequested` for each action.
5. Optional editor dialog under `Dialogs/`.

### 7.4 Property metadata

All properties exposed in the WPF property grid should carry:

```csharp
[Category("Section")]
[Description("One-line, plain-English explanation of what this controls.")]
public double MyProperty { get; set; }
```

Read-only values use `[ReadOnly(true)]`. Hidden runtime caches use `[Browsable(false)]` and/or `[XmlIgnore]`.

### 7.4.1 Atmospheric defaults for new solvers

When you add a new entry to `CfdSolverType`, also extend `CfdConfigurationPresets.ApplyForSolver` with a `case` arm that seeds the appropriate atmospheric defaults (Sc_t, σ_ε, C_ε3, ground BC). Look at the existing `RhoReactingBuoyantFoam` case as the reference recipe — it is the most-validated configuration. The cryogenic override at the bottom of `ApplyForSolver` reads `GasLibraryItem.IsCryogenic` and bumps Sc_t / GroundT BC for LNG vapour clouds; new solvers that handle temperature should respect the same flag.

### 7.5a Adding a new FluidX3D runner

1. Add an entry to `CfdSolverType` (see §3.3 for the existing four).
2. Add the six-character code to `Core/SolverCode.cs` and its `DisplayName` map.
3. Subclass the existing `FluidX3D*Runner` skeleton if your runner reuses the
   LBM wind field + a CPU tracer; otherwise build directly on top of
   `FluidX3DBridge`. The runner exposes the same `ProgressUpdated` /
   `Completed` / `Failed` event surface as the OpenFOAM runners so
   `SimulationManager` can dispatch to it without special-casing.
4. Route the new enum in `SimulationManager.RunCfdAsync`, the simulation
   editor combo (`SimulationEditorDialog`), the headless CLI parser and
   `MemoryEstimator.For(...)`.
5. If you need a new OpenCL feature flag, edit `FluidX3D/src/defines.hpp`
   and rebuild. The post-build event copies `FluidX3D.dll` next to every
   C# output directory automatically.

### 7.5 Headless / CLI

`DisperSim3D.CLI <project-file> [options]`

Project file is either `.dsproj` (self-contained ZIP, recommended) or legacy `.xml`. The CLI loads `.dsproj` via `ProjectBundle.Open` (extracted to a session temp dir; CFD case folders inside the bundle remain readable for the run's duration).

Common modes:

- **List the project** — `--list` / `-l`: dumps gases, top-level sources, wind fields, and simulations with names + IDs. Use this to discover names before invoking `--simulation`.
- **Run a project Simulation** — `--simulation <name|id>`: picks an entry from `Project.Simulations` and executes its snapshot via `HeadlessRunner.RunSimulation`. The simulation's `SnapshotCfdConfig` (atmospheric BL, Sc_t, ground T BC, etc.) is honoured; the CLI re-applies `CfdConfigurationPresets.ApplyForSolver` defensively before running.
- **Run a Gaussian / CFD solver directly** — `-s <solver>`: bypasses the project's Simulation list. Solver names: `plume`, `puff`, `scalartransportfoam`, `scalarsimplefoam`, `pimplefoam`, `buoyantpimplefoam`, `reactingfoam`, `rhosimplefoam`, `rhoreactingbuoyantfoam`.
- **Legacy XML** — `--scenario <index>`: pick a `DispersionScenario` from old XML files (no Simulation list).

OpenFOAM environment selection (`--env native|wsl|docker|bluecfd`, `--openfoam-path <path>`, `--wsl-distro <name>`) and tuning (`--grid <N>`, `--nprocs <N>`) are unchanged from the legacy CLI.

**Native Windows OpenFOAM v2512 is the recommended path** and is proven end-to-end with `rhoReactingBuoyantFoam`. The standard ESI installer puts the project root at `%APPDATA%\ESI-OpenCFD\OpenFOAM\v2512\msys64\home\ofuser\OpenFOAM\OpenFOAM-v2512` — pass that as `--openfoam-path`. WSL/Docker/BlueCFD remain available as alternatives.

Examples:

```
DisperSim3D.CLI project.dsproj --list
DisperSim3D.CLI project.dsproj --simulation "Stack#1 x 5m/s SW" --env native --openfoam-path "C:\Users\<you>\AppData\Roaming\ESI-OpenCFD\OpenFOAM\v2512\msys64\home\ofuser\OpenFOAM\OpenFOAM-v2512"
DisperSim3D.CLI project.dsproj -s plume
DisperSim3D.CLI legacy.xml -s rhoReactingBuoyantFoam --env native --openfoam-path "<openfoam-root>" --grid 60
```

---

## 8. Validation

### 8.1 Reference experiments

The CFD pipeline reproduces the test cases of:

- **Birch et al. (1984, 1987)** — methane sonic jets at 2.0 and 3.5 bar upstream pressure — molar fraction decay along the centreline
- **Chuech et al. (1989)** — air sonic jet velocity decay
- **Gant & Ivings (2005)** — jet simulation from a 10.5 mm orifice at 5.0 bar / 250 K (cloud volume comparison)
- **Fiates & Vianna (2016)** — full 416×488×77 m offshore platform with 5 leak directions × 4 wind directions, comparison against ANSYS-CFX

Expected agreement (per Fiates & Vianna 2016): within 10 % of experimental data on jet centreline; within 20 % of commercial-CFD results on cloud volume; 7 % difference on the largest cloud case.

For atmospheric / heavy-gas / cryogenic releases, the validated targets are:

- **Mack & Spruijt 2013** — `reactingFoam` with `Sc_t = 0.7` and `C_ε3 = -0.33` reproduces Hamburg WT dataset DAT632 (SF₆ over 8.6° slope) within 8 / 8 sensors at FAC2 and matches Fluent's solution on a CO₂ release in atmospheric boundary layer over hilly terrain (8821 kg/s)
- **Vu 2019** — `gasDispersionBuoyantFoam` with HHTSL k-ε constants, `Sc_t = 0.15`, `FixedTemperature` ground reproduces Burro 3/7/8/9 LNG vapour dispersion peak concentrations within Hanna SPM ranges (FAC2 = 1.0, MRB = -0.15, RMSE = 0.10) — outperforming FLACS on every metric
- **Schalau et al. 2021** — `rhoReactingBuoyantFoam` with z₀-based atmospheric BCs achieves VDI 3783/9 hit ratio q > 66 % on cube and 7×3 building array test cases

### 8.2 Validation harness

DisperSim ships an integrated harness that runs benchmarks end-to-end and scores them with Hanna Statistical Performance Measures. Live entry points:

- **CLI** — `DisperSim3D.CLI --validate <file-or-dir>`. Single file or whole directory of `.dsbench` files. Exit code 0 when every benchmark passes its acceptance ranges; 2 otherwise.
- **UI** — `Dispersion → Validate against Benchmarks…` opens a dialog with multi-select file list, Run-all button, colour-coded SPM table (green = pass, red = fail) and an `Export Markdown…` button for reports.
- **API** — `DisperSim3D.Validation.ValidationRunner.Run(spec, envCfg, log)` returns a `ValidationReport` for use in custom harnesses.

### 8.3 Hanna SPM definitions

Implemented in [`DisperSim3D/Validation/SpmCalculator.cs`](DisperSim3D/Validation/SpmCalculator.cs) as a pure function over `IList<SensorPair>`. Definitions (Vu 2019 §1.4.2, Chang & Hanna 2004):

| Metric | Formula | Acceptable | Perfect |
|---|---|---|---|
| **MRB** Mean Relative Bias | `2·mean((Co−Cp)/(Co+Cp))` | [-0.4, 0.4] | 0 |
| **RMSE** (normalised) | `sqrt(mean((Co−Cp)²)) / mean(Co)` | < 2.3 | 0 |
| **NMSE** | `mean((Co−Cp)²) / (mean(Co)·mean(Cp))` | — | 0 |
| **FAC2** | fraction with `0.5 ≤ Cp/Co ≤ 2.0` | [0.5, 2.0] | 1 |
| **MG** geometric mean bias | `exp(mean(ln Co − ln Cp))` | [0.67, 1.5] | 1 |
| **VG** geometric variance | `exp(var(ln Co − ln Cp))` | < 3.3 | 1 |

Geometric (log-based) metrics floor zero values at 1e-12 to avoid `−∞`.

### 8.4 `.dsbench` file format

JSON document defining a complete recipe + the published-numerical observed values. Schema version `dsbench/v1`. Top-level fields:

| Field | Purpose |
|---|---|
| `name`, `citation`, `description` | provenance |
| `source` | one Source: gas (with `isCryogenic`), position, release rate, pool/stack diameter, exit conditions |
| `meteo` | wind speed/direction, Pasquill stability, ambient T/p, `roughnessLengthM` |
| `domain` | size, grid resolution, simulation duration, timestep |
| `solver` | string matching `CfdSolverType` enum (e.g. `RhoReactingBuoyantFoam`, `GaussianPlume`) |
| `concentrationKind` | `PeakOverTime` (transient cell max across all timesteps) or `FinalSnapshot` |
| `unit` | `KgPerM3`, `MoleFraction`, `MassFraction` — informational, must match the engine output unit for the chosen solver |
| `sensors[]` | name, position [x,y,z], `measuredKgM3` (the value type is the unit named above; the field name is historical) |
| `acceptance` | per-metric `{ "min": …, "max": … }`. Either bound may be omitted to mean `±∞`. |

JS-style `// comments` are accepted (System.Text.Json with `ReadCommentHandling.Skip`).

### 8.5 Bundled benchmarks

Located under `benchmarks/` at the repo root:

All 5 bundled benchmarks **PASS** as regression baselines:

| File | Solver | Role |
|---|---|---|
| `gauss-D-smoketest.dsbench` | GaussianPlume | Engine self-consistency for `PasquillGiffordCoefficients` |
| `gauss-puff-smoketest.dsbench` | GaussianPuff | Engine self-consistency for the puff `StepTo` loop + Slade coefficients |
| `burro9.dsbench` | RhoReactingBuoyantFoam | LNG cryogenic, neutral ABL, 3 arcs (Koopman 1982 / Vu 2019 §5.4); validates atmospheric BL stack + cryogenic preset + continuous fvOptions species source + atm libs |
| `burro8.dsbench` | RhoReactingBuoyantFoam | Same setup under stable ABL (Pasquill F, U=1.8 m/s) — confirms `buoyantKEpsilon` survives stable stratification |
| `dat632.dsbench` | RhoReactingBuoyantFoam | SF₆ over slope, Hamburg WT (Mack & Spruijt 2013) — exercises the SF6 species/thermo path |

**Important — what these benchmarks lock in.** The observed values for the CFD benches (Burro 8/9, DAT632) are **regression baselines captured from the current solver pipeline at the current grid resolution**, NOT the experimental ground truth from the cited papers. Two reasons:

1. **`rhoReactingBuoyantFoam` (stock OpenFOAM v2512) does not expose Sc_t.** Its species transport equation reads `fvm::laplacian(turbulence->muEff(), Yi)` — equivalent to `Sc_t = 1.0` implicit. Vu 2019 reached experimental FAC2 = 1.0 by writing a custom solver `gasDispersionBuoyantFoam` with Sc_t = 0.15 for LNG. Without that custom code, our predictions are systematically ~3× lower than Vu's at the LNG arcs.

2. **Mesh resolution.** Vu used 897 k cells; we use 100³/2 = 500 k base + refinement (and on a wind-tunnel scale DAT632 needs even finer near-source). Stretching to Vu's resolution costs hours of wall-clock per bench.

So the CFD benches today **catch any change in the case-writer or solver pipeline** that would alter the predicted concentrations — they're a regression net, not a quantitative match against the original experiments. To upgrade to true validation against published numbers, integrate a custom `gasDispersionBuoyantFoam`-style solver (or wait for OpenFOAM upstream to expose Sc_t) and refine the meshes.

Re-running `--validate benchmarks/` is the smoke test — anything other than 5/5 PASS means a regression.

### 8.6 Adding a new benchmark

1. Copy the closest existing `.dsbench` as a template.
2. Fill in `source` / `meteo` / `domain` / `solver` from the experiment's published parameters; cite the paper and tables in `citation`.
3. List sensors with their measured values from the citation. Verify `unit` matches your chosen solver's output (`MoleFraction` for species-transport solvers, `KgPerM3` for Gaussian).
4. Set `acceptance` ranges. Default Hanna ranges are usually fine; tighten only when the publication justifies it.
5. Run `DisperSim3D.CLI --validate path/to/your.dsbench` to exercise it.

### 8.7 Limitations of the harness

- **`rhoReactingBuoyantFoam` (stock) does not expose Sc_t** — species transport uses `turbulence->muEff()` directly, equivalent to Sc_t = 1.0. For LNG / heavy-gas dispersion this systematically over-mixes the cloud. The validated recipe (Vu 2019, Mack 2013) uses Sc_t < 1; reproducing their FAC2 numbers requires a custom solver. Until that lands, the CFD benchmarks are regression baselines, not quantitative matches against papers.
- Engine output unit handling is the recipe author's responsibility — `Unit` is informational, no automatic conversion. The mass-fraction ↔ mole-fraction conversion is `Y = x · M_species / M_air` (CH4: × 0.553; SF6: × 5.03 in air).
- The `.dsbench` recipe has no notion of obstacles. For built-up-area validation (VDI 3783/9), wait for the `Geometry` extension or pre-build a Project bundle by hand.
- Existing detector optimisation, fire modelling, and exceedance-curve features are **not** validated by the harness — they have their own internal regression tests under `Tests/`.



### 8.8 Detector optimisation validation

`SetCoveringSolver.SolveExact` is verified against:

- Trivial 4-variable problem (Vianna 2019 §5.1, Eq. 8–12): expected solution `Z = 52`, `X = [1, 0, 0, 1]`
- p-median test (10 facilities, Vianna 2019 §5.2, Table 3): identical results to CPLEX
- 9 covering instances ranging from 25 to 14 400 cells (Vianna 2019 §5.3): same optimal cardinality

For greedy-only mode, expect ≤ 1 column over the optimum on structured (axis-aligned cubic) instances.

### 8.9 Performance reference

Vianna 2019 Table 4 — `T(n) ≈ 4.21 · n^2.98` seconds where `n` is cell count, on Intel Xeon @ 2.0 GHz. DisperSim 3D is in the same order of magnitude on similar hardware (greedy faster, exact slower with 200k node budget).

---

## 9. Limitations and design constraints

| Area | Limitation | Mitigation |
|---|---|---|
| Operating system | Windows-only (UseWindowsForms + UseWPF) | OpenFOAM runs natively (ESI v2512 mingw build) or via WSL2/Docker; the C# layer is Windows-only by design |
| Mesh max size | OpenFOAM cases beyond ~10 M cells exceed practical wallclock | Mesh-sensitivity analysis suggests ~1.3 M cells is the optimum (Fiates & Vianna 2016 Mesh_02) |
| Detector optimisation | Greedy ≤ 1 detector over optimum on structured grids; exact solver caps at 200 000 nodes | Use Moore neighbourhood to reduce row count; or shrink the protected region |
| BinaryFormatter | Disabled in .NET 9+/10 — older WeifenLuo versions break | Project pinned to DockPanelSuite 3.1.1 (NuGet) which doesn't use BinaryFormatter |
| HP leak chemistry | Birch expanded source assumes ideal-gas, single-species | Multi-component HP leaks would need a real isentropic flash; out of scope |
| Wind field | Steady simpleFoam — no diurnal cycle, no terrain thermals | Multiple wind fields per project allow scenario sweeps |
| Atmospheric stratification | Only neutral stratification supported (stock OpenFOAM `atmBoundaryLayerInlet*` BCs assume neutral) | For stable / unstable runs, use a Gaussian solver (Pasquill class is honoured) or extend with Monin-Obukhov-aware inlet BCs |
| Schalau 2021 power-law inlet | Not implemented (would require custom wall functions / `codedFixedValue`) | Stock log-law via `atmBoundaryLayerInlet*` covers most use cases; revisit if built-up cases require it |
| Mesh vs z₀ check | Advisory only, written to `LOG_atmospheric.txt` in the case dir; no UI warning yet | Inspect the case folder's `LOG_atmospheric.txt` before interpreting results |
| WPF/WinForms interop | ElementHost-hosted controls have separate input-routing; some keyboard shortcuts may not propagate | Use menu items / right-click for all critical actions |

---

## 10. GPU and memory tooling

### 10.1 GPU device selection

**File**: [`Dialogs/GpuPerformanceSettingsDialog.cs`](DisperSim3D/Dialogs/GpuPerformanceSettingsDialog.cs).

`Settings → GPU & Memory…` opens a two-tab dialog. The **Compute GPU** tab calls `FluidX3DBridge.ListDevicesJson()` and renders every detected OpenCL device in a `ListView` with name, type, memory, compute units and max work-group size. Selecting a row and clicking *Set as default* stores the device id in `AppSettings.PreferredComputeDeviceId` (default `-1` = let FluidX3D pick). All four FluidX3D runners read that setting on `RunAsync` and call `fx3d_create_on_device(cfg, device_id)`.

When `fx3d_list_devices` fails (e.g. DLL missing, OpenCL ICD not installed, driver issue), the underlying P/Invoke exception is captured in `FluidX3DBridge.LastListDevicesError` and surfaced in the dialog's status line — far more useful than the silent "No OpenCL devices reported" that would otherwise appear.

### 10.2 Memory estimator

**File**: [`Core/MemoryEstimator.cs`](DisperSim3D/Core/MemoryEstimator.cs).

The Memory Estimator tab of the same dialog computes per-cell footprint × cell count for any solver before you commit to a run. Per-cell costs (compile-time constants):

| Component | RAM | VRAM |
|---|---|---|
| FluidX3D D3Q19 FP32 baseline | – | 93 B |
| `+ TEMPERATURE` extension | – | +32 B |
| `+ SUBGRID` LES | – | +24 B |
| CPU `DispersionTracerEngine` | 41 B | – |
| CPU `FireTracerEngine` | 57 B | – |
| OpenFOAM steady (`simpleFoam`) | – | – (≈ 150 B/cell on disk) |
| OpenFOAM transient reactive | – | – (≈ 450 B/cell per write) |

Public API:

```csharp
MemoryEstimate EstimateFluidX3DWind(int Nx, int Ny, int Nz);
MemoryEstimate EstimateDispersionCpu(int Nx, int Ny, int Nz, int writeCount);
MemoryEstimate EstimateFire(int Nx, int Ny, int Nz, int writeCount);
MemoryEstimate EstimateOpenFoam(int Nx, int Ny, int Nz, int writeCount,
                                CfdSolverType solver);
MemoryEstimate For(CfdSolverType solver, int Nx, int Ny, int Nz, int writeCount);
```

`MemoryEstimate` carries `RamBytes`, `VramBytes`, `DiskBytes`, plus a `HumanBytes` formatter that produces strings like `VRAM 103 MB · RAM 36 MB · Disk 213 MB`.

---

## 11. References

1. **Fiates, J., Vianna, S.S.V.** (2016). *Numerical modelling of gas dispersion using OpenFOAM*. Process Safety and Environmental Protection, 104, 277–293. https://doi.org/10.1016/j.psep.2016.09.011

2. **Vianna, S.S.V.** (2019). *The set covering problem applied to optimisation of gas detectors in chemical process plants*. Computers and Chemical Engineering, 121, 388–395. https://doi.org/10.1016/j.compchemeng.2018.11.008

3. **Birch, A.D., Brown, D.R., Dodson, M.G., Swaffield, F.** (1984). *The structure and concentration decay of natural gas*. Combustion Science and Technology, 36, 249–261.

4. **Birch, A.D., Hughes, D.J., Swaffield, F.** (1987). *Velocity decay of high pressure jets*. Combustion Science and Technology, 52, 161–171.

5. **Birch, A.D., Schefer, R.W.** (1984). Pseudo-source equivalent-diameter approach for sonic underexpanded jets — see Benintendi (2010) for the engineering form used here.

6. **Chuech, S.G., Lai, M.C., Faeth, G.M.** (1989). *Structure of turbulent sonic underexpanded free jets*. AIAA Journal, 27 (5), 549–559.

7. **Gant, S.E., Ivings, M.J.** (2005). *CFD modelling of low pressure jets for area classification*. Health and Safety Laboratory.

8. **Benintendi, R.** (2010). *Turbulent jet modelling for hazardous area classification*. Journal of Loss Prevention in the Process Industries.

9. **Balas, E.** (1965). *An additive algorithm for solving linear programs with zero-one variables*. Operations Research, 13, 517–545.

10. **British Petroleum** (n.d.). *Fire and Gas Detection Engineering Technical Practice*. GP 30-85.

11. **Pasquill, F., Smith, F.B.** (1983). *Atmospheric Diffusion*. Ellis Horwood.

12. **Briggs, G.A.** (1969, 1971, 1975). Plume rise correlations for buoyant and momentum sources.

13. **OpenFOAM Foundation / ESI** — OpenFOAM v2306+ / v2512 user guide, https://www.openfoam.com/documentation

14. **Mack, A., Spruijt, M.P.N.** (2013). *Validation of OpenFoam for heavy gas dispersion applications*. Journal of Hazardous Materials, 262, 504–516. https://doi.org/10.1016/j.jhazmat.2013.08.065

15. **Tran Le Vu** (2019). *On numerical modelling of atmospheric gas dispersion using CFD approach*. PhD thesis, Nanyang Technological University, Singapore. (Validated `gasDispersionBuoyantFoam` against Burro LNG vapour dispersion field tests, outperforming FLACS on all SPMs.)

16. **Schalau, S., Habib, A., Michel, S.** (2021). *Atmospheric Wind Field Modelling with OpenFOAM for Near-Ground Gas Dispersion*. Atmosphere, 12 (8), 933. https://doi.org/10.3390/atmos12080933

17. **VDI 3783/9** (2017). *Environmental Meteorology — Prognostic Microscale Wind Field Models — Evaluation for Flow around Buildings and Obstacles*. Beuth Verlag, Berlin.

18. **Lehmann, M.** (2022). *Esoteric Pull and Esoteric Push: Two simple in-place streaming schemes for the lattice Boltzmann method on GPUs*. Computation, 10 (6), 92. https://doi.org/10.3390/computation10060092 — underpins the [FluidX3D](https://github.com/ProjectPhysX/FluidX3D) solver embedded for the four GPU LBM runners.

19. **Khronos Group** — OpenCL 1.2 specification and ICD loader. https://www.khronos.org/opencl/

20. **HelixToolkit.Wpf** — https://github.com/helix-toolkit/helix-toolkit (MIT)

21. **DockPanelSuite** — https://github.com/dockpanelsuite/dockpanelsuite (MIT)

22. **HandyControl** — https://github.com/HandyOrg/HandyControl (MIT)

---

*Document last updated on 2026-05-12. Initial v1.0 documentation generated on 2026-05-09; this revision adds the FluidX3D GPU LBM solver family (§3.7), Dispersion Studies and Detector Allocation (§3.8), the FluidX3D case layout (§6.5) and the GPU & Memory tooling (§10).*
