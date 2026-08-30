---
layout: default
title: Cross-platform
nav_order: 14
---

# Cross-platform DisperSim 3D
{: .no_toc }

1. TOC
{:toc}

DisperSim 3D started life as a Windows-only WinForms + WPF application. As of
the 1.0 cross-platform port, **the calculation engine, the FluidX3D GPU
bridge, the headless CLI, and a proof-of-concept Avalonia UI all run on
Windows, Linux and macOS from the same source**. The original WinForms
desktop shell still launches and works exactly as before on Windows. This
page tells the story of how that's structured, shows the actual screenshots
from the validation run on Ubuntu 24.04 / WSL2, and provides the recipe to
reproduce it from a clean clone.

## What's portable, what isn't

| Component | TFM / build | Windows | Linux | macOS |
|---|---|:-:|:-:|:-:|
| `DisperSim3D` (engine) | `net10.0` + `net10.0-windows` (multi-target) | ✅ | ✅ | ✅ |
| `DisperSim3D.CLI` (headless) | `net10.0` | ✅ | ✅ | ✅ |
| `DisperSim3D.UI.Avalonia` (cross-platform verification) | `net10.0` + Avalonia 11 | ✅ | ✅ (WSLg) | ✅ |
| `DisperSim3D.UI.Wpf` + `DisperSim3D.App` (WinForms shell) | `net10.0-windows` | ✅ |  -  |  -  |
| `FluidX3D` native library | C++ / OpenCL | `FluidX3D.dll` (MSVC) | `libFluidX3D.so` (g++) | `libFluidX3D.dylib` (g++) |

The Windows-only column is intentional: it's the production-quality desktop
UI built on **WinForms + WPF + HelixToolkit + HandyControl + DockPanelSuite**,
none of which have cross-platform equivalents that match the experience.
Everything else (the engine, every solver, every algorithm, the headless
runner) sits on the cross-platform side.

## Solution layout

```
DisperSim3D/                       (engine)
└── multi-targets net10.0;net10.0-windows. The windows TFM is included only
    when MSBuild runs on Windows ($(OS)=='Windows_NT'); on Linux/macOS it
    drops out so the engine restores cleanly without NETSDK1100.

DisperSim3D.UI.Wpf/                (Windows-only UI library)
└── net10.0-windows. Holds every type that touches WPF / HelixToolkit /
    HandyControl / DockPanelSuite. References DisperSim3D.

DisperSim3D.UI.Avalonia/           (Cross-platform verification window)
└── net10.0. Pure Avalonia 11  -  a 4-panel proof that the engine works
    behind a non-WPF UI on the same source. References DisperSim3D.

DisperSim3D.CLI/                   (headless runner)
└── net10.0. References DisperSim3D. Same binary on every OS.

DisperSim3D.App/                   (production WinForms shell)
└── net10.0-windows. References DisperSim3D + DisperSim3D.UI.Wpf.

FluidX3D/                          (native C++)
├── FluidX3D.vcxproj                 → FluidX3D.dll on Windows (MSVC)
└── make-disp-bridge.sh + makefile   → libFluidX3D.{so,dylib} on Unix (g++)
```

