# DisperSim 3D - TODO

Running list of work to come back to.

## Engine performance

### Port BuoyantTracerEngine to native FluidX3D (GPU)

The buoyant scalar tracer is currently a C# CPU semi-Lagrangian solver
(`DisperSim3D/Core/BuoyantTracerEngine.cs`) with BFECC advection,
density-based buoyancy, and gravity-current lateral spreading. It is the
slow stage of every `FluidX3DDispersion` run: the GPU LBM wind field
takes 1-2 min, the CPU tracer takes 25-35 min on a 240 cubed grid.
During the tracer step the RTX 5070 idles at ~3% utilisation.

Goal: move the tracer kernel into the FluidX3D native DLL so it runs on
the same OpenCL device as the wind field. Reuses the existing buffer
layout (no CPU/GPU copies per snapshot) and brings the whole dispersion
runtime down by roughly an order of magnitude.

What needs porting (current C# logic in `BuoyantTracerEngine`):

- Semi-Lagrangian advection with BFECC error correction (3 passes).
- Density-based vertical buoyancy from mass-fraction Y and temperature T.
- Gravity-current lateral spreading (front speed model, Cgc = 0.5).
- Species + temperature diffusion (constant D, k).
- Mass injection source (point, sphere, pool) at runtime.
- Obstacle handling (boolean blocked array from voxelised AABBs).

API: extend `disp_bridge.cpp` with `fx3d_step_buoyant_tracer(handle, dt)`
and snapshot getters that read the tracer field back to host memory only
when DisperSim asks for it. Keep the C# `BuoyantTracerEngine` interface
unchanged so callers (`FluidX3DRunner`, validation harness) do not have
to change.

Estimated effort: 1-2 weeks. Wait until current validation campaign is
done so the regression baselines stay stable while the port lands.
