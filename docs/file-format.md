---
layout: default
title: Project File Format
nav_order: 13
---

# Project File Format
{: .no_toc }

1. TOC
{:toc}

DisperSim 3D supports two on-disk representations:

| Format | Extension | Contents |
|---|---|---|
| **Project bundle** (recommended) | `.dsproj` | ZIP with `project.xml` + referenced CFD case folders + assets |
| **Plain XML** | `.xml` | Single XML document, no case folders (used for legacy interchange) |

The CLI accepts either form.

## XML schema

Root element is `<Project Version="2">`. Legacy `<Scene3D Version="1">`
files are auto-migrated on load: inline gases are hoisted into
`GasLibrary`, sources promoted to top-level, and old scenarios converted
into `Simulation` snapshots (idempotent  -  safe to load and re-save).

### Top-level structure (Version 2)

```xml
<Project Version="2">
  <GeneralSettings>...</GeneralSettings>
  <GasLibrary>
    <Gas Kind="Pure" Id="..." Name="Methane"
         MolarMass="0.01604" LFL="0.033" UFL="0.17" IDLH="0"
         HeatComb="50100000" CAtoms="1" HAtoms="4" BurningVelocity="0.4" />
    <Gas Kind="Mixture" Id="..." Name="Sour Gas">
      <Mixture>
        <Component Name="CH4" MolarMass="0.016" MoleFrac="0.8"
                   LFL="0.033" IDLH="0" />
        <Component Name="H2S" MolarMass="0.034" MoleFrac="0.2"
                   LFL="0.043" IDLH="0.0696" />
      </Mixture>
    </Gas>
  </GasLibrary>

  <Decorations>
    <Decoration Id="..." Name="Tank-01" FilePath="...glb"
                PosX="0" PosY="0" PosZ="0" />
  </Decorations>

  <Sources>
    <Source Id="..." Name="Flange Leak" GasRefId="..."
            PosX="0" PosY="0" PosZ="2"
            ReleaseRate="0.5" PuffInterval="1"
            Azimuth="0" Elevation="0">
      <HPLeak VesselP="..." VesselT="..." Orifice="..." Volume="..."
              Gamma="1.4" MolarMass="0.016" Cd="0.65" />
    </Source>
  </Sources>

  <WindFieldScenarios>
    <WindFieldScenario Id="..." Name="5 m/s SW"
                       DomainSize="200" DomainHeight="100" GridRes="40"
                       Status="Ready" CasePath="...">
      <Meteo WindSpeed="5" WindDir="225" Stability="D"
             Temp="293.15" Pressure="101325" />
    </WindFieldScenario>
  </WindFieldScenarios>

  <Simulations>
    <Simulation Id="..." Name="Sim 1"
                SourceId="..." WindFieldId="..." SolverType="GaussianPuff"
                FireSourceId="..." IgnitionId="..."
                Status="Completed"
                DomainSize="200" GridRes="40" Duration="300" TimeStep="0.5"
                CasePath="..." MaxC="...">
      <SnapshotSource>...</SnapshotSource>
      <SnapshotGas>...</SnapshotGas>
      <SnapshotMeteo>...</SnapshotMeteo>
      <SnapshotCfdConfig>...</SnapshotCfdConfig>
      <Result MaxC="..." />
    </Simulation>
  </Simulations>

  <MonitorPoints>...</MonitorPoints>
  <GasDetectors>...</GasDetectors>
  <DispersionStudies>...</DispersionStudies>
  <DetectorAllocations>...</DetectorAllocations>
  <FireScenario>...</FireScenario>
</Project>
```

Numeric values use `InvariantCulture` (decimal point, no thousands
separator).

Fields added for fires, all optional, so older files load unchanged:

- **Gas combustion data:** on a library `Gas`, a source's `Gas` and a mixture
  `Component`: `HeatComb` (net heat of complete combustion, J/kg), `CAtoms`,
  `HAtoms` (atoms per molecule), `SootYield` and `CoYield` (kg per kg of fuel
  burnt) and `BurningVelocity` (peak laminar burning velocity, m/s). Written only
  when set; a missing one is resolved from the gas name (Tewarson, SFPE Handbook
  3rd ed. 2002, for the heat of combustion and the yields; NFPA 68 for the burning
  velocity). A mixture weighs the atoms by mole fraction and the heat of
  combustion and yields by mass.
- **Fire source kind:** `<Fire Kind="Jet|Pool|Fireball" FireballMass="1000">`.
  `IsPool` is still written; a file without `Kind` reads it.
- **Fire simulations:** a `Simulation` with `FireSourceId` or `IgnitionId` runs
  that fire source, or that ignition of a finished dispersion simulation, on a
  solver extender that declares the Fire capability (`SolverType="Extender"`).
