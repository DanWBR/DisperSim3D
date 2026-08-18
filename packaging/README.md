# Packaging

Installer / package builders for the three desktop targets. Each script is what
[`.github/workflows/installers.yml`](../.github/workflows/installers.yml) runs,
so a release build can always be reproduced by hand on the matching OS.

| Target | Script | Output | Ships |
|---|---|---|---|
| Windows | `windows/build-installer.ps1` | `dist/DisperSim3D-<ver>-win-x64-setup.exe` | WinForms shell (`DisperSim3D.App`) + CLI |
| Linux | `linux/build-deb.sh` | `dist/dispersim3d_<ver>_amd64.deb` | Avalonia UI + CLI |
| macOS | `macos/build-dmg.sh` | `dist/DisperSim3D-<ver>-osx-<arch>.dmg` | Avalonia UI + CLI |

Every payload is a **self-contained .NET 10 publish**: no runtime prerequisite
on the target machine. Each one also carries the native FluidX3D bridge for its
platform, compiled from `FluidX3D/src` as part of the build.

The Windows package ships the WinForms shell because that is the production
Windows UI (HelixToolkit / HandyControl / DockPanelSuite have no cross-platform
equivalent); Linux and macOS ship the Avalonia UI, which is the same engine
behind a portable front end.

## Building locally

```powershell
# Windows — needs VS with the C++ workload (MSBuild + v143) and Inno Setup 6
packaging\windows\build-installer.ps1 -Version 1.0.0
```

```bash
# Linux — needs build-essential, imagemagick, dpkg-deb and the .NET 10 SDK
packaging/linux/build-deb.sh --version 1.0.0
```

```bash
# macOS — needs the Xcode command line tools and the .NET 10 SDK
packaging/macos/build-dmg.sh --version 1.0.0
```

All three accept `--skip-native` / `-SkipNative` to reuse an already compiled
FluidX3D bridge, which is the fast path when iterating on packaging itself.

## CI

`Installers` runs on every push to `master`, on pull requests, and on demand.
Pushing a `v*` tag additionally publishes the three files plus `SHA256SUMS.txt`
as a GitHub release:

```bash
git tag -a v1.0.0 -m "1.0.0" && git push origin v1.0.0
```

Untagged runs stamp `<csproj version>-ci.<run number>` and only upload build
artifacts.

## Platform notes

**Windows.** The installer is per-machine by default; `/CURRENTUSER` on the
command line installs into `%LOCALAPPDATA%` without admin rights. `AppId` in
`DisperSim3D.iss` is what makes an install replace the previous version instead
of stacking — never change it.

**Linux.** The `.deb` is built on the CI image's glibc and the `libc6` floor in
`Depends` is read out of the compiled `libFluidX3D.so`, so the package refuses
to install on an older distro rather than failing at runtime. To support an
older baseline, build the package on that distro. GPU runs need an OpenCL ICD —
a vendor driver, or `pocl-opencl-icd` for a universal CPU fallback.

**macOS.** The `.app` is ad-hoc signed (`codesign -s -`), the minimum Apple
Silicon accepts, but it is **not notarised**: first launch shows the
unidentified-developer prompt and has to be allowed from
System Settings → Privacy & Security. Notarisation needs a paid Developer ID —
wire it into `build-dmg.sh` when one exists. The runner is Apple Silicon, so
the published `.dmg` is `osx-arm64`; an Intel `.dmg` needs an Intel runner (the
native bridge is compiled by the runner, so cross-publishing alone is not
enough).

## Icon

`assets/dispersim3d.png` (512×512) is the packaging master, upscaled from the
largest frame of `DisperSim3D/Resources/Icons/Air.ico` — the same icon the
Windows shell and the Avalonia window use. The Linux script downscales it into
the hicolor theme, the macOS script turns it into an `.icns`; Windows uses the
`.ico` directly.
