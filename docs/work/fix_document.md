# Lantern — Simplify Windows Startup and Fix Quick Start

## Objective

The current Lantern Quick Start is unnecessarily complicated and incomplete.

Users must manually create directories, copy configuration files, generate secrets, configure environment variables, and start the application.

Additionally, the instructions do not start Meilisearch, which is required for keyword and hybrid search.

The repository currently contains these scripts:

```text
scripts/
    Build-Release.ps1
    download-meilisearch.ps1
    run-feasibility.ps1
    set-embedding.ps1
    start-meilisearch.ps1
    stop-meilisearch.ps1
```

There is no simple `start-lantern.ps1` or `start-lantern.sh` entry point.

**Your task is to simplify the Windows installation and startup experience by providing a reliable, unified startup mechanism that initializes Lantern, starts the required services, and launches the application.**

Update the documentation to match the implementation.

This task is intended to close the practical usability gaps left by Phase 12.

---

## 1. Investigate the Existing Startup Architecture

Before making changes:

1. Read `README.md`.
2. Read `release/docs/INSTALL.md`.
3. Read `roadmap.md`.
4. Inspect all existing scripts.
5. Inspect `Build-Release.ps1`.
6. Inspect Lantern's application entry point.
7. Inspect Meilisearch startup and shutdown handling.
8. Inspect SQLite initialization and migrations.
9. Inspect administrator credential provisioning.
10. Inspect Meilisearch API key configuration.
11. Inspect embedding server startup.
12. Inspect GGUF model path resolution.
13. Inspect Windows Service support, if implemented.
14. Inspect the actual release ZIP structure.

Determine which initialization steps already happen automatically.

Do not duplicate existing functionality.

Do not assume the original Phase 12 roadmap was fully implemented.

## 2. Desired User Experience

The Windows release should support the following workflow:

```powershell
# Extract Lantern-<version>-win-x64.zip

cd C:\Lantern

.\start-lantern.ps1
```

On the first run, the startup process should:

1. Detect that Lantern has not been initialized.
2. Create the required application data directories.
3. Generate required secrets securely.
4. Create or validate application configuration.
5. Provision the initial administrator securely.
6. Start the bundled Meilisearch instance.
7. Wait for Meilisearch readiness.
8. Start Lantern.
9. Initialize the existing embedding infrastructure.
10. Display the actual application URL.

On subsequent runs, it should reuse existing configuration and data.

### Critical requirements

- Do not regenerate secrets on every startup.
- Do not overwrite administrator credentials.
- Do not recreate existing databases.
- Do not delete existing Meilisearch indexes.
- Do not require repeated manual setup.
- Do not require Docker, Python, Node.js, or a separately installed .NET runtime.
- Do not require administrator elevation for ordinary interactive startup.

The final workflow must be straightforward for non-developer administrators.

## 3. Create a Unified Windows Startup Script

Introduce a user-facing startup script.

Preferred filename:

```text
start-lantern.ps1
```

Place it at the root of the extracted release archive.

The source location may differ, but the packaged location must be predictable.

### Responsibilities

The script should:

1. Resolve its own installation directory.
2. Locate the Lantern executable.
3. Locate bundled Meilisearch.
4. Locate the Granite embedding model.
5. Resolve the persistent data directory.
6. Initialize missing configuration safely.
7. Validate existing configuration.
8. Start Meilisearch.
9. Wait for readiness.
10. Start Lantern.
11. Handle Ctrl+C and process termination.
12. Stop only processes owned by this startup session.

### Path resolution

Use `$PSScriptRoot`.

Do not depend on the current working directory.

Support installation paths containing spaces.

Do not hardcode:

```text
C:\Lantern
```

as the only possible installation directory.

### Failure handling

If any required component is missing, report an actionable error.

Examples:

```text
Lantern executable not found.
Meilisearch executable not found.
Granite embedding model not found.
Configuration is invalid.
Meilisearch failed to start.
Port 7700 is already in use.
```

Do not silently continue with invalid infrastructure.

## 4. First-Run Initialization

Implement an idempotent initialization operation.

Prefer using existing application initialization services where possible.

The PowerShell script should orchestrate initialization rather than duplicate complex application logic.

