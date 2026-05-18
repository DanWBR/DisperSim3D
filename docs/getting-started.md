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
| **.NET 10 SDK** | Required for every target. |
| **Visual Studio 2022 17.14+** _or_ `dotnet` CLI | Either works. |
| **GPU with OpenCL 1.2+** | Required for FluidX3D solvers (NVIDIA, AMD or Intel). |
| **OpenFOAM v2512+** _(optional)_ | Required for the CFD solver family. Native Windows build is recommended; WSL2/Docker/BlueCFD are also supported. |
| **Windows 10/11** | Only required for **DisperSim3D.App** (WinForms shell + WPF viewport). The calculation engine and CLI run on Linux/macOS. |

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

| Project | Output | Target framework(s) | Purpose |
|---|---|---|---|
| `DisperSim3D` | Library | **`net10.0`** + `net10.0-windows` (multi-target) | Calculation engine: models, solvers, validation harness, portable `Geometry` types. Cross-platform on the `net10.0` TFM. |
| `DisperSim3D.UI.Wpf` | Library | `net10.0-windows` | WPF + WinForms UI: viewport, dialogs, renderers, property adapters, `SimulationManager`. References `DisperSim3D`. |
| `DisperSim3D.UI.Avalonia` | Avalonia exe | **`net10.0`** | Cross-platform Avalonia 11 verification window  -  same engine, same source, runs on Windows / Linux / macOS / WSL2. |
| `DisperSim3D.CLI` | Console exe | **`net10.0`** | Headless batch runner. Cross-platform. |
| `DisperSim3D.App` | WinForms exe | `net10.0-windows` | Standalone host that embeds the editor panel. References both `DisperSim3D` and `DisperSim3D.UI.Wpf`. |
| `FluidX3D` | `FluidX3D.dll` (Windows) / `libFluidX3D.so` / `.dylib` (Unix) | Native C++ | GPU LBM bridge, auto-copied to C# output dirs. |

After a successful Windows build, run **DisperSim3D.App** (or the executable bundled in a release artifact) and you have the full GUI.

### Cross-platform verification window (Avalonia)

For Linux / macOS / WSL2 you can run **DisperSim3D.UI.Avalonia**, a small 4-panel
Avalonia 11 window that exercises the same engine code as the WinForms app but
compiles for plain `net10.0`. Each panel has a button: portable geometry
self-test, IOGP 434-01 self-test, FluidX3D OpenCL device probe, and a synthetic
Gaussian plume run.

```bash
cd DisperSim3D.UI.Avalonia
dotnet run -c Release
```

On WSL2 the window appears on your Windows desktop via WSLg. On bare-metal Linux
it opens through X11 or Wayland. On macOS it uses CoreGraphics. Same source, same
binary across all three. The 4 panels turn green left-to-right, top-to-bottom:

1. **Portable geometry self-test**  -  19/19 PASS on `DisperSim3D.Geometry.Point3D` / `Vector3D` operators (matches the WPF semantics exactly so engine code is bit-equivalent on either type).
2. **IOGP 434-01 risk frequency self-test**  -  27/27 PASS round-tripping the embedded leak-frequency database against published values.
3. **FluidX3D  -  list OpenCL devices**  -  JSON describing every OpenCL device the host exposes. Requires `libFluidX3D.so` / `.dylib` next to the .NET binary (auto-copied if the `make-disp-bridge.sh --copy` step below has run) plus an OpenCL ICD installed on the host.
4. **Engine end-to-end (Gaussian plume)**  -  synthetic methane scenario on a 32³ grid; reports `MaxC` and its location. Sanity check: `MaxC > 0` and the X-coord should be downwind (`+x` since wind comes from 270°).

The same numeric result for `MaxC` should appear on Windows when you run the equivalent code path through `DisperSim3D.App` or `DisperSim3D.CLI`  -  that's the cross-platform-arithmetic guarantee in action.

### Cross-platform build (Linux / macOS)

The engine and CLI build with stock `dotnet` on any OS that ships .NET 10  -  no Windows desktop SDK needed:

```bash
git clone https://github.com/DanWBR/DisperSim3D.git
cd DisperSim3D
dotnet build DisperSim3D/DisperSim3D.csproj -c Release     # multi-target engine
dotnet build DisperSim3D.CLI/DisperSim3D.CLI.csproj -c Release   # plain net10.0 CLI
```

Self-tests that exercise the portable geometry types and the embedded IOGP table:

```bash
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --geometry-selftest   # 19/19 PASS
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --iogp-selftest       # 27/27 PASS
```

