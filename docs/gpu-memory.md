---
layout: default
title: GPU & Memory
nav_order: 9
---

# GPU &amp; Memory
{: .no_toc }

1. TOC
{:toc}

Open via **Settings → GPU &amp; Memory...**. Two tabs:

- **Compute GPU** — lists the OpenCL devices DisperSim 3D can see and lets
  you pin the FluidX3D solvers to a specific GPU.
- **Memory Estimator** — sizes RAM, VRAM and disk for a given solver +
  grid combination before you commit to a run.

## Compute GPU tab

### Device enumeration

The list comes from `fx3d_list_devices`, which is a thin wrapper around
FluidX3D's own `get_devices()` and returns JSON like:

```json
[
  { "id": 0, "name": "NVIDIA GeForce RTX 5070", "type": "GPU",
    "memory_mb": 12288, "compute_units": 78, "max_work_group_size": 1024 },
  { "id": 1, "name": "NVIDIA GeForce RTX 3060", "type": "GPU",
    "memory_mb": 12288, "compute_units": 28, "max_work_group_size": 1024 }
]
```

If the dialog shows "No OpenCL devices reported", check
`FluidX3DBridge.LastListDevicesError` (logged to the dialog status line) —
it usually means one of:

- `FluidX3D.dll` is not next to the executable (post-build copy didn't run).
- The OpenCL ICD loader is not installed or the driver is out of date.
- The DLL is loaded by a previous process and was rebuilt in place — close
  and restart the app.

### Pinning a device

Selecting a row and pressing **Set as default** stores its `id` in
`AppSettings.PreferredComputeDeviceId` (default `-1` = let FluidX3D pick).
All four FluidX3D runners read that setting on `Start` and call
`fx3d_create_on_device(cfg, device_id)`. The choice is persisted across
sessions via the standard `AppSettings` save path.

You can also override on a per-run basis from the simulation editor when
needed — but for most workflows pinning the higher-VRAM card once is the
right answer.

## Memory Estimator tab

`MemoryEstimator` computes per-cell footprint × cell count for a chosen
solver, then formats the result as RAM (host arrays + .NET overhead) +
VRAM (LBM lattice, GPU buffers) + disk (binary snapshots × write
frequency).

### Per-cell costs

The numbers below are conservative compile-time constants in
`MemoryEstimator.cs`:

| Solver path | Memory per cell |
|---|---|
| FluidX3D D3Q19 FP32 baseline | 93 B (VRAM) |
| `+ TEMPERATURE` extension | +32 B (VRAM, unused but allocated) |
| `+ SUBGRID` LES | +24 B (VRAM) |
| CPU `DispersionTracerEngine` | 41 B (RAM) — concentration + obstacles + scratch |
| CPU `FireTracerEngine` | 57 B (RAM) — smoke + T + obstacles + scratch |
| OpenFOAM steady (`simpleFoam`) | ~150 B per cell on disk |
| OpenFOAM transient reactive | ~450 B per cell per write |

These are upper-ish bounds — actual usage is a few % below.

### Public API

`MemoryEstimator` is a static class with methods for each runner family:

```csharp
MemoryEstimate EstimateFluidX3DWind(int Nx, int Ny, int Nz);
MemoryEstimate EstimateDispersionCpu(int Nx, int Ny, int Nz, int writeCount);
MemoryEstimate EstimateFire(int Nx, int Ny, int Nz, int writeCount);
MemoryEstimate EstimateOpenFoam(int Nx, int Ny, int Nz, int writeCount,
                                CfdSolverType solver);

// Dispatch helper that picks the right one based on solver enum + grid:
MemoryEstimate For(CfdSolverType solver, int Nx, int Ny, int Nz, int writeCount);
```

`MemoryEstimate` carries `RamBytes`, `VramBytes`, `DiskBytes` and a
human-readable summary via `HumanBytes`.

### Worked example

A $96^3$ FluidX3D Dispersion run with 30 written snapshots:

$$
\begin{aligned}
N_{\mathrm{cells}}
  &\;=\; 96 \times 96 \times 96 \;=\; 884\,736 \\
\mathrm{VRAM\ (LBM)}
  &\;=\; 884\,736 \cdot (93 + 24)\ \mathrm{B}
  \;\approx\; 103\ \mathrm{MB} \\
\mathrm{RAM\ (tracer)}
  &\;=\; 884\,736 \cdot 41\ \mathrm{B}
  \;\approx\; 36\ \mathrm{MB} \\
\mathrm{Disk}
  &\;=\; 884\,736 \cdot 8\ \mathrm{B/snap} \cdot 30
  \;\approx\; 213\ \mathrm{MB}
\end{aligned}
$$

The dialog renders that as **VRAM 103 MB · RAM 36 MB · Disk 213 MB**.
Doubling the grid to $128^3$ multiplies cell count by ~$2.4\times$, so all
three numbers scale roughly linearly.

### When to consult the estimator

- Before running anything beyond 96³ on a 4 GB GPU.
- Before creating a `DispersionStudy` with many simulations — the disk
  footprint times the number of runs adds up fast.
- Before choosing FluidX3DFire vs FluidX3DDispersion — the fire path
  carries the temperature field, so its RAM and disk are higher.
