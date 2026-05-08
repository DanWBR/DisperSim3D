# DisperSim 3D

An interactive 3D gas dispersion simulation tool. Model atmospheric releases, visualize concentration fields with isosurfaces and contour planes, evaluate gas detector placement, and integrate with OpenFOAM for CFD-based wind fields.

Built on **.NET 10** with **HelixToolkit.WPF** for 3D rendering and **DockPanelSuite** for the panel layout.

---

## Features

### Dispersion Modeling

- **Gaussian Puff Engine** with Pasquill-Gifford stability classes (A-F), Slade 1968 puff coefficients, and Briggs plume rise
- **Jet momentum modeling** using TNO Yellow Book jet-in-crossflow correlations
- **High-pressure leak model** with choked/unchoked flow calculation and inventory decay
- **Gas mixtures** with individual component tracking
- **Transient wind profiles** with time-varying speed, direction, and stability
- Built-in gas presets (Methane, H2S, Ammonia) with LFL, IDLH, and ERPG thresholds

### Visualization

- **Isosurfaces** via Marching Cubes algorithm, colored by threshold level
- **Contour planes** (XY, XZ, YZ) with configurable color maps (Jet, Viridis, Inferno, Coolwarm)
- **Particle animation** showing puff transport in real time
- **Wind rose** display (polar chart + 3D visual)
- **Streamlines** and **vector fields** colored by concentration
- **Camera presets** with batch image export for reports
- Multiple camera modes: perspective, isometric, top-down, front, side, free orbit

### Analysis

- **Monitor points** that sample concentration in real time with CSV export
- **Gas detector evaluation** with detection time and coverage scoring
- **Exceedance curves** combining multiple scenarios with frequency weighting
- **Multiple scenarios** per project with independent meteorological conditions
- **Dispersion thresholds** (LFL fractions, IDLH, ERPG, custom values)

### Fire Modeling

- **Jet fire** model using Chamberlain correlation for flame length
- Brzustowski flame tilt under crosswind
- Point-source thermal radiation contours on ground level

### CFD Integration

- **OpenFOAM** case generation (simpleFoam) for steady-state wind fields
- Automatic mesh generation with building/obstacle geometry
- Import CFD velocity fields to drive dispersion instead of uniform wind
- Disk-backed LRU cache for computed fields

### Scene & Import

- Import 3D obstacle/building geometry (`.obj`, `.stl`, `.3ds`)
- Decorations with positioning, scaling, and rotation
- Configurable work planes at multiple elevations
- Snap-to-grid with adjustable spacing

---

## Project Structure

```
DisperSim3D/
├── Models/                     # Data classes
│   ├── Scene3D.cs              # Scene3D: root container
│   ├── DispersionScenario.cs   # Scenario configuration
│   ├── ReleaseSource3D.cs      # Gas release sources
│   ├── MeteorologicalConditions.cs
│   ├── GasProperties.cs        # LFL, IDLH, ERPG presets
│   ├── FireScenario.cs         # Jet fire parameters
│   ├── GasDetector3D.cs        # Detector placement
│   ├── MonitorPoint3D.cs       # Concentration probes
│   ├── WindRoseData.cs         # Wind frequency data
│   ├── CfdConfiguration.cs     # OpenFOAM settings
│   ├── TransientWindProfile.cs # Time-varying wind
│   ├── GasComponent.cs         # Mixture components
│   └── ...
├── Core/                       # Engines and renderers
│   ├── GaussianPuffEngine.cs   # Puff dispersion solver
│   ├── PasquillGiffordCoefficients.cs
│   ├── BriggsPlumerise.cs      # Buoyant/momentum plume rise
│   ├── HighPressureLeakModel.cs
│   ├── JetFireModel.cs         # Chamberlain flame model
│   ├── DispersionRenderer.cs   # Isosurfaces, contours, particles
│   ├── MarchingCubes.cs        # Isosurface extraction
│   ├── ColorMapHelper.cs       # Jet, Viridis, Inferno palettes
│   ├── WindRoseRenderer.cs     # 3D wind rose visual
│   ├── FireRenderer.cs         # Flame cone + radiation contours
│   ├── DetectorEvaluator.cs    # Detector coverage analysis
│   ├── ExceedanceCurveCalculator.cs
│   ├── ModelLoader.cs          # OBJ/STL/3DS import
│   ├── OpenFoamCaseGenerator.cs
│   ├── OpenFoamRunner.cs
│   ├── OpenFoamResultReader.cs
│   └── ...
├── Dialogs/                    # Configuration dialogs
│   ├── DispersionSourceDialog.cs
│   ├── MeteorologicalDialog.cs
│   ├── FireSourceDialog.cs
│   ├── CfdSettingsDialog.cs
│   ├── WindRoseDialog.cs
│   └── ...
├── Controls/                   # Main UI
│   ├── FlowsheetEditor3DControl.cs   # WPF HelixViewport3D host
│   ├── FlowsheetEditorPanel.cs       # WinForms panel with menus/toolbars
│   ├── AddItemPanel.cs               # Item palette
│   └── DockPanels.cs
├── PropertyAdapters/           # PropertyGrid adapters
├── TestApp/                    # Standalone test application
└── DisperSim3D.sln
```

