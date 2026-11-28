# Lantern Installation Guide

## System Requirements

| Requirement | Minimum |
|---|---|
| Windows | 10 x64 (build 1903) or Windows 11 x64 |
| RAM | 2 GB (4 GB recommended if using hybrid search) |
| Disk | 2 GB free (plus ~200 MB for the Granite model) |
| PowerShell | 5.1 (included with Windows) |

No additional software — .NET, Java, Node.js, or Docker — is required.

---

## Installation

### 1. Extract the release archive

Extract `Lantern-<version>-win-x64.zip` to a directory where you have write access.
The installation directory itself only needs to be readable by the user running Lantern.

```
C:\Lantern\                 ← installation directory (readable)
  app\Lantern.Web.exe
  bin\meilisearch\meilisearch.exe
  config\appsettings.example.json
  start-lantern.ps1
  reset-admin.ps1
  docs\
```

Persistent data is written to `C:\ProgramData\Lantern\` automatically.
You do not need to create this directory.

### 2. Open PowerShell in the extracted directory

```powershell
cd C:\Lantern
```

No elevation (Run as Administrator) is required unless you later install the Windows Service.

### 3. Run the startup script

```powershell
.\start-lantern.ps1
```

---

## First-Run Setup

On the first launch, `start-lantern.ps1` performs a one-time initialization:

1. Creates the persistent data directories under `C:\ProgramData\Lantern\`.
2. Generates a cryptographically secure Meilisearch secret key and stores it in
   `C:\ProgramData\Lantern\secrets\`.
3. Copies the configuration template to `C:\ProgramData\Lantern\config\appsettings.json`
   with absolute paths substituted.
4. **Prompts you to create the initial administrator account.**

### Administrator Setup

You are prompted for a username and password:

```
  Administrator Setup
  Create the initial Lantern administrator account.

    Username (1-80 characters): admin
    Password (14-128 characters): ****
    Confirm password: ****
```

**Username requirements:**
- 1–80 characters
- Must not contain control characters

**Password requirements:**
- 14–128 characters
- At least 14 characters is enforced by the application

The credentials are passed to Lantern through a process environment variable
that is cleared as soon as the process starts. They are never written to any file.

The administrator account is created in SQLite at
`C:\ProgramData\Lantern\database\lantern.db`.
The password is stored as a PBKDF2 hash (HMAC-SHA512, 210,000 iterations).

### After Setup

Lantern starts and waits until the application is ready, then displays the URL:

```
==> Starting Lantern...
    OK   Lantern started (PID 1234).
==> Waiting for Lantern to become ready...

  Lantern is running at: http://127.0.0.1:5000
  Log in with the administrator account you just created.
  Press Ctrl+C to stop.
```

Open the URL in your browser and sign in with the credentials you entered.

---

## Subsequent Launches

After the first run, `.\start-lantern.ps1` starts Lantern without any prompts.
Administrator credentials are not required at startup.

The existing administrator account is preserved on every launch.
No credential regeneration occurs unless you explicitly reset the password.

---

## Windows Service Installation

To run Lantern as a Windows Service (starts automatically with Windows, runs in the
background without a logged-in user):

### Prerequisites

1. Complete the first-run setup (run `.\start-lantern.ps1` once and create the
   administrator account) **before** installing the service.
2. Open an elevated (Run as Administrator) PowerShell prompt.

### Install the service

```powershell
# From the Lantern installation directory
.\scripts\Install-LanternService.ps1
```

If the installation does not include this script, install the service manually:

```powershell
$exe = (Resolve-Path '.\app\Lantern.Web.exe').Path
New-Service -Name 'Lantern' -BinaryPathName $exe -DisplayName 'Lantern' -StartupType Automatic
Start-Service -Name 'Lantern'
```

### Verify the service is running

```powershell
Get-Service -Name Lantern
```

### View service logs

Lantern writes structured logs to the Windows Event Log and to
`C:\ProgramData\Lantern\logs\` when logging is configured.

### Uninstall the service

```powershell
Stop-Service -Name Lantern -Force
Remove-Service -Name Lantern
```

Application data in `C:\ProgramData\Lantern\` is not removed by uninstalling the service.

---

## Configuration

The production configuration file is:

```
C:\ProgramData\Lantern\config\appsettings.json
```

This file is created from `config\appsettings.example.json` during first-run setup.
Edit it to change the application URL, Meilisearch settings, and other options.

Changes require a service restart:

```powershell
Restart-Service -Name Lantern
```

See the full configuration reference in `docs\administration.md`.

---

## Signing In

1. Open the URL shown by Lantern (default: `http://127.0.0.1:5000`).
2. Navigate to `/account/login`.
3. Enter the username and password you created during first-run setup.
4. The administration interface is at `/admin`.

**Note:** The administration login requires HTTPS or a loopback connection.
The default `http://127.0.0.1:5000` binding is loopback and is fully supported.
If you expose Lantern on a network interface, configure HTTPS.

---

## Administrator Password Recovery

If you lose access to the administrator account, use `reset-admin.ps1`:

```powershell
# Run from the Lantern installation directory
.\reset-admin.ps1
```

This script:
1. Stops the Windows Service if it is running.
2. Prompts for a new password (input is hidden).
3. Updates the administrator record in SQLite using Lantern's own password hasher.
4. Invalidates all existing sessions (you must log in again after the reset).
5. Offers to restart the Windows Service.

**All other application data is preserved** — crawlers, search categories, job history,
schedules, and Meilisearch indexes are not affected.

For development password resets, see `docs/development.md`.

---

## Upgrade

1. Extract the new release ZIP to a new directory (or overwrite the existing one).
2. Stop the Windows Service if running: `Stop-Service Lantern -Force`
3. Replace the installation files.
4. Start the service: `Start-Service Lantern`

Application data in `C:\ProgramData\Lantern\` is preserved automatically.
EF Core migrations are applied automatically at startup.
You do not need to reprovision the administrator account after an upgrade.

---

## Backup

```powershell
.\scripts\Backup-Lantern.ps1
```

This creates a timestamped ZIP containing the SQLite database, Meilisearch data,
configuration, and secrets. See `docs/backup-recovery.md` for restore procedures.

---

## Uninstallation

1. Stop and remove the service (see above).
2. Delete the installation directory.
3. To remove application data: delete `C:\ProgramData\Lantern\`.
   **This permanently deletes the database, indexes, and configuration.**