- **Monitors and detectors** keep `MeasuredQuantity` (and a detector its
  `MeasuredThreshold`); any `ViewFieldProperty` name, including `Temperature`,
  `ThermalRadiationKwM2`, `SootDensityKgM3`, `COVolumeFraction`,
  `CO2VolumeFraction`, `O2VolumeFraction` and `VisibilityM`.

## Result files

A simulation's `CasePath` holds its results. Every solver that does not write an
OpenFOAM case uses the flat-bin layout: one file per channel and written time,
at the folder's top level.

### Field channels: `{t:F3}[_{channel}].bin`

`t` is the simulated time in seconds with three decimals and a decimal point
(`12.500`). Each file is `nx·ny·nz` little-endian IEEE doubles with no header,
`i` (x) outermost and `k` (z) innermost — what
`OpenFoamResult.SaveBinaryField` writes. The grid is `nx = ny = n`,
`nz = max(8, n/2)`: the reader infers `n` from the file size. Cells are cubes of
side `2·D/n` covering `[cx − D, cx + D] × [cy − D, cy + D] × [0, nz·2D/n]`, where
`D` is the simulation's `DomainSize` and `(cx, cy)` its `DomainCenterX/Y`
(an extender reports them through the bounds of the `OpenFoamResult` it returns,
or gets the ones its request asked for).

| Channel | File | Quantity | Unit |
|---|---|---|---|
| (species) | `{t}.bin` | released species | kg/m³ for extenders, imported results and the Gaussian and unified models; mass fraction for OpenFOAM and FluidX3D |
| `T` | `{t}_T.bin` | temperature | K |
| `Q` | `{t}_Q.bin` | incident thermal radiation | kW/m² |
| `Soot` | `{t}_Soot.bin` | soot mass concentration | kg/m³ |
| `CO` | `{t}_CO.bin` | carbon monoxide | volume fraction |
| `CO2` | `{t}_CO2.bin` | carbon dioxide | volume fraction |
| `O2` | `{t}_O2.bin` | oxygen (0.2095 in clean air) | volume fraction |
| `Vis` | `{t}_Vis.bin` | visibility through smoke | m (the solver's cap in clean air) |

A solver writes the channels it computes. A stored `Q` replaces the analytic fire
models in radiation, dose and fatality views, monitors, detectors and fire studies;
the other channels have no analytic fallback. For `O2` and `Vis` the hazard is a low
value, so their "peak over time" is the per-cell minimum. `ResultChannels.Register`
adds a channel beyond these.

### Surface heat load: `{t:F3}_surface.bin`

The heat load on the plant. For each triangle of the scene's merged decoration
mesh, in the order `DecorationSolids.FromDecorations(scene.Decorations).Triangles`
lists them (every decoration whose STL, OBJ or RVM mesh can be read, in world
coordinates), two little-endian doubles:

1. incident radiative heat flux, kW/m²;
2. total heat flux into the surface, radiative plus convective, kW/m².

Sixteen bytes per triangle, no header; the file size divided by 16 must equal the
triangle count. A Surface view colours the geometry with either value.

## Migration rules

`LegacyProjectMigrator.MigrateInPlace`:

- Sources inline in any `DispersionScenario` are hoisted to
  `TopLevelSources` (same instance, not cloned).
- Each unique inline `Gas` becomes a `GasLibraryItem` (Pure) and the
  source's `GasRefId` is set.
- Each existing `CfdSimulationEntry` becomes a stub `Simulation` with
  `Status = Completed` (or `Failed`).
- Migration is idempotent  -  safe to run on every load.

## `.dsproj` bundle

A `.dsproj` is a ZIP archive (deflate, `CompressionLevel.Fastest`) with:

```
my-project.dsproj
├── project.xml          # the XML described above
├── meta.json            # bundle metadata: version, generator, created-at
├── cases/
│   ├── <wind-field-id>/ # full OpenFOAM case dir
│   ├── <simulation-id>/ # results binary files (.bin per timestep)
│   └── ...
└── assets/
    ├── <decoration-id>.glb
    └── ...
```

`ProjectBundle.Open(path)` extracts to a session temp dir and rewrites
paths so the in-memory `Project` references the extracted files. On save,
`ProjectBundle.Save(project, path)` zips everything back, regenerating
case directory paths.

## Solver code reference

Each `Simulation` stores a six-character `SolverCode` for clean round-trips
through the headless / CLI layer:

| Code | `CfdSolverType` |
|---|---|
| `GAUSSP` | `GaussianPuff` |
| `GAUSSPL` (note 7 chars  -  historical) | `GaussianPlume` |
| `UDMCNT` | `UnifiedDispersion` |
| `RHRBFM` | `RhoReactingBuoyantFoam` |
| `FX3DWN` | `FluidX3DWind` |
| `FX3DDP` | `FluidX3DDispersion` |
| `FX3DDS` | `FluidX3DDispersionSteady` |
| `FX3DFR` | `FluidX3DFire` |

These appear in case directory names (`fx3dds_case_<sim-id>` etc.) and in
the result binary path conventions for the FluidX3D runners.
