#!/usr/bin/env bash
# build-dmg.sh — build DisperSim3D-<version>-osx-<arch>.dmg (Avalonia UI + CLI).
#
#   packaging/macos/build-dmg.sh --version 1.0.0 [--rid osx-arm64|osx-x64]
#
# Steps:
#   1. make disp-bridge-macOS           -> FluidX3D/bin/libFluidX3D.dylib
#   2. dotnet publish (self-contained)  -> DisperSim 3D.app/Contents/MacOS
#   3. .icns from the shared PNG master, Info.plist, ad-hoc codesign
#   4. hdiutil create                   -> dist/DisperSim3D-<version>-osx-<arch>.dmg
#
# The bundle is ad-hoc signed (`codesign -s -`), which is the minimum Apple
# Silicon accepts for a locally built app. It is NOT notarised: on first launch
# macOS shows the unidentified-developer prompt and the user has to allow it
# from System Settings > Privacy & Security. Notarising needs a paid Developer
# ID — wire the certificate into this script when one exists.
set -euo pipefail

VERSION="0.0.0-dev"
RID=""
SKIP_NATIVE=0

while [ $# -gt 0 ]; do
    case "$1" in
        --version)     VERSION="$2"; shift 2 ;;
        --rid)         RID="$2";     shift 2 ;;
        --skip-native) SKIP_NATIVE=1; shift ;;
        -h|--help)     sed -n '2,17p' "$0"; exit 0 ;;
        *) echo "error: unknown argument '$1'" >&2; exit 2 ;;
    esac
done

# Default to the architecture we are running on: the native bridge is compiled
# here by g++/clang, so cross-publishing to the other RID would pair a managed
# osx-x64 app with an arm64 .dylib (or vice versa) and fail to load.
if [ -z "$RID" ]; then
    case "$(uname -m)" in
        arm64)  RID="osx-arm64" ;;
        x86_64) RID="osx-x64"   ;;
        *) echo "error: unsupported architecture $(uname -m)" >&2; exit 1 ;;
    esac
fi
ARCH="${RID#osx-}"

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
STAGE="$REPO_ROOT/build/dmg"
APP="$STAGE/DisperSim 3D.app"
DIST="$REPO_ROOT/dist"
# CFBundleShortVersionString must be a plain numeric x.y.z — strip any
# pre-release suffix (1.0.0-ci.42 -> 1.0.0) and keep the full string for the
# filename and the human-readable CFBundleGetInfoString.
SHORT_VERSION="$(printf '%s' "$VERSION" | sed -E 's/[-+].*$//')"

step() { printf '\033[96m==> %s\033[0m\n' "$*"; }

# ------------------------------------------------------------------ native ---
if [ "$SKIP_NATIVE" -eq 0 ]; then
    step "Building libFluidX3D.dylib"
    make -C "$REPO_ROOT/FluidX3D" disp-bridge-macOS -j"$(sysctl -n hw.ncpu 2>/dev/null || echo 4)"
fi
DYLIB="$REPO_ROOT/FluidX3D/bin/libFluidX3D.dylib"
[ -f "$DYLIB" ] || { echo "error: $DYLIB missing — run without --skip-native" >&2; exit 1; }

# ------------------------------------------------------------------ bundle ---
step "Publishing the Avalonia UI and the CLI ($RID, self-contained)"
rm -rf "$STAGE"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

publish() {
    dotnet publish "$1" \
        -c Release -r "$RID" --self-contained true \
        -p:Version="$VERSION" -p:DebugType=none \
        -o "$2" --nologo
}

publish "$REPO_ROOT/DisperSim3D.UI.Avalonia/DisperSim3D.UI.Avalonia.csproj" "$APP/Contents/MacOS"
# One folder for both apphosts: same TFM, same RID, so every shared assembly is
# byte-identical and each apphost still resolves through its own .deps.json.
# Publishing side by side would duplicate the self-contained runtime.
publish "$REPO_ROOT/DisperSim3D.CLI/DisperSim3D.CLI.csproj" "$APP/Contents/MacOS"

