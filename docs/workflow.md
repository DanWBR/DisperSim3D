---
layout: default
title: Workflow
nav_order: 3
---

# Workflow
{: .no_toc }

1. TOC
{:toc}

## Project-centric model

DisperSim 3D organises everything around a **Project**. A project file
(`.dsproj` bundle or legacy `.xml`) carries a Gas Library, geometry, sources,
wind fields, simulations, monitors and detectors  -  all the inputs and outputs
of a dispersion study, in one place.

The left-side tree shows these as discrete sections:

```
[Project Name]
├── General Settings           defaults: meteo, domain size, grid, CFD config
├── Gases (n)                  pure gases and mixtures, referenced by sources
├── Geometry (n)               imported obstacles (.obj/.stl/.rvm, .3ds on Windows)
├── Sources (n)                top-level release sources
├── Wind Fields (n)            meteo + obstacles → 3D velocity fields
├── Simulations (n)            Source × WindField × Solver immutable runs
├── Monitors (n)               passive concentration probes
└── Detectors (n)              alarm-threshold sensors and SCP placement
```

Each leaf node:

- **Checkbox** controls 3D visibility (where applicable).
- **Status badge** is colour-coded: Ready/Completed = green, Failed = red,
  Running = orange.
- **Right-click** opens a context menu (Add / Edit / Delete / Run / Open Case
  Folder / Duplicate / ...).
- **Double-click** opens the editor dialog or focuses the property grid.
- **Left-click** binds the WPF property grid to that object.

## Snapshot semantics

When a `Simulation` runs, it deep-clones the source, gas, meteo and CFD
configuration into `Snapshot*` fields stored inside the simulation itself.
Result history is **immutable**: editing a source after a simulation has
completed does not change the past results  -  the next simulation you create
will pick up the new values, but the old run keeps its frozen inputs.

This is why drag-to-reposition is intentionally disabled in the viewport
(geometry positions must be edited through the properties panel only). Any
position change is a project-level edit, not a per-run tweak.

## Wind field reuse

Wind fields are computed once and reused across many dispersion simulations.
This is a major cost saving  -  a CFD wind field is the expensive part of a
study. Define a small set of representative `WindFieldScenario`s (e.g. one
per Pasquill class × dominant direction), run them once, then create many
`Simulation`s that reference them.

The Wind Field Manager dialog supports running multiple scenarios in the
background queue. Status badges flip to green as each one converges.

## Job queue

Both wind fields and simulations route through `SimulationManager`'s
background queue. The status grid in the bottom dock shows currently running
jobs, queued jobs and recent failures. Cancel from the right-click menu;
re-run failed jobs from the same place.

## Typical session

1. **File → New Project**  -  name it, set defaults under **General Settings**
   if the project-wide domain or meteo defaults are unusual.
2. **Gases →** add the pure gases or mixtures you will release.
3. **Sources →** place release points; configure release rate, direction and
   optional HP-leak inventory. Sources reference gases via `GasRefId`.
4. **Wind Fields →** define 1–N scenarios; **Run** them in the background.
5. **Simulations →** create one simulation per (Source × Wind Field × Solver)
   combination. **Run** them  -  fast (analytical / FluidX3D) ones first.
6. **Visualize**  -  check simulations in the tree, scrub the playback bar,
   add isosurfaces and contour planes from the **View** menu.
7. **Analyse**  -  flammable cloud volume, monitor traces, detection-time
   metrics.
8. **Optimise detectors**  -  **Dispersion → Optimize Detector Placement...**
   or the newer **Dispersion → Detector Allocation...** for greedy
   max-coverage on a study.
9. **Validate**  -  drop benchmark `.dsbench` files into `benchmarks/` and run
   **Dispersion → Validate against Benchmarks...** or
   `DisperSim3D.CLI --validate`.
10. **Export** the case as a `.dsproj` bundle for sharing or CI.