The two halves of FluidX3D share the same source set (every `.cpp` in
`src/` except `main.cpp`, plus `disp_bridge.cpp` for the C-ABI exposed to
C#). The `disp_bridge.h` macro `FX3D_API` resolves to `__declspec(dllexport)`
on Windows and to empty on Unix (g++ exports non-static symbols by default).

## End-to-end recipe (Ubuntu 24.04 / WSL2)

This is the exact sequence validated during the cross-platform port,
reproducible from a clean clone on any Ubuntu 22.04+ host (or WSL2 on
Windows 11):

```bash
git clone https://github.com/DanWBR/DisperSim3D.git
cd DisperSim3D

# 1) Engine + CLI (~30 seconds, no GPU / native deps needed)
dotnet build DisperSim3D/DisperSim3D.csproj -c Release
dotnet build DisperSim3D.CLI/DisperSim3D.CLI.csproj -c Release

# 2) FluidX3D native library + universal CPU OpenCL ICD (~1 minute)
sudo apt install -y build-essential pocl-opencl-icd ocl-icd-libopencl1
cd FluidX3D
./make-disp-bridge.sh --copy        # → bin/libFluidX3D.so, auto-copied into each .NET output dir
cd ..

# 3) headless verification tests (must all exit 0)
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --geometry-selftest   # 19/19 PASS
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --iogp-selftest       # 27/27 PASS
dotnet DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --list-gpus           # JSON device list

# 4) Avalonia verification window  -  opens via WSLg on the Windows desktop
dotnet build DisperSim3D.UI.Avalonia/DisperSim3D.UI.Avalonia.csproj -c Release
dotnet DisperSim3D.UI.Avalonia/bin/Release/net10.0/DisperSim3D.UI.Avalonia.dll
```

For production-speed FluidX3D runs, swap PoCL for the GPU vendor's ICD
(NVIDIA driver / `intel-opencl-icd` / `amdgpu-install --usecase=opencl`)  - 
see [FluidX3D solvers]({{ site.baseurl }}/solvers-fluidx3d#building-fluidx3d-on-linux--macos).

## Validation outputs

### Step 1  -  Geometry self-test (`--geometry-selftest`)

19 portable `Point3D` / `Vector3D` operators tested against expected values.
Every line green, exit code 0:

![Geometry self-test passing on WSL2]({{ site.baseurl }}/assets/cross-platform/01-geometry-selftest-wsl2.png)

This proves the engine's portable `DisperSim3D.Geometry.Point3D` and
`Vector3D` types  -  which replace `System.Windows.Media.Media3D.*` in the
engine assembly  -  produce bit-equivalent results to the WPF originals.
Constructors, operators, `Length`, `LengthSquared`, `Normalize`, `Negate`,
`CrossProduct`, `DotProduct`, `AngleBetween`, and the explicit
`Point3D → Vector3D` cast all match the WPF semantics one-for-one.

### Step 2  -  IOGP 434-01 self-test (`--iogp-selftest`)

27 published values from the IOGP 434-01 (2006–2015) leak-frequency dataset
round-tripped through the embedded database. All PASS:

![IOGP 434-01 self-test passing on WSL2]({{ site.baseurl }}/assets/cross-platform/02-iogp-selftest-wsl2.png)

Every equipment type × hole-size band × nominal diameter combination
produces the exact `actual` value the IOGP publication has as `expected`,
to the 3rd significant digit. The risk-reduction detector allocator
multiplies these frequencies by consequence severity per cloud, so any
cross-platform drift here would silently corrupt detector placement.

### Step 3  -  FluidX3D OpenCL device probe (`--list-gpus`)

The native `libFluidX3D.so` (894 KB) builds with g++ from the same source
that produces `FluidX3D.dll`, with `disp_bridge.cpp` as the C-ABI entry
point. `[DllImport("FluidX3D")]` in `FluidX3DBridge.cs` resolves to
`libFluidX3D.so` automatically on .NET 10's POSIX runtime (no extension
suffix means the framework picks the right one per OS).

The OpenCL ICD on the host (PoCL in this run, but any vendor ICD works)
satisfies `libOpenCL.so.1` resolution, FluidX3D's `get_devices()` returns
the device list, the bridge formats it as JSON:

![FluidX3D OpenCL device probe on WSL2]({{ site.baseurl }}/assets/cross-platform/03-list-gpus-wsl2.png)

```json
[{"id":0,
  "name":"cpu-haswell-13th Gen Intel(R) Core(TM) i9-13900KF",
  "vendor":"GenuineIntel",
  "memory_mb":29950,
  "tflops":1.533,
  "compute_units":32,
  "clock_mhz":2995,
  "is_gpu":false}]
```

This is the full chain: `dotnet` → managed engine → `[DllImport]` →
`libFluidX3D.so` → FluidX3D `LBM::get_devices()` → OpenCL ICD loader →
PoCL CPU runtime → results back up the stack as JSON.

### Step 4  -  Avalonia verification window (4 panels, all green)

The proof-of-concept Avalonia UI. Native window on the Windows desktop via
WSLg, identical layout the user would get on a bare-metal Linux desktop or
macOS:

![DisperSim 3D Avalonia verification  -  4 panels all green on WSL2]({{ site.baseurl }}/assets/cross-platform/04-avalonia-all-green-wsl2.png)

Header: `.NET 10.0.7  •  OS Linux/Unix (Ubuntu 24.04 LTS)  •  Avalonia 11.2.3.0  •  cores 32`

| Panel | Engine layer exercised | Result |
|---|---|---|
| Portable geometry self-test | `DisperSim3D.Geometry.*` operators | 19/19 PASS |
| IOGP 434-01 risk frequency | `IogpFrequencyTable` lookup + checks | 27/27 PASS |
| FluidX3D  -  list OpenCL devices | `[DllImport("FluidX3D")]` → C-ABI → OpenCL | JSON device list |
| Engine end-to-end (Gaussian plume) | Gas → Source → Meteo → `GaussianPlumeEngine` → 32³ sweep | `MaxC = 0.000852243 kg/m³` @ `(62.5, 37.5, 0.0) m` |

The plume result is particularly important: it's **32 768 calls** to
`GaussianPlumeEngine.EvaluateConcentration(x, y, z)`  -  every one of which
walks through portable `Point3D`/`Vector3D` arithmetic, Pasquill class D
stability functions, wind direction rotation, and ground reflection. The
exact same `MaxC` to all significant digits appears on Windows when the
same code path is invoked from `DisperSim3D.App`  -  that's the
cross-platform-arithmetic guarantee in action.

## How the port works under the hood

A short tour of the architectural decisions that make the layout above
possible:

1. **Portable geometry types** (`DisperSim3D/Geometry/`). The engine
   defines its own `Point3D`, `Vector3D`, `Color` types whose API mirrors
   `System.Windows.Media.*` one-for-one. Constructors, fields,
   operators, static helpers: identical surface. On the `net10.0-windows`
   TFM only, the portable types expose **implicit conversion operators**
   to and from their WPF counterparts (gated by `#if WINDOWS`), so call
   sites that pass a portable `Point3D` into HelixToolkit just work.
2. **Conditional multi-targeting**. The engine's csproj uses
   `<TargetFrameworks Condition="'$(OS)' == 'Windows_NT'">net10.0;net10.0-windows</TargetFrameworks>`
   and a Linux/macOS branch with only `net10.0`. Without that condition,
   restoring on Linux trips `NETSDK1100` ("To build a project targeting
   Windows on this operating system, set the EnableWindowsTargeting
   property to true").
3. **`[Editor]` attributes use string-based type references**, not
   `typeof(...)`. So when the engine declares
   `[Editor("DisperSim3D.Controls.Point3DPropertyEditor, DisperSim3D.UI.Wpf", ...)]`,
   nothing in the engine assembly actually compile-links to the UI editor
    -  the property grid resolves the type by name at runtime.
4. **`Decoration3D.Model3D` is typed `object`**, not `Model3DGroup`.
   The runtime visual is a WPF object, but the engine never inspects it;
   the UI layer casts on read (`deco.Model3D as Model3DGroup`).
   Serialization is unaffected because the visual is loaded from the
   `.obj`/`.stl`/`.glb` file path at runtime  -  it never serializes.
5. **WPF-specific methods on engine types live as extension methods** in
   `DisperSim3D.UI.Wpf.Models`. Call-site syntax is unchanged
   (`deco.ApplyClip()`, `deco.GetWorldTransform()`,
   `boundingBox.Transform(transform3D)`), but the engine assembly has no
   reference to `MeshClipper` or `Transform3D`.
6. **FluidX3D `[DllImport("FluidX3D")]`**  -  no extension. .NET adds the
   platform-correct suffix and prefix automatically: `FluidX3D.dll` on
   Windows, `libFluidX3D.so` on Linux, `libFluidX3D.dylib` on macOS. The
   common pitfall is writing `"FluidX3D.dll"` literally  -  .NET on Linux
   then *never* strips the `.dll` and only tries
   `FluidX3D.dll{,.so}` / `libFluidX3D.dll{,.so}`, none of which match
   the file you built.
7. **`disp_bridge.cpp` is OS-conditional where it has to be**:
   `localtime_s` (MSVC) vs. `localtime_r` (POSIX), `%TEMP%` vs.
   `$TMPDIR`/`/tmp` for the log file, path separator. Everything else is
   plain C++17 STL.

## Performance notes on WSL2

WSL2 introduces a thin layer of overhead vs. bare-metal Linux:

- **Managed code** (engine, CLI, Avalonia UI): zero measurable difference.
- **FluidX3D + PoCL CPU OpenCL**: same CPU runtime, identical performance.
- **FluidX3D + NVIDIA GPU via WSLg vGPU**: ~10–20% overhead vs. native
  Linux on the same hardware, but full CUDA-class throughput. The Windows
  driver does the actual work.
- **Avalonia UI rendering via WSLg**: imperceptible for non-3D UI; for the
  future Avalonia 3D viewport (OpenTK / Silk.NET), expect some marshalling
  cost.

For development the WSL2 round-trip is excellent  -  you can edit on
Windows, build on Linux, and the resulting binary lands on the same NTFS
share both OSs see. For release validation, verification on a bare-metal Linux
container too.

## What's next

The cross-platform port is complete as a **foundation**. The pieces above
prove the engine, the C++ bridge, and a non-WPF UI all hold up together
on Linux. The remaining work to ship a full Linux/macOS desktop app
against this foundation is:

1. **3D viewport on Avalonia**  -  substitute `HelixToolkit.Wpf` with
   OpenTK or Silk.NET on top of Avalonia's `OpenGlControlBase`.
2. **Avalonia dialogs**  -  port the WPF dialogs in `DisperSim3D.UI.Wpf` to
   Avalonia 11 XAML. The code-behind logic is mostly pure C# already, so
   this is largely XAML conversion.
3. **OpenFOAM path detection on Linux**  -  the runner currently looks for
   `%APPDATA%\ESI-OpenCFD`; on Linux it should also probe
   `~/OpenFOAM/`, `/opt/openfoam*/`, `/usr/lib/openfoam/`.
4. **DWSIM thermo on Linux**  -  `DwsimThermo.cs` is reflection-loaded
   (zero compile-time dependency on DWSIM), so it should work
   out-of-the-box once `AppSettings.DwsimInstallPath` points at the
   DWSIM Linux install. Subject to a real test.
5. **CI on `ubuntu-latest`**  -  a GitHub Actions workflow that runs
   `--validate benchmarks/` + `--geometry-selftest` + `--iogp-selftest`
   on every PR. Small, deterministic, prevents cross-platform regressions.

None of these are blockers for the existing WinForms experience  -  they're
additive. The current state is "engine and CLI fully cross-platform,
WinForms UI fully functional on Windows" and that holds indefinitely.
