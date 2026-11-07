#Requires -Version 5.1
<#
.SYNOPSIS
    One-time development environment setup for Lantern.

.DESCRIPTION
    Validates prerequisites, initialises development data directories, generates
    a Meilisearch master key, and stores development admin credentials in User
    Secrets. After this script succeeds, open the solution in Visual Studio and
    press F5 (profile "Lantern Development"), or run scripts\start-dev.ps1.

    Safe to run multiple times — existing secrets and configuration are preserved.

.PARAMETER AdminUsername
    Username for the initial development administrator account.
    Default: admin

.PARAMETER MeilisearchExe
    Path to meilisearch.exe. Resolved relative to the repository root.
    Default: tools\meilisearch\meilisearch.exe

.EXAMPLE
    .\scripts\setup-dev.ps1

.EXAMPLE
    .\scripts\setup-dev.ps1 -AdminUsername myuser

.NOTES
    Prerequisites:
      .NET 10 SDK
      meilisearch.exe in tools\meilisearch\  (download-meilisearch.ps1)
      Optional: Granite GGUF model in resources\ for hybrid search
#>
[CmdletBinding()]
param(
    [string]$AdminUsername = "admin",
    [string]$MeilisearchExe = "tools\meilisearch\meilisearch.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot  = Split-Path $PSScriptRoot -Parent
$webRoot   = Join-Path $repoRoot "src\Lantern.Web"
$appData   = Join-Path $webRoot "App_Data"
$secretsDir = Join-Path $appData "secrets"
$meiliData  = Join-Path $appData "meilisearch"
$meiliKeyFile = Join-Path $secretsDir "meilisearch-master-key.txt"
$localConfig  = Join-Path $webRoot "appsettings.Development.Local.json"
$meiliExeAbs  = if ([System.IO.Path]::IsPathRooted($MeilisearchExe)) { $MeilisearchExe } else { Join-Path $repoRoot $MeilisearchExe }

Write-Host ""
Write-Host "Lantern development setup" -ForegroundColor Cyan
Write-Host "=========================" -ForegroundColor Cyan
Write-Host ""

# ── 1. Check .NET SDK ─────────────────────────────────────────────────────────
Write-Host "Checking .NET SDK..." -NoNewline
try {
    $sdkVersion = (dotnet --version 2>&1).Trim()
    if (-not $sdkVersion.StartsWith("10.")) {
        Write-Host " WARNING" -ForegroundColor Yellow
        Write-Warning "Found .NET $sdkVersion. Lantern requires .NET 10 SDK."
    } else {
        Write-Host " $sdkVersion" -ForegroundColor Green
    }
} catch {
    Write-Host " NOT FOUND" -ForegroundColor Red
    Write-Error "dotnet SDK is not installed or not on PATH. Install .NET 10 SDK from https://dotnet.microsoft.com/download"
    exit 1
}

# ── 2. Check Meilisearch binary ───────────────────────────────────────────────
Write-Host "Checking Meilisearch binary..." -NoNewline
if (Test-Path $meiliExeAbs -PathType Leaf) {
    Write-Host " found" -ForegroundColor Green
    Write-Host "  Path: $meiliExeAbs"
} else {
    Write-Host " NOT FOUND" -ForegroundColor Yellow
    Write-Warning "Meilisearch executable not found at: $meiliExeAbs"
    Write-Warning "Run .\scripts\download-meilisearch.ps1 to download it, then re-run this script."
    Write-Warning "Without Meilisearch, the app will start but search will be unavailable."
}

# ── 3. Check optional GGUF model ──────────────────────────────────────────────
$modelPath = Join-Path $repoRoot "resources\granite-embedding-97M-multilingual-r2.gguf"
Write-Host "Checking embedding model..." -NoNewline
if (Test-Path $modelPath -PathType Leaf) {
    Write-Host " found" -ForegroundColor Green
} else {
    Write-Host " not present (hybrid search disabled)" -ForegroundColor DarkGray
}

# ── 4. Create App_Data structure ──────────────────────────────────────────────
Write-Host "Creating development data directories..."
foreach ($dir in @($appData, $secretsDir, $meiliData, (Join-Path $appData "database"), (Join-Path $appData "keys"), (Join-Path $appData "logs"))) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "  Created: $dir" -ForegroundColor DarkGray
    }
}

