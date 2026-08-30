---
layout: default
title: Visualization
nav_order: 7
---

# Visualization
{: .no_toc }

1. TOC
{:toc}

## 3D viewport

The main viewport is a HelixToolkit-WPF `HelixViewport3D` hosted inside the
WinForms shell via `ElementHost`. Default controls:

| Action | Mouse / Key |
|---|---|
| Orbit | Left-drag |
| Pan | Right-drag |
| Zoom | Mouse wheel |
| Select object | Left-click |
| Deselect | Click empty space |
| Delete selected | `Delete` key |
| Exit current edit mode | `Esc` key |

**Object repositioning by drag is disabled by design.** Geometry positions
are tied to the snapshots cached for each completed simulation  -  moving an
object would silently invalidate those results. Use the properties panel on
the right to edit positions instead.

## Camera presets

The **View → Camera** menu has named presets (Isometric, Top, Front, Side,
Free) that snap the camera to canonical orientations. Batch image export
captures every preset at once and writes PNGs alongside the project file  - 
useful for QA reports.

## Per-item visibility

Every leaf in the project tree has a checkbox. The state controls visibility
in the viewport:

| Item | What "visible" means |
|---|---|
| Geometry | the imported mesh is rendered |
| Source | a sphere + locator pole is drawn at the release point |
| Monitor / Detector | a marker + radius shell are drawn at the position |
| Wind field | animated arrows + optional streamlines render in the viewport |
| Simulation | the concentration field plays back, animated by the playback bar |

The vertical "locator pole" above every source / monitor / detector ensures
the marker stays visible even when the camera is inside or below a tank or
building.

## Isosurfaces

Marching cubes (`MarchingCubesGenerator`) produces a closed mesh at a chosen
concentration threshold. Configurable colour, opacity, smoothing iterations
and threshold in the **View → Isosurface** dialog. Multiple isosurface
layers can be enabled simultaneously (e.g. LFL + ½ LFL + UFL).

Performance: a 64³ grid renders in ~50 ms; 128³ in ~400 ms on a modern CPU.

## Contour planes

Slice planes in XY, XZ or YZ orientation, with one of:

- **Jet**  -  classic blue-green-yellow-red
- **Viridis**  -  perceptually uniform
- **Inferno**  -  perceptually uniform, dark background
- **Coolwarm**  -  diverging

The plane is dragged along its normal axis via a property slider or the
plane handle gizmo (when shown). Texture sampling uses the LRU-cached
concentration field from `OpenFoamResult` so seeking the playback bar is
responsive even on huge cases.

## Wind fields

When a wind field scenario is checked in the tree:

- **Animated arrows** are drawn on a configurable density / colour / opacity
  /length / thickness grid. Animation pulses scale and opacity in sync with
  the playback time.
- **Streamlines**  -  particle paths seeded on a regular grid and integrated
  through the velocity field, optionally coloured by concentration when a
  dispersion result is also loaded.
- **Wind rose**  -  a polar histogram of wind speed × direction in the bottom
  dock, refreshed when the meteo is edited.

All wind-field rendering is read-only  -  the velocity field comes from the
`WindField3D` that the runner populated, never modified live.

## Dispersion playback

Below the viewport, the **playback bar** synchronises:

- **Time slider** seeks the currently checked simulations to a given time.
- **Play / pause** toggles auto-advance at the chosen speed.
- **Speed control** scales wall-clock to simulation time (0.25× to 8×).
- **Frame counter** displays the rendered timestep.

When multiple simulations are checked, all of them advance to the same
wall-clock time  -  useful for side-by-side comparison of solvers on the same
source.

**Steady-state runs hide the playback bar entirely.** A `FluidX3DDispersionSteady`
result has only one converged frame; there is no transport to step through.
The view jumps straight to that frame.

## Particle animation (puff)

The `GaussianPuffEngine` emits discrete puffs at `PuffIntervalS`. Each puff
is rendered as a translucent sphere that grows over time (sigma growth).
The result is a stylised but physically meaningful animation of a transient
release  -  handy for non-technical stakeholders.

## Thermal radiation

When `FireScenario.Sources` carries a `FireSource3D`, a point-source thermal
radiation contour can be rendered on the ground plane (Chamberlain jet-fire
geometry with Brzustowski tilt). Contour levels follow the standard
4 / 12.5 / 37.5 kW/m² thresholds (TNO Green Book).
