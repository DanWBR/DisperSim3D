# Context for Claude Code

This document is the briefing for picking up development of this project in a fresh Claude Code session. Read this first before making changes.

## What this project is

**DisperSim 3D** — an open-source 3D gas dispersion analysis tool. It provides interactive visualization and simulation of atmospheric gas releases, fire/radiation hazards, and CFD-based dispersion modeling in a 3D environment.

The main deliverable is a WinForms `UserControl` (`FlowsheetEditor3DControl`) that renders 3D scenes using HelixToolkit.Wpf hosted via `ElementHost`. Users can place gas release sources, fire sources, monitor points, gas detectors, and imported 3D obstacle geometry (buildings, equipment), then run dispersion simulations and visualize results as isosurfaces, particle clouds, and contour planes.

## Tech stack

- **.NET 10** (`net10.0-windows`)
- **C# 13** — modern syntax is welcome
- **WinForms** (host) + **WPF** (viewport via ElementHost)
- **HelixToolkit.Wpf 3.1.2** (NuGet PackageReference)
- **DockPanelSuite 3.1.1** (NuGet PackageReference) — for dockable panels
- SDK-style csproj with PackageReference
- Root namespace and assembly name: `DisperSim3D`

## Solution structure

The solution (`DisperSim3D.sln`) contains two projects:

1. **DisperSim3D** (class library) — the main library with all models, engines, renderers, and UI controls
2. **TestApp** (WinForms exe) — standalone test harness that hosts the editor panel; supports `--gptest` flag for headless Gaussian puff testing

## Project structure

