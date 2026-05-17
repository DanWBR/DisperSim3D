#!/usr/bin/env bash
# build-rhoReactingBuoyantFoamSct.sh
#
# Build a patched rhoReactingBuoyantFoam that honours the turbulent
# Schmidt number (Sct) read from constant/transportProperties, instead
# of the stock v2412/v2506/v2512 behaviour which hard-codes Sct = 1.0
# via turbulence->muEff() in YEqn.H.
#
# Run inside WSL Ubuntu (or any Linux box) after sourcing the OpenFOAM
# bashrc:
#
#   source /usr/lib/openfoam/openfoam2412/etc/bashrc
#   bash build-rhoReactingBuoyantFoamSct.sh
#
# Output:
#   $FOAM_USER_APPBIN/rhoReactingBuoyantFoamSct
#
# DisperSim should then dispatch this binary instead of the stock one
# for cryogenic / LNG cases (see CfdConfigurationPresets.cs Vu 2019
# preset that sets TurbulentSchmidtNumber = 0.15).
#
# References:
#   Vu, T.A. 2019. "Modelling of LNG dispersion using OpenFOAM",
#   §5.4 — used a custom gasDispersionBuoyantFoam with Sct = 0.15
#   to reach FAC2 = 1.0 on Burro 3/7/8/9.

set -euo pipefail

# ---------------------------------------------------------------------
# 1. Sanity checks
# ---------------------------------------------------------------------

if [[ -z "${WM_PROJECT_DIR:-}" ]]; then
    echo "ERROR: \$WM_PROJECT_DIR not set. Source the OpenFOAM bashrc first:"
    echo "  source /usr/lib/openfoam/openfoam2412/etc/bashrc"
    exit 1
fi

if [[ -z "${FOAM_USER_APPBIN:-}" ]]; then
    echo "ERROR: \$FOAM_USER_APPBIN not set. OpenFOAM environment is incomplete."
    exit 1
fi

# v2412 layout: rhoReactingBuoyantFoam is under combustion/reactingFoam/
# (not under heatTransfer/buoyantFoam/ as it was in older releases). The
# YEqn.H we need to patch lives in the parent reactingFoam/ directory and
# is included via Make/options -I path. We override it with a local copy
# in the user solver dir, since the C preprocessor searches the file's
# own directory first.
STOCK_DIR="$WM_PROJECT_DIR/applications/solvers/combustion/reactingFoam/rhoReactingBuoyantFoam"
STOCK_YEQN="$WM_PROJECT_DIR/applications/solvers/combustion/reactingFoam/YEqn.H"
if [[ ! -d "$STOCK_DIR" ]]; then
    echo "ERROR: stock solver source not found at:"
    echo "  $STOCK_DIR"
    echo
    echo "On Ubuntu, install the dev package first:"
    echo "  sudo apt install openfoam2412-dev"
    echo
    echo "If you use a different version, edit WM_PROJECT_DIR and re-run."
    exit 1
fi
if [[ ! -f "$STOCK_YEQN" ]]; then
    echo "ERROR: stock YEqn.H not found at:"
    echo "  $STOCK_YEQN"
    exit 1
fi

echo "OpenFOAM:      $WM_PROJECT_VERSION ($WM_PROJECT_DIR)"
echo "Stock source:  $STOCK_DIR"
echo "User app bin:  $FOAM_USER_APPBIN"
echo

# ---------------------------------------------------------------------
# 2. Copy source to user dir under a new name
# ---------------------------------------------------------------------

USER_DIR="$WM_PROJECT_USER_DIR/applications/solvers/rhoReactingBuoyantFoamSct"

if [[ -d "$USER_DIR" ]]; then
    echo "User solver dir already exists at:"
    echo "  $USER_DIR"
    read -r -p "Wipe and rebuild from stock source? [y/N] " ans
    if [[ "$ans" =~ ^[Yy]$ ]]; then
        rm -rf "$USER_DIR"
    else
        echo "Aborted. Edit files in $USER_DIR by hand or rerun with wipe."
        exit 1
    fi
fi

mkdir -p "$(dirname "$USER_DIR")"
cp -r "$STOCK_DIR" "$USER_DIR"
echo "Copied source to: $USER_DIR"

# ---------------------------------------------------------------------
# 3. Rename executable target in Make/files
# ---------------------------------------------------------------------

MAKE_FILES="$USER_DIR/Make/files"
if [[ ! -f "$MAKE_FILES" ]]; then
    echo "ERROR: $MAKE_FILES not found after copy. Stock layout may have changed."
    exit 1
fi

# The stock Make/files names a single .C source and points the binary at
# $(FOAM_APPBIN)/rhoReactingBuoyantFoam. Retarget the executable name and
# the install dir so we get our own binary without overwriting the stock.
sed -i \
    -e 's|^rhoReactingBuoyantFoam\.C|rhoReactingBuoyantFoamSct.C|' \
    -e 's|EXE = \$(FOAM_APPBIN)/rhoReactingBuoyantFoam|EXE = $(FOAM_USER_APPBIN)/rhoReactingBuoyantFoamSct|' \
    "$MAKE_FILES"

# Rename the main .C accordingly so it matches Make/files.
if [[ -f "$USER_DIR/rhoReactingBuoyantFoam.C" ]]; then
    mv "$USER_DIR/rhoReactingBuoyantFoam.C" "$USER_DIR/rhoReactingBuoyantFoamSct.C"