### Required directories

Use a configurable data root.

A suitable default is:

```text
%ProgramData%\Lantern\
```

Create only the required directories.

### Configuration

If active configuration does not exist:

1. Locate the packaged configuration template.
2. Validate the template.
3. Create the active configuration.
4. Populate required paths and settings.
5. Preserve safe defaults.

If configuration already exists:

- Load it.
- Validate it.
- Do not overwrite it.

### Partial initialization

A failed initialization must be retryable.

Do not leave misleading success markers.

Do not delete existing data after partial failure.

## 5. Secure Meilisearch Secret Generation

The current README generates keys using:

```powershell
Get-Random
```

Do not use this approach for production secrets.

Use a cryptographically secure random number generator.

### Requirements

- Generate a strong Meilisearch master key.
- Use a cryptographically secure RNG.
- Use an appropriate encoding such as Base64 or hexadecimal.
- Generate secrets only when missing.
- Preserve existing secrets.
- Apply restrictive filesystem permissions.
- Never print secrets to the console.
- Never include secrets in logs.

### Important API key distinction

Investigate how Lantern authenticates to Meilisearch.

Do not assume that writing an arbitrary string to `meili-api-key.txt` creates a valid Meilisearch API key.

Meilisearch API keys must be created or retrieved through the supported Meilisearch authentication mechanism.

### Recommended initialization sequence

1. Generate or load the master key.
2. Start Meilisearch with the master key.
3. Wait for readiness.
4. Create or retrieve the required scoped API key through the Meilisearch API.
5. Store the key securely.
6. Configure Lantern to use that key.

If using the master key internally is already an intentional implementation decision, review its security implications before changing the architecture.

Do not introduce unnecessary additional keys that are not actually used.

## 6. Meilisearch Process Management

The unified startup process must start Meilisearch automatically.

Reuse the existing implementation in:

```text
scripts/start-meilisearch.ps1
scripts/stop-meilisearch.ps1
```

where appropriate.

Refactor those scripts if necessary to support release execution.

### Requirements

- Locate the bundled Meilisearch executable.
- Use the persistent Meilisearch database directory.
- Bind to loopback by default.
- Supply the required authentication configuration.
- Start the process.
- Wait for readiness.
- Report startup failures.
- Preserve existing indexes.
- Avoid duplicate process creation.

### Existing process detection

If Meilisearch is already running:

- Determine whether it is the approved Lantern-managed instance.
- Verify identity and configuration where practical.
- Do not connect to an arbitrary process solely because the port is open.
- Do not terminate unrelated processes.

### Process ownership

Track the process started by the launcher.

On shutdown, stop only processes owned by the current launcher.

Do not use broad process-name termination such as:

```powershell
Get-Process meilisearch | Stop-Process
```

### Readiness

Do not use a fixed sleep as the only readiness check.

Poll the actual Meilisearch health endpoint.

Use a bounded timeout.

Distinguish:

- Process started.
- HTTP endpoint reachable.
- Service ready for Lantern.

### Restart behavior

If Meilisearch exits unexpectedly, report the failure.

Avoid infinite rapid restart loops.

Do not delete its database as a recovery mechanism.

## 7. Lantern Application Startup

After Meilisearch becomes ready, start the actual Lantern executable.

### Requirements

- Use the correct packaged executable path.
- Use the persistent configuration.
- Preserve environment variable overrides.
- Support installation paths containing spaces.
- Forward termination signals appropriately.
- Display startup failures.
- Preserve useful exit codes.

### Application URL

Do not assume:

```text
http://127.0.0.1:5000
```

unless it is the actual configured address.

Determine the effective listening URL from the application configuration or startup output.

Display the correct address.

### Working directory

Do not rely on the user's current directory.

Ensure the application's resource paths are resolved consistently.

## 8. Embedding Server and Model Initialization

Lantern uses the local Granite embedding model.

Verify the existing embedding startup lifecycle.

### Requirements

- Resolve the bundled model path.
- Verify the model exists.
- Validate the model configuration.
- Preserve LlamaPure initialization behavior.
- Preserve the bounded inference worker.
- Keep the embedding endpoint on loopback.
- Report embedding readiness accurately.

