# bundle-dwsimcore.ps1
# Builds DWSIMCore.Foundation (the .NET 10 thermodynamics core) and merges its
# managed dependencies into a single DLL using ILRepack. The merged DLL is
# copied into ../lib/DWSIMCore/ so DisperSim3D can reference it as a binary
# reference instead of a project reference (decouples from the DWSIMCore
# source tree for redistribution).
#
# Usage: powershell -ExecutionPolicy Bypass -File scripts\bundle-dwsimcore.ps1
#
# Requires: dotnet 10 SDK and `dotnet tool install --global dotnet-ilrepack`.

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $PSCommandPath
$repoRoot = Split-Path -Parent $scriptDir
$libDir = Join-Path $repoRoot "lib\DWSIMCore"
$dwsimSrc = Resolve-Path (Join-Path $repoRoot "..\DWSIMCore\DWSIMCore.Foundation\DWSIMCore.Foundation.vbproj")
# Use publish (not build) so all NuGet runtime deps land in one folder ready
# to be merged. Publish to a scratch folder so we don't pollute the source bin.
$dwsimBin = Join-Path $env:TEMP "dwsimcore-publish"

Write-Host "==> Publishing DWSIMCore.Foundation (Release) → $dwsimBin" -ForegroundColor Cyan
if (Test-Path $dwsimBin) { Remove-Item $dwsimBin -Recurse -Force }
& dotnet publish $dwsimSrc -c Release -f net10.0 -o $dwsimBin --verbosity minimal | Out-Null
if ($LASTEXITCODE -ne 0) { throw "DWSIMCore publish failed" }

Write-Host "==> Locating output assemblies in $dwsimBin" -ForegroundColor Cyan
if (-not (Test-Path $dwsimBin)) { throw "Build output not found at $dwsimBin" }

# Primary assembly: DWSIMCore.Foundation.dll. Everything else gets merged in.
$primary = Join-Path $dwsimBin "DWSIMCore.Foundation.dll"
if (-not (Test-Path $primary)) { throw "Primary assembly not found at $primary" }

# Collect ALL managed DLLs in the output folder EXCEPT system / framework ones.
# .NET 10 supplies System.* and Microsoft.Extensions.* at runtime; merging
# them would either fail (signed-assembly conflict) or bloat the bundle.
$excludePatterns = @(
    "System\..*\.dll$",
    "^Microsoft\.Extensions\..*\.dll$",
    "^Microsoft\.CSharp\.dll$",
    "^netstandard\.dll$",
    "^mscorlib\.dll$",
    "^Mono\.Unix\.dll$"    # Unix-only PInvoke wrapper, not needed on Windows
)
$allDlls = Get-ChildItem $dwsimBin -Filter "*.dll" |
    Where-Object {
        $name = $_.Name
        $name -ne "DWSIMCore.Foundation.dll" -and
        -not ($excludePatterns | Where-Object { $name -match $_ })
    } |
    ForEach-Object { $_.FullName }

Write-Host "==> Will merge $($allDlls.Count) assemblies into single DLL:" -ForegroundColor Cyan
$allDlls | ForEach-Object { Write-Host "    $(Split-Path -Leaf $_)" -ForegroundColor DarkGray }

$outputDir = Join-Path $libDir "net10.0"
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$mergedDll = Join-Path $outputDir "DWSIMCore.Foundation.dll"

Write-Host "==> Running ILRepack" -ForegroundColor Cyan
$ilrepackArgs = @(
    "/lib:$dwsimBin",
    "/out:$mergedDll",
    "/internalize",        # hide merged-in types from outside callers
    "/copyattrs",          # carry assembly attributes from primary
    "/parallel",
    "/keyfile:",           # no signing
    $primary
) + $allDlls

# /keyfile: empty arg pattern: drop it
$ilrepackArgs = $ilrepackArgs | Where-Object { $_ -ne "/keyfile:" }

& ilrepack @ilrepackArgs
if ($LASTEXITCODE -ne 0) { throw "ILRepack failed with exit $LASTEXITCODE" }

$mergedSize = (Get-Item $mergedDll).Length / 1MB
Write-Host "==> Bundled DLL written: $mergedDll ($([Math]::Round($mergedSize,1)) MB)" -ForegroundColor Green

# Copy embedded resources (data files needed at runtime) — these are inside
# the merged DLL, so nothing extra needed. Just verify by listing manifest.
$asm = [System.Reflection.Assembly]::LoadFile($mergedDll)
$nDb = ($asm.GetManifestResourceNames() | Where-Object { $_ -match "chemsep|dwsim\.xml|chedl|electrolyte" }).Count
Write-Host "==> Embedded database resources: $nDb" -ForegroundColor Green

Write-Host ""
Write-Host "Done. To consume from DisperSim3D, replace the ProjectReference in" -ForegroundColor Yellow
Write-Host "DisperSim3D.csproj with:" -ForegroundColor Yellow
Write-Host '  <Reference Include="DWSIMCore.Foundation">' -ForegroundColor Yellow
Write-Host '    <HintPath>$(MSBuildThisFileDirectory)..\lib\DWSIMCore\net10.0\DWSIMCore.Foundation.dll</HintPath>' -ForegroundColor Yellow
Write-Host '  </Reference>' -ForegroundColor Yellow
