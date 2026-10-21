#Requires -Version 5.1
<#
.SYNOPSIS
    Builds and packages the Lantern Windows x64 release ZIP.

.DESCRIPTION
    Publishes Lantern as a self-contained win-x64 application, assembles all
    release components, generates a manifest and checksums, and produces:

        artifacts/release/Lantern-<version>-win-x64.zip

.PARAMETER Version
    Override the version from the VERSION file.

.PARAMETER MeilisearchExe
    Path to the pinned meilisearch-windows-amd64.exe to bundle.
    Required unless -SkipMeilisearch is set.

.PARAMETER ModelPath
    Path to the Granite GGUF model file to bundle.
    Required unless -SkipModel is set.

.PARAMETER SkipMeilisearch
    Omit Meilisearch from the archive (for CI builds without the binary).

.PARAMETER SkipModel
    Omit the GGUF model from the archive (for CI builds without the model).

.PARAMETER SkipTests
    Skip dotnet test before packaging.

.PARAMETER OutputDir
    Destination directory for the release ZIP. Default: artifacts/release.
#>
[CmdletBinding()]
param(
    [string]$Version          = '',
    [string]$MeilisearchExe   = '',
    [string]$ModelPath        = '',
    [switch]$SkipMeilisearch,
    [switch]$SkipModel,
    [switch]$SkipTests,
    [string]$OutputDir        = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputDir) { $OutputDir = Join-Path $RepoRoot 'artifacts\release' }

function Write-Step([string]$msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "    [OK] $msg" -ForegroundColor Green }
function Write-Fail([string]$msg) { Write-Host "    [FAIL] $msg" -ForegroundColor Red; exit 1 }

# ── Version ───────────────────────────────────────────────────────────────────

if (-not $Version) {
    $versionFile = Join-Path $RepoRoot 'VERSION'
    if (-not (Test-Path $versionFile)) { Write-Fail "VERSION file not found at $versionFile" }
    $Version = (Get-Content $versionFile -Raw).Trim()
}
if (-not $Version) { Write-Fail "Version is empty" }

$Commit = (git -C $RepoRoot rev-parse --short HEAD 2>$null)
if (-not $Commit) { $Commit = 'unknown' }

Write-Host ""
Write-Host "Lantern Release Build" -ForegroundColor White
Write-Host "  Version: $Version"
Write-Host "  Commit:  $Commit"
Write-Host ""

# ── Constants ─────────────────────────────────────────────────────────────────

$MEILI_VERSION = '1.55.0'
$MEILI_SHA256  = '948535579776EA8AEEFFD166AC824F26624468B4F82D6E46A613230D8C91E62A'
$LLAMA_VERSION = '1.0.4'

# ── Optional tests ────────────────────────────────────────────────────────────

if (-not $SkipTests) {
    Write-Step "Running unit tests..."
    & dotnet test $RepoRoot --configuration Release --filter 'Category!=Integration' --no-build 2>&1 |
        Select-String -Pattern 'Passed|Failed|Error|Skipped' | Select-Object -Last 5
    if ($LASTEXITCODE -ne 0) { Write-Fail "Unit tests failed" }
    Write-OK "Unit tests passed"
}

# ── Publish ───────────────────────────────────────────────────────────────────

$publishDir = Join-Path $RepoRoot 'artifacts\publish\win-x64'
Write-Step "Publishing win-x64 self-contained..."

& dotnet restore (Join-Path $RepoRoot 'src\Lantern.Web\Lantern.Web.csproj') --runtime win-x64
if ($LASTEXITCODE -ne 0) { Write-Fail "dotnet restore failed" }

