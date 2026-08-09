# Publishes a self-contained win-x64 build of CheapShotcutRandomizer to deploy\out\CheapShotcutRandomizer.
# One folder, one exe to double-click — no .NET install needed on the target machine.
# Run from anywhere: .\deploy\publish.ps1 [-Version 2.0.0]
#
# Notes:
# - Single-file bundling packs the managed + native code into CheapShotcutRandomizer.exe;
#   wwwroot static assets (MudBlazor _content, JS bridge) still ship as files beside
#   it — a Blazor host serves them from disk, so "one exe" means "one folder with
#   one exe", not a lone file.
# - NO trimming: Blazor, Avalonia and the XAML loader rely on reflection that
#   trimming breaks silently.
param(
    [string]$Version = "2.0.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $PSScriptRoot "out\CheapShotcutRandomizer"

if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }

# DebugType=none drops managed pdbs AND the native symbol files that
# otherwise ship in the folder.
dotnet publish (Join-Path $repoRoot "CheapShotcutRandomizer.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $outDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Belt and braces: native pdbs are content-copied by some packages regardless.
Get-ChildItem $outDir -Filter "*.pdb" | Remove-Item -Force

$exe = Join-Path $outDir "CheapShotcutRandomizer.exe"
$sizeMb = [math]::Round((Get-ChildItem $outDir -Recurse | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host ""
Write-Host "Published $Version -> $outDir ($sizeMb MB)"
Write-Host "Entry point: $exe"
