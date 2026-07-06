#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Produce a Velopack installer + auto-update release for RecMode (plan §3.5 — installer/auto-update is a
    later add-on to the portable-first distribution, sharing the same build).
.DESCRIPTION
    Self-contained win-x64 publish of RecMode.App (identical to publish-portable.ps1's publish step), then
    packages it with the Velopack CLI (`vpk pack`) into an installer (Setup.exe), full/delta nupkg packages,
    and a releases.win.json feed — written to -OutputRoot (default .\Releases).

    This is infrastructure, not a finished release process: real distribution needs a hosting location for
    the Releases folder (a URL) and, eventually, code signing — neither is set up yet. Until then, this
    script is how to build and locally test the whole Velopack pipeline (see the -TestLocalUpdate example
    below and DOCS — RecMode.App/Services/UpdateChecker.cs's VelopackFeedUrl constant is what you point at
    real hosting once it exists).
.EXAMPLE
    ./publish-installer.ps1 -Version 0.1.0
    ./publish-installer.ps1 -Version 0.1.1 -OutputRoot ./Releases2   # a "newer" release, for local update testing
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$PackId = 'RecMode',
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'Releases'),
    [string]$Icon = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$app = Join-Path $root 'src/RecMode.App/RecMode.App.csproj'
$stage = Join-Path $root "artifacts/RecMode-$Version-installer-stage-win-x64"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "==> vpk CLI not found; installing (dotnet tool install -g vpk)" -ForegroundColor Cyan
    dotnet tool install -g vpk
}

Write-Host "==> Publishing self-contained win-x64 (ReadyToRun) for packaging" -ForegroundColor Cyan
dotnet publish $app -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:IsPublishable=true -p:Version=$Version `
    -o $stage

Write-Host "==> Packing with vpk (RecMode $Version)" -ForegroundColor Cyan
$vpkArgs = @(
    'pack'
    '--packId', $PackId
    '--packVersion', $Version
    '--packDir', $stage
    '--mainExe', 'RecMode.exe'
    '--outputDir', $OutputRoot
)
if ($Icon -and (Test-Path $Icon)) {
    $vpkArgs += @('--icon', $Icon)
}
& vpk @vpkArgs

Write-Host "==> Velopack release ready:" -ForegroundColor Green
Write-Host "    $OutputRoot  (Setup.exe, .nupkg packages, releases.win.json)"
Write-Host ""
Write-Host "This is a LOCAL test release — nothing is hosted anywhere yet. To verify the update pipeline" -ForegroundColor DarkYellow
Write-Host "end-to-end without real hosting, install this Setup.exe once, then re-run this script with a" -ForegroundColor DarkYellow
Write-Host "higher -Version pointed at the SAME -OutputRoot; the installed app's Settings > Check now will" -ForegroundColor DarkYellow
Write-Host "find it if RecMode.App/Services/UpdateChecker.cs's VelopackFeedUrl is temporarily set to this" -ForegroundColor DarkYellow
Write-Host "folder's full path (a file path is a valid Velopack update source, no server needed for testing)." -ForegroundColor DarkYellow
