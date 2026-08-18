<#
.SYNOPSIS
    Builds the Windows installer (DisperSim3D-<version>-win-x64-setup.exe).

.DESCRIPTION
    Three steps, all runnable on a clean clone:

      1. MSBuild FluidX3D.vcxproj  -> bin\FluidX3D.dll (the OpenCL/LBM bridge)
      2. dotnet publish            -> build\win-x64\  (WinForms shell + CLI,
                                      self-contained win-x64, no runtime
                                      prerequisite on the target machine)
      3. ISCC DisperSim3D.iss      -> dist\*-setup.exe

    Same script the CI workflow calls, so a local reproduction of a release
    build is `packaging\windows\build-installer.ps1 -Version 1.0.0`.

.PARAMETER Version
    Version stamped into the installer and its filename. Defaults to 0.0.0-dev.

.PARAMETER SkipNative
    Reuse an existing bin\FluidX3D.dll instead of invoking MSBuild. Useful when
    iterating on the installer itself.

.PARAMETER SkipInstaller
    Stop after the publish step. Produces build\win-x64\ without needing Inno
    Setup installed.
#>
[CmdletBinding()]
param(
    [string]$Version = "0.0.0-dev",
    [switch]$SkipNative,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $PSCommandPath
$repoRoot  = Resolve-Path (Join-Path $scriptDir "..\..")
$payload   = Join-Path $repoRoot "build\win-x64"
$distDir   = Join-Path $repoRoot "dist"

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

# ---------------------------------------------------------------- native ----
if (-not $SkipNative) {
    Write-Step "Building FluidX3D.dll (MSBuild, Release|x64)"
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found — install Visual Studio with the C++ workload." }
    $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
               Select-Object -First 1
    if (-not $msbuild) { throw "MSBuild not found — install the 'Desktop development with C++' workload." }

    & $msbuild (Join-Path $repoRoot "FluidX3D\FluidX3D.vcxproj") `
        /p:Configuration=Release /p:Platform=x64 /v:minimal /nologo /m
    if ($LASTEXITCODE -ne 0) { throw "FluidX3D build failed" }
}

$fluidDll = Join-Path $repoRoot "bin\FluidX3D.dll"
if (-not (Test-Path $fluidDll)) { throw "bin\FluidX3D.dll missing — run without -SkipNative." }

# --------------------------------------------------------------- publish ----
Write-Step "Publishing DisperSim3D.App (self-contained win-x64) -> $payload"
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
& dotnet publish (Join-Path $repoRoot "DisperSim3D.App\DisperSim3D.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:DebugType=none `
    -o $payload --nologo
if ($LASTEXITCODE -ne 0) { throw "DisperSim3D.App publish failed" }

# The headless runner goes into a subfolder: it targets plain net10.0 while the
# shell targets net10.0-windows, so publishing both into one directory would
# have the two DisperSim3D.dll flavours overwrite each other.
Write-Step "Publishing DisperSim3D.CLI -> $payload\cli"
& dotnet publish (Join-Path $repoRoot "DisperSim3D.CLI\DisperSim3D.CLI.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:DebugType=none `
    -o (Join-Path $payload "cli") --nologo
if ($LASTEXITCODE -ne 0) { throw "DisperSim3D.CLI publish failed" }

# Both csproj files copy ..\bin\FluidX3D.dll when it exists at evaluation time;
# copy it again so the payload is correct even if the DLL appeared later.
Copy-Item $fluidDll (Join-Path $payload "FluidX3D.dll") -Force
Copy-Item $fluidDll (Join-Path $payload "cli\FluidX3D.dll") -Force

$sizeMb = [Math]::Round(((Get-ChildItem $payload -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "    payload: $sizeMb MB" -ForegroundColor DarkGray

if ($SkipInstaller) { Write-Step "Done (installer skipped)"; return }

# ------------------------------------------------------------- installer ----
Write-Step "Compiling the installer (Inno Setup)"
$iscc = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) { throw "ISCC.exe not found — install Inno Setup 6 (winget install JRSoftware.InnoSetup)." }

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
# The VERSIONINFO resource only takes digits and dots, so hand the .iss the
# stripped form alongside the display version (1.0.0-ci.42 -> 1.0.0).
$numericVersion = ($Version -split '[-+]')[0]
if ($numericVersion -notmatch '^\d+(\.\d+){0,3}$') { $numericVersion = "0.0.0" }

& $iscc "/DAppVersion=$Version" "/DAppVersionNumeric=$numericVersion" `
    "/DRepoRoot=$repoRoot" "/DPayloadDir=$payload" "/DOutputDir=$distDir" `
    (Join-Path $scriptDir "DisperSim3D.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit $LASTEXITCODE" }

Get-ChildItem $distDir -Filter "*-setup.exe" | ForEach-Object {
    Write-Host "==> $($_.FullName) ($([Math]::Round($_.Length / 1MB, 1)) MB)" -ForegroundColor Green
}
