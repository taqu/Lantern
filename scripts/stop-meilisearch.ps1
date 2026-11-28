#Requires -Version 5.1
<#
.SYNOPSIS
    Stops the Meilisearch process started by start-meilisearch.ps1.

.DESCRIPTION
    Reads the PID from tools/meilisearch/meilisearch.pid and stops only that process.
    Does not terminate unrelated Meilisearch processes.

.PARAMETER MeilisearchPath
    Path to the meilisearch.exe executable (used to locate the PID file).
    Default: .\tools\meilisearch\meilisearch.exe

.EXAMPLE
    .\scripts\stop-meilisearch.ps1
#>
[CmdletBinding()]
param(
    [string]$MeilisearchPath = ".\tools\meilisearch\meilisearch.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
$repoRoot = Split-Path $scriptDir -Parent

if (-not [System.IO.Path]::IsPathRooted($MeilisearchPath)) {
    $MeilisearchPath = Join-Path $repoRoot $MeilisearchPath
}

$pidFile = Join-Path (Split-Path $MeilisearchPath -Parent) "meilisearch.pid"

if (-not (Test-Path $pidFile)) {
    Write-Warning "No PID file found at: $pidFile"
    Write-Warning "Meilisearch may not be running, or was not started by start-meilisearch.ps1."
    exit 0
}

$storedPid = (Get-Content $pidFile -ErrorAction SilentlyContinue).Trim()

if ([string]::IsNullOrWhiteSpace($storedPid)) {
    Write-Warning "PID file is empty. Removing stale PID file."
    Remove-Item $pidFile -Force
    exit 0
}

$process = Get-Process -Id $storedPid -ErrorAction SilentlyContinue

if ($null -eq $process) {
    Write-Host "Process $storedPid is not running. Removing stale PID file."
    Remove-Item $pidFile -Force
    exit 0
}

Write-Host "Stopping Meilisearch (PID $storedPid)..."
Stop-Process -Id $storedPid -Force

# Wait up to 5 seconds for clean exit
$timeout = 5
$elapsed = 0
while (($elapsed -lt $timeout) -and ($null -ne (Get-Process -Id $storedPid -ErrorAction SilentlyContinue))) {
    Start-Sleep -Milliseconds 500
    $elapsed += 0.5
}

if ($null -ne (Get-Process -Id $storedPid -ErrorAction SilentlyContinue)) {
    Write-Warning "Process $storedPid did not exit cleanly within ${timeout}s."
} else {
    Write-Host "Meilisearch stopped."
}

Remove-Item $pidFile -Force
