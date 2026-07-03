#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Restore, build, and test RecMode.
.DESCRIPTION
    The everyday dev loop. Runs against RecMode.slnx. Use -Configuration Release for a release build.
.EXAMPLE
    ./build.ps1
    ./build.ps1 -Configuration Release -NoTest
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoTest
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$solution = Join-Path $root 'RecMode.slnx'

Write-Host "==> Restoring" -ForegroundColor Cyan
dotnet restore $solution

Write-Host "==> Building ($Configuration)" -ForegroundColor Cyan
dotnet build $solution -c $Configuration --no-restore

if (-not $NoTest) {
    Write-Host "==> Testing" -ForegroundColor Cyan
    dotnet test $solution -c $Configuration --no-build
}

Write-Host "==> Done" -ForegroundColor Green