# ── 5. Generate Meilisearch master key ────────────────────────────────────────
if (Test-Path $meiliKeyFile) {
    Write-Host "Meilisearch master key: already exists (preserved)"
} else {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $rng.Dispose()
    $key = [Convert]::ToBase64String($bytes)
    Set-Content -Path $meiliKeyFile -Value $key -NoNewline -Encoding UTF8
    Write-Host "Meilisearch master key: generated" -ForegroundColor Green
}

# ── 6. Write appsettings.Development.Local.json with absolute paths ───────────
# This file is gitignored (matches appsettings.*.Local.json). It supplies the
# machine-specific absolute paths that MeilisearchProcessManager needs.
if (Test-Path $localConfig) {
    Write-Host "appsettings.Development.Local.json: already exists (preserved)"
} else {
    $localJson = [ordered]@{
        MeilisearchProcess = [ordered]@{
            ExecutablePath = $meiliExeAbs -replace '\\', '\\'
            DataDirectory  = $meiliData   -replace '\\', '\\'
            MasterKeyFile  = $meiliKeyFile -replace '\\', '\\'
        }
    } | ConvertTo-Json -Depth 4
    Set-Content -Path $localConfig -Value $localJson -Encoding UTF8
    Write-Host "appsettings.Development.Local.json: created" -ForegroundColor Green
}

# ── 7. Initialise development admin User Secrets ──────────────────────────────
Write-Host "Configuring development admin credentials..."

# Check if secret already set
#$existingUser = dotnet user-secrets get "DevAdmin:Username" --project $webRoot 2>&1
$existingUser = (dotnet user-secrets list --project $webRoot) | 
                Where-Object { $_ -match '^DevAdmin:Username\s*=\s*(.*)$' } | 
                ForEach-Object { $Matches[1].Trim() }
if ($existingUser -match "DevAdmin:Username") {
    Write-Host "  Admin User Secrets: already set (preserved)"
} else {
    # Generate a random 20-char password meeting the 14-char minimum
    $pwdBytes = New-Object byte[] 15
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($pwdBytes)
    $rng.Dispose()
    $password = [Convert]::ToBase64String($pwdBytes).TrimEnd('=')

    dotnet user-secrets set "DevAdmin:Username" $AdminUsername --project $webRoot | Out-Null
    dotnet user-secrets set "DevAdmin:Password" $password      --project $webRoot | Out-Null

    Write-Host ""
    Write-Host "  Development administrator credentials" -ForegroundColor Cyan
    Write-Host "  Username : $AdminUsername"
    Write-Host "  Password : $password" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Save the password now. It is stored in User Secrets and will not be shown again." -ForegroundColor Yellow
    Write-Host "  To reset: delete App_Data\database\lantern.db and re-run this script."
    Write-Host ""
}

# ── 8. Trust dev HTTPS certificate ────────────────────────────────────────────
Write-Host "Checking ASP.NET Core HTTPS development certificate..."
$certCheck = dotnet dev-certs https --check 2>&1
if ($certCheck -match "No valid certificate") {
    Write-Host "  Trusting development certificate..."
    dotnet dev-certs https --trust | Out-Null
    Write-Host "  Certificate trusted." -ForegroundColor Green
} else {
    Write-Host "  Certificate already trusted."
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Setup complete." -ForegroundColor Green
Write-Host ""
Write-Host "Development ports:" -ForegroundColor Cyan
Write-Host "  Lantern Web  : https://localhost:7108  (http://localhost:5271)"
Write-Host "  Meilisearch  : http://127.0.0.1:7702"
Write-Host "  Embedding    : http://127.0.0.1:7703  (when enabled)"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  Visual Studio : open Lantern.slnx, select 'Lantern Development', press F5"
Write-Host "  VS Code       : open workspace, press F5 (Lantern Development)"
Write-Host "  CLI           : .\scripts\start-dev.ps1"
Write-Host ""
