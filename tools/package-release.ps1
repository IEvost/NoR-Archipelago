# Builds a player release zip: BepInEx plugins (without the dev-only data dumper), translations, bridge, apworld, docs.
# Usage: pwsh tools/package-release.ps1 [-Version 0.1.0]
# Output: dist/NoR-Archipelago-<version>.zip  (extract into the game folder; contains no game files)
param([string]$Version = "0.1.0")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# 1. Fresh builds (no deploy into the local game)
dotnet publish tools/NoR.ApBridge -c Release -o .tools/bridge-publish -v q | Out-Null
dotnet build NoR.Archipelago.slnx -c Release -p:DeployToGame=false -v q | Out-Null

# 2. Stage
$stage = Join-Path $root "dist/stage"
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$plugins = Join-Path $stage "BepInEx/plugins"
New-Item -ItemType Directory -Force $plugins | Out-Null

function Copy-Plugin($project, $files) {
    $dst = Join-Path $plugins $project
    New-Item -ItemType Directory -Force $dst | Out-Null
    $bin = Join-Path $root "src/$project/bin/Release"
    foreach ($f in $files) { Copy-Item (Join-Path $bin $f) $dst }
}
Copy-Plugin "NoR.Randomizer" @("NoR.Randomizer.dll", "NoR.Core.dll")
Copy-Plugin "NoR.Localization" @("NoR.Localization.dll", "NoR.Core.dll")
Copy-Plugin "NoR.Widescreen" @("NoR.Widescreen.dll")
Copy-Plugin "NoR.Archipelago" @("NoR.Archipelago.dll", "NoR.Core.dll", "Archipelago.MultiClient.Net.dll", "websocket-sharp.dll", "Newtonsoft.Json.dll")
Copy-Item ".tools/bridge-publish/NoR.ApBridge.exe" (Join-Path $plugins "NoR.Archipelago")

# Translation tables (hash + English only; no Japanese game text)
Copy-Item "data/translation" (Join-Path $plugins "NoR.Localization/translation") -Recurse
Remove-Item (Join-Path $plugins "NoR.Localization/translation/missing_seen.txt") -ErrorAction SilentlyContinue

# 3. apworld + docs
& (Join-Path $PSScriptRoot "package-apworld.ps1") -Destination $stage | Out-Null
Copy-Item README.md, LICENSE, THIRD_PARTY_NOTICES.md $stage
Copy-Item apworld/nightofrevenge/docs/setup_en.md (Join-Path $stage "SETUP.md")

# 4. Zip
$dist = Join-Path $root "dist"
$zip = Join-Path $dist "NoR-Archipelago-$Version.zip"
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Remove-Item -LiteralPath $stage -Recurse -Force
Write-Output "Release: $zip"
