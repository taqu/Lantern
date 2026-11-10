#Requires -Version 5.1
<#
.SYNOPSIS
    Resets the Lantern administrator password.

.DESCRIPTION
    Prompts for a new administrator password and updates the Lantern SQLite database.

    If the Lantern Windows Service is running, it is stopped automatically and you are
    offered the option to restart it after the reset.

    The new password is passed to Lantern.Web.exe through a process environment variable
    and is not written to any file or shown on the console.

.PARAMETER DataRoot
    Override the persistent data directory.  Defaults to %ProgramData%\Lantern.

.EXAMPLE
    .\reset-admin.ps1

.NOTES
    Run as the same Windows user that owns the Lantern data directory.
    For a Windows Service installation, run from an elevated PowerShell prompt.
#>
[CmdletBinding()]
param(
    [string]$DataRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "    OK   $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "    WARN $msg" -ForegroundColor Yellow }
function Fail([string]$msg)       { Write-Host "`nERROR: $msg`n" -ForegroundColor Red; exit 1 }

$InstallRoot = $PSScriptRoot
$AppExe      = Join-Path $InstallRoot 'app\Lantern.Web.exe'

if (-not (Test-Path $AppExe)) {
    Fail "Lantern executable not found: $AppExe"
}

if (-not $DataRoot) { $DataRoot = Join-Path $env:ProgramData 'Lantern' }

$dbPath = Join-Path $DataRoot 'database\lantern.db'
if (-not (Test-Path $dbPath -PathType Leaf)) {
    Write-Host ""
    Write-Host "ERROR: Lantern database not found: $dbPath" -ForegroundColor Red
    Write-Host "       If the database is not in the default location, specify the data directory:" -ForegroundColor Yellow
    Write-Host "       .\reset-admin.ps1 -DataRoot D:\YourDataPath" -ForegroundColor Yellow
    exit 1
}

# ── Stop the Windows Service if running ──────────────────────────────────────

$serviceStopped = $false
$svc = Get-Service -Name 'Lantern' -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -eq 'Running') {
    Write-Host ""
    Write-Host "  The Lantern Windows Service is running." -ForegroundColor Yellow
    Write-Host "  Stopping the service before resetting credentials..." -ForegroundColor Yellow
    Stop-Service -Name 'Lantern' -Force
    $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    $serviceStopped = $true
    Write-OK "Service stopped."
}

# ── Prompt for new password ───────────────────────────────────────────────────

Write-Host ""
Write-Host "Lantern — Reset Administrator Password" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Database : $dbPath"
Write-Host ""

$resetPassword = $null
do {
    $ss1 = Read-Host "  New password (14-128 characters)" -AsSecureString
    $ss2 = Read-Host "  Confirm password"                 -AsSecureString

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
    $resetPassword = $p1
    $p1 = $p2 = $null
} while ($null -eq $resetPassword)

# ── Run Lantern in --admin-reset mode ────────────────────────────────────────

Write-Step "Resetting administrator password..."

$psi                       = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName              = $AppExe
$psi.Arguments             = '--admin-reset'
$psi.WorkingDirectory      = Join-Path $InstallRoot 'app'
$psi.UseShellExecute       = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError  = $true
$psi.Environment['ASPNETCORE_ENVIRONMENT']          = 'Production'
$psi.Environment['LANTERN_ADMIN_RESET_PASSWORD']    = $resetPassword
$resetPassword = $null

# Point to the production data directory so the exe finds the right database.
$psi.Environment['Storage__DatabasePath']           = $dbPath
$psi.Environment['Storage__DataProtectionKeysPath'] = Join-Path $DataRoot 'keys'

$exitCode = 1
try {
    $proc   = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    $exitCode = $proc.ExitCode
    $proc.Dispose()
} finally {
    # Belt-and-suspenders: clear the env var from this process too.
    $env:LANTERN_ADMIN_RESET_PASSWORD = $null
}

if ($exitCode -eq 0) {
    Write-OK "Password reset successfully."
    Write-Host "    Existing sessions have been invalidated." -ForegroundColor DarkGray
} else {
    Write-Host ""
    Write-Host "ERROR: Password reset failed (exit code $exitCode)." -ForegroundColor Red
    if ($stderr.Trim()) { Write-Host $stderr.Trim() -ForegroundColor Red }
    if ($stdout.Trim()) { Write-Host $stdout.Trim() }
}

# ── Optionally restart the service ───────────────────────────────────────────

if ($serviceStopped) {
    Write-Host ""
    if ($exitCode -eq 0) {
        $answer = Read-Host "  Restart the Lantern Windows Service? [Y/n]"
        if ($answer -eq '' -or $answer -match '^[Yy]') {
            Start-Service -Name 'Lantern'
            Write-OK "Service restarted."
        } else {
            Write-Host "    Service not restarted.  Start it manually: Start-Service Lantern" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  Attempting to restart the service after failed reset..." -ForegroundColor Yellow
        try {
            Start-Service -Name 'Lantern'
            Write-OK "Service restarted."
        } catch {
            Write-Warn "Could not restart service: $_"
        }
        exit 1
    }
}

Write-Host ""
if ($exitCode -ne 0) { exit 1 }
