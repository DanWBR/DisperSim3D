#!/usr/bin/env bash
# build-deb.sh — build dispersim3d_<version>_amd64.deb (Avalonia UI + headless CLI).
#
#   packaging/linux/build-deb.sh --version 1.0.0
#
# Steps:
#   1. make disp-bridge-Linux            -> FluidX3D/bin/libFluidX3D.so
#   2. dotnet publish (self-contained)   -> build/deb/opt/dispersim3d
#   3. desktop entry, hicolor icons, /usr/bin wrappers, DEBIAN/control
#   4. dpkg-deb --build                  -> dist/dispersim3d_<version>_amd64.deb
#
# The payload is a self-contained .NET 10 publish, so the package has no
# dotnet-runtime dependency. The glibc floor in Depends is read straight out of
# the freshly compiled libFluidX3D.so, which is the only thing here built
# against the host's C library — build on the oldest distro you want to support.
set -euo pipefail

VERSION="0.0.0-dev"
ARCH="amd64"
RID="linux-x64"
SKIP_NATIVE=0
SKIP_PUBLISH=0

while [ $# -gt 0 ]; do
    case "$1" in
        --version)      VERSION="$2"; shift 2 ;;
        --arch)         ARCH="$2";    shift 2 ;;
        --rid)          RID="$2";     shift 2 ;;
        --skip-native)  SKIP_NATIVE=1; shift ;;
        --skip-publish) SKIP_PUBLISH=1; shift ;;
        -h|--help)      sed -n '2,20p' "$0"; exit 0 ;;
        *) echo "error: unknown argument '$1'" >&2; exit 2 ;;
    esac
done

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
STAGE="$REPO_ROOT/build/deb"
APPDIR="$STAGE/opt/dispersim3d"
DIST="$REPO_ROOT/dist"
# Debian versions have no '-' in the upstream part when there is no revision,
# and '~' sorts *before* the release it prefixes — exactly what a CI
# pre-release build wants (1.0.0~ci42 < 1.0.0).
DEB_VERSION="$(printf '%s' "$VERSION" | tr '-' '~')"

step() { printf '\033[96m==> %s\033[0m\n' "$*"; }

# ------------------------------------------------------------------ native ---
if [ "$SKIP_NATIVE" -eq 0 ]; then
    step "Building libFluidX3D.so"
    make -C "$REPO_ROOT/FluidX3D" disp-bridge-Linux -j"$(nproc 2>/dev/null || echo 4)"
fi
SO="$REPO_ROOT/FluidX3D/bin/libFluidX3D.so"
[ -f "$SO" ] || { echo "error: $SO missing — run without --skip-native" >&2; exit 1; }

# ----------------------------------------------------------------- publish ---
step "Publishing the Avalonia UI and the CLI ($RID, self-contained)"
rm -rf "$STAGE"
mkdir -p "$APPDIR"

publish() {
    dotnet publish "$1" \
        -c Release -r "$RID" --self-contained true \
        -p:Version="$VERSION" -p:DebugType=none \
        -o "$2" --nologo
}

if [ "$SKIP_PUBLISH" -eq 0 ]; then
    publish "$REPO_ROOT/DisperSim3D.UI.Avalonia/DisperSim3D.UI.Avalonia.csproj" "$APPDIR"
    # One folder for both apphosts. Here — unlike on Windows, where the shell
    # is net10.0-windows and the CLI is net10.0 — the two targets are the same
    # TFM and RID, so every shared assembly is byte-identical and each apphost
    # still resolves through its own .deps.json. Publishing side by side would
    # duplicate the ~115 MB self-contained runtime.
    publish "$REPO_ROOT/DisperSim3D.CLI/DisperSim3D.CLI.csproj" "$APPDIR"
fi

# The csproj copies the .so when it exists at evaluation time; copy it again so
# a stale evaluation cannot leave the package without its bridge.
install -m 644 "$SO" "$APPDIR/libFluidX3D.so"

# ------------------------------------------------------------------ layout ---
step "Assembling the package tree"
install -d "$STAGE/usr/bin" "$STAGE/usr/share/applications" \
           "$STAGE/usr/share/doc/dispersim3d" "$STAGE/DEBIAN"

cat > "$STAGE/usr/bin/dispersim3d" <<'EOF'
#!/bin/sh
# DisperSim 3D — Avalonia desktop UI.
exec /opt/dispersim3d/DisperSim3D.UI.Avalonia "$@"
EOF

cat > "$STAGE/usr/bin/dispersim3d-cli" <<'EOF'
#!/bin/sh
# DisperSim 3D — headless simulation runner.
exec /opt/dispersim3d/DisperSim3D.CLI "$@"
EOF

install -m 644 "$REPO_ROOT/packaging/linux/dispersim3d.desktop" \
               "$STAGE/usr/share/applications/dispersim3d.desktop"
install -m 644 "$REPO_ROOT/LICENSE" "$STAGE/usr/share/doc/dispersim3d/copyright"

