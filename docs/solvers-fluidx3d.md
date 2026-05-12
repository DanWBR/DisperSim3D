---
layout: default
title: FluidX3D solvers
nav_order: 5
---

# FluidX3D solvers
{: .no_toc }

1. TOC
{:toc}

[FluidX3D](https://github.com/ProjectPhysX/FluidX3D) is a GPU lattice Boltzmann
(LBM) solver from Moritz Lehmann. DisperSim 3D embeds it as a sibling C++
project compiled to `FluidX3D.dll` and invoked via P/Invoke through a thin
C-ABI bridge (`disp_bridge.cpp`). The solver runs on any OpenCL 1.2+ device
— typically a discrete GPU.

## Why GPU LBM

A 64³ steady wind field that takes minutes in `simpleFoam` (16 CPU cores,
RANS) finishes in 5–10 seconds on a mid-range GPU using FluidX3D LBM with
Smagorinsky-Lilly subgrid LES. Memory cost on the host stays low because the
heavy lifting happens in VRAM.

That speed unlocks workflows that are impractical with OpenFOAM: parameter
sweeps, design iteration with a "rebuild &amp; re-run" cycle of seconds, and
the [Dispersion Studies](studies-detectors) workflow where dozens of
simulations feed a single detector-allocation problem.

## The four FluidX3D runners

### `FluidX3DWind` — wind field

| | |
|---|---|
| **File** | `FluidX3DWindFieldRunner.cs` |
| **Code** | `FX3DWN` |
| **Output** | `WindField3D` (3-D velocity field on the project grid) |
| **Typical runtime** | 5–30 s for 64³–96³ on RTX 3060 |

Pipeline:

1. Convert SI inputs (domain size, wind speed, kinematic viscosity) to
   lattice units via `FluidX3DUnits`.
2. `fx3d_create_on_device` allocates an LBM grid on the user-selected GPU.
3. `FluidX3DObstacleVoxelizer.Voxelize` rasterises every `Decoration3D` AABB
   to `TYPE_S` solid cells.
4. `fx3d_set_inlet_wind` installs an equilibrium-boundary inlet on the -X
   face with the requested wind vector.
5. `fx3d_run(steady_steps)` advances the LBM until the flow stabilises
   (default 3000 steps).
6. `fx3d_read_velocity` copies `u.x`, `u.y`, `u.z` back to the host as
   `float[Nx·Ny·Nz]` arrays.
7. Convert lattice → SI, populate `wf.WindField`, mark the scenario
   **Ready**.

### `FluidX3DDispersion` — transient dispersion

| | |
|---|---|
| **File** | `FluidX3DRunner.cs` |
| **Code** | `FX3DDP` |
| **Output** | `OpenFoamResult` with one frame per `writeInterval` |

Uses the wind field computed by `FluidX3DWind` (or any other wind source —
loads from disk if the wind-field scenario already ran). Concentration is
advected by a separate **CPU semi-Lagrangian tracer** with explicit
diffusion + first-order decay
(`DispersionTracerEngine`). This split avoids a known issue where
FluidX3D's `TEMPERATURE` extension perturbs velocities when used for passive
scalars; the tracer reads the frozen LBM velocity at every cell and never
writes back to it.

Source treatment:

- Position / radius from `Simulation.SnapshotSource.Position`.
- Magnitude from `ReleaseRateKgPerS` converted via molar mass and voxel
  volume.
- Birch &amp; Schefer expanded source applied for HP leaks (same as the
  OpenFOAM path).

### `FluidX3DDispersionSteady` — convergence-driven steady run

| | |
|---|---|
| **File** | `FluidX3DSteadyDispersionRunner.cs` |
| **Code** | `FX3DDS` |
| **Output** | `OpenFoamResult` with a **single** converged frame |
| **Convergence** | Cell-by-cell L2 relative delta between successive snapshots |

Identical mechanics to `FluidX3DDispersion`, except the runner keeps stepping
in chunks (`ConvergenceChecks` chunks across `SnapshotDurationS`) and
compares each snapshot to the previous one. When
`‖cₙ − cₙ₋₁‖₂ / ‖cₙ‖₂ < ConvergenceTolerance` (default `1e-3`), the run
terminates and the final frame is written.

The `OpenFoamResult.IsSteadyState` flag is set on the result so the
viewport hides the playback bar — there is only one frame to display.

### `FluidX3DFire` — buoyant fire plume

| | |
|---|---|
| **File** | `FluidX3DFireRunner.cs` |
| **Code** | `FX3DFR` |
| **Output** | `OpenFoamResult` with smoke field + temperature field per frame |

A dual-tracer extension of the dispersion runner. `FireTracerEngine` advects
two scalars simultaneously — a smoke mass fraction `Y` and a temperature
`T` (Kelvin) — with a **Boussinesq buoyancy** term injected into the vertical
velocity:

$$
u_{z}^{\mathrm{eff}}
  \;=\; u_{z}^{\mathrm{LBM}}
        + \beta\,g\,(T - T_{\mathrm{amb}})\,\Delta t
\qquad\text{(capped at } 0.5\,\Delta x / \Delta t\text{)}
$$

Default fire exit temperature is **1500 K** (`ExitTemperatureK` override
available on the source). The pre-computed wind field stays unchanged — the
buoyancy correction only affects the tracer advection.

Output binary files:

```
<time>.bin       smoke mass fraction
<time>_T.bin     temperature field in K
```

The viewport renders smoke as a translucent isosurface and temperature as
contour bands when both are toggled in the View menu.

## GPU device selection

Multi-GPU systems route through the **Compute GPU** dialog under
**Settings → GPU &amp; Memory...**. See [GPU &amp; Memory](gpu-memory).

If `AppSettings.PreferredComputeDeviceId` is set (default `-1` = let
FluidX3D pick), `fx3d_create_on_device(..., device_id)` pins the LBM
context to that OpenCL device. Device IDs come from `fx3d_list_devices`,
which returns a JSON manifest of every detected OpenCL device with name,
type, memory, max work group size and compute units.

## C++ bridge

The C-ABI surface in `FluidX3D/src/disp_bridge.h`:

```cpp
extern "C" {
  uint64_t fx3d_create(const Fx3dConfig* cfg);
  uint64_t fx3d_create_on_device(const Fx3dConfig* cfg, int device_id);
  void     fx3d_list_devices(char* buf, uint32_t max_bytes);
  void     fx3d_add_box_obstacle(uint64_t h, float xmin, ymin, zmin, xmax, ymax, zmax);
  void     fx3d_set_inlet_wind(uint64_t h, float ux, float uy, float uz);
  void     fx3d_add_release_source(uint64_t h, float x, float y, float z,
                                    float radius_m, float concentration);
  int      fx3d_run(uint64_t h, uint32_t steps,
                    void(*progress_cb)(uint32_t done, uint32_t total));
  void     fx3d_read_velocity(uint64_t h, float* ux, float* uy, float* uz);
  void     fx3d_read_concentration(uint64_t h, float* c);
  void     fx3d_destroy(uint64_t h);
}
```

The C# side mirrors these in `FluidX3DBridge.cs` with `[DllImport]`
declarations. Handles are opaque 64-bit integers backed by a
`std::unordered_map<uint64_t, std::unique_ptr<LBM>>` on the native side, so
many simulations can run sequentially without state leak between them.

## FluidX3D feature flags

Compiled into `FluidX3D.dll` via `defines.hpp`:

| Flag | Why |
|---|---|
| `VOLUME_FORCE` | gravity + Boussinesq force injection |
| `EQUILIBRIUM_BOUNDARIES` | inlet/outlet cells as `TYPE_E` with fixed U, ρ |
| `SUBGRID` | Smagorinsky-Lilly LES — required at atmospheric Re ≥ 10⁵ |
| `D3Q19`, `FP32` | velocity set / precision (default; FP16 optional later) |
| **off:** `INTERACTIVE_GRAPHICS` | headless DLL, no SDL/window |
| **off:** `TEMPERATURE` | replaced by the CPU tracer to keep velocity clean |

## Failure paths

The runners surface OpenCL / device errors through the usual
`SimulationStatus.Failed` with a status string:

- `fx3d_create` returns `0` when no OpenCL device is available → status
  reads `"FluidX3D: no OpenCL device available"`.
- `fx3d_list_devices` failure is captured in
  `FluidX3DBridge.LastListDevicesError` — surfaced in the **Compute GPU**
  dialog so you can tell whether the DLL is loaded, the OpenCL ICD is
  installed and the GPU driver is up to date.