### Important

Do not create a second embedding server.

Reuse the existing in-process embedding implementation.

Do not change the validated Granite model or inference behavior.

### Failure policy

If embedding initialization fails:

- Report the error clearly.
- Preserve keyword search where possible.
- Do not claim Hybrid Search is ready.
- Avoid destroying existing search data.

## 9. Administrator First-Run Setup

The current Quick Start requires:

```powershell
$env:LANTERN_ADMIN_USERNAME = "admin"
$env:LANTERN_ADMIN_PASSWORD = "<14-to-128-character-password>"
```

Investigate how the application actually consumes these variables.

### Desired behavior

First-run administrator provisioning should be simple and secure.

### Requirements

- Do not ship default passwords.
- Do not hardcode credentials.
- Do not print passwords.
- Do not store passwords in plaintext configuration.
- Do not repeat provisioning after successful initialization.
- Do not require the user to manually maintain environment variables on every startup.

### Recommended implementation

Prefer a secure one-time setup operation supported by the application.

For example:

- A controlled local CLI initialization command.
- A secure interactive initialization prompt.
- Another existing supported provisioning mechanism.

If credentials must be supplied through environment variables, restrict them to the first-run child process and clear them after use.

Avoid placing passwords in process command-line arguments.

### Noninteractive startup

After initialization, Lantern must start without prompting for administrator credentials.

This is required for Windows Service operation.

## 10. Interactive Shutdown

The startup script must handle ordinary shutdown correctly.

### Expected behavior

When the user presses Ctrl+C:

1. Request Lantern shutdown.
2. Allow application-hosted services to stop.
3. Allow active crawler jobs to receive cancellation.
4. Preserve persistent job recovery information.
5. Stop the Meilisearch process owned by this launcher.
6. Exit without leaving unnecessary child processes.

### Requirements

- Do not kill unrelated Meilisearch processes.
- Do not delete temporary or persistent data indiscriminately.
- Avoid orphaned processes.
- Avoid hanging indefinitely.
- Use bounded shutdown behavior.

Test actual Windows PowerShell process and signal behavior.

Do not assume Ctrl+C automatically propagates correctly through every child-process arrangement.

## 11. Start and Stop Entry Points

Provide a simple entry point for normal startup:

```text
start-lantern.ps1
```

A separate stop script may be added if necessary:

```text
stop-lantern.ps1
```

However, avoid implementing redundant process management mechanisms.

### Requirements

- Console startup is straightforward.
- Shutdown behavior is documented.
- Service mode remains separate.
- Scripts do not require hardcoded installation paths.
- Scripts operate correctly from the extracted ZIP.

### PowerShell compatibility

Support Windows PowerShell 5.1 unless the project explicitly requires PowerShell 7.

Do not require users to install PowerShell 7 solely to start Lantern.

### Shell scripts

A Unix `start-lantern.sh` script is not required for the Windows x64 release.

Do not introduce unsupported Linux startup instructions merely for symmetry.

## 12. Windows Service Compatibility

Review the existing Windows Service implementation.

The new startup script must not conflict with it.

### Important

Windows Service operation should use a noninteractive startup path.

Do not make the Windows Service depend on an interactive PowerShell prompt.

### Requirements

- Reuse existing persistent configuration.
- Reuse existing Meilisearch credentials.
- Reuse existing data directories.
- Do not generate new credentials during every service start.
- Ensure Meilisearch starts before dependent search operations.
- Preserve automatic job recovery.

### Architecture

Prefer using shared application startup and service orchestration logic.

The PowerShell launcher should not become a second independent implementation of complex application lifecycle behavior.

If the current application already manages Meilisearch internally, delegate to it instead of launching a duplicate process.

### Scope

Do not rewrite Windows Service installation unless required to correct an actual startup defect.

## 13. Release Packaging Updates

Update:

```text
scripts/Build-Release.ps1
```

or the existing release packaging implementation.

### Requirements

- Include the unified startup script.
- Include required helper scripts.
- Include Meilisearch.
- Include the Lantern executable.
- Include the GGUF model.
- Include configuration templates.
- Preserve third-party notices.
- Exclude secrets.
- Exclude development data.

