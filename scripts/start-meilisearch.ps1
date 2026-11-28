#Requires -Version 5.1
<#
.SYNOPSIS
    Starts Meilisearch for local development.

.DESCRIPTION
    Starts a Meilisearch process bound to 127.0.0.1:7700.
    Does not require administrator privileges.
    Does not install a Windows service.
    Does not modify global environment variables.
    Writes the process PID to tools/meilisearch/meilisearch.pid for use by stop-meilisearch.ps1.

.PARAMETER MeilisearchPath
    Path to the meilisearch.exe executable.
    Default: .\tools\meilisearch\meilisearch.exe

.PARAMETER DataDir
    Directory where Meilisearch stores its data.
    Default: .\tools\meilisearch\data

.PARAMETER Port
    Port for the Meilisearch HTTP server.
    Default: 7700

.EXAMPLE
    .\scripts\start-meilisearch.ps1

.EXAMPLE
    $env:MEILI_MASTER_KEY = "devkey-$(New-Guid)"
    .\scripts\start-meilisearch.ps1

.NOTES
    The MEILI_MASTER_KEY environment variable must be set before calling this script.
    Do not hardcode the master key in scripts or source files.
    The official Meilisearch binary is available at:
    https://github.com/meilisearch/meilisearch/releases
#>
[CmdletBinding()]
param(
    [string]$MeilisearchPath = ".\tools\meilisearch\meilisearch.exe",
    [string]$DataDir = ".\tools\meilisearch\data",
    [int]$Port = 7700
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolve paths relative to the repo root (script is in scripts/ subfolder)
$scriptDir = $PSScriptRoot
$repoRoot = Split-Path $scriptDir -Parent

if (-not [System.IO.Path]::IsPathRooted($MeilisearchPath)) {
    $MeilisearchPath = Join-Path $repoRoot $MeilisearchPath
}
if (-not [System.IO.Path]::IsPathRooted($DataDir)) {
    $DataDir = Join-Path $repoRoot $DataDir
}

$pidFile = Join-Path (Split-Path $MeilisearchPath -Parent) "meilisearch.pid"

# Check master key
$masterKey = $env:MEILI_MASTER_KEY
if ([string]::IsNullOrWhiteSpace($masterKey)) {
    Write-Error @"
MEILI_MASTER_KEY environment variable is not set.

Set it before calling this script:
    `$env:MEILI_MASTER_KEY = "your-development-key-here"

Do not use a weak or predictable key even in development.
"@
    exit 1
}

# Check executable
if (-not (Test-Path $MeilisearchPath -PathType Leaf)) {
    Write-Error @"
Meilisearch executable not found at: $MeilisearchPath

Download the Windows x64 binary from:
    https://github.com/meilisearch/meilisearch/releases/latest

Extract meilisearch.exe to: $(Split-Path $MeilisearchPath -Parent)

Meilisearch is licensed under SSPL v1. See docs/dependencies.md for details.
"@
    exit 1
}

# Check if already running (from our PID file)
if (Test-Path $pidFile) {
    $existingPid = Get-Content $pidFile -ErrorAction SilentlyContinue
    if ($existingPid -and (Get-Process -Id $existingPid -ErrorAction SilentlyContinue)) {
        Write-Warning "Meilisearch appears to already be running (PID $existingPid). Use stop-meilisearch.ps1 to stop it first."
        exit 0
    }
    # Stale PID file
    Remove-Item $pidFile -Force
}

# Create data directory
New-Item -ItemType Directory -Path $DataDir -Force | Out-Null

Write-Host "Starting Meilisearch..."
Write-Host "  Executable : $MeilisearchPath"
Write-Host "  Data dir   : $DataDir"
Write-Host "  Bind       : 127.0.0.1:$Port"
Write-Host ""

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $MeilisearchPath
$startInfo.Arguments = "--db-path `"$DataDir`" --http-addr `"127.0.0.1:$Port`" --master-key `"$masterKey`""
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $false
$startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Normal

$process = [System.Diagnostics.Process]::Start($startInfo)

if ($null -eq $process) {
    Write-Error "Failed to start Meilisearch process."
    exit 1
}

# Save PID
$process.Id | Set-Content $pidFile

Write-Host "Meilisearch started (PID $($process.Id))."
Write-Host "  Health URL : http://127.0.0.1:$Port/health"
Write-Host "  PID file   : $pidFile"
Write-Host ""
Write-Host "Wait a few seconds for startup, then verify:"
Write-Host "  Invoke-RestMethod http://127.0.0.1:$Port/health"
Write-Host ""
Write-Host "To stop: .\scripts\stop-meilisearch.ps1"
