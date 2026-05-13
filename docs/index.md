---
layout: default
title: Home
nav_order: 1
description: "DisperSim 3D — interactive 3D gas dispersion analysis for process safety."
permalink: /
---

# DisperSim 3D
{: .fs-9 }

Interactive 3D gas dispersion analysis for process safety. Build a project,
define release sources and gases, run analytical, OpenFOAM or GPU-accelerated
FluidX3D simulations, visualise concentration fields and optimise gas-detector
placement.
{: .fs-6 .fw-300 }

[Get started](getting-started){: .btn .btn-primary .fs-5 .mb-4 .mb-md-0 .mr-2 }
[View on GitHub](https://github.com/DanWBR/DisperSim3D){: .btn .fs-5 .mb-4 .mb-md-0 }

---

## What it does

DisperSim 3D is a .NET 10 desktop application for simulating accidental gas
releases and their atmospheric dispersion in industrial environments. The
**WinForms + WPF desktop UI is Windows-only**, but everything else —
**the calculation engine, the FluidX3D GPU bridge, the headless CLI, and an
Avalonia cross-platform UI smoke** — runs on Windows, Linux and macOS from
the same source. The engine multi-targets `net10.0;net10.0-windows`, the
CLI and Avalonia smoke are plain `net10.0`, and FluidX3D builds as
`libFluidX3D.so` / `.dylib` from the same C++ sources that produce the
Windows `FluidX3D.dll`. Validated end-to-end on Ubuntu 24.04 / WSL2 — see
[Getting started](getting-started) for the cross-platform recipe.

It combines three solver families inside a single project-centric workflow:

- **Analytical** — Gaussian puff, Gaussian plume, Briggs plume rise, TNO
  Yellow Book jets, Birch &amp; Schefer expanded source for sonic releases.
- **CFD with OpenFOAM v2512+** — `simpleFoam`, `pimpleFoam`,
  `buoyantPimpleFoam`, `rhoReactingBuoyantFoam` and friends, with Fiates &amp;
  Vianna 2016 / Vu 2019 / Mack &amp; Spruijt 2013 atmospheric BCs baked in.
- **GPU LBM with FluidX3D** — wind field, dispersion, steady-state dispersion
  and fire-plume runners that finish in seconds on a consumer GPU. Multi-GPU
  selection via the **Compute GPU** dialog.

Once you have one or more completed simulations, the same project file feeds
**Dispersion Studies** (groups of related runs), **Detector Allocation**
(greedy maximum-coverage placement) and the **Validation Harness** (Hanna
SPMs against `.dsbench` benchmark files).

## Target audience

Process safety engineers, HSE consultants and plant designers performing
dispersion modelling, area classification and gas-detector siting studies.

## Site map

| Page | What you'll find |
|---|---|
| [Getting started](getting-started) | Install requirements, build, first project walkthrough |
| [Workflow](workflow) | Project-centric model, tree sections, snapshot semantics |
| [Solvers](solvers) | Decision matrix, analytical, OpenFOAM and FluidX3D pages |
| [Visualization](visualization) | Isosurfaces, contour planes, streamlines, wind fields, playback |
| [Dispersion Studies &amp; Detector Allocation](studies-detectors) | Curated collections and greedy max-coverage placement |
| [Risk-Reduction Allocation](risk-allocation) | Greedy MRR + embedded IOGP 434-01 leak-frequency database |
| [GPU &amp; Memory](gpu-memory) | OpenCL device selection, RAM/VRAM/disk estimator |
| [Validation](validation) | Hanna SPMs, `.dsbench` files, bundled benchmarks |
| [Project File Format](file-format) | XML schema, `.dsproj` bundle, migration rules |
| [Cross-platform](cross-platform) | Solution layout, end-to-end Linux/WSL2 recipe, validation screenshots, architecture decisions |
| [References](references) | Bibliography and bundled PDFs |

## License and contributions

DisperSim 3D is released under the [GNU GPL v3](https://www.gnu.org/licenses/gpl-3.0.html).
Issues and pull requests are welcome on
[GitHub](https://github.com/DanWBR/DisperSim3D). If the project is useful to
you, consider [supporting development](https://donate.stripe.com/fZu14na8v9AG0PB2iJbMQ01).
