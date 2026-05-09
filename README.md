# DisperSim 3D

An interactive 3D gas dispersion analysis tool for process safety. Build a project, define release sources and gases, run Gaussian or CFD-based dispersion simulations, visualize concentration fields, and optimize gas detector placement.

Built on **.NET 10** with **HelixToolkit.WPF** for 3D rendering, **DockPanelSuite** for the docked layout, and **OpenFOAM** for CFD-based wind fields and reactive transport.

For a detailed description of physical models, file format, OpenFOAM case structure, and validation, see [TECHNICAL_DOCUMENTATION.md](TECHNICAL_DOCUMENTATION.md).

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

- **scalarTransportFoam** — passive scalar transport on a steady wind field
- **simpleFoam** — steady-state RANS wind field generation
- **pimpleFoam** — transient incompressible
- **buoyantPimpleFoam** — buoyant transient
- **reactingFoam** — combustion / dispersion with reactions
- **rhoSimpleFoam** — compressible steady-state
- **rhoReactingBuoyantFoam** — compressible buoyant reactive transient (recommended for heavy gas / fuel-air clouds, per Fiates & Vianna 2016)

Automatic mesh generation with snappyHexMesh, building/obstacle refinement zones, and proper handling of v2512 `topoSetDict` syntax. Default `MaxCourantNumber = 10` and `wallDist meshWave` follow Fiates & Vianna recommendations.

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
- **Monitor points** sampling concentration in real time
- **Gas detector optimization** via Set Covering Problem (Vianna 2019), with both greedy and exact Balas branch-and-bound solvers, Cardinal or Moore neighborhoods, and on-demand result loading from saved CFD cases
- **Detection time** scoring per detector
- **Exceedance curves** with frequency weighting
- **Dispersion thresholds** (LFL fractions, IDLH, ERPG, custom)

### Fire Modeling

- **Jet fire** model (Chamberlain) with Brzustowski tilt
- Point-source thermal radiation contours

---

## Project Structure

```
DisperSim3D/
├── Models/                       # Data classes
│   ├── Project.cs / Scene3D.cs   # Root container (legacy XML alias)
│   ├── ProjectSettings.cs        # General defaults
│   ├── GasLibraryItem.cs         # Pure gas or mixture
│   ├── ReleaseSource3D.cs        # Top-level sources
│   ├── WindFieldScenario.cs      # Wind field + visualization
│   ├── Simulation.cs             # Snapshot-based runnable
│   ├── CfdConfiguration.cs       # OpenFOAM settings
│   ├── GasProperties.cs          # LFL, UFL, IDLH, ERPG
│   └── ...
├── Core/                         # Engines
│   ├── GaussianPuffEngine.cs
│   ├── GaussianPlumeEngine.cs
│   ├── HighPressureLeakModel.cs  # incl. Birch & Schefer expanded source
│   ├── FlammableCloudCalculator.cs
│   ├── DetectorOptimizer.cs      # Vianna 2019 SCP orchestrator
│   ├── SetCoveringSolver.cs      # Greedy + Balas exact
│   ├── OpenFoamCaseGenerator.cs
│   ├── OpenFoamRunner.cs
│   ├── OpenFoamResultReader.cs
│   ├── LegacyProjectMigrator.cs
│   └── ...
├── Dialogs/
│   ├── DispersionSourceDialog.cs
│   ├── MeteorologicalDialog.cs
│   ├── CfdSettingsDialog.cs
│   ├── DetectorOptimizationDialog.cs
│   └── ...
├── Controls/
│   ├── Scene3DEditorPanel.cs       # WinForms shell with docked layout
│   ├── Scene3DEditorControl.cs     # WPF HelixViewport3D host
│   ├── ProjectTreeWpfPanel.cs      # WPF TreeView via ElementHost
│   ├── PropertyGridWpfPanel.cs     # WinForms PropertyGrid wrapper
│   ├── PlaybackBar.cs              # Sync'd playback control
│   └── ...
├── TestApp/
└── DisperSim3D.sln
```

---

## Building

```powershell
dotnet build DisperSim3D.sln
```

### Requirements

- **.NET 10 SDK**
- **Visual Studio 2022 17.14+** (or `dotnet` CLI)
- **Windows** (WinForms shell + WPF viewport)
- **OpenFOAM v2512+** (optional, for CFD features)

---

## Quick Start

```csharp
using DisperSim3D.Controls;
using DisperSim3D.Models;
using System.Windows.Media.Media3D;

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
- TNO Yellow Book — *Methods for the Calculation of Physical Effects.*

---

## Supporting the Project

If you find DisperSim 3D useful, consider making a donation to help fund continued development:

[![Donate](https://img.shields.io/badge/Donate-Stripe-635bff?style=for-the-badge&logo=stripe&logoColor=white)](https://donate.stripe.com/fZu14na8v9AG0PB2iJbMQ01)

---

## License

This project is licensed under the **GNU General Public License v3.0** — see the [LICENSE](LICENSE) file for details.

Copyright (C) 2026 Daniel Wagner Oliveira de Medeiros