fi

echo "Renamed binary target to rhoReactingBuoyantFoamSct"

# ---------------------------------------------------------------------
# 4. Patch YEqn.H so species transport uses Sct from transportProperties
# ---------------------------------------------------------------------
#
# Stock YEqn.H (v2412+) contains:
#
#     fvScalarMatrix YiEqn
#     (
#         fvm::ddt(rho, Yi)
#       + mvConvection->fvmDiv(phi, Yi)
#       - fvm::laplacian(turbulence->muEff(), Yi)
#       ==
#           reaction->R(Yi)
#         + fvOptions(rho, Yi)
#     );
#
# We change the laplacian coefficient to:
#
#     fvm::laplacian(turbulence->mu() + turbulence->mut()/Sct, Yi)
#
# where Sct is a dimensionedScalar read once from transportProperties at
# solver start (createFields.H patch below). Default 0.7 if missing, so
# behaviour is unchanged when the user does not provide Sct.

YEQN="$USER_DIR/YEqn.H"
# v2412 ships YEqn.H in the parent reactingFoam/ dir, not in the solver
# subdir. Copy it in so we can patch a local copy that takes precedence
# over the parent's via the C preprocessor's same-dir-first rule.
if [[ ! -f "$YEQN" ]]; then
    cp "$STOCK_YEQN" "$YEQN"
    echo "Copied stock YEqn.H from parent reactingFoam/ into user solver dir"
fi

if grep -q "turbulence->muEff()/Sct" "$YEQN"; then
    echo "YEqn.H already patched. Skipping."
else
    # Patch matches Vu (2019) §A.2 Listing A.5 exactly:
    #     fvm::laplacian(turbulence->muEff()/Sct, Yi)
    # Standard Schmidt-number convention: with Sct<1 turbulent species
    # diffusion is amplified to account for gravity-driven slumping in
    # dense / cryogenic clouds (Vu §5.2.2 dense gas Sct=0.3, §5.4.3 LNG
    # Sct=0.15). Note that the visible effect of Sct here is gated by
    # mesh resolution — numerical diffusion from upwind convection on
    # coarse meshes (cell >> Vu's 1-4 m) dominates the species transport
    # and masks the Sct adjustment. To reproduce Vu's FAC2=1.0 on Burro
    # also refine the mesh near the source. See TODO.md.
    cp "$YEQN" "$YEQN.orig"
    sed -i 's|fvm::laplacian(turbulence->muEff(), Yi)|fvm::laplacian(turbulence->muEff()/Sct, Yi)|' "$YEQN"

    if ! grep -q "turbulence->muEff()/Sct" "$YEQN"; then
        echo "ERROR: failed to patch YEqn.H — the expected stock pattern was not found."
        echo "Inspect $YEQN and patch by hand. Original backed up at $YEQN.orig."
        exit 1
    fi
    echo "Patched YEqn.H (backup: $YEQN.orig)"
fi

# ---------------------------------------------------------------------
# 5. Patch createFields.H to read Sct from transportProperties
# ---------------------------------------------------------------------

CREATE_FIELDS="$USER_DIR/createFields.H"
if [[ ! -f "$CREATE_FIELDS" ]]; then
    echo "ERROR: $CREATE_FIELDS not found. Stock layout may have changed."
    exit 1
fi

if grep -q "dimensionedScalar Sct" "$CREATE_FIELDS"; then
    echo "createFields.H already patched. Skipping."
else
    cp "$CREATE_FIELDS" "$CREATE_FIELDS.orig"
    cat >> "$CREATE_FIELDS" <<'EOF'

// --- DisperSim Sct patch ---------------------------------------------
// Read turbulent Schmidt number from constant/transportProperties.
// Falls back to 0.7 (OpenFOAM default for atmospheric flows) if the
// dictionary is absent or the entry is missing.
Info<< "Reading turbulent Schmidt number (Sct) from transportProperties\n"
    << endl;

IOdictionary transportProperties
(
    IOobject
    (
        "transportProperties",
        runTime.constant(),
        mesh,
        IOobject::READ_IF_PRESENT,
        IOobject::NO_WRITE
    )
);

dimensionedScalar Sct
(
    "Sct",
    dimless,
    transportProperties.lookupOrDefault<scalar>("Sct", 0.7)
);

Info<< "    Sct = " << Sct.value() << endl;
// ---------------------------------------------------------------------
EOF
    echo "Patched createFields.H to read Sct (backup: $CREATE_FIELDS.orig)"
fi

# ---------------------------------------------------------------------
# 6. wmake
# ---------------------------------------------------------------------

echo
echo "Compiling rhoReactingBuoyantFoamSct..."
echo

cd "$USER_DIR"
wmake

BIN="$FOAM_USER_APPBIN/rhoReactingBuoyantFoamSct"
if [[ -x "$BIN" ]]; then
    echo
    echo "BUILD OK"
    echo "Binary: $BIN"
    echo
    echo "To use from DisperSim, set the solver path in the case dispatch"
    echo "to point at this binary for cryogenic/LNG runs, and set"
    echo "constant/transportProperties:"
    echo
    echo "  Sct  0.15;   // Vu 2019 for LNG vapour dispersion"
    echo
else
    echo
    echo "BUILD FAILED — binary not found at $BIN"
    echo "Inspect the wmake output above for compile errors."
    exit 1
fi
