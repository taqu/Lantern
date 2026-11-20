#Requires -Version 5.1

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot       = Split-Path $PSScriptRoot -Parent
$webRoot        = Join-Path $repoRoot "src\Lantern.Web"
$appData        = Join-Path $webRoot "AppData"
$secretsDir     = Join-Path $appData "secrets"
$meiliData      = Join-Path $appData "meilisearch"
$dbPath       = Join-Path $appData "database"

# --- 1. Clean directories ---
Write-Host "Clean development data directories..."
if (Test-Path $dbPath -PathType Container) {
    Remove-Item -Path $dbPath -Recurse
}
if (Test-Path $meiliData -PathType Container) {
    Remove-Item -Path $meiliData -Recurse
}

# --- 2. Create AppData structure ---
Write-Host "Creating development data directories..."
foreach ($dir in @($appData, $secretsDir, $meiliData, (Join-Path $appData "database"), (Join-Path $appData "keys"), (Join-Path $appData "logs"))) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "  Created: $dir" -ForegroundColor DarkGray
    }
}

