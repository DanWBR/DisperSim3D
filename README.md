# DisperSim 3D

An interactive 3D gas dispersion analysis tool for process safety. Build a project, define release sources and gases, run Gaussian, CFD or GPU LBM dispersion simulations, visualize concentration fields, and optimize gas detector placement.

Built on **.NET 10** with **HelixToolkit.WPF** for 3D rendering on Windows, **Avalonia 11** as the cross-platform UI proof of concept, **OpenFOAM** for CFD-based wind fields and reactive transport, and **FluidX3D** (GPU lattice Boltzmann) for fast wind, dispersion and fire-plume runs.

**Cross-platform status — validated end-to-end on Ubuntu 24.04 / WSL2:**

| Component | TFM / build | Windows | Linux | macOS |
|---|---|:-:|:-:|:-:|
| `DisperSim3D` (engine) | `net10.0` + `net10.0-windows` | ✅ | ✅ | ✅ |
| `DisperSim3D.CLI` (headless) | `net10.0` | ✅ | ✅ | ✅ |
| `DisperSim3D.UI.Avalonia` (cross-plat smoke) | `net10.0` + Avalonia 11 | ✅ | ✅ (WSLg) | ✅ |
| `DisperSim3D.UI.Wpf` + `DisperSim3D.App` (WinForms shell) | `net10.0-windows` | ✅ | — | — |
| `FluidX3D` native (`FluidX3D.dll` / `libFluidX3D.so` / `.dylib`) | C++ / OpenCL | MSVC | `g++` via `make-disp-bridge.sh` | `g++` via `make-disp-bridge.sh` |

The solution splits cleanly into a **portable engine** (`DisperSim3D.csproj` multi-targeting `net10.0;net10.0-windows`) and a **Windows-only UI library** (`DisperSim3D.UI.Wpf.csproj`) so the engine compiles on Linux/macOS without dragging in the Windows desktop SDK. The Avalonia smoke window (`DisperSim3D.UI.Avalonia.csproj`) proves the engine works behind a non-WPF UI on the same source.

