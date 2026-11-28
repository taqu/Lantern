# Lantern — Developer Environment, IDE Debugging, and Development Documentation

## Objective

Improve Lantern's developer experience so that developers can build, run, and debug the application directly from the source repository using Visual Studio or Visual Studio Code.

Currently, Lantern's startup and initialization workflow is oriented toward release packaging, and the application cannot be conveniently launched and debugged from an IDE.

**Your task is to establish a reliable development startup workflow, provide working IDE debugging configurations, and create comprehensive developer documentation.**

This task complements the Windows startup improvements.

The production release workflow and the developer workflow must remain separate but share the same application initialization and service lifecycle logic where practical.

Do not require developers to build a release ZIP merely to debug Lantern.

---

## 1. Investigate the Existing Repository

Before making changes, inspect:

1. The solution and project structure.
2. The ASP.NET Core application entry point.
3. `Program.cs` and dependency injection configuration.
4. Existing `launchSettings.json`.
5. Existing Visual Studio solution configurations.
6. Any `.vscode/launch.json` and `.vscode/tasks.json`.
7. Existing application settings and environment variable overrides.
8. Meilisearch startup and shutdown scripts.
9. SQLite initialization and EF Core migrations.
10. Administrator provisioning.
11. The embedded REST inference server.
12. LlamaPure native library loading.
13. Granite GGUF model path resolution.
14. Existing scripts and documentation.
15. Phase 11 CI and test infrastructure.
16. Release startup implementation.

Determine the actual reasons why IDE debugging does not currently work.

Do not assume the cause is only missing documentation.

Fix the underlying development startup problems where necessary.

Preserve the existing production and Windows Service behavior.

## 2. Target Developer Experience

The primary goal is:

**A developer should be able to open the Lantern solution in Visual Studio, select the Lantern startup project, press F5, and debug the application.**

Visual Studio Code should provide an equivalent workflow.

### Expected behavior

When debugging:

1. Lantern loads development configuration.
2. Development data directories are selected.
3. Required application configuration is validated.
4. SQLite initializes or applies supported migrations.
5. Development administrator provisioning works.
6. Meilisearch is started or connected to safely.
7. The local embedding infrastructure is initialized when enabled.
8. The ASP.NET Core application starts.
9. The configured browser URL is available.
10. Breakpoints work in application code.

### Requirements

- Do not require building a release ZIP.
- Do not require copying binaries into a release directory.
- Do not require manually creating production configuration.
- Do not require production administrator credentials.
- Do not require Windows administrator privileges for normal IDE debugging.
- Do not modify production SQLite or Meilisearch data.
- Do not depend on hardcoded developer-machine paths.
- Do not silently connect to an unrelated Meilisearch instance.

## 3. Separate Development and Production Configuration

Use standard ASP.NET Core environment configuration.

Support:

```text
Development
Production
```

Use the established configuration system.

Suggested files:

```text
appsettings.json
appsettings.Development.json
appsettings.Production.json
```

Do not introduce files unnecessarily if the existing implementation already has an appropriate structure.

### Development configuration

Provide development-specific defaults for:

- Application binding.
- SQLite location.
- Meilisearch executable.
- Meilisearch data directory.
- Meilisearch port.
- Embedding server port.
- GGUF model path.
- Logging.
- Development administrator initialization.

### Production configuration

Preserve existing security requirements.

Do not weaken production authentication or network isolation for convenience.

### Secrets

Do not commit actual passwords or API keys.

Use:

- ASP.NET Core User Secrets.
- Development-only generated secrets.
- Environment variables where appropriate.

Document how secrets are initialized.

Do not require developers to edit tracked configuration with personal credentials.

## 4. Development Data Isolation

Development execution must use a dedicated data directory.

Recommended default:

```text
<RepositoryRoot>/.lantern-dev/
```

Suggested structure:

```text
.lantern-dev/
    |
    +-- config/
    +-- database/
    +-- meilisearch/
    +-- secrets/
    +-- logs/
    +-- runtime/
```

This is illustrative.

### Requirements

