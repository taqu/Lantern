#Requires -Version 5.1
<#
.SYNOPSIS
    Unified Lantern startup script for Windows.

.DESCRIPTION
    On first run, initialises the data directory, generates Meilisearch secrets
    securely, copies the configuration template, and prompts for the initial
    administrator credentials.

    On subsequent runs, starts Lantern directly without any prompts.

    Lantern manages the bundled Meilisearch process internally.  This script
    starts Lantern.Web.exe and waits for it to report ready, then displays the
    application URL.  Press Ctrl+C to stop.

.PARAMETER DataRoot
    Override the persistent data directory.  Defaults to %ProgramData%\Lantern.

.EXAMPLE
    .\start-lantern.ps1

.EXAMPLE
    .\start-lantern.ps1 -DataRoot D:\LanternData
#>
[CmdletBinding()]
param(
    [string]$DataRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Path constants ─────────────────────────────────────────────────────────────

$InstallRoot     = $PSScriptRoot
$AppDir          = Join-Path $InstallRoot 'app'
$AppExe          = Join-Path $AppDir      'Lantern.Web.exe'
$MeiliExe        = Join-Path $InstallRoot 'bin\meilisearch\meilisearch.exe'
$ModelFile       = Join-Path $InstallRoot 'models\granite-embedding-97M-multilingual-r2.gguf'
$ConfigTemplate  = Join-Path $InstallRoot 'config\appsettings.example.json'

if (-not $DataRoot) { $DataRoot = Join-Path $env:ProgramData 'Lantern' }

$ConfigDir      = Join-Path $DataRoot 'config'
$ConfigFile     = Join-Path $ConfigDir  'appsettings.json'
$SecretsDir     = Join-Path $DataRoot   'secrets'
$MasterKeyFile  = Join-Path $SecretsDir 'meili-master-key.txt'
$ApiKeyFile     = Join-Path $SecretsDir 'meili-api-key.txt'

# ── Helpers ────────────────────────────────────────────────────────────────────

function Write-Step([string]$msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "    OK   $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "    WARN $msg" -ForegroundColor Yellow }
function Fail([string]$msg)       { Write-Host "`nERROR: $msg`n" -ForegroundColor Red; exit 1 }

# ── Validate installation ──────────────────────────────────────────────────────

Write-Step "Validating installation..."

if (-not (Test-Path $AppExe)) {
    Fail "Lantern executable not found: $AppExe"
}
if (-not (Test-Path $MeiliExe)) {
    Fail "Meilisearch executable not found: $MeiliExe"
}
if (-not (Test-Path $ConfigTemplate)) {
    Fail "Configuration template not found: $ConfigTemplate"
}

if (-not (Test-Path $ModelFile)) {
    Write-Warn "Granite embedding model not found — hybrid search will be unavailable."
    Write-Warn "  Expected: $ModelFile"
}

Write-OK "Installation verified."

# ── First-run detection ────────────────────────────────────────────────────────

$isFirstRun      = -not (Test-Path $MasterKeyFile)
$adminUsername   = $null
$adminPassword   = $null

if ($isFirstRun) {
    Write-Host ""
    Write-Step "First-run initialisation..."

    # Create persistent data directories.
    foreach ($dir in @(
        $ConfigDir,
        $SecretsDir,
        (Join-Path $DataRoot 'database'),
        (Join-Path $DataRoot 'keys'),
        (Join-Path $DataRoot 'meilisearch')
    )) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    Write-OK "Data directories: $DataRoot"

    # Generate a cryptographically secure 32-byte key (Base64, 44 characters).
    # Meilisearch accepts its master key as an API bearer token, so a single key
    # serves both roles — appropriate for a loopback-only deployment.
    $rng   = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = [byte[]]::new(32)
    $rng.GetBytes($bytes)
    $meiliKey = [Convert]::ToBase64String($bytes)
    $rng.Dispose()
    $bytes = $null

    Set-Content -Path $MasterKeyFile -Value $meiliKey -NoNewline -Encoding ASCII
    Set-Content -Path $ApiKeyFile    -Value $meiliKey -NoNewline -Encoding ASCII
    $meiliKey = $null
    Write-OK "Meilisearch keys generated."

    # Restrict access to the secrets directory to the current user only.
    try {
        $acl  = Get-Acl -Path $SecretsDir
        $acl.SetAccessRuleProtection($true, $false)   # disable inheritance
        $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $user, 'FullControl',
            [System.Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',
            [System.Security.AccessControl.PropagationFlags]::None,
            [System.Security.AccessControl.AccessControlType]::Allow)
        $acl.SetAccessRule($rule)
        Set-Acl -Path $SecretsDir -AclObject $acl
        Write-OK "Secrets directory permissions restricted to current user."
    } catch {
        Write-Warn "Could not restrict secrets directory permissions: $_"
    }

    # Create configuration from the bundled template if it does not exist yet.
    if (-not (Test-Path $ConfigFile)) {
        $cfg = Get-Content -Path $ConfigTemplate -Raw | ConvertFrom-Json

        # Update paths that must be absolute.
        $cfg.Meilisearch.ApiKeyFile            = $ApiKeyFile
        $cfg.MeilisearchProcess.DataDirectory  = Join-Path $DataRoot 'meilisearch\data.ms'
        $cfg.MeilisearchProcess.MasterKeyFile  = $MasterKeyFile
        # Set model path to absolute so it is found regardless of working directory.
        $cfg.Embedding.ModelPath               = $ModelFile

        $cfg | ConvertTo-Json -Depth 10 |
            Set-Content -Path $ConfigFile -Encoding UTF8
        Write-OK "Configuration written: $ConfigFile"
    } else {
        Write-OK "Using existing configuration: $ConfigFile"
    }

    # Interactive administrator setup.
    Write-Host ""
    Write-Host "  Administrator Setup" -ForegroundColor White
    Write-Host "  Create the initial Lantern administrator account."
    Write-Host ""

    do {
        $adminUsername = (Read-Host "    Username (1–80 characters)").Trim()
    } while ([string]::IsNullOrEmpty($adminUsername) -or $adminUsername.Length -gt 80)

    $adminPassword = $null
    do {
        $ss1 = Read-Host "    Password (14–128 characters)" -AsSecureString
        $ss2 = Read-Host "    Confirm password"             -AsSecureString

        $ptr1 = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($ss1)
        $ptr2 = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($ss2)
        try {
            $p1 = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr1)
            $p2 = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr2)
        } finally {
            [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr1)
            [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr2)
        }
        $ss1.Dispose(); $ss2.Dispose()

        if ($p1 -ne $p2) {
            Write-Warn "Passwords do not match — try again."
            $p1 = $p2 = $null
            continue
        }
        if ($p1.Length -lt 14 -or $p1.Length -gt 128) {
            Write-Warn "Password must be 14 to 128 characters — try again."
            $p1 = $p2 = $null
            continue
        }
        $adminPassword = $p1
        $p1 = $p2 = $null
    } while ($null -eq $adminPassword)

    Write-OK "Administrator credentials accepted."
}

