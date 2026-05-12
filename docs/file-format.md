---
layout: default
title: Project File Format
nav_order: 10
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
into `Simulation` snapshots (idempotent — safe to load and re-save).

### Top-level structure (Version 2)

```xml
<Project Version="2">
  <GeneralSettings>...</GeneralSettings>
  <GasLibrary>
    <Gas Kind="Pure" Id="..." Name="Methane"
         MolarMass="0.01604" LFL="0.033" UFL="0.17" IDLH="0" />
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

## Migration rules

`LegacyProjectMigrator.MigrateInPlace`:

- Sources inline in any `DispersionScenario` are hoisted to
  `TopLevelSources` (same instance, not cloned).
- Each unique inline `Gas` becomes a `GasLibraryItem` (Pure) and the
  source's `GasRefId` is set.
- Each existing `CfdSimulationEntry` becomes a stub `Simulation` with
  `Status = Completed` (or `Failed`).
- Migration is idempotent — safe to run on every load.

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
| `GAUSSPL` (note 7 chars — historical) | `GaussianPlume` |
| `SCTRFM` | `ScalarTransportFoam` |
| `SCTRFS` | `ScalarTransportFoamSteady` |
| `SCSMFM` | `ScalarSimpleFoam` |
| `RSIMFM` | `RhoSimpleFoam` |
| `PIMPLE` | `PimpleFoam` |
| `BPIMPL` | `BuoyantPimpleFoam` |
| `REACFM` | `ReactingFoam` |
| `RHRBFM` | `RhoReactingBuoyantFoam` |
| `FX3DWN` | `FluidX3DWind` |
| `FX3DDP` | `FluidX3DDispersion` |
| `FX3DDS` | `FluidX3DDispersionSteady` |
| `FX3DFR` | `FluidX3DFire` |

These appear in case directory names (`fx3dds_case_<sim-id>` etc.) and in
the result binary path conventions for the FluidX3D runners.