- Add the development data directory to `.gitignore`.
- Do not store generated secrets in Git.
- Do not use `%ProgramData%\Lantern` for normal development.
- Do not use production Meilisearch indexes.
- Do not reuse production job history.
- Do not require developer-specific absolute paths.
- Support an override for the development data root.

### Important

If the application already implements configurable data roots, reuse that mechanism.

Do not introduce a second incompatible configuration system.

### Path resolution

Resolve repository-relative development paths consistently.

Do not rely on the IDE's current working directory.

Use a stable application content root or explicitly configured repository root.

## 5. IDE Debugging Configuration

Provide a working Visual Studio launch profile.

Suggested location:

```text
Properties/launchSettings.json
```

### Requirements

- Set `ASPNETCORE_ENVIRONMENT=Development`.
- Use an explicit development URL.
- Use the correct startup project.
- Use a valid working directory.
- Ensure project references resolve.
- Ensure native dependencies can load.
- Make the configuration compatible with F5 debugging.

### Example

```json
{
  "profiles": {
    "Lantern Development": {
      "commandName": "Project",
      "launchBrowser": true,
      "applicationUrl": "http://127.0.0.1:5100",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

This is an example.

Use the actual project paths and supported settings.

Do not hardcode ports already occupied by another required development service.

### Visual Studio

Verify that:

- The solution opens.
- The correct project can be selected.
- F5 launches the application.
- Breakpoints are hit.
- Exceptions are visible.
- Stopping debugging shuts down owned development processes safely.

Do not claim Visual Studio F5 was verified unless it was actually tested.

## 6. Visual Studio Code Debugging

Provide or update:

```text
.vscode/launch.json
.vscode/tasks.json
```

### Requirements

- Support debugging the ASP.NET Core application.
- Use the correct built assembly or project.
- Set the development environment.
- Configure the correct working directory.
- Include a build task.
- Preserve environment variables.
- Support breakpoints.

### Prelaunch tasks

Use standard `dotnet build` where appropriate.

Do not require a release publish.

### Process lifecycle

Ensure stopping the debugger does not leave orphaned Meilisearch processes unnecessarily.

Do not use broad process-name termination.

### Documentation

Describe required VS Code extensions and SDK prerequisites.

Do not claim automatic browser debugging or hot reload works unless verified.

## 7. Development Meilisearch Management

The developer workflow must support Meilisearch reliably.

Inspect the existing scripts:

```text
scripts/start-meilisearch.ps1
scripts/stop-meilisearch.ps1
```

### Preferred behavior

Reuse the shared Meilisearch lifecycle implementation introduced for production startup.

Development mode should use:

- A dedicated Meilisearch data directory.
- A development-only master key.
- A development-specific port.
- Loopback binding.
- A verified native executable.

### Startup options

Support a clear documented approach:

**Option A — Automatic development startup**

Lantern manages Meilisearch during application startup.

This is preferred if the application already supports managed Meilisearch processes.

**Option B — Explicit development dependency startup**

A development helper script starts Meilisearch before IDE debugging.

Use this only if automatic management is not practical or would conflict with the existing architecture.

### Important

Do not implement two independent Meilisearch process managers.

Use the same process ownership, readiness checks, and safe shutdown rules where practical.

### Existing process detection

If the development port is already occupied:

- Identify the process.
- Verify whether it is a recognized Lantern development instance.
- Avoid connecting to arbitrary services.
- Report conflicts clearly.

### Isolation

Do not allow a development run to modify production Meilisearch indexes.

## 8. Development Embedding Configuration

The development workflow must support the existing LlamaPure and Granite embedding infrastructure.

Model:

```text
resources/granite-embedding-97M-multilingual-r2.gguf
```

### Requirements

- Locate the model from the repository.
- Avoid hardcoded absolute paths.
- Support a configurable model path.
- Validate model existence.
- Verify native library loading.
- Preserve existing embedding dimensions.
- Preserve the validated inference behavior.

### Development startup

The embedding server may be enabled or disabled through development configuration.

### When enabled

- Start the existing inference worker.
- Start the existing loopback REST endpoint.
- Validate model readiness.
- Enable Hybrid Search when ready.

### When disabled

- Allow normal keyword search.
- Report Hybrid Search as unavailable.
- Do not silently use a different embedding provider.

### Important

Do not implement a second embedding server.

Do not replace LlamaPure.

Do not download the GGUF model automatically during every debug session.

## 9. Native Library Loading

Investigate how LlamaPure loads native llama.cpp dependencies.

### Requirements

- Support Debug configuration.
- Support Windows x64.
- Resolve native libraries correctly from IDE build output.
- Avoid relying on globally installed DLLs.
- Avoid relying on release ZIP directory layout.
- Preserve existing Release behavior.

### Verification

Run a native embedding smoke test from the development build output.

A successful managed build does not prove native DLL loading works.

### Failure diagnostics

Provide clear errors for:

```text
Native library not found
Incorrect architecture
Missing dependent DLL
Model file not found
Model initialization failed
```

Do not report a missing native dependency as a generic application startup failure without useful context.

## 10. SQLite Initialization and Migrations

Use a development SQLite database.

### Requirements

- Create the development database when missing.
- Apply existing EF Core migrations.
- Preserve existing development data across debug sessions.
- Support explicit reset through a development command.
- Prevent accidental production database access.

### Reset operation

Provide a safe development-only reset mechanism if useful.

The reset operation must:

- Clearly identify the target directory.
- Reject production data paths.
- Require explicit confirmation.
- Avoid deleting unrelated files.

Do not automatically reset the development database on every F5 launch.

### Schema updates

Developers should be able to test migrations directly.

Document the supported EF Core migration workflow.

## 11. Development Administrator Provisioning

Provide a reliable method to create the initial development administrator.

### Requirements

- Do not use hardcoded production credentials.
- Do not commit passwords.
- Do not require repeated provisioning.
- Do not require setting environment variables manually for every debug session.

### Recommended approach

Use the existing administrator provisioning mechanism with development-only configuration.

Possible options:

- A local development initialization command.
- User Secrets.
- A one-time development setup script.
- A secure interactive setup process.

Choose the simplest option supported by the existing implementation.

### Important

Do not add an unauthenticated administrator creation endpoint.

Do not disable production authentication.

Do not automatically replace an existing administrator account.

## 12. Development Helper Script

Provide a developer-oriented setup script if it materially improves the workflow.

Suggested filename:

```text
scripts/setup-dev.ps1
```

### Responsibilities

- Validate .NET SDK availability.
- Validate Meilisearch binary availability.
- Validate the model path when embedding is enabled.
- Initialize development directories.
- Initialize development secrets.
- Prepare a development configuration.
- Report the selected local ports.
- Explain the next step for IDE debugging.

### Requirements

- Make repeated execution safe.
- Do not require administrator elevation.
- Do not modify production directories.
- Do not automatically install unnecessary external tools.
- Do not overwrite existing development secrets.
- Do not launch a second application instance unintentionally.

### Important

The setup script should prepare the environment.

It should not require developers to use PowerShell for every subsequent F5 debugging session.

## 13. Development Startup Script

A simple CLI development startup script may also be useful.

Suggested filename:

```text
scripts/start-dev.ps1
```

### Expected behavior

```powershell
.\scripts\start-dev.ps1
```

This should run Lantern in Development mode using the same configuration as IDE debugging.

### Requirements

- Use `dotnet run` or the existing development host.
- Avoid release packaging.
- Use development data paths.
- Start required dependencies through the shared lifecycle.
- Display the effective application URL.
- Support clean shutdown.

### Consistency

The CLI development workflow and IDE debugging must behave consistently.

Do not allow `start-dev.ps1` to work while Visual Studio F5 remains broken.

**IDE debugging is the primary acceptance criterion.**

## 14. Blazor Development Experience

Preserve normal ASP.NET Core and Blazor development features.

### Requirements

- Support Debug builds.
- Support breakpoints in server-side components.
- Provide useful developer exception diagnostics.
- Preserve static web assets.
- Avoid production-only middleware behavior breaking development.
- Support normal development logging.

### Hot reload

Verify whether hot reload is supported by the existing configuration.

Document actual behavior.

Do not introduce a custom frontend build system.

Do not require Node.js for Blazor development unless an existing genuine dependency requires it.

## 15. Debugging Background Services

Lantern runs several background services.

These may include:

- Meilisearch process supervision.
- Embedding inference.
- Persistent job dispatch.
- Crawler scheduling.
- Category synchronization.
- Recovery processing.

### Requirements

- Ensure hosted services initialize correctly under the debugger.
- Provide meaningful log categories.
- Avoid swallowing background task exceptions.
- Preserve cancellation behavior.
- Avoid blocking application startup indefinitely.
- Allow breakpoints in crawler execution and job management.

### Debugger pauses

A debugger may suspend execution for an extended period.

Review whether service health checks or timeouts can mistakenly interpret a paused debugger as a production failure.

Provide development-appropriate configuration where justified.

Do not globally disable production timeouts.

## 16. Port Configuration

Document development ports.

Suggested separation:

| Service | Development port |
|---|---|
| Lantern Web | 5100 |
| Meilisearch | 7702 |
| Embedding REST | 7703 |

These are illustrative.

Use actual available ports and established repository conventions.

### Requirements

- Avoid production port conflicts.
- Avoid hardcoded conflicting settings.
- Keep internal services on loopback.
- Validate binding configuration.
- Report port conflicts clearly.

### Multiple development instances

If only one development instance is supported, document that limitation.

Do not implement complex multi-instance management solely for IDE debugging.

## 17. Development Documentation

Create or update a dedicated development guide.

Suggested path:

```text
docs/development.md
```

If the repository already uses separate English and Japanese documentation trees, follow that structure.

For example:

```text
docs/en/development.md
docs/ja/development.md
```

### Required sections

1. Development prerequisites.
2. Repository checkout.
3. .NET SDK setup.
4. Meilisearch dependency setup.
5. Granite GGUF model placement.
6. Initial development configuration.
7. Development administrator provisioning.
8. Visual Studio debugging.
9. VS Code debugging.
10. CLI development startup.
11. SQLite database and migrations.
12. Meilisearch process management.
13. Embedding server troubleshooting.
14. Running tests.
15. Resetting development data.
16. Debugging background services.
17. Common errors and solutions.
18. Differences between Development and Production.

### Important

The documentation must describe commands and files that actually exist.

Do not reference planned scripts or hypothetical launch profiles.

## 18. README Developer Section

Add a concise development section to the root README.

Suggested structure:

```markdown
## Development