```
DisperSim3D/                       (main library)
├── Models/                        (26 files) POCOs, no UI dependencies
│   ├── Enums.cs                   CameraMode, EditMode
│   ├── DispersionEnums.cs         PasquillStabilityClass, DispersionThresholdType, DispersionSimulationState
│   ├── Flowsheet3D.cs             Scene3D — top-level container for all scene elements
│   ├── DispersionScenario.cs      Complete scenario: sources + meteo + thresholds + solver config
│   ├── ReleaseSource3D.cs         Gas release source with rate, duration, orientation, high-pressure params
│   ├── MeteorologicalConditions.cs  Wind speed/direction, stability class, wind shear
│   ├── TransientWindProfile.cs    Time-varying wind with interpolation
│   ├── GasProperties.cs           Hazard thresholds (LFL, IDLH, ERPG) per gas species
│   ├── GasComponent.cs            GasComponent + GasMixture for multi-component releases
│   ├── DispersionThreshold.cs     Concentration level with color/opacity for visualization
│   ├── ContourPlaneConfig.cs      2D contour slice (XY/XZ/YZ) through the domain
│   ├── FireScenario.cs            FireSource + FireScenario for jet/pool fire modeling
│   ├── GasDetector3D.cs           Point gas detector with threshold and time-series
│   ├── MonitorPoint3D.cs          Concentration monitor (point, line, or region)
│   ├── Decoration3D.cs            Imported 3D geometry (buildings, obstacles) with clipping
│   ├── CfdConfiguration.cs        OpenFOAM solver parameters and environment config
│   ├── CfdSimulationEntry.cs      Metadata for completed CFD runs
│   ├── CfdSolverType.cs           CfdSolverType enum + OpenFoamEnvironmentType enum
│   ├── OpenFoamResult.cs          CFD results with lazy-loading and LRU caching
│   ├── WindField3D.cs             3D wind velocity grid with trilinear interpolation
│   ├── WindRoseData.cs            Wind direction/speed frequency distribution
│   ├── CameraPreset.cs            Named camera viewpoints
│   ├── Visual3DTag.cs             Links WPF Visual3D elements to domain objects
│   ├── GeometryHelpers.cs         Point3D extensions, snap-to-grid, BoundingBox with AABB
│   ├── MaterialType3D.cs          Matte, Metallic, Glass, Emissive
│   └── WorkPlane.cs               Horizontal elevation planes for scene layers
│
├── Core/                          (23 files) Logic, no UI dependencies
│   ├── GaussianPuffEngine.cs      Main dispersion engine (IConcentrationField); puff transport + PG dispersion
│   ├── IConcentrationField.cs     Interface: EvaluateConcentration(x, y, z)
│   ├── PasquillGiffordCoefficients.cs  Lateral/vertical dispersion coefficients by stability class
│   ├── BriggsPlumerise.cs         Plume rise from buoyancy/momentum
│   ├── HighPressureLeakModel.cs   Choked/unchoked flow, blowdown profile
│   ├── DispersionRenderer.cs      Isosurface + particle cloud visualization from IConcentrationField
│   ├── MarchingCubes.cs           Isosurface mesh generation from 3D scalar fields
│   ├── MeshClipper.cs             Clip meshes along X/Y/Z planes for cross-sections
│   ├── ColorMapHelper.cs          Jet, Viridis, Inferno, Coolwarm colormaps
│   ├── JetFireModel.cs            Flame length, tilt, radiation at distance (jet + pool fire)
│   ├── FireRenderer.cs            3D flame geometry + radiation contour rendering
│   ├── DetectorEvaluator.cs       Evaluates detector response during simulation
│   ├── ExceedanceCurveCalculator.cs  Probability of threshold exceedance across scenarios
│   ├── OpenFoamCaseGenerator.cs   Generates full OpenFOAM case directory (mesh, BC, solver config)
│   ├── OpenFoamRunner.cs          Async OpenFOAM execution with progress reporting
│   ├── OpenFoamEnvironment.cs     Detects/configures OpenFOAM (WSL2, Docker, BlueCFD, native)
│   ├── OpenFoamResultReader.cs    Parses OpenFOAM output into concentration fields
│   ├── OpenFoamConcentrationField.cs  Wraps CFD results as IConcentrationField
│   ├── WindRoseRenderer.cs        3D wind rose wedge visualization
│   ├── ModelLoader.cs             Loads OBJ/STL/3DS files for obstacle geometry
│   ├── MaterialHelper.cs          Creates WPF 3D materials
│   ├── AppSettings.cs             Singleton for persisting CFD config defaults to XML
│   └── FormExtensions.cs          DPI scaling helpers for WinForms
│
├── Controls/                      (5 files) UI controls
│   ├── FlowsheetEditor3DControl.cs       Main UserControl (~3100 lines): viewport, simulation, serialization, hit testing
│   ├── FlowsheetEditor3DControl.Designer.cs
│   ├── FlowsheetEditor3DControl.resx
│   ├── FlowsheetEditorPanel.cs           Full app shell: menus, toolbars, status strip, dock panels
│   ├── AddItemPanel.cs                   Panel for adding sources/detectors/monitors
│   └── DockPanels.cs                     PropertiesDockPanel + CfdSimulationsDockPanel
│
├── Dialogs/                       (17 files) Modal configuration dialogs
│   ├── DispersionSourceDialog.cs         Gas release source parameters
│   ├── HighPressureSourceDialog.cs       High-pressure leak with discharge calculations
│   ├── FireSourceDialog.cs               Jet/pool fire parameters
│   ├── MeteorologicalDialog.cs           Wind, stability, ambient conditions
│   ├── TransientWindDialog.cs            Time-varying wind profiles + ESD
│   ├── ThresholdsDialog.cs               Concentration threshold levels
│   ├── GasMixtureDialog.cs               Multi-component gas mixture editor
│   ├── MonitorPointDialog.cs             Monitor point placement
│   ├── DetectorResultsDialog.cs          Detector evaluation results display
│   ├── ExceedanceDialog.cs               Exceedance probability curves
│   ├── WindRoseDialog.cs                 Wind rose data + scenario generation
│   ├── ScenarioManagerDialog.cs          Manage multiple dispersion scenarios
│   ├── CfdSettingsDialog.cs              OpenFOAM solver configuration
│   ├── CfdProgressPanel.cs              CFD solve progress display
│   ├── CfdSimulationsPanel.cs           CFD simulation management + playback
│   ├── ImportModelDialog.cs              3D model import with preview
│   └── BatchExportDialog.cs              Batch image export from camera presets
│
├── PropertyAdapters/              (2 files) PropertyGrid adapters
│   ├── DecorationPropertyAdapter.cs
│   └── ReleaseSourcePropertyAdapter.cs
│
├── Resources/
│   ├── Icons/                     44 UI icons (PNG + ICO)
│   └── Models3D/                  1 model: CSTR.obj
│
└── DisperSim3D.csproj

TestApp/                           (standalone test harness)
├── Program.cs                     Entry point; --gptest for headless test
├── MainForm.cs                    Hosts FlowsheetEditorPanel
├── GpTest.cs                      Gaussian puff engine validation
├── Dialogs/                       Copies of 4 dialogs from main library
├── PropertyAdapters/              3 adapters (includes StreamConnection + UnitOperation)
└── TestApp.csproj
```

## Three main subsystems

### 1. Gaussian Puff Dispersion
The core atmospheric modeling engine. `GaussianPuffEngine` implements `IConcentrationField` and manages discrete puffs that are emitted, transported by wind, and dispersed using Pasquill-Gifford coefficients. Supports:
- Multiple release sources per scenario
- Pasquill stability classes A–F with wind shear
- Briggs plume rise (buoyancy + momentum)
- High-pressure leak modeling (choked/unchoked flow, blowdown profiles)
- Transient wind profiles with interpolation
- Multi-component gas mixtures

