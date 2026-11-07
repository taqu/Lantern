#Requires -Version 5.1
<#
.SYNOPSIS
    Starts Lantern in development mode from the command line.

.DESCRIPTION
    Equivalent to pressing F5 in Visual Studio with the "Lantern Development" profile.
    Runs dotnet run against src\Lantern.Web with ASPNETCORE_ENVIRONMENT=Development.

    Prerequisites: run .\scripts\setup-dev.ps1 first (one time).

.EXAMPLE
    .\scripts\start-dev.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$webProject = Join-Path $repoRoot "src\Lantern.Web"

# Verify setup has been run
$localConfig = Join-Path $webProject "appsettings.Development.Local.json"
if (-not (Test-Path $localConfig)) {
    Write-Error "Development environment is not initialised. Run .\scripts\setup-dev.ps1 first."
    exit 1
}

Write-Host ""
Write-Host "Starting Lantern in Development mode..." -ForegroundColor Cyan
Write-Host "  Project : $webProject"
Write-Host "  URL     : https://localhost:7108"
Write-Host "  Press Ctrl+C to stop."
Write-Host ""

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS        = "https://localhost:7108;http://localhost:5271"

dotnet run --project $webProject --launch-profile "Lantern Development"
