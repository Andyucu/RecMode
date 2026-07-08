#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Produce the RecMode portable folder + zip (plan §3.5).
.DESCRIPTION
    Self-contained win-x64 publish of RecMode.App, then assembles the portable layout:
        RecMode.exe + runtime files
        portable.marker
        ffmpeg\        (copied from tools\ffmpeg if present; otherwise a README placeholder)
        licenses\      (copied from .\licenses)
    Data\ and Recordings\ are created by the app at first run. Finally zips the folder.
.EXAMPLE
    ./publish-portable.ps1
    ./publish-portable.ps1 -Version 0.1.0
#>
[CmdletBinding()]
param(
    # 0.9.x-beta scheme: bump x on every new build (see CLAUDE.md working notes).
    [string]$Version = '0.9.8-beta',
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'artifacts')
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$app = Join-Path $root 'src/RecMode.App/RecMode.App.csproj'
$stage = Join-Path $OutputRoot "RecMode-$Version-portable-win-x64"
$zip = "$stage.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Write-Host "==> Publishing self-contained win-x64 (ReadyToRun)" -ForegroundColor Cyan
dotnet publish $app -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:IsPublishable=true -p:Version=$Version `
    -o $stage

# portable.marker ships via CopyToOutputDirectory, but guarantee it here too.
$marker = Join-Path $stage 'portable.marker'
if (-not (Test-Path $marker)) {
    Set-Content -Path $marker -Value 'RecMode portable-mode marker.'
}

# ffmpeg: copy the bundled build if the dev machine has one staged under tools\ffmpeg.
$ffmpegSrc = Join-Path $root 'tools/ffmpeg'
$ffmpegDst = Join-Path $stage 'ffmpeg'
New-Item -ItemType Directory -Force -Path $ffmpegDst | Out-Null
if ((Test-Path $ffmpegSrc) -and (Get-ChildItem $ffmpegSrc -Filter *.exe -ErrorAction SilentlyContinue)) {
    Copy-Item (Join-Path $ffmpegSrc '*') $ffmpegDst -Recurse -Force
    Write-Host "    bundled ffmpeg from tools/ffmpeg" -ForegroundColor DarkGray
} else {
    Copy-Item (Join-Path $root 'ffmpeg/README.md') $ffmpegDst -ErrorAction SilentlyContinue
    Write-Warning "No ffmpeg binaries in tools/ffmpeg — the zip ships without ffmpeg (slim variant). See ffmpeg/README.md."
}

# licenses
$licSrc = Join-Path $root 'licenses'
if (Test-Path $licSrc) {
    Copy-Item $licSrc $stage -Recurse -Force
}

Write-Host "==> Zipping" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force

Write-Host "==> Portable build ready:" -ForegroundColor Green
Write-Host "    folder: $stage"
Write-Host "    zip:    $zip"