# ── Read the application URL from configuration ────────────────────────────────

$lanternUrl = 'http://127.0.0.1:5000'
if (Test-Path $ConfigFile) {
    try {
        $cfgUrls = (Get-Content -Path $ConfigFile -Raw | ConvertFrom-Json).Urls
        if ($cfgUrls) {
            $lanternUrl = $cfgUrls.Split(';')[0].Trim().TrimEnd('/')
        }
    } catch {
        Write-Warn "Could not read URL from configuration; using default: $lanternUrl"
    }
}

# ── Launch Lantern ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Step "Starting Lantern..."

$psi                  = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName         = $AppExe
$psi.WorkingDirectory = $AppDir
$psi.UseShellExecute  = $false   # inherit current console; no new window

# Supply administrator credentials only for the first-run process.
# They are not written to any file and are not echoed to the console.
if ($isFirstRun -and $adminUsername -and $adminPassword) {
    $psi.Environment['LANTERN_ADMIN_USERNAME'] = $adminUsername
    $psi.Environment['LANTERN_ADMIN_PASSWORD'] = $adminPassword
}

$proc = [System.Diagnostics.Process]::Start($psi)
if (-not $proc) { Fail "Failed to start Lantern." }

# Clear sensitive variables from memory immediately after the process starts.
$adminUsername = $null
$adminPassword = $null

Write-OK "Lantern started (PID $($proc.Id))."

# ── Wait for readiness ─────────────────────────────────────────────────────────

Write-Step "Waiting for Lantern to become ready..."

$healthUrl  = "$lanternUrl/api/health"
$deadline   = [DateTime]::UtcNow.AddSeconds(120)
$ready      = $false

while ([DateTime]::UtcNow -lt $deadline -and -not $proc.HasExited) {
    try {
        $response = Invoke-RestMethod -Uri $healthUrl -Method GET -ErrorAction Stop
        if ($response.status -in 'healthy', 'degraded') {
            $ready = $true
            break
        }
    } catch { }
    Start-Sleep -Milliseconds 1000
}

if ($proc.HasExited) {
    Fail "Lantern exited unexpectedly during startup (exit code $($proc.ExitCode))."
}
if (-not $ready) {
    Write-Warn "Lantern did not report ready within 120 seconds.  It may still be starting."
}

# ── Display status ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  Lantern is running at: $lanternUrl" -ForegroundColor Green
if ($isFirstRun) {
    Write-Host "  Log in with the administrator account you just created." -ForegroundColor Cyan
}
Write-Host "  Press Ctrl+C to stop." -ForegroundColor Gray
Write-Host ""

# ── Wait for exit and handle shutdown ─────────────────────────────────────────

try {
    while (-not $proc.HasExited) {
        Start-Sleep -Milliseconds 500
    }
    if ($proc.ExitCode -ne 0) {
        Write-Warn "Lantern exited with code $($proc.ExitCode)."
    }
}
finally {
    # Reached on normal exit or when Ctrl+C interrupts Start-Sleep.
    if (-not $proc.HasExited) {
        Write-Host ""
        Write-Step "Stopping Lantern..."
        try { [void]$proc.CloseMainWindow() } catch { }
        if (-not $proc.WaitForExit(15000)) {
            Write-Warn "Lantern did not stop within 15 s; terminating."
            try { $proc.Kill() } catch { }
        } else {
            Write-OK "Lantern stopped."
        }
    }
    $proc.Dispose()
}