### 2. CFD (OpenFOAM) Integration
Optional high-fidelity solver using OpenFOAM's `scalarTransportFoam`. The pipeline:
- `OpenFoamEnvironment` detects the platform (WSL2, Docker, BlueCFD, native Windows)
- `OpenFoamCaseGenerator` writes the case directory (blockMesh, boundary conditions, fvSchemes)
- `OpenFoamRunner` executes the solver asynchronously with progress callbacks
- `OpenFoamResultReader` parses timestep output files
- `OpenFoamConcentrationField` wraps results as `IConcentrationField` for the same visualization pipeline

### 3. Fire & Radiation Modeling
`JetFireModel` computes flame length, tilt angle, and thermal radiation at distance for jet fires and pool fires. `FireRenderer` generates 3D flame geometry and radiation contour surfaces.

## Visualization pipeline

All dispersion results flow through a common interface:

```
IConcentrationField (GaussianPuffEngine or OpenFoamConcentrationField)
    → DispersionRenderer
        → MarchingCubes (isosurface mesh generation)
        → ColorMapHelper (concentration → color)
        → MeshClipper (optional cross-section slicing)
    → FlowsheetEditor3DControl (adds to WPF viewport)
```

Additional renderers: `FireRenderer` for flame/radiation visuals, `WindRoseRenderer` for wind frequency display.

## Simulation flow

### Startup
1. User clicks Run in `FlowsheetEditorPanel` toolbar
2. `FlowsheetEditor3DControl.StartDispersion()` initializes:
   - `GaussianPuffEngine.Initialize(scenario)` — builds puff emission schedule
   - `DispersionRenderer.Initialize(scenario)` — creates 3D sampling grid
   - `DispersionRenderer.ComputeOccupancyGrid()` — marks obstacle cells
   - `DetectorEvaluator.Reset()` — clears detector state
   - Starts `DispatcherTimer` at 33ms interval (~30 FPS)

### Each timestep (timer tick)
1. **Advance physics**: `engine.StepTo(newTime)` — emits scheduled puffs, advects all active puffs by wind, expands sigma (PG dispersion), applies decay/deposition, prunes negligible puffs
2. **Transient effects**: update wind (if transient profile active), update HP leak rate (if blowdown), handle ESD shutdown
3. **Sample monitors**: evaluate concentration at each monitor point, store in `TimeSeries`
4. **Evaluate detectors**: check concentration against detector thresholds
5. **Update 3D visuals** (at staggered intervals for performance):
   - Every 2 frames: `GenerateIsosurfaces()` via Marching Cubes
   - Every frame: `GenerateParticleCloud()` (lightweight)
   - Every 4 frames: contour planes, vector fields, streamlines, fire visuals

### Concentration evaluation
`GaussianPuffEngine.EvaluateConcentration(x, y, z)` sums 3D Gaussian contributions from all active puffs at the query point, including ground reflection and mixing height lid.

### Async pre-computation (optional)
`RunGaussianPuffAsync()` runs the full simulation in a background worker, stores discretized concentration fields as binary files per timestep, then plays them back via the timer for smooth visualization.

## What works

- Full Gaussian puff dispersion simulation with real-time 3D visualization
- Isosurface rendering at multiple concentration thresholds
- Particle cloud visualization
- Fire source modeling (jet fire + pool fire) with radiation contours
- Gas detector placement and evaluation (coverage analysis, detection times)
- Monitor points (point, line, region) with time-series data + CSV export
- OpenFOAM case generation, execution, result reading, and playback
- XML serialization/deserialization of complete scene state
- Multi-scenario management
- Transient wind profiles
- Wind rose visualization and scenario generation
- 3D model import (OBJ, STL, 3DS) for obstacles/buildings
- Camera presets and multiple view modes
- Batch image export
- DPI-aware UI with dockable panels
- Hit testing and interactive object selection/drag
- Snap-to-grid placement
- Contour plane slicing (XY, XZ, YZ)
- Exceedance probability curve calculation
- High-pressure leak blowdown profiles

## Build

```
dotnet build DisperSim3D.sln
```

## Code conventions

- Modern C# syntax (file-scoped namespaces, target-typed new, etc.)
- XML doc comments in English on public classes/methods
- 4-space indentation, Allman braces (opening brace on new line)
- No WPF bindings or MVVM — straight code-behind with classic events
- `IConcentrationField` interface for polymorphic concentration evaluation

## Design decisions (don't reverse without reason)

- **WinForms host + WPF viewport** via ElementHost — avoids full WPF app complexity
- **No MVVM** in the control — classic event-driven code-behind
- **`IConcentrationField` abstraction** — allows Gaussian puff and CFD results to share the same visualization pipeline
- **OpenFOAM as optional** — the Gaussian puff engine works standalone; CFD is an upgrade path
- **Procedural shapes as fallback** — works without `.obj` models
- **Scene3D is standalone** — no external dependencies on other simulators

## Things to NOT do

- Don't introduce WPF `Window` / MVVM (was removed on purpose)
- Don't break the `IConcentrationField` abstraction — all concentration sources must implement it

## License

GPLv3 — Copyright 2026 Daniel Wagner Oliveira de Medeiros
