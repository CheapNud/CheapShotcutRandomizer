# Release pipeline driven by .forgejo/workflows/release.yml. Inputs arrive as
# env vars (REF_NAME, FORGE_TOKEN, REPO) so nothing gets interpolated into code.
$ErrorActionPreference = 'Stop'

# The tag is a trust boundary: passed via env (never interpolated into the
# script) and validated before it touches any command line
if ($env:REF_NAME -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+$') { throw "unexpected tag format: $env:REF_NAME" }
$ver = $env:REF_NAME.TrimStart('v')

powershell -ExecutionPolicy Bypass -File deploy/publish.ps1 -Version $ver
if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }

$token = $env:FORGE_TOKEN
$api = "http://192.168.1.15:3000/api/v1/repos/$($env:REPO)"
$repoUrl = "http://192.168.1.15:3000/$($env:REPO)"

# Velopack owns packaging and the release: Setup.exe (installer with
# auto-update), its own Portable.zip, full/delta nupkgs and the update
# manifest all get uploaded to a release it creates for this tag.
dotnet tool update -g vpk
if ($LASTEXITCODE -ne 0) { throw "vpk install failed ($LASTEXITCODE)" }

# Previous release as delta base - best-effort (first release has none)
vpk download gitea --repoUrl $repoUrl --token $token
if ($LASTEXITCODE -ne 0) { Write-Host "No previous Velopack release for delta base (ok)" }

vpk pack --packId CheapShotcutRandomizer --packVersion $ver `
         --packDir deploy/out/CheapShotcutRandomizer `
         --mainExe CheapShotcutRandomizer.exe `
         --packTitle "Cheap Shotcut Randomizer" `
         --packAuthors "CheapNud"
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)" }

vpk upload gitea --repoUrl $repoUrl --token $token --publish `
          --tag $env:REF_NAME --releaseName "CheapShotcutRandomizer $ver"
if ($LASTEXITCODE -ne 0) { throw "vpk upload failed ($LASTEXITCODE)" }

Write-Host "Published release $ver via Velopack (Setup.exe + Portable.zip + update packages)"

# Retention: keep only the newest 3 releases. Tags always stay, so any old
# build remains reproducible. Best-effort - pruning must never fail a release.
try {
  $releases = Invoke-RestMethod -Uri "$api/releases?limit=50" -Headers @{ Authorization = "token $token" }
  $stale = $releases | Sort-Object { [datetime]$_.created_at } -Descending | Select-Object -Skip 3
  foreach ($old in $stale) {
    Invoke-RestMethod -Method Delete -Uri "$api/releases/$($old.id)" -Headers @{ Authorization = "token $token" } | Out-Null
    Write-Host "Pruned old release $($old.tag_name)"
  }
} catch {
  Write-Host "Release pruning skipped: $($_.Exception.Message)"
}
