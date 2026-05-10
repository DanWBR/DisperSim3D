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
- Three solver families:
  - **Gaussian Puff** (transient analytical)
  - **Gaussian Plume** (steady-state analytical with bent-plume trajectory)
  - **CFD** (OpenFOAM): scalarTransportFoam, simpleFoam, pimpleFoam, buoyantPimpleFoam, reactingFoam, rhoSimpleFoam, **rhoReactingBuoyantFoam** (recommended universal, after Fiates & Vianna 2016)
- High-pressure leak modelling with **Birch & Schefer expanded-diameter** for sonic releases
- Pre-computed wind fields shared across multiple dispersion runs
- Snapshot-based simulations: history is immutable; editing a source after a run does not change past results
- Animated wind-field visualization with user-tunable arrow appearance
- **Detector placement optimisation** via the Set Covering Problem (Vianna 2019)
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

| Enum | OpenFOAM solver | Use case |
|---|---|---|
| `ScalarTransportFoam` / `ScalarTransportFoamSteady` | `scalarTransportFoam` | Passive scalar in a frozen velocity field |
| `ScalarSimpleFoam` | `simpleFoam` + scalar | Steady-state RANS |
| `RhoSimpleFoam` | `rhoSimpleFoam` + scalar | Compressible steady |
| `PimpleFoam` | `pimpleFoam` + fvOptions scalar | Transient incompressible |
| `BuoyantPimpleFoam` | `buoyantPimpleFoam` | Transient with buoyancy (heavy/light gas) |
| `ReactingFoam` | `reactingFoam` | Multi-species, combustion off |
| **`RhoReactingBuoyantFoam`** | `rhoReactingBuoyantFoam` | **Recommended**: compressible + buoyant + multi-species, subsonic & sonic, combustion off |

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

---

## 4. Workflow

### 4.1 Typical user session

1. **File → New Project**
2. Right-click **Gases → Add Pure Gas...** → enter Methane / Custom
3. Right-click **Sources → Add Source...** → click on the map → fill the source dialog (gas, release rate, direction, optional HP leak)
4. Right-click **Wind Fields → Add Wind Field...** in the Manager → set wind speed/direction/stability → **Run** (executes simpleFoam in background, status changes to `Ready`)
5. Right-click **Simulations → New Simulation...** → pick source × wind field × solver → OK creates a `Configured` simulation
6. Right-click the simulation → **Run** → `Configured → Queued → Running → Completed`. Snapshot of source/gas/meteo/cfd-config is taken at this moment.
7. Check the simulation's checkbox in the tree → 3D playback in the viewport, controls in the bottom playback bar
8. **Dispersion → Optimize Detector Placement...** → pick simulations + protected region → outputs minimum detector set, adds them to the project as `OptDet N`

### 4.2 Project tree sections

```
[Project Name]
├── General Settings           (defaults: wind, domain, grid)
├── Gases (n)                  (project Gas Library — pure + mixtures)
├── Geometry (n)               (3D models / decorations / obstacles)
├── Sources (n)                (release sources, top-level)
├── Wind Fields (n)            (pre-computed simpleFoam runs)
├── Simulations (n)            (Source × WindField × Solver runs)
├── Monitors (n)               (passive concentration probes)
└── Detectors (n)              (alarm-threshold detectors)
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

### 7.5 Headless / CLI

`DisperSim3D.CLI scene.xml -s <solver> [--env wsl|docker|native|bluecfd] [--openfoam-path <path>] [--scenario N] [--grid N] [--nprocs N]`

Solver names accepted: `plume`, `puff`, `scalartransportfoam`, `buoyantpimplefoam`, `pimplefoam`, `reactingfoam`, `scalarsimplefoam`, `rhosimplefoam`, `rhoreactingbuoyantfoam`.

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

A formal validation harness consuming `.dsbench` benchmark files is planned (see plan file under `~/.claude/plans/`).

### 8.2 Detector optimisation validation

`SetCoveringSolver.SolveExact` is verified against:

- Trivial 4-variable problem (Vianna 2019 §5.1, Eq. 8–12): expected solution `Z = 52`, `X = [1, 0, 0, 1]`
- p-median test (10 facilities, Vianna 2019 §5.2, Table 3): identical results to CPLEX
- 9 covering instances ranging from 25 to 14 400 cells (Vianna 2019 §5.3): same optimal cardinality

For greedy-only mode, expect ≤ 1 column over the optimum on structured (axis-aligned cubic) instances.

### 8.3 Performance reference

Vianna 2019 Table 4 — `T(n) ≈ 4.21 · n^2.98` seconds where `n` is cell count, on Intel Xeon @ 2.0 GHz. DisperSim 3D is in the same order of magnitude on similar hardware (greedy faster, exact slower with 200k node budget).

---

## 9. Limitations and design constraints

| Area | Limitation | Mitigation |
|---|---|---|
| Operating system | Windows-only (UseWindowsForms + UseWPF) | OpenFOAM is invoked via WSL2/Docker — Linux-side compute works fine |
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

## 10. References

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

14. **HelixToolkit.Wpf** — https://github.com/helix-toolkit/helix-toolkit (MIT)

15. **DockPanelSuite** — https://github.com/dockpanelsuite/dockpanelsuite (MIT)

16. **HandyControl** — https://github.com/HandyOrg/HandyControl (MIT)

---

*Document generated on 2026-05-09 alongside the v1.0 codebase.*