### Suggested ZIP layout

```text
Lantern/
    |
    +-- start-lantern.ps1
    |
    +-- app/
    |     +-- Lantern.Web.exe
    |
    +-- bin/
    |     +-- meilisearch/
    |           +-- meilisearch.exe
    |
    +-- models/
    |     +-- granite-embedding-97M-multilingual-r2.gguf
    |
    +-- config/
    |     +-- appsettings.example.json
    |
    +-- scripts/
    |     +-- start-meilisearch.ps1
    |     +-- stop-meilisearch.ps1
    |
    +-- docs/
          +-- INSTALL.md
```

This structure is illustrative.

Adapt it to the actual release layout.

Do not document paths that differ from the packaged files.

## 14. Simplify README Quick Start

Rewrite the root README Quick Start section.

The desired result should be approximately:

```markdown
## Quick Start

1. Extract `Lantern-<version>-win-x64.zip`.

2. Open PowerShell in the extracted directory.

3. Start Lantern:

   ```powershell
   .\start-lantern.ps1
   ```

4. On first launch, complete the administrator setup
   when prompted.

5. Open the URL displayed by Lantern.

See the installation guide for Windows Service setup
and advanced configuration.
```

This is a target user experience, not verified current behavior.

Only publish these instructions after the implementation supports them.

### Documentation requirements

- Do not require manual directory creation.
- Do not require manual secret generation.
- Do not require manual Meilisearch startup.
- Do not require manual environment variable setup on every launch.
- Do not hardcode an unverified HTTP port.
- Do not require administrator elevation for normal console startup.

If any of these requirements cannot be met, clearly document the remaining limitation.

## 15. Update Installation Documentation

Update:

```text
README.md
release/docs/INSTALL.md
```

and other relevant documentation.

### Include

- Quick Start.
- First-run initialization.
- Administrator provisioning.
- Console startup.
- Console shutdown.
- Windows Service startup.
- Persistent data location.
- Meilisearch lifecycle.
- Embedding model initialization.
- Configuration overrides.
- Common startup errors.
- Upgrade compatibility.

### Documentation consistency

Search for outdated references to:

```text
Initialize-Lantern.ps1
Start-Lantern.ps1
start-lantern.sh
start-meilisearch.ps1
meili-master-key.txt
meili-api-key.txt
LANTERN_ADMIN_PASSWORD
```

Correct instructions that conflict with the implemented workflow.

Do not remove legitimate developer instructions merely because they differ from release instructions.

Clearly distinguish developer workflows from end-user workflows.

## 16. Security Requirements

Preserve the security improvements from Phase 10.

### Requirements

- Use cryptographically secure secret generation.
- Protect generated secrets.
- Avoid plaintext password persistence.
- Preserve loopback isolation for Meilisearch.
- Preserve loopback isolation for embedding.
- Avoid broad filesystem permissions.
- Avoid requiring elevation for ordinary operation.
- Avoid exposing secrets in process arguments.
- Avoid logging credentials.
- Avoid connecting to an unverified existing Meilisearch process.

### PowerShell execution policy

Document supported script execution methods.

Do not recommend permanently disabling PowerShell execution policy system-wide.

If a temporary process-scoped execution policy is necessary, explain it accurately and avoid making it the default when unnecessary.

## 17. Tests

Add automated tests where practical.

### Script tests

Verify:

1. Installation path resolution.
2. Paths containing spaces.
3. Missing Lantern executable.
4. Missing Meilisearch executable.
5. Missing GGUF model.
6. First-run detection.
7. Existing configuration preservation.
8. Existing secret preservation.
9. Secure secret generation.
10. Partial initialization recovery.
11. Port conflict detection.
12. Meilisearch readiness timeout.
13. Startup failure propagation.
14. Process ownership tracking.
15. Shutdown cleanup.

### First-run integration test

Use an isolated Windows test directory.

Verify:

1. Extract the release archive.
2. Execute the startup script.
3. Create configuration.
4. Generate secrets.
5. Provision the administrator.
6. Start bundled Meilisearch.
7. Start Lantern.
8. Verify application readiness.
9. Verify keyword search.
10. Verify hybrid search where the model is available.
11. Stop Lantern.
12. Restart Lantern.
13. Verify persistent data.
14. Verify administrator credentials are preserved.