Full project documentation is published on **[GitHub Pages](https://danwbr.github.io/DisperSim3D/)**. For a detailed description of physical models, file format, OpenFOAM case structure, and validation, see [TECHNICAL_DOCUMENTATION.md](TECHNICAL_DOCUMENTATION.md).

---

## Features

### Project-Centric Workflow

DisperSim 3D organizes work around a **Project** with discrete sections shown in a left-side dockable tree:

- **General Settings** — defaults for meteo, domain size, grid, CFD config
- **Gases** — central library of pure gases and mixtures, referenced by sources
- **Geometry** — imported obstacles and decorations (`.obj`, `.stl`, `.glb`, `.3ds`)
- **Sources** — top-level release sources with gas-library references
- **Wind Fields** — meteo + obstacle scenarios resolved to 3D velocity fields (uniform or CFD)
- **Simulations** — frozen snapshot of source × wind field × CFD config; immutable history
- **Monitors** — concentration probes with CSV export
- **Detectors** — fixed sensors and SCP-based placement optimization

### Dispersion Modeling

- **Gaussian Puff Engine** with Pasquill-Gifford stability classes (A-F), Slade 1968 puff coefficients, ground reflection, mixing-height lid
- **Gaussian Plume Engine** for steady-state continuous releases
- **Briggs plume rise** (buoyant and momentum-dominated)
- **Jet momentum modeling** using TNO Yellow Book correlations
- **High-pressure leak model** with choked/unchoked flow and inventory decay
- **Birch & Schefer expanded source** for sonic underexpanded jets, automatically wired into CFD source patches and timestep sizing
- **Gas mixtures** with per-component tracking
- **Transient wind profiles** with time-varying speed, direction, stability

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
- **Validation harness** — compare any solver against published benchmarks via `.dsbench` JSON files; computes Hanna SPMs (MRB, RMSE, FAC2, MG, VG) with pass/fail per metric. Available via `Dispersion → Validate against Benchmarks…` (UI) or `DisperSim3D.CLI --validate <file-or-dir>` (CI-friendly). Ships with 5 starter benchmarks under `benchmarks/`.
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
- Point-source thermal radiation contours

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
│   ├── CfdSolverType.cs           # Solver enum (Gaussian, OpenFOAM, FluidX3D)
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
│   ├── ModelLoader.cs             # HelixToolkit importer (.obj/.stl/.glb/.3ds)
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
DisperSim3D.UI.Avalonia/           # Cross-platform Avalonia 11 smoke window — net10.0
                                   # 4-panel proof window: geometry, IOGP, OpenCL devices,
                                   # Gaussian plume. Same engine binary as the WinForms App.
DisperSim3D.App/                   # WinForms host that embeds the editor panel — net10.0-windows
                                   # References both DisperSim3D and DisperSim3D.UI.Wpf
docs/                              # GitHub Pages site (Jekyll + Just the Docs)
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
ICD setup and the Avalonia smoke window — with the actual validation screenshots
from Ubuntu 24.04 / WSL2 — see [docs/cross-platform.md](docs/cross-platform.md).

Both succeed without the Windows desktop SDK. The CLI binary lands in
`DisperSim3D.CLI/bin/Release/net10.0/`. On Linux, run smoke tests with:

```bash
dotnet DisperSim3D.CLI.dll --geometry-selftest   # 19/19 portable Point3D/Vector3D checks
dotnet DisperSim3D.CLI.dll --iogp-selftest       # 27/27 IOGP 434-01 frequency checks
```

### Requirements

| Component | Notes |
|---|---|
| **.NET 10 SDK** | Required for every target. |
| **Visual Studio 2022 17.14+** _or_ `dotnet` CLI | Either works. |
| **OpenCL 1.2+ device** | Required for FluidX3D solvers (NVIDIA, AMD, Intel). |
| **OpenFOAM v2512+** _(optional)_ | Required only for the CFD solver family. WSL2/Docker/native Windows builds supported. |
| **Windows 10/11** | Only required to run **DisperSim3D.App** (WinForms shell + WPF viewport). The engine and headless CLI run on Linux/macOS. |

### Cross-platform smoke recipe (Linux / WSL2)

End-to-end recipe that's been validated on Ubuntu 24.04 (WSL2 on Windows 11):

```bash
# 1) Engine + CLI (~30 seconds)
dotnet build DisperSim3D/DisperSim3D.csproj -c Release
dotnet build DisperSim3D.CLI/DisperSim3D.CLI.csproj -c Release

# 2) FluidX3D native library (~1 minute, requires g++ + make)
sudo apt install -y build-essential pocl-opencl-icd ocl-icd-libopencl1
cd FluidX3D && ./make-disp-bridge.sh --copy && cd ..

# 3) Smoke tests (must all exit 0)
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --geometry-selftest   # 19/19 PASS
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --iogp-selftest       # 27/27 PASS
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --list-gpus           # JSON device list

# 4) (Optional) Avalonia smoke window — opens via WSLg on the Windows desktop
dotnet build DisperSim3D.UI.Avalonia/DisperSim3D.UI.Avalonia.csproj -c Release
dotnet DisperSim3D.UI.Avalonia/bin/Release/net10.0/DisperSim3D.UI.Avalonia.dll
```

The Avalonia smoke window opens a 4-panel proof that exercises:

1. **Portable geometry** — 19 operator-level checks on `DisperSim3D.Geometry.Point3D` / `Vector3D`
2. **IOGP 434-01 frequency database** — 27 published-value round-trips
3. **FluidX3D OpenCL probe** — calls `[DllImport("FluidX3D")]` → resolves `libFluidX3D.so` → enumerates devices
4. **Gaussian plume end-to-end** — synthetic methane scenario, 32³ grid, `MaxC` and location reported

Same source code as the WinForms `DisperSim3D.App`, compiled for the cross-platform TFM. See [docs/solvers-fluidx3d.md](docs/solvers-fluidx3d.md#building-fluidx3d-on-linux--macos) for the FluidX3D native build details and OpenCL ICD options (PoCL for universal CPU, NVIDIA/AMD/Intel for GPU performance).

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

---

## Supporting the Project

If you find DisperSim 3D useful, consider making a donation to help fund continued development:

[![Donate](https://img.shields.io/badge/Donate-Stripe-635bff?style=for-the-badge&logo=stripe&logoColor=white)](https://donate.stripe.com/fZu14na8v9AG0PB2iJbMQ01)

---

## License

This project is licensed under the **GNU General Public License v3.0** — see the [LICENSE](LICENSE) file for details.

Copyright (C) 2026 Daniel Wagner Oliveira de Medeiros
