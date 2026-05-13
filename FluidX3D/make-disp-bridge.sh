#!/usr/bin/env bash
# make-disp-bridge.sh — build libFluidX3D.{so,dylib} for DisperSim 3D on
# Linux / macOS. The Windows build (FluidX3D.vcxproj) produces FluidX3D.dll
# from the same sources MINUS main.cpp PLUS disp_bridge.cpp; this script
# does the same for Unix.
#
# Usage:
#   ./make-disp-bridge.sh            # build into bin/libFluidX3D.{so,dylib}
#   ./make-disp-bridge.sh --copy     # also copy into ../DisperSim3D.CLI/bin/Release/net10.0/
#
# Requires:
#   - g++ with C++17 support (apt: build-essential / brew: clang)
#   - make (apt: make)
#   - An OpenCL runtime — the bundled src/OpenCL/lib/libOpenCL.so is used at
#     link time; at runtime the GPU driver's ICD loader (libOpenCL.so.1 from
#     mesa-opencl-icd, nvidia-opencl-icd, intel-opencl-icd, etc.) takes over.

set -euo pipefail
cd "$(dirname "$0")"

case "$(uname -s)" in
    Darwin*) target=disp-bridge-macOS; out=bin/libFluidX3D.dylib ;;
    Linux*)  target=disp-bridge-Linux; out=bin/libFluidX3D.so    ;;
    *)       echo "Error: unsupported OS '$(uname -s)' — this script targets Linux and macOS." >&2; exit 1 ;;
esac

if ! command -v make >/dev/null 2>&1; then
    echo "Error: 'make' not found. Install it (e.g. 'sudo apt install build-essential')." >&2
    exit 1
fi

echo -e "\033[92mInfo\033[0m: building ${out} via 'make ${target} -j$(nproc 2>/dev/null || echo 4)'"
make "${target}" -j"$(nproc 2>/dev/null || echo 4)"

echo -e "\033[92mInfo\033[0m: built $(ls -lh "${out}" | awk '{print $5, $9}')"

if [ "${1:-}" = "--copy" ]; then
    for tfm in net10.0 net10.0-windows; do
        for cfg in Release Debug; do
            for proj in DisperSim3D.CLI DisperSim3D.App DisperSim3D DisperSim3D.UI.Avalonia DisperSim3D.UI.Wpf; do
                dir="../${proj}/bin/${cfg}/${tfm}"
                if [ -d "${dir}" ]; then
                    cp -v "${out}" "${dir}/"
                fi
            done
        done
    done
fi

echo -e "\033[92mInfo\033[0m: done. Smoke-test from the CLI with:"
echo "    cp ${out} ../DisperSim3D.CLI/bin/Release/net10.0/"
echo "    dotnet ../DisperSim3D.CLI/bin/Release/net10.0/DisperSim3D.CLI.dll --list-gpus"
