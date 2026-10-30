# DisperSim 3D

An interactive 3D gas dispersion analysis tool for process safety. Build a project, define release sources and gases, run Gaussian, integral (UDM-type), CFD or GPU LBM dispersion simulations, visualize concentration fields, and optimize gas detector placement.

Built on **.NET 10** with **HelixToolkit.WPF** for 3D rendering on Windows, **Avalonia 11** with its own OpenGL 3.3 viewport for the Linux and macOS UI, **OpenFOAM** for CFD-based wind fields and reactive transport, **FluidX3D** (GPU lattice Boltzmann) for fast wind, dispersion and fire-plume runs, and the **DWSIM core** (`DWSIMCore.Foundation`, bundled) for the two-phase source and cloud thermodynamics of the integral model.

**Cross-platform status — validated end-to-end on Ubuntu 24.04 / WSL2:**

| Component | TFM / build | Windows | Linux | macOS |
|---|---|:-:|:-:|:-:|
| `DisperSim3D` (engine) | `net10.0` + `net10.0-windows` | ✅ | ✅ | ✅ |
| `DisperSim3D.CLI` (headless) | `net10.0` | ✅ | ✅ | ✅ |
| `DisperSim3D.UI.Avalonia` (Linux / macOS UI) | `net10.0` + Avalonia 11 | ✅ | ✅ (WSLg) | ✅ |
| `DisperSim3D.UI.Wpf` + `DisperSim3D.App` (WinForms shell) | `net10.0-windows` | ✅ | — | — |
| `FluidX3D` native (`FluidX3D.dll` / `libFluidX3D.so` / `.dylib`) | C++ / OpenCL | MSVC | `g++` via `make-disp-bridge.sh` | `g++` via `make-disp-bridge.sh` |

The solution splits cleanly into a **portable engine** (`DisperSim3D.csproj` multi-targeting `net10.0;net10.0-windows`) and a **Windows-only UI library** (`DisperSim3D.UI.Wpf.csproj`) so the engine compiles on Linux/macOS without dragging in the Windows desktop SDK. The Avalonia project (`DisperSim3D.UI.Avalonia.csproj`) is the UI the `.deb` and the `.dmg` ship: the same project editor behind a portable front end, with its own OpenGL viewport and its own dialogs over the same engine binary.