# Icons: one 512px master in the repo, downscaled into the hicolor theme.
ICON_SRC="$REPO_ROOT/packaging/assets/dispersim3d.png"
if command -v magick >/dev/null 2>&1; then IM="magick"
elif command -v convert >/dev/null 2>&1; then IM="convert"
else echo "error: ImageMagick not found (apt install imagemagick)" >&2; exit 1; fi
for size in 16 32 48 64 128 256 512; do
    dir="$STAGE/usr/share/icons/hicolor/${size}x${size}/apps"
    install -d "$dir"
    "$IM" "$ICON_SRC" -resize "${size}x${size}" "$dir/dispersim3d.png"
    chmod 644 "$dir/dispersim3d.png"
done

# --------------------------------------------------------------- metadata ---
# glibc floor: highest GLIBC_x.y symbol version the native bridge references.
GLIBC_MIN="$(objdump -T "$SO" 2>/dev/null \
    | grep -oE 'GLIBC_[0-9]+\.[0-9]+' | sed 's/GLIBC_//' | sort -V | tail -1)"
GLIBC_MIN="${GLIBC_MIN:-2.35}"
step "Native bridge needs glibc >= $GLIBC_MIN"

INSTALLED_KB="$(du -ks --exclude=DEBIAN "$STAGE" | cut -f1)"

cat > "$STAGE/DEBIAN/control" <<EOF
Package: dispersim3d
Version: $DEB_VERSION
Section: science
Priority: optional
Architecture: $ARCH
Maintainer: Daniel Wagner Oliveira de Medeiros <danielwag@gmail.com>
Homepage: https://github.com/DanWBR/DisperSim3D
Installed-Size: $INSTALLED_KB
Depends: libc6 (>= $GLIBC_MIN), libgcc-s1, libstdc++6, zlib1g,
 libicu76 | libicu74 | libicu72 | libicu71 | libicu70,
 libssl3t64 | libssl3,
 libx11-6, libxrandr2, libxi6, libxcursor1, libxext6, libfontconfig1,
 libgl1, libegl1,
 fonts-dejavu-core | fonts-liberation | fonts-freefont-ttf,
 ocl-icd-libopencl1 | libopencl1
Recommends: pocl-opencl-icd
Suggests: openfoam
Description: 3D gas dispersion analysis for process safety
 DisperSim 3D builds a 3D scene of a process plant, defines leak sources and
 gases, and runs Gaussian, CFD (OpenFOAM) or GPU lattice-Boltzmann (FluidX3D /
 OpenCL) dispersion simulations. It visualises the resulting concentration
 fields and optimises gas detector placement against IOGP leak frequencies.
 .
 This package ships the cross-platform Avalonia desktop UI (dispersim3d) and
 the headless runner (dispersim3d-cli), both as a self-contained .NET 10
 build — no dotnet runtime is required on the target system.
 .
 GPU simulations need an OpenCL ICD: install a vendor driver for GPU speed, or
 pocl-opencl-icd for a universal CPU fallback.
EOF

cat > "$STAGE/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "configure" ]; then
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q /usr/share/applications || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -f /usr/share/icons/hicolor || true
    fi
fi
EOF

cat > "$STAGE/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q /usr/share/applications || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -f /usr/share/icons/hicolor || true
    fi
fi
EOF

# ------------------------------------------------------------ permissions ---
# The publish output can carry whatever umask (or 777 from a /mnt/c checkout)
# the build host had; normalise so dpkg-deb writes a lintian-clean tree.
find "$STAGE" -type d -exec chmod 755 {} +
find "$STAGE" -type f -exec chmod 644 {} +
chmod 755 "$STAGE/usr/bin/dispersim3d" "$STAGE/usr/bin/dispersim3d-cli" \
          "$STAGE/DEBIAN/postinst" "$STAGE/DEBIAN/postrm"
chmod 755 "$APPDIR/DisperSim3D.UI.Avalonia" "$APPDIR/DisperSim3D.CLI"
find "$STAGE/opt" -name '*.so' -exec chmod 755 {} +
find "$STAGE/opt" -name 'createdump' -exec chmod 755 {} +

# md5sums lets `dpkg -V` verify the installed files.
( cd "$STAGE" && find . -path ./DEBIAN -prune -o -type f -print0 \
    | sed -z 's|^\./||' | xargs -0 md5sum > DEBIAN/md5sums )
chmod 644 "$STAGE/DEBIAN/md5sums"

# ------------------------------------------------------------------ build ---
step "dpkg-deb --build"
mkdir -p "$DIST"
DEB="$DIST/dispersim3d_${DEB_VERSION}_${ARCH}.deb"
dpkg-deb --root-owner-group --build "$STAGE" "$DEB"

printf '\033[92m==> %s (%s)\033[0m\n' "$DEB" "$(du -h "$DEB" | cut -f1)"
