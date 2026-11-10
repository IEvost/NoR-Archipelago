# Packages apworld/nightofrevenge into nightofrevenge.apworld (a zip with the package folder at its root).
# Usage: pwsh tools/package-apworld.ps1 [-Destination <folder>]   (default: .tools/Archipelago/custom_worlds)
param([string]$Destination)

$root = Split-Path -Parent $PSScriptRoot
if (-not $Destination) { $Destination = Join-Path $root ".tools/Archipelago/custom_worlds" }
New-Item -ItemType Directory -Force $Destination | Out-Null

$src = Join-Path $root "apworld/nightofrevenge"
$out = Join-Path (Resolve-Path $Destination) "nightofrevenge.apworld"
if (Test-Path $out) { Remove-Item -LiteralPath $out -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($out, 'Create')
try {
    $srcFull = (Resolve-Path $src).Path
    Get-ChildItem $srcFull -Recurse -File | Where-Object { $_.FullName -notmatch '__pycache__' } | ForEach-Object {
        $rel = "nightofrevenge/" + $_.FullName.Substring($srcFull.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $rel) | Out-Null
    }
}
finally { $zip.Dispose() }
Write-Output "Packaged $out"