cp -f "$DYLIB" "$APP/Contents/MacOS/libFluidX3D.dylib"
chmod 755 "$APP/Contents/MacOS/DisperSim3D.UI.Avalonia" "$APP/Contents/MacOS/DisperSim3D.CLI"

# -------------------------------------------------------------------- icon ---
step "Generating DisperSim3D.icns"
ICONSET="$STAGE/DisperSim3D.iconset"
mkdir -p "$ICONSET"
SRC_PNG="$REPO_ROOT/packaging/assets/dispersim3d.png"
for size in 16 32 128 256 512; do
    sips -z "$size" "$size"           "$SRC_PNG" --out "$ICONSET/icon_${size}x${size}.png"      >/dev/null
    sips -z $((size * 2)) $((size * 2)) "$SRC_PNG" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/DisperSim3D.icns"
rm -rf "$ICONSET"

# --------------------------------------------------------------- Info.plist --
step "Writing Info.plist"
cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>                   <string>DisperSim 3D</string>
    <key>CFBundleDisplayName</key>            <string>DisperSim 3D</string>
    <key>CFBundleIdentifier</key>             <string>com.github.danwbr.dispersim3d</string>
    <key>CFBundleExecutable</key>             <string>DisperSim3D.UI.Avalonia</string>
    <key>CFBundleIconFile</key>               <string>DisperSim3D.icns</string>
    <key>CFBundlePackageType</key>            <string>APPL</string>
    <key>CFBundleInfoDictionaryVersion</key>  <string>6.0</string>
    <key>CFBundleShortVersionString</key>     <string>$SHORT_VERSION</string>
    <key>CFBundleVersion</key>                <string>$SHORT_VERSION</string>
    <key>CFBundleGetInfoString</key>          <string>DisperSim 3D $VERSION</string>
    <key>NSHumanReadableCopyright</key>       <string>Copyright © 2026 Daniel Wagner Oliveira de Medeiros</string>
    <key>LSApplicationCategoryType</key>      <string>public.app-category.productivity</string>
    <key>LSMinimumSystemVersion</key>         <string>12.0</string>
    <key>NSHighResolutionCapable</key>        <true/>
    <key>NSSupportsAutomaticGraphicsSwitching</key> <true/>
</dict>
</plist>
EOF

# ---------------------------------------------------------------- codesign ---
step "Ad-hoc signing the bundle"
# Sign the inner Mach-O binaries first: nested code must already carry a
# signature when the bundle's own signature seals it. Apple Silicon refuses to
# load an unsigned dylib, so an unsigned libFluidX3D.dylib would mean an app that
# starts and then cannot reach the GPU bridge — never silence these.
#
# -exec ... + rather than xargs: BSD xargs has no --no-run-if-empty, so the GNU
# spelling would either fail or run codesign with no arguments on a bundle that
# happens to carry no dylibs.
find "$APP/Contents/MacOS" \
    \( -name '*.dylib' -o -name '*.so' -o -name 'createdump' \) \
    -exec codesign --force --timestamp=none --sign - {} +
codesign --force --timestamp=none --sign - "$APP/Contents/MacOS/DisperSim3D.CLI"
codesign --force --timestamp=none --sign - "$APP"

# Verify WITHOUT --deep. The managed assemblies next to the app host are PE
# files that dyld never loads and codesign cannot sign, but --deep walks them
# and reports each one as an unsigned code object. Apple deprecated --deep for
# exactly this class of false positive; verifying the bundle seal is the check
# that means something here.
codesign --verify --strict "$APP"
echo "    signature OK"

# --------------------------------------------------------------------- dmg ---
step "Creating the disk image"
ln -s /Applications "$STAGE/Applications"
mkdir -p "$DIST"
DMG="$DIST/DisperSim3D-${VERSION}-osx-${ARCH}.dmg"
rm -f "$DMG"
hdiutil create -volname "DisperSim 3D" -srcfolder "$STAGE" \
    -fs HFS+ -format UDZO -ov -quiet "$DMG"

printf '\033[92m==> %s (%s)\033[0m\n' "$DMG" "$(du -h "$DMG" | cut -f1)"
