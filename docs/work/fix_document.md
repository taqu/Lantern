# Lantern — Fix Quick Start and Installation Documentation

## Objective

The current README contains installation instructions that do not match the actual Lantern implementation.

Specifically, the Quick Start section references:

```powershell
.\scripts\Initialize-Lantern.ps1
```

However, `Initialize-Lantern.ps1` does not exist.

Your task is to investigate the actual installation, initialization, and startup mechanisms implemented in the repository, then correct the documentation accordingly.

**The documentation must describe the existing implementation, not an assumed or planned implementation.**

Do not create missing scripts merely to make the existing documentation appear correct.

## 1. Investigate the Actual Implementation

Before editing documentation, inspect the following:

1. Root `README.md`.
2. `release/docs/INSTALL.md`.
3. Other installation and deployment documentation.
4. `scripts/` and `release/scripts/`, if present.
5. The Phase 12 packaging implementation.
6. The release ZIP generation script.
7. GitHub Actions release workflows.
8. The application's actual entry point.
9. Existing configuration and initialization mechanisms.
10. Administrator provisioning logic.
11. Meilisearch startup and lifecycle management.
12. Windows Service installation and startup mechanisms.

Determine how Lantern is actually installed and started.

Do not assume the Phase 12 roadmap was implemented exactly as originally described.

### Questions to answer

- Does Lantern initialize automatically on first launch?
- Is there an existing initialization command?
- Does administrator provisioning occur through the browser, CLI, or another mechanism?
- Does `Start-Lantern.ps1` actually exist?
- Is `Start-Lantern.ps1` included in the release ZIP?
- Can Lantern be started directly through its executable?
- Does Lantern automatically launch Meilisearch?
- Is the application URL actually `http://127.0.0.1:5000`?
- Is administrator elevation really required?
- Where are configuration and persistent data stored?
- What files and directories are actually included in the release ZIP?

Answer these questions from the implementation.

Do not invent behavior.

## 2. Correct README Quick Start

Replace the existing incorrect Quick Start section.

Current documentation:

```markdown
## Quick Start

1. Extract `Lantern-<version>-win-x64.zip` to `C:\Lantern\`
2. Run as Administrator:
   ```powershell
   .\scripts\Initialize-Lantern.ps1
   ```
3. Start Lantern:
   ```powershell
   .\scripts\Start-Lantern.ps1
   ```
4. Open `http://127.0.0.1:5000` in your browser.

See [`release/docs/INSTALL.md`](release/docs/INSTALL.md) for full instructions.
```

Replace it with instructions that accurately reflect the actual release package.

### Requirements

- Remove references to nonexistent scripts.
- Use commands that work with the current release ZIP.
- Use paths relative to the documented working directory.
- Document the actual first-run behavior.
- Document administrator provisioning if required.
- Use the actual configured listening address.
- Mention administrator elevation only when necessary.
- Keep the Quick Start concise.
- Link to the correct detailed installation documentation.

Do not provide speculative alternative commands.

If the application supports multiple startup methods, document the simplest supported method in Quick Start and explain the alternatives in the installation guide.

## 3. Verify the Release ZIP Structure

Inspect the actual release packaging logic.

If a release ZIP is available, inspect its contents.

Otherwise, build the release artifact using the existing packaging workflow where practical.

Verify the actual locations of:

- Lantern executable.
- Startup scripts.
- Service installation scripts.
- Configuration templates.
- Meilisearch executable.
- Embedding model.
- Documentation.

### Important

Do not assume that the source repository structure matches the ZIP structure.

For example, a file located under `release/scripts/` in the source repository may appear under `scripts/` in the archive.

Documentation intended for end users must refer to the paths inside the extracted archive.

Do not document a command unless its referenced executable or script exists at that location.

## 4. Correct Installation Documentation

Review:

```text
release/docs/INSTALL.md
```

If this file exists, update it to reflect the current implementation.

Also inspect other installation-related documentation.

Correct inconsistent information concerning:

- Initial setup.
- Configuration generation.
- Administrator provisioning.
- Application startup.
- Meilisearch startup.
- Embedding model initialization.
- Windows Service installation.
- Windows Service removal.
- Persistent data directories.
- Default HTTP port.
- Required permissions.

Remove references to nonexistent scripts, commands, and configuration files.

Preserve documented functionality that is actually implemented.

Do not remove useful instructions merely because they require clarification.

## 5. Distinguish Source Execution from Release Execution

Clearly separate developer instructions from end-user installation instructions.

### Developer execution

If supported, document how to run Lantern from the source repository using .NET tooling.

### Release execution

Document how to run Lantern from the extracted Windows x64 release ZIP.

The root README Quick Start should prioritize the release workflow.

Do not require end users to install:

- .NET SDK.
- Visual Studio.
- Python.
- Node.js.
- Docker.

Unless a dependency is genuinely required by the existing release, do not introduce it into the installation instructions.

If an unexpected dependency exists, report it as a discrepancy rather than silently documenting it as an intentional requirement.

## 6. Verify First-Run Behavior

Inspect the actual initialization implementation.

Determine whether the application:

- Automatically creates required directories.
- Automatically initializes SQLite.
- Applies database migrations.
- Generates application secrets.
- Initializes Meilisearch.
- Creates or provisions the administrator.
- Requires manual configuration before startup.

Document the exact supported sequence.

### Important restrictions

Do not claim automatic initialization exists unless the code implements it.

Do not claim an administrator account is created automatically unless verified.

Do not introduce default credentials.

Do not invent an initialization command.

If a required initialization step is missing from the implementation, document it as a functional gap in the completion report.

Do not silently implement a new setup system as part of this documentation task.

## 7. Verify Startup and Network Address

Inspect the actual startup configuration.

Verify:

- Executable name.
- Startup command.
- HTTP/HTTPS binding.
- Default port.
- Configuration overrides.
- Startup dependencies.
- Meilisearch lifecycle.

If the default address is not `http://127.0.0.1:5000`, correct the README.

