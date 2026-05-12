---
layout: default
title: Getting started
nav_order: 2
---

# Getting started
{: .no_toc }

1. TOC
{:toc}

## Requirements

| Component | Notes |
|---|---|
| **Windows 10/11** | WinForms shell + WPF viewport — Windows only by design |
| **.NET 10 SDK** | Build target is `net10.0-windows` |
| **Visual Studio 2022 17.14+** _or_ `dotnet` CLI | Either works |
| **GPU with OpenCL 1.2+** | Required for FluidX3D solvers (NVIDIA, AMD or Intel) |
| **OpenFOAM v2512+** _(optional)_ | Required for the CFD solver family. Native Windows build is recommended; WSL2/Docker/BlueCFD are also supported. |

The native Windows ESI installer puts OpenFOAM at
`%APPDATA%\ESI-OpenCFD\OpenFOAM\v2512\msys64\home\ofuser\OpenFOAM\OpenFOAM-v2512`.
Point the **OpenFOAM environment** dialog at that folder.

## Building from source

```powershell
git clone https://github.com/DanWBR/DisperSim3D.git
cd DisperSim3D
dotnet build DisperSim3D.sln -c Release
```

The solution contains:

| Project | Output | Purpose |
|---|---|---|
| `DisperSim3D` | `net10.0-windows` library | Models, solvers, viewport, dialogs |
| `DisperSim3D.CLI` | Console exe | Headless batch runner |
| `DisperSim3D.App` | WinForms exe | Standalone host that embeds the editor panel |
| `FluidX3D` | `FluidX3D.dll` | C++ GPU LBM bridge, auto-copied to C# output dirs |

After a successful build, run **DisperSim3D.App** (or the executable bundled in a
release artifact) and you have the full GUI.

## First simulation — 5-minute walkthrough

1. **File → New Project**. The project tree on the left now shows empty
   sections.
2. Right-click **Gases → Add Pure Gas...** → pick **Methane** (or define a
   custom gas with `LFL`, `UFL`, `MolarMass`).
3. Right-click **Sources → Add Source...** → click on the ground plane to
   place the release point → fill in the source dialog (gas, release rate,
   direction, optional HP-leak inventory).
4. Right-click **Wind Fields → Add Wind Field...** → set wind speed, direction
   and Pasquill class → **Run**. For a quick first run, pick
   **FluidX3D Wind (GPU LBM)** — finishes in seconds. The status badge in the
   tree flips to green when ready.
5. Right-click **Simulations → New Simulation...** → pick the source, the
   wind field and a solver (start with **GaussianPuff** or
   **FluidX3DDispersion** for fast first results) → OK.
6. Right-click the new simulation → **Run**. The status moves through
   `Queued → Running → Completed`.
7. Tick the simulation's checkbox in the tree — the 3D viewport plays back
   the concentration field and the bottom playback bar lets you scrub.

From here you can:

- Add **monitor points** to record concentration vs. time at fixed locations.
- Run a **Dispersion Study** that bundles many simulations and feed it to the
  **Detector Allocation** algorithm — see
  [Dispersion Studies &amp; Detector Allocation](studies-detectors).
- **Export** the case to a `.dsproj` bundle (zipped self-contained project)
  for sharing or re-running headlessly via `DisperSim3D.CLI`.

## CLI quick reference

```text
DisperSim3D.CLI project.dsproj --list                            # enumerate gases/sources/sims
DisperSim3D.CLI project.dsproj --simulation "Stack#1 5m/s SW"    # run an existing snapshot
DisperSim3D.CLI project.dsproj -s plume                          # run a solver directly
DisperSim3D.CLI --validate benchmarks/                           # run benchmark harness
```

`--validate` returns exit code 0 only when every `.dsbench` file passes its
Hanna acceptance ranges, so it is CI-friendly.