Lantern is developed with .NET 10 on Windows.

1. Install the .NET 10 SDK.
2. Clone the repository.
3. Run the development setup command.
4. Open the solution in Visual Studio.
5. Select the Lantern web project.
6. Press F5.

For VS Code and command-line development,
see the development guide.
```

This is illustrative.

Only publish it after the corresponding workflow works.

Keep release Quick Start and developer instructions separate.

## 19. Documentation Consistency

Review existing documentation for outdated developer instructions.

Check references to:

```text
dotnet run
launchSettings.json
Meilisearch
start-meilisearch.ps1
set-embedding.ps1
appsettings.Development.json
LANTERN_ADMIN_USERNAME
LANTERN_ADMIN_PASSWORD
ProgramData
```

Correct inconsistencies.

Do not blindly replace valid production instructions.

Clarify which instructions apply to development and which apply to release installations.

## 20. Testing Requirements

Implement automated checks where practical.

### Development initialization tests

Verify:

1. Development data directory creation.
2. Configuration initialization.
3. Secret preservation.
4. SQLite migration.
5. Administrator provisioning.
6. Model path resolution.
7. Missing dependency diagnostics.

### IDE configuration validation

Verify:

1. `launchSettings.json` is valid.
2. The startup project exists.
3. Development environment variables are correct.
4. Development ports are consistent.
5. Working directory assumptions are valid.
6. Native dependencies are available in Debug output.

### CLI development test

Verify:

1. `dotnet build` succeeds.
2. `dotnet run` starts the application.
3. Meilisearch becomes ready.
4. SQLite is initialized.
5. Keyword search works.
6. Hybrid search works when enabled.
7. Shutdown is clean.

### Visual Studio validation

On Windows, verify:

1. Open the solution.
2. Select the correct startup project.
3. Press F5.
4. Confirm application startup.
5. Hit a breakpoint.
6. Stop debugging.
7. Verify owned processes are cleaned up.

If IDE automation is unavailable, provide the exact manual validation procedure and clearly report that it was not executed.

### VS Code validation

Verify the provided launch and task configurations.

Test debugging where the environment supports it.

Do not claim F5 validation based only on JSON syntax checks.

### Production regression

Ensure:

- Production release startup remains functional.
- Windows Service startup remains functional.
- Production data directories remain unchanged.
- Production authentication remains secure.
- Release packaging remains functional.

## 21. Acceptance Criteria

This task is complete when:

- Visual Studio can launch Lantern using F5.
- Breakpoints work.
- VS Code has a supported debugging configuration.
- Developers do not need to build a release ZIP.
- Development data is isolated from production.
- Meilisearch starts or connects through a safe documented mechanism.
- Debug startup uses valid Meilisearch credentials.
- SQLite initializes correctly.
- Development administrator provisioning works.
- Native LlamaPure libraries load in Debug builds.
- The Granite model is resolved correctly when enabled.
- Keyword search works.
- Hybrid search works when enabled.
- Background services operate correctly.
- Debugger shutdown does not leave unintended processes.
- A CLI development startup path exists.
- Developer documentation reflects actual behavior.
- README clearly distinguishes developer and end-user workflows.
- Existing Windows release behavior is preserved.

Do not claim these criteria are satisfied unless they have actually been verified.

## 22. Deliverables

Provide:

1. Working Visual Studio launch profile.
2. Working VS Code debug configuration.
3. Development-specific application configuration.
4. Isolated development data directory.
5. Safe development secret initialization.
6. Development Meilisearch startup integration.
7. Native library Debug configuration.
8. Development administrator provisioning.
9. Development setup script where useful.
10. CLI development startup script.
11. Developer documentation.
12. README development section.
13. Automated development startup tests.
14. Production regression verification.

## 23. Required Completion Report

At the end of implementation, report:

### A. Root causes

Explain why IDE debugging did not previously work.

### B. Changes implemented

Describe fixes to configuration, lifecycle, native dependencies, and debugging profiles.

### C. Files changed

List new and modified files.

### D. Developer startup procedure

Provide exact working commands.

### E. Visual Studio debugging

Describe the actual F5 procedure and verification result.

### F. VS Code debugging

Describe the actual debugging procedure and verification result.

### G. Service initialization

Explain how Meilisearch, SQLite, and embedding inference are initialized in Development mode.

### H. Security and isolation

Confirm how development data and secrets are isolated from production.

### I. Test results

List commands executed and distinguish passed, failed, skipped, and unexecuted tests.

### J. Known limitations

Identify any remaining IDE debugging problems.

Do not claim a successful F5 workflow unless actually verified.

---

## Final Instructions

Inspect the existing Lantern repository and identify why the application cannot currently be launched and debugged directly from an IDE.

Implement the minimum necessary development infrastructure to make Visual Studio and VS Code debugging reliable.

Use the existing ASP.NET Core configuration and application lifecycle infrastructure.

Keep Development and Production environments clearly separated.

Provide isolated development SQLite, Meilisearch, configuration, and secret storage.

Reuse existing Meilisearch startup and embedding infrastructure.

Preserve the validated LlamaPure and Granite model behavior.

Ensure native dependencies load correctly from Debug build outputs.

Provide a working Visual Studio F5 profile and VS Code debug configuration.

Make the development startup workflow independent of release packaging.

Document exact development prerequisites, setup commands, launch profiles, debugging procedures, and troubleshooting steps.

Keep the release Quick Start separate from developer instructions.

**The primary success condition is that a developer can clone Lantern, complete a one-time development setup, open the solution in Visual Studio or VS Code, press F5, hit breakpoints, and debug the running application with SQLite, Meilisearch, and optional local hybrid search functioning correctly—without creating a release ZIP or modifying production data.**
