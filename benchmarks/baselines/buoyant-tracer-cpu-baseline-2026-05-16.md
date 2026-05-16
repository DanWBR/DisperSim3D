# BuoyantTracerEngine CPU baseline (2026-05-16)

Reference output of the C# CPU `BuoyantTracerEngine` for the
`FluidX3DDispersion` benches in the validation suite. Used to verify that
the future native FluidX3D OpenCL port (see [`TODO.md`](../../TODO.md))
reproduces the same numbers.

## Build identity

| Item | Value |
|---|---|
| Git commit | `ca91f61e10e4de1cc7b2bc23a0da50e638e56089` |
| Engine assembly | DisperSim3D.dll, net10.0 |
| FluidX3D native DLL | bin/FluidX3D.dll, 457216 bytes, 2026-05-15 |
| BuoyantTracerEngine | `DisperSim3D/Core/BuoyantTracerEngine.cs` |
| Wind field LBM | FluidX3D native CUDA, RTX 5070 |
| Tracer | C# CPU, semi-Lagrangian + BFECC |
| Date | 2026-05-16 |
| OpenFOAM (only for context, not used by the buoyant tracer) | ESI v2512 |
| Compiler | dotnet 10.0.300 |

## Reference predictions (per sensor)

### gant-ivings-2005.dsbench

Methane sonic jet through 10.5 mm orifice at 5.0 bar, 250 K. Birch and
Schefer expanded source at 32 mm diameter and 100 m/s.

Grid: 180 cubed (3x base of 60). Cell ~0.11 m.

| Sensor | Position [m] | Predicted (mass fraction) |
|---|---|---|
| jet_1m  | [1, 0, 1.0] | 0.08081 |
| jet_2m  | [2, 0, 1.0] | 0.04181 |
| jet_3m  | [3, 0, 1.0] | 0.03098 |
| jet_5m  | [5, 0, 1.0] | 0.01949 |

Cloud volume (LFL = 0.028, UFL = 0.089 mass fraction CH4): **1.169 m^3**.

SPMs against reference: MRB = -1.7e-5, RMSE = 6.6e-5, FAC2 = 1.0, MG = 1.0, VG = 1.0.

### must-trial-11.dsbench

Propylene tracer (200 g/s, 7 m/s wind, neutral stability) released near the
upwind edge of a 12 x 10 ISO container array. 120 obstacles voxelised
through the FluidX3D obstacle pipeline.

Grid: 240 cubed (2x base of 120). Cell ~0.83 m. Wind LBM 480 cubed.

| Sensor | Position [m] | Predicted (kg/m^3) |
|---|---|---|
| downwind-25m  | [25, 0, 1.6] | 1.958e-6 |
| downwind-50m  | [50, 0, 1.6] | 4.067e-5 |
| downwind-100m | [100, 0, 1.6] | 4.550e-5 |
| downwind-150m | [150, 0, 1.6] | 3.466e-5 |

Notes:
- The 25 m sensor sits in a narrow gap between containers and is depleted
  by the voxel resolution. The 50 m sensor is the first one well outside
  the array. Concentrations stay nearly flat between 50 and 150 m because
  the array channels the plume.
- The wind field at 480 cubed produces |U|_si mean = 7.000 m/s with 99 %
  of cells non-zero (the 1 % zero cells are inside the container voxels).

SPMs against reference (regression baseline): MRB = 7.3e-4, RMSE = 8.4e-4,
FAC2 = 1.0, MG = 1.001, VG = 1.0.

### Pending in same batch (still running)

- co2pipehaz-6mm.dsbench (FluidX3D, 6 mm supercritical CO2)
- spadeadam-co2.dsbench (FluidX3D, 25.62 mm cold liquid CO2)
- hydrogen-jet-schefer.dsbench (FluidX3D, 1.91 mm H2 at 207 bar)

These will be added to this baseline as they complete.

## Algorithm choices reflected in the predictions

The CPU `BuoyantTracerEngine` implements:

1. **Semi-Lagrangian advection** with cubic interpolation for stability at
   any Courant number.
2. **BFECC** (Back and Forth Error Compensation and Correction) over three
   passes per timestep. Reduces numerical diffusion from first to second
   order at ~3x the per-step cost of a single semi-Lagrangian pass.
3. **Density-based buoyancy**: `v_buoy = g * (rho_air - rho_mix) / rho_air`,
   added to the cell-centred velocity before advection.
4. **Gravity-current lateral spreading**: front-speed coefficient
   `Cgc = 0.5`, direction along the dense-cloud density gradient,
   activated only when `rho_mix > rho_air`.
5. **Species + temperature diffusion** with constant coefficients
   `D_species = 7.5e-3 m^2/s`, `D_thermal = 2.2e-5 m^2/s`.
6. **Mass-source injection** point/sphere/pool with cell-averaged Q.
7. **Obstacle handling**: boolean `_blocked` array set from voxelised AABBs
   during the wind-field setup; advection skips blocked cells.

The OpenCL port has to reproduce all six elements bit-exact within
floating-point tolerance to match this baseline.

## Acceptance criteria for the port

For each bench above, the OpenCL kernel must reproduce the CPU predictions
within the following per-sensor relative tolerance:

| Stage | Sensor relative tolerance | Cloud volume tolerance |
|---|---|---|
| Initial port (single-precision OpenCL) | 5 % | 5 % |
| Final port (after kernel tuning) | 1 % | 1 % |

Anything worse than 5 % per sensor means the algorithm port has a real
bug (operator order, boundary handling, BFECC limiter). Anything between
1 % and 5 % is floating-point noise or grid-aligned cell averaging.

## Reproduction

```powershell
# Build current engine (net10.0)
dotnet build "DisperSim3D.CLI\DisperSim3D.CLI.csproj" -c Release

# Run a single FluidX3D bench from the baseline above
DisperSim3D.CLI\bin\Release\net10.0\DisperSim3D.CLI.exe `
    --validate benchmarks\gant-ivings-2005.dsbench `
    --env native `
    --openfoam-path "C:\Users\danie\AppData\Roaming\ESI-OpenCFD\OpenFOAM\v2512"
```

Expected output: `Result: PASS  MRB=-1.7e-5 ... MG=1.0 VG=1.0`.

If the per-sensor predictions differ from the table above by more than
~1e-6 absolute, the CPU baseline itself has drifted (compiler change,
algorithm tweak, RNG seed change). Investigate before assuming the
OpenCL port is wrong.