Analytical Gaussian solvers and external OpenFOAM runs work cross-platform from day one. **FluidX3D solvers** (`FX3DWN`/`FX3DDP`/`FX3DDS`/`FX3DFR`) now also build on Linux/macOS  -  see the [Building FluidX3D on Linux / macOS](solvers-fluidx3d#building-fluidx3d-on-linux--macos) section. Short version:

```bash
# Build the native library
cd FluidX3D
./make-disp-bridge.sh --copy     # → bin/libFluidX3D.so + copy into the .NET output dir

# Install an OpenCL ICD (PoCL = CPU-based, works on any Linux/WSL2)
sudo apt install -y pocl-opencl-icd ocl-icd-libopencl1

# Verify the whole pipeline end-to-end
dotnet ../DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --list-gpus
```

Expected output: JSON describing every OpenCL device  -  at minimum your CPU under PoCL, plus your GPU if a vendor ICD (NVIDIA/AMD/Intel) is installed. Requires `g++` + `make` (apt: `build-essential make`).

## First simulation  -  5-minute walkthrough

1. **File → New Project**. The project tree on the left now shows empty
   sections.
2. Right-click **Gases → Add Pure Gas...** → pick **Methane** (or define a
   custom gas with `LFL`, `UFL`, `MolarMass`).
3. Right-click **Sources → Add Source...** → click on the ground plane to
   place the release point → fill in the source dialog (gas, release rate,
   direction, optional HP-leak inventory).
4. Right-click **Wind Fields → Add Wind Field...** → set wind speed, direction
   and Pasquill class → **Run**. For a quick first run, pick
   **FluidX3D Wind (GPU LBM)**  -  finishes in seconds. The status badge in the
   tree flips to green when ready.
5. Right-click **Simulations → New Simulation...** → pick the source, the
   wind field and a solver (start with **GaussianPuff** or
   **FluidX3DDispersion** for fast first results) → OK.
6. Right-click the new simulation → **Run**. The status moves through
   `Queued → Running → Completed`.
7. Tick the simulation's checkbox in the tree  -  the 3D viewport plays back
   the concentration field and the bottom playback bar lets you scrub.

From here you can:

- Add **monitor points** to record concentration vs. time at fixed locations.
- Run a **Dispersion Study** that bundles many simulations and feed it to the
  **Detector Allocation** algorithm  -  see
  [Dispersion Studies &amp; Detector Allocation](studies-detectors).
- **Export** the case to a `.dsproj` bundle (zipped self-contained project)
  for sharing or re-running headlessly via `DisperSim3D.CLI`.

## CLI quick reference

The headless runner `DisperSim3D.CLI` exposes every project-level operation
the app can do  -  running simulations, allocating detectors, validating
benchmarks  -  plus a handful of diagnostic modes that don't need a project
file at all.

### Run modes (project required)

```text
DisperSim3D.CLI project.dsproj --list                            # enumerate every section
DisperSim3D.CLI project.dsproj --simulation "Stack#1 5m/s SW"    # run an existing snapshot
DisperSim3D.CLI project.dsproj --simulation "Fast LBM" -s fluidx3dDispersion --gpu-device 0
DisperSim3D.CLI project.dsproj --allocation "Site A  -  risk"     # re-run a detector allocation
DisperSim3D.CLI project.dsproj -s plume                          # run a solver directly
DisperSim3D.CLI legacy.xml -s rhoReactingBuoyantFoam --env native --openfoam-path "<path>"
```

`--list` dumps every project section in a single pass: gases, sources
**with their IOGP equipment inventory + effective leak frequency**, wind
fields, simulations, dispersion studies (incl. per-scenario risk weights),
detector allocations (incl. RRF for risk-strategy runs), wind rose,
monitor points, and gas detectors.

`-s <solver>` accepts the analytical names (`plume`, `puff`), the OpenFOAM
solver (`rhoReactingBuoyantFoam`) **and the FluidX3D family** (`fluidx3dDispersion`, `fluidx3dDispersionSteady`,
`fluidx3dFire`). FluidX3D solvers run in-process via the GPU bridge  -  no
OpenFOAM environment required.

`--allocation` re-executes a saved detector allocation and prints its
results to stdout (read-only  -  the project file is not modified). Useful
for QRA reporting pipelines.

### Diagnostic modes (no project required)

```text
DisperSim3D.CLI --list-gpus                                      # enumerate OpenCL devices
DisperSim3D.CLI --iogp-selftest                                  # verify embedded IOGP 434-01 table
DisperSim3D.CLI --geometry-selftest                              # verify portable Point3D / Vector3D semantics
DisperSim3D.CLI --list-iogp FlangedJoint                         # dump one IOGP datasheet
DisperSim3D.CLI --list-iogp                                      # dump all 24 datasheets
DisperSim3D.CLI --memory-estimate FluidX3DDispersion 128         # VRAM/RAM/disk for a 128³/2 run
DisperSim3D.CLI --validate benchmarks/                           # run the benchmark harness
```

`--validate` returns exit code 0 only when every `.dsbench` file passes
its Hanna acceptance ranges, so it is CI-friendly.

`--iogp-selftest` exits 0 only when the 27 self-checks against the
published IOGP 434-01 values all pass  -  pair it with `--validate` for a
two-step CI verification that covers both the dispersion solvers and the
risk-frequency database.

`--geometry-selftest` exercises the engine's portable `Point3D` and
`Vector3D` types against 19 expected results (constructors, operators,
`Length`, `Normalize`, `CrossProduct`, `DotProduct`, `AngleBetween`,
explicit `Point3D → Vector3D` cast, etc.). Exit 0 = every test passed.
The portable types are designed to be API-compatible with WPF's
`System.Windows.Media.Media3D` counterparts, so the same algorithm code
runs identically on Windows and Linux.

`--gpu-device <id>` persists into `%APPDATA%\DisperSim3D\settings.xml`, so
subsequent CLI / app runs also honour the pinned device until you change
it.