### Subsequent startup test

Verify that the second launch does not:

- Regenerate secrets.
- Replace configuration.
- Reset the database.
- Reset administrator credentials.
- Delete search indexes.
- Trigger unnecessary model downloads.

### Shutdown test

Verify that stopping the launcher:

- Stops Lantern.
- Stops only the Meilisearch instance it owns.
- Preserves job recovery information.
- Leaves persistent data intact.

### Service regression test

Verify that Windows Service startup still works without interactive prompts.

### Packaging test

Build the actual release ZIP.

Extract it into a clean directory.

Execute the documented Quick Start.

Do not test only the source repository.

## 18. Acceptance Criteria

The task is complete when:

- A unified Windows startup command exists.
- The release ZIP includes the startup entry point.
- Users do not need to start Meilisearch manually.
- First-run initialization is idempotent.
- Secrets are generated securely.
- Existing secrets are preserved.
- Administrator provisioning works.
- Normal subsequent startup is noninteractive.
- Lantern starts with the correct configuration.
- Meilisearch readiness is verified.
- The embedding model loads correctly.
- Keyword search works.
- Hybrid search works when configured and ready.
- Ctrl+C or normal shutdown cleans up owned processes.
- Existing Windows Service operation remains functional.
- README Quick Start matches the implementation.
- The installation guide matches the implementation.
- No nonexistent startup scripts are referenced.
- The extracted release ZIP passes startup tests.
- No existing application data is lost.

Do not report these criteria as satisfied unless they have actually been verified.

## 19. Deliverables

Provide:

1. Unified Windows startup entry point.
2. Safe first-run initialization integration.
3. Secure secret generation.
4. Meilisearch startup orchestration.
5. Meilisearch readiness verification.
6. Lantern application startup.
7. Safe interactive shutdown.
8. Existing process detection.
9. Administrator provisioning integration.
10. Windows Service compatibility.
11. Release packaging updates.
12. Simplified README Quick Start.
13. Updated installation documentation.
14. Automated startup tests.
15. Release ZIP smoke test.

## 20. Required Completion Report

At the end of implementation, provide:

### A. Existing startup architecture

Explain how Lantern, Meilisearch, and the embedding server were started before the changes.

### B. Implemented startup architecture

Explain the new unified startup mechanism.

### C. Files changed

List all created and modified files.

### D. Initialization behavior

Explain:

- Configuration creation.
- Secret generation.
- Administrator provisioning.
- SQLite initialization.
- Meilisearch initialization.
- Embedding initialization.

### E. Process lifecycle

Explain:

- Meilisearch ownership.
- Readiness checks.
- Ctrl+C behavior.
- Failure cleanup.
- Service compatibility.

### F. Updated Quick Start

Show the final README instructions.

### G. Verification

Report actual commands executed and observed results.

### H. Known limitations

Document any unsupported or partially implemented startup behavior.

Do not claim the release ZIP is validated unless the packaged artifact was actually tested.

---

## Final Instructions

Inspect the current Lantern repository and identify the actual initialization and startup behavior.

Implement a simple unified Windows startup entry point that works from the extracted release ZIP.

Prefer a root-level `start-lantern.ps1`.

Reuse existing Meilisearch scripts and application services rather than duplicating functionality.

Provide idempotent first-run configuration, secure secret generation, and secure administrator provisioning.

Start the bundled Meilisearch instance automatically and verify readiness.

Start the Lantern application using the correct packaged executable and configuration.

Preserve the existing local embedding server and Granite model behavior.

Handle interactive shutdown correctly and stop only owned child processes.

Preserve Windows Service compatibility.

Update release packaging to include the new startup entry point.

Rewrite README and installation documentation to match the actual tested workflow.

**The primary success condition is that a user can extract Lantern's Windows x64 ZIP, run `.\start-lantern.ps1`, complete secure first-run setup if necessary, and use Lantern without manually configuring secrets, starting Meilisearch, or performing undocumented initialization steps.**
