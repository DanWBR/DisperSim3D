---
layout: default
title: Dispersion Studies & Detector Allocation
nav_order: 7
---

# Dispersion Studies &amp; Detector Allocation
{: .no_toc }

1. TOC
{:toc}

DisperSim 3D supports two related-but-distinct detector-placement workflows:

- **Set Covering Problem (SCP) optimisation** (Vianna 2019) — exact /
  greedy minimum-cardinality cover over the flammable cloud volume of one
  or more simulations. Long-standing feature, accessed via
  **Dispersion → Optimize Detector Placement...**.
- **Dispersion Study + Detector Allocation** (newer) — a project-level
  collection of related simulations bundled into a single **Study**, and a
  greedy **maximum-coverage** allocator that places `K` detectors to cover
  as many clouds as possible. Designed for design-review use cases where
  the total detector budget is fixed.

This page focuses on the newer Study + Allocation workflow. The classic SCP
optimiser is documented in
[TECHNICAL_DOCUMENTATION.md §3.6](https://github.com/DanWBR/DisperSim3D/blob/main/TECHNICAL_DOCUMENTATION.md#36-detector-placement-optimisation).

## Dispersion Studies

A **`DispersionStudy`** is a named collection of `Simulation` IDs plus a
single detection criterion that turns each simulation's concentration field
into a binary "this cell is detectable" mask:

| Property | Purpose |
|---|---|
| `Name` | display name shown in the tree |
| `SimulationIds[]` | references to existing `Simulation` entries |
| `DetectionQuantity` | `ViewFieldProperty` enum — `PercentLfl`, `Ppm`, `MoleFraction`, `MassFraction`, `Temperature`, `ThermalRadiation` |
| `DetectionThreshold` | the threshold value in the units defined by `DetectionQuantity` |

Example: `DetectionQuantity = PercentLfl`, `DetectionThreshold = 25`
captures every cell where the local concentration is ≥ 25 % LFL across any
of the bundled simulations.

Studies live in the project tree under their own section and are saved
inside the project XML / `.dsproj` bundle.

### Cloud snapshot

For each simulation in the study, `DispersionStudyEngine.LoadClouds` reads
the **last** concentration timestep (or the steady-state frame, for
`FluidX3DDispersionSteady`) and builds a **`CloudSnapshot`** — a list of
flagged cells together with an axis-aligned bounding box used to short-cut
the radius test in the allocator (`CellWithinRadius` skips clouds whose
bbox does not intersect the detector radius sphere).

## Detector Allocation

A **`DetectorAllocation`** is the second project-level object: it pins a
candidate grid, a detection radius and a budget against a study, and runs
the greedy max-coverage allocator.

### Configuration

| Property | Purpose |
|---|---|
| `DispersionStudyId` | study to allocate against |
| `Objective` | `CoverAll` (stop only when every cloud has at least one detector) or `CoverPercentage` (stop when the target % is reached) |
| `MaxDetectors` | hard cap on detector count (applied to either objective) |
| `DetectionRadiusM` | sphere radius around each candidate detector that counts as "covered" |
| `MinZ`, `MaxZ` | vertical band where detectors may be placed (typically head-height: 1.5 – 3 m) |
| `CandidateNx, Ny, Nz` | candidate grid resolution |
| `UseExistingDetectors` | when true, the project's existing `GasDetector3D` instances are pinned as already-placed and the allocator only fills the remaining gap |

### Algorithm

`DetectorAllocator.Allocate`:

1. Build a Cartesian candidate grid `Nx · Ny · Nz` within the project
   domain, clipped to `[MinZ, MaxZ]`.
2. Cull candidates whose centres lie inside any obstacle (`Decoration3D`
   bounding box).
3. For each candidate `cᵢ`, compute the set
   `cover[cᵢ] = { cloud j : any flagged cell in cloud j lies within
   DetectionRadiusM of cᵢ }`.
4. Greedy loop: while not done and `|placed| < MaxDetectors`, pick the
   candidate that covers the most still-uncovered clouds; add it to the
   placed set; remove the newly-covered clouds from consideration; repeat.
5. Pinned existing detectors (when `UseExistingDetectors = true`) are
   accounted for in step 4 before the greedy loop runs.

The allocator is deliberately **greedy max-coverage**, not exact set-cover
— it scales to thousands of candidates × dozens of clouds without the
combinatorial blow-up of Balas branch-and-bound. For exact minimum-set-cover
runs use the classic SCP dialog instead.

### Results

After running, the allocation populates:

- `AllocatedPositions[]` — chosen 3-D positions (excluding any pinned
  existing detectors).
- `AchievedCoveragePercent` — fraction of study clouds covered by the final
  placement.
- `PerCloudCovered[]` — boolean per simulation, useful for spotting which
  cloud(s) the greedy didn't reach.

## Visualisation

`StudyAllocationRenderer`:

- **Per-cloud isosurfaces** — marching-cubes mesh for every simulation in
  the study, colour-cycled from a palette so each cloud is distinguishable.
- **Allocated detectors** — orange spheres at the chosen positions.
- **Detection radius** — translucent spheres of radius `DetectionRadiusM`
  drawn around each detector to make coverage gaps visible at a glance.

Toggle the study or the allocation visibility in the tree to drill in.

## When to use which workflow

| Goal | Use |
|---|---|
| Minimum number of detectors to cover **every** flammable cell of a single sim | Classic SCP (`Dispersion → Optimize Detector Placement...`) |
| Best `K`-detector placement across a portfolio of release scenarios | Dispersion Study + Detector Allocation |
| Iterate detector budget vs. coverage curves | Detector Allocation with varying `MaxDetectors` |
| Validate an existing detector layout against new release cases | Allocation with `UseExistingDetectors = true` |