---

## Building

### Visual Studio 2022

1. Open `DisperSim3D.sln`
2. Build (NuGet packages restore automatically)

### Command Line

```powershell
dotnet build DisperSim3D.sln
```

### Requirements

- .NET 10 SDK
- Visual Studio 2022 17.14+ (or `dotnet` CLI)
- [OpenFOAM](https://www.openfoam.com/) v2512+ (optional, for CFD features only)

---

## Quick Start

```csharp
using DisperSim3D.Controls;
using DisperSim3D.Models;
using System.Windows.Media.Media3D;

// Host the editor in a WinForms panel
var editor = new FlowsheetEditor3DControl { Dock = DockStyle.Fill };
panel.Controls.Add(editor);

// Configure a release source
var source = new ReleaseSource3D
{
    Name = "Flange Leak",
    Position = new Point3D(0, 0, 2),
    ReleaseRateKgPerS = 0.5,
    ReleaseDurationS = 120,
    Gas = GasProperties.Methane()
};

// Set meteorological conditions
var scenario = editor.Flowsheet.ActiveScenario;
scenario.Sources.Add(source);
scenario.Meteo.WindSpeed = 3.0;
scenario.Meteo.WindDirectionDeg = 225;
scenario.Meteo.PasquillStabilityClass = 'D';

// Run the simulation
editor.StartDispersionSimulation();
```

---

## Atmospheric Dispersion Model

The core engine implements the **Gaussian Puff** model:

- Discrete puffs released at configurable intervals
- Each puff advects with the wind field and grows according to Pasquill-Gifford sigma correlations
- Concentration at any point is the superposition of all active puffs
- **Plume rise** via Briggs equations (buoyant and momentum-dominated)
- **Jet momentum** with exponential velocity decay (TNO Yellow Book)
- Wind profile power law with stability-dependent shear exponents (urban/rural)
- Ground reflection and mixing height lid
- First-order chemical decay (configurable half-life)
- Dry deposition velocity

The solver supports both **steady-state** (continuous release) and **transient** (time-varying wind, ESD scenarios) modes.

---

## OpenFOAM Integration

DisperSim 3D can generate OpenFOAM cases for computing 3D wind fields around buildings:

1. Define obstacle geometry (import `.obj`/`.stl` or use built-in shapes)
2. Configure domain size, mesh resolution, and boundary conditions
3. Generate case files (`system/`, `constant/`, `0/`)
4. Run `simpleFoam` (steady-state RANS)
5. Import the velocity field back to drive puff advection

This replaces the uniform wind assumption with a realistic flow field that accounts for building wakes, channeling, and recirculation zones.

---

## Supporting the Project

If you find DisperSim 3D useful, consider making a donation to help fund continued development:

[![Donate](https://img.shields.io/badge/Donate-Stripe-635bff?style=for-the-badge&logo=stripe&logoColor=white)](https://donate.stripe.com/fZu14na8v9AG0PB2iJbMQ01)

---

## License

This project is licensed under the **GNU General Public License v3.0** - see the [LICENSE](LICENSE) file for details.

Copyright (C) 2026 Daniel Wagner Oliveira de Medeiros