Full project documentation is published on **[GitHub Pages](https://danwbr.github.io/DisperSim3D/)**. For a detailed description of physical models, file format, OpenFOAM case structure, and validation, see [TECHNICAL_DOCUMENTATION.md](TECHNICAL_DOCUMENTATION.md).

**Theory manual** — [docs/theory.md](docs/theory.md) states every model the program evaluates as the code implements it: the Gaussian correlations, the source terms, the LBM scalar transport, the equations `rhoReactingBuoyantFoam` solves, the fire and radiation models, and a section on what is deliberately not modelled. The unified integral model has its own page, [docs/unified-dispersion-model.md](docs/unified-dispersion-model.md), with the closures, the additions beyond the published UDM and the field-trial scores.

**Validation dossier** — [`docs/DisperSim3D-Validacao.pptx`](docs/DisperSim3D-Validacao.pptx) (Portuguese, 22 slides) presents every benchmark the project runs, the defects the literature found, the stated limits of the model and the 38 sources behind them. Rebuild it with `python docs/build_deck.py`. The underlying numbers are in [docs/validation.md](docs/validation.md) and [docs/benchmark-results.md](docs/benchmark-results.md); the integral model is scored separately on 15 field trials (13 pass) at the end of the latter.

---

## Features

### Project-Centric Workflow

DisperSim 3D organizes work around a **Project** with discrete sections shown in a left-side dockable tree:

- **General Settings** — defaults for meteo, domain size, grid, CFD config
- **Gases** — central library of pure gases and mixtures, referenced by sources
- **Geometry** — imported obstacles and decorations (`.obj`, `.stl`, `.rvm`; `.3ds` on Windows)
- **Sources** — top-level release sources with gas-library references
- **Wind Fields** — meteo + obstacle scenarios resolved to 3D velocity fields (uniform or CFD)
- **Simulations** — frozen snapshot of source × wind field × CFD config; immutable history
- **Monitors** — concentration probes with CSV export
- **Detectors** — fixed sensors and SCP-based placement optimization

### Dispersion Modeling

- **Gaussian Puff Engine** with Pasquill-Gifford stability classes (A-F), Slade 1968 puff coefficients, ground reflection, mixing-height lid
- **Gaussian Plume Engine** for steady-state continuous releases
- **Unified integral model** — jet, heavy-gas and passive dispersion in one centreline integration; see the next section
- **Briggs plume rise** (buoyant and momentum-dominated)
- **Jet momentum modeling** using TNO Yellow Book correlations
- **High-pressure leak model** with choked/unchoked flow and inventory decay
- **Birch & Schefer expanded source** for sonic underexpanded jets, automatically wired into CFD source patches and timestep sizing
- **Gas mixtures** with per-component tracking
- **Transient wind profiles** with time-varying speed, direction, stability

### Unified Integral Model (`UnifiedDispersion`, code `UDMCNT`)

A steady model for continuous releases that integrates the cloud itself instead of prescribing σ(x): one set of ordinary differential equations along the cloud centreline (air mass flow, horizontal and vertical momentum fluxes, enthalpy flux, position, crosswind radius), on the formulation of the DNV Unified Dispersion Model (Witlox & Holt; Phast 8.5 public theory and validation documents).

- **One trajectory, four regimes** — momentum jet, touchdown, grounded heavy gas with gravity spreading, passive far field; similarity profiles `c = c₀·exp(−(|y|/Ry)^m)·exp(−(|z−z_c|/Rz)^n)` whose exponents follow the density excess (m: 2 → 50) and the stability (n: 2 → 1)
- **Entrainment closures** — jet (α₁ = 0.17 on a Gaussian basis), cross-wind (α₂ = 0.35) with the Morton / DNV near-field suppression and the Ooms air drag of the bent-over jet (k = 0.3, Cd = 0.39, fitted to the Pratte–Baines rise), Disselhorst near-field passive, heavy-gas top entrainment κu_T/Φ(Ri*), far-field growth from the McMullen σy (averaging-time power 0.2) and the Hosker σz with roughness; the four-criterion UDM transition to passive dispersion, blended to 2·x_tr
- **Grounded cloud** — gravity spreading (C_E = 1.15), UDM ground drag and impact forces, ground heat flux (McAdams natural / Holman forced), a convective velocity scale from the ground heating that the published UDM lacks
- **Atmosphere** — Monin–Obukhov length from class and roughness (Havens & Spicer), log profile with the Businger / Paulson ψ functions, intermediate classes ("D-E") accepted
- **Any discharge direction** — downwind, vertical or **against the wind**: the jet decelerates to a stall and is blown back over the source; the sampler combines the outgoing and returning passes
- **Sources** — jets with the Birch pseudo-source, pools and low-momentum area sources (cryogenic pool size from the vaporisation flux), passive point sources
- **Two-phase releases** — the flashed liquid is carried as aerosol; every step runs a DWSIM pressure–enthalpy flash of {component, N₂, O₂, H₂O} (humid air), so temperature, liquid fraction and density are part of the state (Desert Tortoise 4 passes). No rainout yet
- **Cost** — no grid, about one second per case, which makes it the screening tool for dense, cryogenic or high-momentum releases on open terrain where the Gaussian correlations do not apply and a CFD run is not warranted
- **Validation** — 15 field trials scored against the FLACS cohort acceptance ranges: Burro 3/7, Coyote 3/5, Falcon 1/3/4, Maplin Sands 27, Kit Fox U5-2, Desert Tortoise 4, Prairie Grass 7/11/22/29/35. **13 pass**; the failures are the strongly convective Prairie Grass 7 (no convective lofting) and the mid-field dense persistence of Maplin 27. LNG observations come from the PHMSA Model Validation Database (short-average arc maxima)
- **How to run it** — a project simulation in both desktop UIs (Simulation Editor, or the toolbar solver dropdown for the active scenario), `DisperSim3D.CLI <project> -s unified` headlessly, and `DisperSim3D.CLI --validate <bench-or-dir> -s UnifiedDispersion` against the trial set; `scripts/udm_calibrate.py` scores the trial set and takes closure overrides as `KEY=value` arguments (`ALPHA1=0.2`, `KTOP=0.5`, …, exported as `UDM_KEY`); `UDM_DUMP=<csv>` writes the centreline and the entrainment budget. The result is the centreline sampled on the scenario grid, so isosurfaces, contour planes, monitors and detectors read it like any other steady field

Details, constants and limitations: [docs/unified-dispersion-model.md](docs/unified-dispersion-model.md).

### CFD Integration (OpenFOAM v2512+)

Choose a solver per simulation:

- **rhoReactingBuoyantFoam** — compressible buoyant reactive transient, the universal CFD solver for heavy gas / fuel-air clouds (Fiates & Vianna 2016)

Automatic mesh generation with snappyHexMesh, building/obstacle refinement zones, and proper handling of v2512 `topoSetDict` syntax. Default `MaxCourantNumber = 10` and `wallDist meshWave` follow Fiates & Vianna recommendations.

### GPU Lattice Boltzmann (FluidX3D)

Embedded sibling C++ project (`FluidX3D.dll`) compiled from [Moritz Lehmann's FluidX3D](https://github.com/ProjectPhysX/FluidX3D) and invoked via P/Invoke. Runs on any OpenCL 1.2+ device. Four runners exposed as `CfdSolverType` values:

- **FluidX3DWind** (`FX3DWN`) — steady wind field via Smagorinsky-Lilly LES (typically 5–30 s for 64³–96³ on a mid-range GPU)
- **FluidX3DDispersion** (`FX3DDP`) — transient dispersion: GPU LBM wind field + CPU semi-Lagrangian tracer (`DispersionTracerEngine`) with mass-injection source model and Smagorinsky subgrid diffusivity (Cs = 0.092, Sct = 0.7). Validated against DAT632 (SF₆ wind tunnel, Mack & Spruijt 2013) — all Hanna SPMs pass
- **FluidX3DDispersionSteady** (`FX3DDS`) — same tracer driven until cell-by-cell L2 delta drops below tolerance; result is a single converged frame, playback bar hidden
- **FluidX3DFire** (`FX3DFR`) — buoyant fire plume via dual-tracer `FireTracerEngine` (smoke + temperature) with Boussinesq buoyancy `β·g·(T-T_amb)`

The dispersion tracers block the cells the decorations' meshes occupy (`DecorationSolids`): the triangles of every STL, OBJ and RVM decoration, read from its file and placed as the 3D view places it, mark the cells they cross and the cells they enclose; surfaces at ground level, such as paving, block nothing. A decoration whose mesh cannot be read (a procedural primitive) blocks its bounding box. `--obstacle-mask-selftest` checks it.

GPU selection (multi-GPU systems) and on-demand VRAM/RAM/disk sizing live under **Settings → GPU & Memory…**.

**Atmospheric Boundary Layer treatment** is enabled by default (per-solver presets via `CfdConfigurationPresets`):

- Log-law inlet (`atmBoundaryLayerInletVelocity`/`...InletK`/`...InletEpsilon`) with `Uref`/`Zref`/`z₀` from `MeteorologicalConditions`
- Rough ground via `nutkAtmRoughWallFunction(z₀)`
- HHTSL k-ε constants (`σ_ε = 1.167`, Vu 2019)
- Buoyant k-ε model with `C_ε3 = -0.33` for heavy-gas runs (Mack & Spruijt 2013)
- Configurable turbulent Schmidt number `Sc_t` (default 0.7; auto 0.15 when source gas is flagged cryogenic — Vu 2019 §5.4)
- Selectable ground thermal BC: `Adiabatic` (default), `FixedTemperature` (recommended for LNG), `FixedFlux`
- Mesh-vs-`z₀` advisory written to `LOG_atmospheric.txt` in each generated case

### Visualization

- **Isosurfaces** via Marching Cubes, colored by threshold
- **Contour planes** (XY, XZ, YZ) with Jet, Viridis, Inferno, Coolwarm color maps
- **Wind fields** as configurable animated arrows (density, color, opacity, length, thickness, animation)
- **Particle animation** of puff transport
- **Wind rose** (polar chart + 3D visual)
- **Streamlines** colored by concentration
- **Per-item visibility checkboxes** in the project tree
- **Synchronized playback bar** with seekable timeline and speed control
- **Camera presets** with batch image export

### Analysis

- **Flammable cloud volume** between LFL and UFL
- **Validation harness** — compare any solver against published benchmarks via `.dsbench` JSON files; computes Hanna SPMs (MRB, RMSE, FAC2, MG, VG) with pass/fail per metric. Available via `Dispersion → Validate against Benchmarks…` (UI) or `DisperSim3D.CLI --validate <file-or-dir>` (CI-friendly). Ships with 30+ benchmarks under `benchmarks/`; `-s UnifiedDispersion` runs any continuous-release bench through the integral model.
- **Monitor points** sampling concentration in real time
- **Gas detector optimization** via Set Covering Problem (Vianna 2019), with both greedy and exact Balas branch-and-bound solvers, Cardinal or Moore neighborhoods, and on-demand result loading from saved CFD cases
- **Dispersion Studies** — curated collections of related simulations bundled into a single object with a detection criterion (`PercentLfl`/`Ppm`/`MoleFraction`/`MassFraction`/`Temperature`/`ThermalRadiation` × threshold)
- **Detector Allocation** — greedy detector placement against a Dispersion Study, with two strategies:
  - **Max Coverage** (default) — minimise the number of detectors that touch every cloud
  - **Min Residual Risk** (Rad et al. 2017 MRR) — minimise expected unmitigated risk `Σ R_s` where `R_s = frequency_s · consequence_s · P_d`, with optional distance weighting (Rad &amp; Rashtchian 2016)
- **IOGP 434-01 leak-frequency database** — embedded 2006–2015 dataset covering all 24 process equipment types × 5 hole-size bands × 6 nominal diameters. Per-source equipment inventories sum into a forward-looking leak frequency that feeds the risk allocator
- **Detection time** scoring per detector
- **Exceedance curves** with frequency weighting
- **Dispersion thresholds** (LFL fractions, IDLH, ERPG, custom)

### Fire Modeling

- **Jet fire** model (Chamberlain) with Brzustowski tilt
- **Pool fire** flame length (Thomas), vertical flame axis
- **Solid-flame thermal radiation** — the flame is a tilted cylinder radiating at a
  surface emissive power, with a numerically integrated view factor and Pietersen
  atmospheric transmissivity: `I = τ · SEP · F`. SEP comes from the energy balance,
  capped by Mudan's soot correlation for pools and 350 kW/m² for jets, and both the
  flame diameter and the SEP can be overridden per source
- **Point-source radiation** kept as the fast preview model, selectable per fire source
- **Obstacle shading** — plant geometry casts radiation shadows: a flame panel a wall
  hides contributes nothing, and a cell inside solid geometry is not exposed at all
- Receiver orientation per scenario: worst case, horizontal, or vertical
- 3D isosurfaces and contour planes of kW/m², plus radiation as a monitor and
  detector quantity
- **Flash fire** — ignite a dispersion result at a point and an instant. Only the
  cloud connected to the ignition point burns, obstacles block the flood fill, and
  the envelope is drawn at half the LFL (turbulent-fluctuation allowance). Outputs
  the hazard envelope, the flame arrival time per cell, the burnt volume and mass,
  and the burn duration that a thermal-dose calculation needs
- **Thermal dose and harm probability** — `V = t·I^(4/3)` with the Eisenberg
  fatality probit and the TNO burn probits, rendered as isosurfaces of dose or of
  fatality probability. Exposure is the scenario's escape time for jet and pool
  fires, and the flame arrival time for a flash fire
- **Fire studies** — a separate study object that ranks fire scenarios by
  frequency × consequence. Consequence is the footprint above the study's harm
  threshold (1% lethality by default); frequency for a flash fire chains back to
  the IOGP leak frequency of the release that made the cloud, times an ignition
  probability. A jet or pool fire placed by hand has no such chain and says so
  instead of defaulting

---

## Project Structure

The solution splits into a **cross-platform calculation engine** and a **Windows-only WPF UI library**, keeping the WinForms desktop app intact while letting the engine and CLI build on Linux/macOS.

```
DisperSim3D/                       # Calculation engine — multi-targets net10.0 + net10.0-windows
├── Geometry/                      # Portable types (no WPF dependency)
│   ├── Point3D.cs                 # Mirrors System.Windows.Media.Media3D.Point3D
│   ├── Vector3D.cs                # Mirrors System.Windows.Media.Media3D.Vector3D
│   └── Color.cs                   # Mirrors System.Windows.Media.Color (R/G/B/A + ScR/ScG/ScB/ScA)
├── Models/                        # Data classes
│   ├── Project.cs / Scene3D.cs    # Root container (legacy XML alias)
│   ├── ProjectSettings.cs         # General defaults
│   ├── GasLibraryItem.cs          # Pure gas or mixture
│   ├── ReleaseSource3D.cs         # Top-level sources
│   ├── WindFieldScenario.cs       # Wind field + visualization
│   ├── Simulation.cs              # Snapshot-based runnable
│   ├── Decoration3D.cs            # Imported geometry (Model3D field typed object — UI casts to Model3DGroup)
│   ├── CfdSolverType.cs           # Solver enum (Gaussian, unified integral, OpenFOAM, FluidX3D)
│   ├── CfdConfiguration.cs        # OpenFOAM settings
│   ├── DispersionStudy.cs         # Curated simulation collection + ScenarioRisk weights
│   ├── DetectorAllocation.cs      # Placement config + results (coverage + risk reduction)
│   ├── IogpEquipmentType.cs       # IOGP 434-01 24-type enum + hole-size bands
│   ├── EquipmentInventoryItem.cs  # One inventory row on a release source
│   ├── GasProperties.cs           # LFL, UFL, IDLH, ERPG
│   └── ...
├── Core/                          # Engines (all portable)
│   ├── GaussianPuffEngine.cs
│   ├── GaussianPlumeEngine.cs
│   ├── UnifiedDispersionEngine.cs # UDMCNT integral model: jet / heavy gas / passive, no grid
│   ├── AerosolCloudThermo.cs      # Two-phase cloud thermodynamics (DWSIM PH flash with humid air)
│   ├── JetTransitionModel.cs      # Free-jet momentum length, σ₀ and Briggs rise for the Gaussian jet mode
│   ├── HighPressureLeakModel.cs   # incl. Birch & Schefer expanded source
│   ├── FlammableCloudCalculator.cs
│   ├── DetectorOptimizer.cs       # Vianna 2019 SCP (exact + greedy)
│   ├── SetCoveringSolver.cs       # Greedy + Balas exact
│   ├── DispersionStudyEngine.cs   # Cloud snapshots, bbox-culled radius test
│   ├── DetectorAllocator.cs       # Greedy max-coverage + min-residual-risk dispatcher
│   ├── RiskWeightHelper.cs        # Auto frequency/consequence resolution
│   ├── IogpFrequencyTable.cs      # IOGP 434-01 2006-2015 leak-frequency database
│   ├── OpenFoamCaseGenerator.cs
│   ├── OpenFoamRunner.cs
│   ├── OpenFoamResultReader.cs
│   ├── FluidX3DBridge.cs          # P/Invoke surface over FluidX3D.dll
│   ├── FluidX3DUnits.cs           # SI <-> lattice unit conversion
│   ├── FluidX3DObstacleVoxelizer.cs # Background-safe GPU voxelization (engine half)
│   ├── FluidX3DWindFieldRunner.cs # FX3DWN
│   ├── FluidX3DRunner.cs          # FX3DDP transient dispersion
│   ├── FluidX3DSteadyDispersionRunner.cs # FX3DDS convergence-driven
│   ├── FluidX3DFireRunner.cs      # FX3DFR buoyant fire plume
│   ├── DispersionTracerEngine.cs  # CPU semi-Lagrangian tracer over LBM velocity
│   ├── FireTracerEngine.cs        # Dual tracer (smoke + T) with Boussinesq
│   ├── MemoryEstimator.cs         # VRAM/RAM/disk per solver × grid
│   ├── AppSettings.cs             # PreferredComputeDeviceId etc.
│   ├── LegacyProjectMigrator.cs
│   ├── HeadlessRunner.cs          # CLI dispatcher (used by DisperSim3D.CLI)
│   └── ...
├── Validation/                    # Hanna SPM benchmark harness + GeometrySelfTest
└── DisperSim3D.csproj             # <TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks>

DisperSim3D.UI.Wpf/                # WPF + WinForms UI library — net10.0-windows only
├── Geometry/
│   └── WpfInterop.cs              # .ToWpf() / .ToPortable() bridges (Point3D, Vector3D)
├── Models/
│   ├── Decoration3D.Wpf.cs        # ApplyClip / GetWorldTransform / UpdateBoundingBox extensions
│   └── BoundingBoxWpfExtensions.cs # BoundingBox.Transform(Transform3D)
├── Core/
│   ├── DispersionRenderer.cs      # Builds Model3DGroup isosurfaces / contour planes
│   ├── ViewRenderer.cs            # View → Model3DGroup pipeline
│   ├── EnvironmentRenderer.cs     # Sky / sun / ground lighting
│   ├── WindFieldVisual.cs / WindFieldStreamlineVisual.cs
│   ├── FireRenderer.cs / WindRoseRenderer.cs / StudyAllocationRenderer.cs
│   ├── MarchingCubes.cs           # Returns MeshGeometry3D (UI-only)
│   ├── MeshClipper.cs             # WPF mesh clipping for decorations
│   ├── ModelLoader.cs             # HelixToolkit importer (.obj/.stl/.3ds) + engine RVM
│   ├── MaterialHelper.cs / DecorationTextureRenderer.cs
│   ├── SimulationManager.cs       # Job queue + WPF-typed SteadyStateResultData
│   ├── SimulationRunner.cs        # Bridges Simulation snapshots to the Scene3DEditorControl
│   ├── FluidX3DObstacleVoxelizer.Wpf.cs # Walks Model3DGroup trees (UI-thread)
│   ├── WpfColorEditor.cs / FormExtensions.cs / Point3DStringConverter
│   └── ...
├── Controls/
│   ├── Scene3DEditorPanel.cs      # WinForms shell with docked layout
│   ├── Scene3DEditorControl.cs    # WPF HelixViewport3D host
│   ├── ProjectTreeWpfPanel.cs     # WPF TreeView via ElementHost
│   ├── PropertyGridWpfPanel.cs    # WinForms PropertyGrid wrapper
│   ├── PlaybackBar.cs             # Sync'd playback control
│   └── ...
├── Dialogs/
│   ├── DispersionSourceDialog.cs
│   ├── MeteorologicalDialog.cs
│   ├── CfdSettingsDialog.cs
│   ├── DetectorOptimizationDialog.cs       # classic SCP
│   ├── DispersionStudyDialog.cs            # study editor
│   ├── DetectorAllocationDialog.cs         # max-coverage + risk-reduction allocator
│   ├── EquipmentInventoryDialog.cs         # IOGP equipment inventory editor per source
│   ├── GpuPerformanceSettingsDialog.cs     # GPU picker + Memory Estimator
│   └── ...
└── PropertyAdapters/               # HandyControl property-grid bridges

FluidX3D/                          # Sibling C++ project — cross-platform native LBM
├── src/disp_bridge.{h,cpp}        # C-ABI bridge exposed to C# (Windows + POSIX)
├── src/defines.hpp                # Feature flags (D3Q19 FP32, VOLUME_FORCE, SUBGRID, …)
├── make-disp-bridge.sh            # Linux/macOS: builds libFluidX3D.{so,dylib}
├── FluidX3D.vcxproj               # Windows: builds FluidX3D.dll
└── …                              # Upstream FluidX3D from ProjectPhysX

DisperSim3D.CLI/                   # Headless batch runner — net10.0 (cross-platform)
                                   # --list / --simulation / --allocation / --validate /
                                   # --list-gpus / --iogp-selftest / --geometry-selftest /
                                   # --list-iogp / --memory-estimate
DisperSim3D.UI.Avalonia/           # Cross-platform Avalonia 11 UI — net10.0; ships in the .deb and the .dmg
                                   # Project editor: tree + property editor + OpenGL 3.3 viewport
                                   # (GlViewport), ~30 dialogs. Same engine binary as the WinForms App.
DisperSim3D.App/                   # WinForms host that embeds the editor panel — net10.0-windows
                                   # References both DisperSim3D and DisperSim3D.UI.Wpf
docs/                              # GitHub Pages site (Jekyll + Just the Docs)
benchmarks/                        # 31 .dsbench field trials and self-tests for --validate
scripts/udm_calibrate.py           # Scores the integral model on the trial set, closure overrides via UDM_* env vars
lib/DWSIMCore/                     # Bundled DWSIMCore.Foundation (thermodynamics for the two-phase source and the integral model)
DisperSim3D.sln
```

---

## Building

```powershell
# Full Windows build — engine, WPF UI library, WinForms app, headless CLI
dotnet build DisperSim3D.sln
```

To build just the cross-platform pieces (engine + CLI) on Linux/macOS or Windows:

```bash
dotnet build DisperSim3D/DisperSim3D.csproj      # multi-targets net10.0 + net10.0-windows
dotnet build DisperSim3D.CLI/DisperSim3D.CLI.csproj   # plain net10.0
```

For the complete Linux / WSL2 recipe including the FluidX3D native build, OpenCL
ICD setup and the Avalonia UI — with the actual validation screenshots
from Ubuntu 24.04 / WSL2 — see [docs/cross-platform.md](docs/cross-platform.md).

Both succeed without the Windows desktop SDK. The CLI binary lands in
`DisperSim3D.CLI/bin/Release/net10.0/`. On Linux, run the self-tests with:

```bash
dotnet DisperSim3D.CLI.dll --geometry-selftest   # 19/19 portable Point3D/Vector3D checks
dotnet DisperSim3D.CLI.dll --iogp-selftest       # 27/27 IOGP 434-01 frequency checks
```

### Installers

Ready-to-install packages are built by the [`Installers`](.github/workflows/installers.yml)
workflow and attached to every `v*` release: a Windows setup executable, a
Debian/Ubuntu `.deb` and a macOS `.dmg`. All three bundle a self-contained
.NET 10 runtime and the native FluidX3D bridge, so there is no prerequisite to
install first. The Windows setup installs **both UIs** — the WinForms shell
under the product name and the Avalonia UI in `avalonia\`, each with its own
Start menu entry and optional desktop icon — plus the headless CLI in `cli\`;
the `.deb` and the `.dmg` carry the Avalonia UI and the CLI.

To build one locally, see [packaging/README.md](packaging/README.md):

```bash
packaging/linux/build-deb.sh --version 1.0.0
```

### Requirements

| Component | Notes |
|---|---|
| **.NET 10 SDK** | Required for every target. |
| **Visual Studio 2022 17.14+** _or_ `dotnet` CLI | Either works. |
| **OpenCL 1.2+ device** | Required for FluidX3D solvers (NVIDIA, AMD, Intel). |
| **OpenFOAM v2512+** _(optional)_ | Required only for the CFD solver family. WSL2/Docker/native Windows builds supported. |
| **DWSIMCore.Foundation** _(bundled in `lib/DWSIMCore`)_ | Thermodynamics for the two-phase source and the aerosol path of the integral model; rebuilt with `scripts/bundle-dwsimcore.ps1`. |
| **Windows 10/11** | Only required to run **DisperSim3D.App** (WinForms shell + WPF viewport). The engine and headless CLI run on Linux/macOS. |

### Cross-platform build recipe (Linux / WSL2)

End-to-end recipe that's been validated on Ubuntu 24.04 (WSL2 on Windows 11):

```bash
# 1) Engine + CLI (~30 seconds)
dotnet build DisperSim3D/DisperSim3D.csproj -c Release
dotnet build DisperSim3D.CLI/DisperSim3D.CLI.csproj -c Release

# 2) FluidX3D native library (~1 minute, requires g++ + make)
sudo apt install -y build-essential pocl-opencl-icd ocl-icd-libopencl1
cd FluidX3D && ./make-disp-bridge.sh --copy && cd ..

# 3) Self-tests (must all exit 0)
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --geometry-selftest   # 19/19 PASS
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --iogp-selftest       # 27/27 PASS
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --list-gpus           # JSON device list

# 4) The Avalonia UI — opens via WSLg on the Windows desktop
dotnet build DisperSim3D.UI.Avalonia/DisperSim3D.UI.Avalonia.csproj -c Release
dotnet DisperSim3D.UI.Avalonia/bin/Release/net10.0/DisperSim3D.UI.Avalonia.dll
```

The Avalonia window is the project editor that the `.deb` and the `.dmg` ship:

- **Project** — new / open / save / save as over the same `.dsproj` and legacy `.xml` the Windows shell reads and writes (`SceneFileLoader` / `SceneFileSaver` / `ProjectBundle`), with the section tree and the property editor
- **3D viewport** — `GlViewport`, an OpenGL 3.3 renderer (OpenGL ES 3.0 via ANGLE) on Avalonia's `OpenGlControlBase`: ground grid, sky, imported `.stl` / `.obj` / `.rvm` geometry, wind lines, `PortableMarchingCubes` isosurfaces, contour planes, flash-fire envelopes and playback of a result's time steps
- **Dialogs** — sources and high-pressure leaks, gases and DWSIM mixtures, fire sources and ignitions, meteorology, transient wind / ESD, wind rose, scenarios, simulations and the simulation manager, views, thresholds, dispersion and fire studies, exceedance curves, monitors, detector optimisation and allocation, CFD and GPU settings, batch image export, the benchmark harness and diagnostics

What stays on Windows is the HelixToolkit viewport, the DockPanelSuite docking layout and the `.3ds` importer; everything else is the same engine binary behind a portable front end. See [docs/solvers-fluidx3d.md](docs/solvers-fluidx3d.md#building-fluidx3d-on-linux--macos) for the FluidX3D native build details and OpenCL ICD options (PoCL for universal CPU, NVIDIA/AMD/Intel for GPU performance).

---

## Quick Start

```csharp
using DisperSim3D.Controls;     // Scene3DEditorPanel — lives in DisperSim3D.UI.Wpf
using DisperSim3D.Models;
using DisperSim3D.Geometry;     // portable Point3D / Vector3D / Color

var editor = new Scene3DEditorPanel { Dock = DockStyle.Fill };
panel.Controls.Add(editor);

var project = editor.Project;

// Add a gas to the library
var methane = GasLibraryItem.FromGasProperties(GasProperties.Methane());
project.GasLibrary.Add(methane);

// Add a top-level source referencing the gas
project.Sources.Add(new ReleaseSource3D
{
    Name = "Flange Leak",
    Position = new Point3D(0, 0, 2),
    ReleaseRateKgPerS = 0.5,
    ReleaseDurationS = 120,
    GasRefId = methane.Id
});

// Add a wind field
project.WindFieldScenarios.Add(new WindFieldScenario
{
    Name = "5 m/s SW",
    Meteo = { WindSpeed = 5, WindDirectionDeg = 225, PasquillStabilityClass = 'D' }
});

// Refresh the tree to surface them in the UI
editor.RefreshProjectTree();
```

> `Point3D` and `Vector3D` in `DisperSim3D.Geometry` mirror the WPF API one-for-one
> (same constructors, same `X/Y/Z` accessors, same operators, `CrossProduct`,
> `DotProduct`, `Normalize`, `Negate`, etc.). On `net10.0-windows` they expose
> implicit conversions to and from `System.Windows.Media.Media3D` so existing
> renderer code keeps working without `.ToWpf()` calls everywhere.

---

## Headless Quick Start

```bash
# Score a field trial with the integral model (about a second)
DisperSim3D.CLI --validate benchmarks/burro3.dsbench -s UnifiedDispersion

# Run a project's active scenario through the integral model
DisperSim3D.CLI project.dsproj -s unified

# The whole continuous-release set, with a closure override
python scripts/udm_calibrate.py ALPHA1=0.2          # KEY=value becomes the UDM_KEY environment override

# Same bench through the CFD solver on WSL2
DisperSim3D.CLI --validate benchmarks/burro3.dsbench --env wsl --openfoam-path /usr/lib/openfoam/openfoam2412 --nprocs 16
```

A `.dsbench` describes the source (`elevationDeg`, `releaseAzimuthDeg` for a release against the wind, `jetTransition`, optional `twoPhase` flash), the meteorology (`stability`, also "D-E"; `averagingTimeS`; `relativeHumidity`), the domain, the solver and the observed sensor values; see [docs/validation.md](docs/validation.md).

---

## Extenders

Menu commands can be added to both desktop UIs without rebuilding them, on the
model of DWSIM's extenders. At start-up each UI loads every DLL whose name
contains `Extender` from these folders, directly or one folder down:

- `extenders\` beside the executable, for what ships with an install
- `%APPDATA%\DisperSim3D\extenders\` (`~/.config/DisperSim3D/extenders/` elsewhere), for what the user adds
- any folder listed in the `DISPERSIM3D_EXTENDERS` environment variable

An extender implements the contract in `DisperSim3D.Extensibility`: a public
`IExtenderCollection` (ID, display text, menu `Category`) holding `IExtender`
commands (display text, optional PNG icon, `InsertAtPosition`, `RunAsync`).
Unlike DWSIM's, a command never receives the host's window: it talks to the host
through `IExtenderHost` — open and save dialogs, open a project, show a message,
set the status text — which the WinForms shell and the Avalonia UI each
implement, so one DLL runs in both.

Each extender loads in a load context of its own and resolves its dependencies,
native libraries included, from its own folder and `.deps.json` (build it with
`<EnableDynamicLoading>true</EnableDynamicLoading>`). Whatever the host already
ships — `DisperSim3D.dll` above all — is shared rather than loaded twice, so an
extender's `Scene3D` is the host's. Reference the engine with `Private="false"`
and `PrivateAssets="all"` to keep it out of the extender's folder.

`DisperSim3D.CLI --list-extenders` loads them exactly as the UIs do and prints
every command and every load decision, without opening a window.

A collection that also implements `IExtenderStartup` is called once when the
main window is up, for extenders that work in the background. Newer host
features come through `IExtenderHost2` (save the project, read the status bar,
capture the 3D view as a PNG, run code on the UI thread); check for it with
`host is IExtenderHost2`.

A collection that also implements `ISolverExtenderCollection` adds dispersion
solvers. Each `ISolverExtender` (ID, six-letter code, display name, CLI aliases,
`Run`) is listed after the built-in solvers in the Simulation Editor of both UIs,
is saved as `SolverType="Extender"` plus its `ExtenderSolverId`, and runs through
the same paths as the built-in ones: the simulation managers, `DisperSim3D.CLI
<project> -s <alias>` and `--validate <bench> -s <alias>`. `Run` receives the
scenario, the CFD settings and the obstacle boxes, and returns its snapshots as
an `OpenFoamResult` on the scenario grid; it may also return a point evaluator,
which the validation harness then samples instead of the grid.

### Automation server

The installer puts one extender in `extenders\Automation\`: a small HTTP server
on `127.0.0.1` that lets a script, a test or an AI agent drive either desktop UI.
It is off by default. Start it from **File → Automation Server: Start**, or at
launch with environment variables:

| Variable | Meaning |
|---|---|
| `DISPERSIM3D_AUTOMATION=1` | start the server when the main window opens |
| `DISPERSIM3D_AUTOMATION_PORT` | port, `5013` by default; `0` takes any free one |
| `DISPERSIM3D_AUTOMATION_TOKEN` | when set, every request must send it in `X-Automation-Token` |

| Endpoint | |
|---|---|
| `GET /status` | host, version, open project, status bar |
| `GET /scene` | the open project: name, counts, decorations, sources, detectors |
| `GET /commands` | the extender menu commands |
| `POST /commands/run` | `{"id": "...", "answers": ["C:\\file.ifx"]}` — runs a command; its file dialogs take the answers in order, and its messages come back in the response instead of being shown |
| `POST /project/open` | `{"path": "..."}` |
| `POST /project/save` | `{"path": "..."}`, or no body to save in place |
| `GET /viewport.png?width=&height=` | the 3D view as a PNG (the Avalonia UI captures at the on-screen size) |
| `POST /viewport/save` | `{"path": "...", "width": 0, "height": 0}` |
| `GET /simulations` | the project's simulations, each with its latest run |
| `POST /simulations` | `{"source": "Release 01", "solver": "RhoReactingBuoyantFoam", "windField": "...", "durationS": 300, "timeStepS": 0.5, "domainSizeM": 200, "gridResolution": 40, "snapshotCount": 20}` — source and solver required (a built-in solver, or an extender solver by its ID or code), the rest default as in Add Simulation |
| `POST /simulations/run` | `{"id": "..."}` — queues it in the simulation manager, as its Run command does |
| `GET /simulations/status?id=` | state, progress and last log line of its latest run |
| `POST /simulations/cancel` | `{"id": "..."}` — cancels its latest run |
| `POST /simulations/show` | `{"id": "...", "timeS": 30}` — loads a completed result into the 3D view, paused at the snapshot at or before `timeS` (the last snapshot when omitted); capture it with `/viewport.png` |
| `GET /log?after=N` | what the server did |

```bash
curl http://127.0.0.1:5013/status
curl -X POST http://127.0.0.1:5013/project/open -d "{\"path\": \"C:\\\\Projects\\\\plant.dsproj\"}"
curl -o view.png "http://127.0.0.1:5013/viewport.png?width=1600&height=900"
```

The server listens on the loopback interface only and turns away requests a web
page could make (any request with an `Origin` header, or for a host name other
than `127.0.0.1`/`localhost`). `DisperSim3D.CLI --automation-selftest` starts it
against a host with no window and checks every endpoint over HTTP.

## XML File Format

Projects are saved as XML with root `<Project Version="2">`. Legacy `<Scene3D>` files are auto-migrated on load: inline gases are hoisted into `GasLibrary`, sources promoted to top level, and old scenarios converted into `Simulation` snapshots.

```xml
<Project Version="2">
  <GeneralSettings>...</GeneralSettings>
  <GasLibrary>
    <Gas Kind="Pure" Id="..." Name="Methane">...</Gas>
  </GasLibrary>
  <Sources>...</Sources>
  <WindFieldScenarios>...</WindFieldScenarios>
  <Simulations>
    <Simulation Status="Completed">
      <SnapshotSource>...</SnapshotSource>
      <SnapshotGas>...</SnapshotGas>
      <SnapshotMeteo>...</SnapshotMeteo>
      <Result CasePath="..." MaxC="..." />
    </Simulation>
  </Simulations>
  ...
</Project>
```

See [TECHNICAL_DOCUMENTATION.md](TECHNICAL_DOCUMENTATION.md) for the full schema.

---

## References

- Fiates, J. & Vianna, S. S. V. (2016). *Numerical modelling of gas dispersion using OpenFOAM.* Process Safety and Environmental Protection.
- Vianna, S. S. V. et al. (2019). *Optimal allocation of gas detectors as a Set Covering Problem.* Computers & Chemical Engineering.
- Birch, A. D., Hughes, D. J., & Swaffield, F. (1987). *Velocity decay of high pressure jets.* Combustion Science and Technology.
- Mack, A. & Spruijt, M. P. N. (2013). *Validation of OpenFoam for heavy gas dispersion applications.* Journal of Hazardous Materials.
- Tran, L. V. (2019). *On numerical modelling of atmospheric gas dispersion using CFD approach.* PhD thesis, Nanyang Technological University.
- Schalau, S., Habib, A. & Michel, S. (2021). *Atmospheric Wind Field Modelling with OpenFOAM for Near-Ground Gas Dispersion.* Atmosphere, 12(8), 933.
- Lehmann, M. (2022). *Esoteric Pull and Esoteric Push: Two simple in-place streaming schemes for the lattice Boltzmann method on GPUs.* Computation. — underpins the [FluidX3D](https://github.com/ProjectPhysX/FluidX3D) solver used by the GPU runners.
- Rad, A., Rashtchian, D. & Badri, N. (2017). *A risk-based methodology for optimum placement of flammable gas detectors.* Process Safety and Environmental Protection, 105, 175–183. — the Maximum Risk Reduction (MRR) greedy used by the risk-reduction allocator.
- Rad, A. & Rashtchian, D. (2016). *A new approach for optimal placement of gas detectors.* Chemical Engineering Transactions, 53, 145–150. — the optional distance-weighted refinement.
- **IOGP Report 434-01** (2019, rev 1.1 May 2021). *Risk Assessment Data Directory — Process Release Frequencies.* International Association of Oil & Gas Producers. — the embedded leak-frequency database (24 equipment types, 2006–2015 dataset).
- TNO Yellow Book — *Methods for the Calculation of Physical Effects.*

Unified integral model:

- Witlox, H. W. M. & Holt, A. (1999). *A unified model for jet, heavy and passive dispersion including droplet rainout and re-evaporation.* AIChE/CCPS International Conference and Workshop on Modeling the Consequences of Accidental Releases of Hazardous Materials, San Francisco. — the UDM formulation.
- DNV (2024). *Phast 8.5 — Unified Dispersion Model (UDM): Theory; Validation; Verification.* Public technical documentation. — profiles, entrainment closures, transition criteria and the published trial set the implementation follows.
- Witlox, H. W. M., Fernandez, M., Harper, M., Oke, A., Stene, J. & Xu, Y. (2018). *Verification and validation of Phast consequence models for accidental releases of toxic or flammable chemicals to the atmosphere.* Journal of Loss Prevention in the Process Industries, 55, 457–470.
- Morton, B. R., Taylor, G. I. & Turner, J. S. (1956). *Turbulent gravitational convection from maintained and instantaneous sources.* Proceedings of the Royal Society A, 234, 1–23. — entrainment hypothesis and near-field suppression length.
- Ooms, G. (1972). *A new method for the calculation of the plume path of gases emitted by a stack.* Atmospheric Environment, 6, 899–909. — air drag on the bent-over jet.
- Pratte, B. D. & Baines, W. D. (1967). *Profiles of the round turbulent jet in a cross flow.* Journal of the Hydraulics Division, ASCE, 93(HY6), 53–64. — rise of the vertical jet in cross-wind, used to fit the suppression constant.
- Chen, C. J. & Rodi, W. (1980). *Vertical Turbulent Buoyant Jets: A Review of Experimental Data.* Pergamon. — free-jet decay and entrainment basis.
- Birch, A. D., Brown, D. R., Dodson, M. G. & Swaffield, F. (1984). *The structure and concentration decay of high pressure jets of natural gas.* Combustion Science and Technology, 36, 249–261. — pseudo-source of under-expanded jets.
- Disselhorst, J. H. M. (1992). *The incorporation of atmospheric turbulence in the HGSYSTEM dispersion models.* Shell Research, Thornton. — near-field passive entrainment.
- McMullen, R. W. (1975). *The change of concentration standard deviations with distance.* Journal of the Air Pollution Control Association, 25(10), 1057–1058. — far-field σy and the averaging-time correction.
- Hosker, R. P. (1974). *Estimates of dry deposition and plume depletion over forests and grassland.* Physical Behaviour of Radioactive Contaminants in the Atmosphere, IAEA-SM-181/19, Vienna. — σz with the surface-roughness function.
- Havens, J. & Spicer, T. (1985). *Development of an atmospheric dispersion model for heavier-than-air gas mixtures (DEGADIS).* US Coast Guard report CG-D-22-85. — Monin–Obukhov length from stability class and roughness.
- Businger, J. A., Wyngaard, J. C., Izumi, Y. & Bradley, E. F. (1971). *Flux-profile relationships in the atmospheric surface layer.* Journal of the Atmospheric Sciences, 28, 181–189; Paulson, C. A. (1970). *The mathematical representation of wind speed and temperature profiles in the unstable atmospheric surface layer.* Journal of Applied Meteorology, 9, 857–861. — stability functions of the wind profile.
- Britter, R. E. & McQuaid, J. (1988). *Workbook on the Dispersion of Dense Gases.* HSE Contract Research Report 17/88. — heavy-gas spreading and Richardson-number regimes.
- McAdams, W. H. (1954). *Heat Transmission*, 3rd ed., McGraw-Hill; Holman, J. P. (2010). *Heat Transfer*, 10th ed., McGraw-Hill. — ground-to-cloud heat transfer correlations.
- Hanna, S. R., Hansen, O. R. & Dharmavaram, S. (2004). *FLACS CFD air quality model performance evaluation with Kit Fox, MUST, Prairie Grass, and EMU observations.* Atmospheric Environment, 38, 4675–4687; Hansen, O. R., Gavelli, F., Ichard, M. & Davis, S. G. (2010). *Validation of FLACS against experimental data sets from the model evaluation database for LNG vapor dispersion.* Journal of Loss Prevention in the Process Industries, 23, 857–877. — the cohort statistics behind the acceptance ranges.
- PHMSA (2024). *Model Validation Database for Flammable Dispersion*, Rev. B, Pipeline and Hazardous Materials Safety Administration. — short-average observations of Burro, Coyote and Maplin Sands used by the integral-model benches.
- Chang, J. C. & Hanna, S. R. (2004). *Air quality model performance evaluation.* Meteorology and Atmospheric Physics, 87, 167–196. — the statistical performance measures.

---

## Supporting the Project

If you find DisperSim 3D useful, consider making a donation to help fund continued development:

[![Donate](https://img.shields.io/badge/Donate-Stripe-635bff?style=for-the-badge&logo=stripe&logoColor=white)](https://donate.stripe.com/fZu14na8v9AG0PB2iJbMQ01)

---

## License

This project is licensed under the **GNU General Public License v3.0** — see the [LICENSE](LICENSE) file for details.

Copyright (C) 2026 Daniel Wagner Oliveira de Medeiros
