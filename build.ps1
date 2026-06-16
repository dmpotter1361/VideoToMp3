<#
    Reproducible build for Video to MP3.
    Publishes a self-contained, single-file Windows build and zips it for release.
    Paths are computed from this script's location, so it works from any clone.

    Usage:  pwsh ./build.ps1 [-Version 1.0.0]
    Requires: .NET 10 SDK
#>
param([string]$Version = "1.0.0")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$pub  = Join-Path $root "publish"
$dist = Join-Path $root "dist"
$proj = Join-Path $root "VideoToMp3.csproj"
$zip  = Join-Path $dist "VideoToMp3-$Version-x64.zip"

Write-Host "Publishing self-contained single-file build..." -ForegroundColor Cyan
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version -o $pub

Write-Host "Packaging zip ($Version)..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $dist | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pub "VideoToMp3.exe") -DestinationPath $zip

Write-Host "Done: $zip" -ForegroundColor Green