& dotnet publish (Join-Path $RepoRoot 'src\Lantern.Web\Lantern.Web.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $publishDir `
    /p:Version=$Version `
    /p:InformationalVersion="$Version+$Commit"
if ($LASTEXITCODE -ne 0) { Write-Fail "dotnet publish failed" }
Write-OK "Published to $publishDir"

# ── Validate publish outputs ──────────────────────────────────────────────────

Write-Step "Validating publish outputs..."
$required = @(
    'Lantern.Web.exe',
    'wwwroot',
    'llama.dll',
    'ggml.dll'
)
foreach ($f in $required) {
    $p = Join-Path $publishDir $f
    if (-not (Test-Path $p)) {
        Write-Host "    [WARN] Expected file/dir not found: $f" -ForegroundColor Yellow
    } else {
        Write-OK $f
    }
}

# ── Stage release layout ──────────────────────────────────────────────────────

$stagingDir = Join-Path $RepoRoot "artifacts\staging\Lantern-$Version-win-x64"
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }

Write-Step "Assembling release layout..."

# app/
$appDir = Join-Path $stagingDir 'app'
Copy-Item -Path $publishDir -Destination $appDir -Recurse
Write-OK "app/ assembled"

# bin/meilisearch/
$meiliDir = Join-Path $stagingDir 'bin\meilisearch'
New-Item -ItemType Directory -Path $meiliDir | Out-Null
if (-not $SkipMeilisearch) {
    if (-not $MeilisearchExe) {
        # Check for pre-downloaded binary in artifacts/
        $candidate = Join-Path $RepoRoot 'artifacts\meilisearch\meilisearch.exe'
        if (Test-Path $candidate) { $MeilisearchExe = $candidate }
    }
    if (-not $MeilisearchExe -or -not (Test-Path $MeilisearchExe)) {
        Write-Fail "Meilisearch executable not found. Provide -MeilisearchExe or place it at artifacts\meilisearch\meilisearch.exe"
    }
    # Verify checksum.
    $hash = (Get-FileHash -Path $MeilisearchExe -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($hash -ne $MEILI_SHA256.ToUpperInvariant()) {
        Write-Fail "Meilisearch SHA-256 mismatch.`n  Expected: $MEILI_SHA256`n  Got:      $hash"
    }
    Copy-Item $MeilisearchExe (Join-Path $meiliDir 'meilisearch.exe')
    Write-OK "meilisearch.exe (verified SHA-256)"
}

# models/
$modelsDir = Join-Path $stagingDir 'models'
New-Item -ItemType Directory -Path $modelsDir | Out-Null
if (-not $SkipModel) {
    if (-not $ModelPath) {
        $candidate = Join-Path $RepoRoot 'resources\granite-embedding-97M-multilingual-r2.gguf'
        if (Test-Path $candidate) { $ModelPath = $candidate }
    }
    if (-not $ModelPath -or -not (Test-Path $ModelPath)) {
        Write-Fail "GGUF model not found. Provide -ModelPath or place it at resources\granite-embedding-97M-multilingual-r2.gguf"
    }
    $modelDest = Join-Path $modelsDir (Split-Path $ModelPath -Leaf)
    Copy-Item $ModelPath $modelDest
    $modelHash = (Get-FileHash -Path $modelDest -Algorithm SHA256).Hash.ToUpperInvariant()
    Write-OK "GGUF model (SHA-256: $modelHash)"
}

# config/
$cfgDir = Join-Path $stagingDir 'config'
New-Item -ItemType Directory -Path $cfgDir | Out-Null
$exampleSrc = Join-Path $RepoRoot 'release\config\appsettings.example.json'
if (Test-Path $exampleSrc) {
    Copy-Item $exampleSrc (Join-Path $cfgDir 'appsettings.example.json')
    Write-OK "config/"
}

# scripts/
$scriptsDir = Join-Path $stagingDir 'scripts'
Copy-Item (Join-Path $RepoRoot 'release\scripts') $scriptsDir -Recurse
Write-OK "scripts/"

# docs/
$docsDir = Join-Path $stagingDir 'docs'
$releaseDocs = Join-Path $RepoRoot 'release\docs'
if (Test-Path $releaseDocs) {
    Copy-Item $releaseDocs $docsDir -Recurse
    Write-OK "docs/"
}

# VERSION
Copy-Item (Join-Path $RepoRoot 'VERSION') (Join-Path $stagingDir 'VERSION')

# ── Generate manifest ─────────────────────────────────────────────────────────

Write-Step "Generating manifest..."
$manifest = [ordered]@{
    product     = 'Lantern'
    version     = $Version
    commit      = $Commit
    buildTime   = (Get-Date -Format 'o')
    runtime     = 'win-x64'
    deployment  = 'self-contained'
    components  = [ordered]@{
        dotnet         = '10'
        meilisearch    = $MEILI_VERSION
        llamapure      = $LLAMA_VERSION
        embeddingModel = 'granite-embedding-97M-multilingual-r2.gguf'
    }
    checksums   = [ordered]@{
        meilisearch    = if ($SkipMeilisearch) { 'not-included' } else { $MEILI_SHA256 }
        embeddingModel = if ($SkipModel -or -not $modelHash) { 'not-included' } else { $modelHash }
    }
}
$manifest | ConvertTo-Json -Depth 5 |
    Set-Content -Path (Join-Path $stagingDir 'manifest.json') -Encoding UTF8
Write-OK "manifest.json"

# ── Create ZIP ────────────────────────────────────────────────────────────────

Write-Step "Creating release archive..."
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir | Out-Null }

$archiveName = "Lantern-$Version-win-x64.zip"
$archivePath = Join-Path $OutputDir $archiveName

if (Test-Path $archivePath) { Remove-Item $archivePath -Force }
Compress-Archive -Path "$stagingDir\*" -DestinationPath $archivePath -CompressionLevel Optimal

$archiveSize = (Get-Item $archivePath).Length
$archiveSizeMB = [math]::Round($archiveSize / 1MB, 2)
$archiveHash = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash.ToUpperInvariant()

"$archiveHash  $archiveName" | Set-Content -Path "$archivePath.sha256" -Encoding UTF8

# Patch manifest with archive hash.
$mfPath = Join-Path $stagingDir 'manifest.json'
$mf = Get-Content $mfPath | ConvertFrom-Json
$mf.checksums | Add-Member -NotePropertyName 'archive' -NotePropertyValue $archiveHash -Force
$mf | ConvertTo-Json -Depth 5 | Set-Content $mfPath -Encoding UTF8
# Re-zip with updated manifest.
Remove-Item $archivePath -Force
Compress-Archive -Path "$stagingDir\*" -DestinationPath $archivePath -CompressionLevel Optimal
$archiveHash = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash.ToUpperInvariant()
"$archiveHash  $archiveName" | Set-Content -Path "$archivePath.sha256" -Encoding UTF8

Write-OK "Archive: $archivePath ($archiveSizeMB MB)"
Write-OK "SHA-256: $archiveHash"

Write-Host ""
Write-Host "Release build complete." -ForegroundColor Green
Write-Host "  Archive:  $archivePath"
Write-Host "  Checksum: $archivePath.sha256"
Write-Host "  Version:  $Version"
Write-Host "  Commit:   $Commit"
Write-Host ""