If the address depends on configuration, explain how to determine the actual address.

Do not hardcode an unverified URL.

Do not instruct users to expose the application to their network unnecessarily.

## 8. Verify Administrator Requirements

Review which steps require Windows administrator privileges.

For example:

- Extracting into a user-writable directory may not require elevation.
- Running Lantern interactively should not require administrator privileges unless there is a concrete implementation dependency.
- Installing a Windows Service normally requires elevation.

Document these distinctions accurately.

Do not instruct users to run the entire application as Administrator merely because a setup operation requires elevation.

Preserve the least-privilege approach established in Phase 10.

## 9. Check Documentation Consistency

Search the repository for references to:

```text
Initialize-Lantern.ps1
Start-Lantern.ps1
Install-LanternService.ps1
Uninstall-LanternService.ps1
127.0.0.1:5000
Quick Start
First Run
Installation
```

Inspect each relevant occurrence.

Correct outdated instructions where necessary.

Do not perform a blind text replacement.

Some references may be historical or architectural descriptions rather than executable instructions.

Keep the scope focused on user-facing installation, startup, and deployment documentation.

Ensure README links point to existing files.

## 10. Validate the Corrected Instructions

Do not stop after editing Markdown.

Validate the documented procedure against the actual implementation.

### Required checks

1. Verify every referenced script exists.
2. Verify every referenced executable exists.
3. Verify documented relative paths.
4. Verify the actual release archive layout.
5. Verify the startup command.
6. Verify the first-run process.
7. Verify the application listening address.
8. Verify links to detailed documentation.

If practical, execute the corrected startup procedure in an isolated environment.

Do not use or modify production Lantern data.

### Important

If full installation testing is unavailable, perform static verification and explicitly report what remains untested.

Do not claim the Quick Start was executed successfully unless it actually was.

## 11. Scope Restrictions

This is primarily a documentation correction task.

Do not:

- Create `Initialize-Lantern.ps1` merely because README references it.
- Redesign first-run initialization.
- Rewrite the Windows Service implementation.
- Change the release directory layout unnecessarily.
- Introduce a new installer.
- Change the Meilisearch lifecycle.
- Change the embedding infrastructure.
- Change persistent data storage.
- Introduce unrelated application features.

If the implementation is genuinely broken, identify the issue separately.

Only make a minimal code or packaging correction when it is essential to make an already supported startup workflow function, and clearly explain why that change is necessary.

## 12. Acceptance Criteria

The task is complete when:

- README Quick Start reflects the actual implementation.
- No nonexistent initialization script is referenced as a required step.
- All documented commands correspond to real files or supported application commands.
- Paths match the extracted release ZIP.
- First-run behavior is accurately documented.
- Administrator provisioning instructions are accurate.
- Default application URL is verified.
- Administrator privileges are requested only when necessary.
- `release/docs/INSTALL.md` is consistent with README.
- Other relevant installation documentation is consistent.
- Documentation links are valid.
- Existing functionality is preserved.
- Validation results are reported.

## 13. Required Completion Report

After making the changes, report:

### A. Actual installation mechanism

Explain how Lantern is currently initialized and started.

### B. Root cause

Explain why README referenced `Initialize-Lantern.ps1` even though it does not exist.

Distinguish confirmed causes from assumptions.

### C. Documentation changes

List every modified file.

### D. Corrected Quick Start

Show the final Quick Start section.

### E. Verification

Report:

- Files and scripts verified.
- Packaging layout verified.
- Commands executed.
- Startup results.
- Tests performed.
- Tests not performed.

### F. Remaining discrepancies

Identify any functionality described in the original Phase 12 roadmap but not actually implemented.

Do not silently fill these gaps with speculative documentation.

---

## Final Instructions

Investigate the actual Lantern repository and Phase 12 packaging implementation.

Correct README and installation documentation to match the current working implementation.

Remove invalid references to `Initialize-Lantern.ps1`.

Verify whether `Start-Lantern.ps1` and other documented scripts actually exist.

Verify the startup command, initialization behavior, administrator provisioning, release ZIP structure, and application URL.

Prioritize accurate, tested instructions over the originally planned Phase 12 workflow.

Do not implement nonexistent scripts merely to satisfy outdated documentation.

**The primary success condition is that a user can follow the README Quick Start exactly as written, using only files and commands actually provided by the current Lantern release, and successfully start the application without encountering missing scripts or undocumented initialization steps.**
