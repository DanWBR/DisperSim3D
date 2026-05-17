# rhoReactingBuoyantFoamSct — patched LNG dispersion solver

OpenFOAM v2412+ ships `rhoReactingBuoyantFoam` with a species transport
equation that hard-codes the turbulent Schmidt number at `Sct = 1.0`
(via `turbulence->muEff()` in `YEqn.H`). Vu (2019, §5.4) showed that
LNG vapour dispersion needs `Sct ≈ 0.15` to match the Burro/Coyote
field trials at FAC2 = 1.0. The Sct value DisperSim writes to
`constant/turbulenceProperties` is silently ignored by the stock solver.

This script builds a user-local patched binary `rhoReactingBuoyantFoamSct`
that reads `Sct` from `constant/transportProperties` and uses
`muEff/Sct` for the species diffusion coefficient, matching Vu (2019)
§A.2 Listing A.5 exactly.

> **Reference**: Vu, Tran Le. *On numerical modelling of atmospheric gas
> dispersion using CFD approach*. PhD thesis, Nanyang Technological
> University, Singapore, 2019. Handle:
> <https://dr.ntu.edu.sg/handle/10356/103659>. PDF in `docs/`.

## Prerequisites

WSL2 Ubuntu (or any Linux) with the OpenFOAM dev package:

```bash
sudo apt install openfoam2412-dev
```

Then source the OpenFOAM environment:

```bash
source /usr/lib/openfoam/openfoam2412/etc/bashrc
```

## Build

```bash
bash scripts/build-rhoReactingBuoyantFoamSct.sh
```

The script:
1. Copies `applications/solvers/combustion/reactingFoam/rhoReactingBuoyantFoam`
   to `$WM_PROJECT_USER_DIR/applications/solvers/rhoReactingBuoyantFoamSct/`.
   (The solver source directory was reorganized in v2412; older releases
   had it under `heatTransfer/buoyantFoam/`.)
2. Copies the parent `reactingFoam/YEqn.H` into the user solver dir,
   then patches it: replaces `turbulence->muEff()` with
   `turbulence->muEff()/Sct` (Vu 2019 §A.2 Listing A.5 exact form).
   The C preprocessor picks up the local copy via the same-dir-first
   include rule, so the original `reactingFoam/YEqn.H` stays untouched.
3. Patches `createFields.H` to read `Sct` from `transportProperties`
   (defaults to 0.7 if absent — same as upstream behaviour).
4. Retargets `Make/files` so the binary lands in `$FOAM_USER_APPBIN/`
   and does not overwrite the stock solver.
5. Runs `wmake`.

Output binary: `$FOAM_USER_APPBIN/rhoReactingBuoyantFoamSct`.

## Use from DisperSim

For cryogenic / LNG cases, write `Sct` into `constant/transportProperties`
in addition to `constant/turbulenceProperties`:

```
Sct  0.15;
```

Then dispatch the patched binary instead of the stock one. The simplest
path is a wrapper script `rhoReactingBuoyantFoam` on `$PATH` that calls
`rhoReactingBuoyantFoamSct` when the case has `Sct` in
`transportProperties`, or to add a config flag in
`CfdConfigurationPresets.cs` that selects the patched binary for the
Vu 2019 preset.

## Expected validation gain

The Sct fix on its own is **necessary but not sufficient** to close the
three LNG FAILs (Burro 9, Coyote 3, Maplin Sands 27). Vu (2019)
Table 5.15 reports FAC2 = 1.0 on Burro 3/7/8/9 with the full setup:
Sct = 0.15 plus a refined mesh (4 m base, 1 m near source), a steady
ABL precursor, polynomial T-dependent thermophysics, and modified k-ε.
DisperSim today implements only the Sct piece end-to-end. See
`TODO.md` "Reproducing Vu (2019)" for the remaining gaps.

Empirically on Coyote 3 (DisperSim's 100³ uniform 8 m mesh, no
precursor, perfect-gas thermo): the patched solver runs cleanly via WSL
and the Sct value reaches `YEqn.H`, but the FAC2 stays around 0.4 to
0.6 instead of climbing to 1.0. The dominant remaining error is
numerical diffusion from the coarse mesh under linearUpwind convection,
not the Sct term.
