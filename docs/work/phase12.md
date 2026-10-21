# Lantern — Phase 12 Implementation Instructions
## Windows x64 Packaging, Installation, Upgrade, and Release

You are implementing **Phase 12: Windows Packaging and Release** of the Lantern project.

This is the final implementation phase of the current Lantern roadmap.

Lantern is a lightweight, self-hosted full-text search platform intended primarily for Windows and intranet environments.

The application uses:

- .NET 10 and ASP.NET Core.
- Blazor Web App with Interactive Server.
- Meilisearch Community Edition.
- SQLite with Entity Framework Core.
- LlamaPure and native llama.cpp dependencies.
- IBM Granite Embedding 97M Multilingual R2.
- Local REST-based embedding and hybrid search.
- Document extraction and structure-aware chunking.
- Incremental file and web crawlers.
- Persistent crawler jobs and scheduling.
- Administrator-managed search categories.
- Document-oriented keyword and hybrid search.

Phases 0–11 have established application functionality, security, reliability, recovery procedures, and continuous integration.

**Your task is to transform the existing Lantern application into a complete, reproducible, self-contained Windows x64 distribution that can be installed, initialized, operated, upgraded, recovered, and uninstalled safely.**

The primary distribution method must be a ZIP archive containing the required application binaries, native dependencies, Meilisearch Community Edition, and the local embedding model.

Support both interactive console operation and Windows Service operation.

Do not introduce Docker, Java, Node.js, or cloud dependencies.

---

## 1. Review the Existing Implementation

Before modifying the repository:

1. Read `roadmap.md` and existing architecture documentation.
2. Inspect the .NET solution and application entry point.
3. Review Phase 11 build, test, and release qualification workflows.
4. Review current self-contained Windows publishing behavior.
5. Review LlamaPure and llama.cpp native dependency loading.
6. Review the Granite GGUF model loading configuration.
7. Review Meilisearch Community Edition binary requirements.
8. Review Meilisearch configuration and API key management.
9. Review SQLite database initialization and migrations.
10. Review administrator first-run provisioning.
11. Review crawler job startup and recovery.
12. Review existing application health and readiness endpoints.
13. Review Phase 10 backup and restoration procedures.
14. Review application data directories and filesystem permissions.
15. Inspect existing release documentation.
16. Execute the existing build and relevant tests.

The repository is the source of truth.

Do not assume that a planned packaging feature already exists.

Reuse established infrastructure wherever possible.

Avoid unrelated application changes.

## 2. Phase Objectives

Implement:

1. Reproducible Windows x64 self-contained publishing.
2. Complete ZIP distribution.
3. Bundled Meilisearch Community Edition.
4. Bundled native LlamaPure dependencies.
5. Bundled Granite GGUF embedding model.
6. Deterministic application directory layout.
7. First-run initialization.
8. Secure administrator provisioning.
9. Meilisearch process lifecycle management.
10. Local embedding service initialization.
11. Console execution mode.
12. Windows Service execution mode.
13. Windows Service installation and removal scripts.
14. Persistent application data separation.
15. Safe application upgrades.
16. Backup and rollback procedures.
17. Clean uninstallation.
18. Offline deployment verification.
19. Fresh Windows installation tests.
20. Release artifact generation.
21. Release qualification and documentation.

Prefer a maintainable ZIP-based distribution over a complex installer.

Do not introduce MSI, MSIX, WiX, or a GUI setup wizard unless an existing project requirement explicitly demands one.

## 3. Distribution Principles

The release must follow these principles:

- Download once, operate offline.
- No Docker.
- No Java runtime.
- No Node.js runtime.
- No separately installed .NET runtime.
- No independently installed Meilisearch.
- No cloud embedding API.
- No mandatory online activation.
- No administrator privileges for ordinary application execution.
- No changes to original crawler source documents.
- Persistent data must survive application upgrades.
- All native dependencies must be accounted for.
- Failed initialization must produce actionable diagnostics.
- Destructive operations must require explicit confirmation.

A clean installation should require only Windows, an appropriate x64 environment, and administrator access when installing a Windows Service or configuring protected system resources.

## 4. Windows Platform Target

Target:

```text
Runtime Identifier: win-x64
Target Framework: net10.0
Deployment Mode: Self-contained
```

Use the current project target framework if it already includes justified Windows-specific requirements.

### Requirements

- Build for Windows x64.
- Use Release configuration.
- Include the required .NET runtime.
- Include required ASP.NET Core runtime components.
- Include required native libraries.
- Include static web assets.
- Preserve supported globalization behavior.
- Preserve Japanese and English Unicode processing.

### Publish options

Start with a conventional self-contained publish.

Do not enable trimming by default.

Do not enable Native AOT.

Do not enable single-file publishing automatically.

These options can break reflection-heavy libraries, native dependency loading, model discovery, or dynamic application features.

Introduce them only when compatibility has been verified.

### Example

```powershell
dotnet publish `
  src/Lantern.Web/Lantern.Web.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output artifacts/publish/win-x64
```

The project path is illustrative.

Adapt it to the repository.

## 5. Release Directory Layout

Create a predictable distribution structure.

Recommended layout:

```text
Lantern/
│
├── app/
│   ├── Lantern.exe
│   ├── *.dll
│   ├── native/
│   └── wwwroot/
│
├── bin/
│   └── meilisearch/
│       └── meilisearch.exe
│
├── models/
│   └── granite-embedding-97M-multilingual-r2.gguf
│
├── config/
│   └── appsettings.example.json
│
├── scripts/
│   ├── Initialize-Lantern.ps1
│   ├── Install-LanternService.ps1
│   ├── Uninstall-LanternService.ps1
│   ├── Start-Lantern.ps1
│   ├── Stop-Lantern.ps1
│   └── Backup-Lantern.ps1
│
├── docs/
│   ├── README.md
│   ├── INSTALL.md
│   ├── CONFIGURATION.md
│   ├── UPGRADE.md
│   ├── BACKUP-RESTORE.md
│   └── THIRD-PARTY-NOTICES.md
│
├── VERSION
└── manifest.json
```

This structure is illustrative.

Adapt it to actual runtime requirements.

### Important separation

Application installation files and persistent mutable data must be separated.

Do not place the active SQLite database inside a versioned application binary directory.

Do not place active Meilisearch database files inside the Meilisearch executable directory.

Do not make upgrades depend on preserving modified files inside the published application directory.

## 6. Persistent Data Layout

Use a configurable application data root.

For Windows Service deployments, a suitable default is:

```text
%ProgramData%\Lantern\
```

Suggested structure:

```text
%ProgramData%\Lantern\
│
├── config/
│   └── appsettings.json
│
├── database/
│   └── lantern.db
│
├── meilisearch/
│   └── data.ms/
│
├── logs/
│
├── backups/
│
├── runtime/
│
└── secrets/
```

These paths are illustrative.

### Requirements

- Resolve the data directory independently of the current working directory.
- Support explicit configuration of the data root.
- Create required directories during initialization.
- Apply appropriate filesystem permissions.
- Prevent ordinary users from reading sensitive configuration.
- Preserve user data during application upgrades.
- Preserve data during normal Windows Service reinstallations.

### Model location

The model may remain in the versioned installation tree if it is treated as a release asset.

Do not copy large GGUF files into multiple locations unnecessarily.

Provide an explicit model path setting.

Do not place mutable application state inside the model directory.

## 7. Release Manifest

Generate a machine-readable release manifest.

Example:

```json
{
  "product": "Lantern",
  "version": "1.0.0",
  "runtime": "win-x64",
  "deployment": "self-contained",
  "components": {
    "dotnet": "10",
    "meilisearch": "<pinned-version>",
    "llamapure": "<pinned-revision>",
    "embeddingModel": "granite-embedding-97M-multilingual-r2.gguf"
  }
}
```

The values are illustrative.

### Requirements

Record:

- Lantern version.
- Source commit.
- Build timestamp.
- Target runtime.
- Meilisearch version.
- LlamaPure revision.
- Native llama.cpp revision where applicable.
- Embedding model identifier.
- Embedding model checksum.
- Release archive checksum.
- Important compatibility metadata.

Generate values from actual build inputs.

Do not invent version numbers or checksums.

### Artifact verification

Use SHA-256 checksums for release artifacts.

Ensure the manifest accurately reflects packaged components.

## 8. Bundled Meilisearch Community Edition

Include the approved Meilisearch Community Edition Windows x64 executable.

### Requirements

- Use the pinned version validated in Phase 11.
- Download from an approved official source.
- Verify its checksum.
- Preserve applicable licenses.
- Package the native executable.
- Do not require Docker.
- Do not automatically download Meilisearch during first launch.
- Do not silently replace it with another distribution or edition.

### Compatibility

Verify that the bundled version supports the existing:

- Document index.
- Chunk index.
- Filterable attributes.
- REST Embedder integration.
- Hybrid search.
- Asynchronous indexing tasks.
- Category filters.

Do not automatically upgrade Meilisearch independently of Lantern.

### Data compatibility

A Meilisearch binary upgrade may require an explicit migration procedure.

Do not assume its internal database files are compatible across all versions.

Inspect the documented compatibility and migration procedure for the selected source and target versions.

Never start a newer Meilisearch binary against existing production data without a verified compatibility or migration path.

## 9. Meilisearch Process Management

Lantern must manage the bundled Meilisearch process.

### Required behavior

1. Resolve the bundled executable path.
2. Resolve the persistent Meilisearch data directory.
3. Load protected configuration.
4. Verify network binding.
5. Start Meilisearch when appropriate.
6. Wait for readiness.
7. Detect startup failures.
8. Detect unexpected process termination.
9. Report operational status.
10. Stop the owned process during controlled shutdown.

### Process ownership

Only terminate Meilisearch processes started and owned by the current Lantern instance.

Do not kill arbitrary processes by executable name.

Do not terminate an unrelated Meilisearch instance that happens to be using a configured port.

### Existing process detection

If the required port is occupied:

- Determine whether the listener is an approved managed instance.
- Otherwise fail safely with actionable diagnostics.
- Do not silently connect to an arbitrary local Meilisearch server.

### Startup

Use bounded readiness checks.

Do not assume the process is ready immediately after launch.

Distinguish process startup from API readiness.

### Failure handling

If Meilisearch fails to start:

- Record the failure.
- Preserve its data directory.
- Avoid destructive automatic initialization.
- Do not falsely report healthy search service status.

### Restart behavior

Use a bounded, documented restart policy where appropriate.

Avoid infinite rapid restart loops.

Do not delete indexes as a recovery shortcut.

## 10. Meilisearch Network and Credential Security

Meilisearch is an internal component.

### Requirements

- Bind to loopback by default.
- Keep its API credentials server-side.
- Generate strong secrets during initialization.
- Store secrets in a protected location.
- Avoid exposing credentials in command-line arguments where practical.
- Avoid exposing credentials in logs.
- Do not expose Meilisearch directly to intranet clients.

### Important

Loopback binding does not prevent access from other local processes.

Preserve the authentication and secret-protection mechanisms established in Phase 10.

### Browser access

The browser must communicate with Lantern.

Do not require the browser to contact Meilisearch directly.

## 11. Bundled LlamaPure and Native Dependencies

Include all required LlamaPure and llama.cpp native components.

### Requirements

- Verify Windows x64 compatibility.
- Include required native DLLs.
- Preserve the validated LlamaPure implementation.
- Preserve embedding output compatibility.
- Verify native library load paths.
- Verify CPU execution.
- Include required runtime dependencies or document truly unavoidable Windows prerequisites.

Do not substitute a different LlamaPure version automatically.

Do not rebuild the inference backend during first launch.

### GPU dependencies

Do not require CUDA.

Do not require Vulkan.

Do not require a discrete GPU.

CPU-only inference is the required baseline.

Optional GPU support must not complicate the basic distribution.

### Test

Run actual native inference from the packaged application.

A successful `dotnet publish` is insufficient.

## 12. Bundled Granite Embedding Model

Include:

```text
granite-embedding-97M-multilingual-r2.gguf
```

Use the validated model artifact from the existing project.

### Requirements

- Include the correct GGUF file.
- Verify the file checksum.
- Verify expected embedding dimensions.
- Verify model load success.
- Verify Japanese input.
- Verify English input.
- Verify mixed-language input.
- Preserve the validated inference configuration.

### Model security

Treat the model as a packaged trusted asset.

Do not accept arbitrary model paths through unauthenticated endpoints.

Do not download a model automatically during application startup.

### Missing model

If the model is missing or corrupted:

- Report a clear error.
- Prevent incorrect hybrid search readiness.
- Preserve keyword search where possible.
- Do not silently use another embedding model.

### License

Include the relevant model license and notices.

Do not assume the model license is identical to the llama.cpp or LlamaPure license.

## 13. Embedding Server Lifecycle

The local embedding REST server is part of Lantern's process.

### Requirements

- Start its listener on an approved loopback address.
- Preserve the existing bounded inference queue.
- Initialize the native inference worker safely.
- Report health and readiness separately.
- Handle model load failure.
- Handle application shutdown.
- Dispose native resources appropriately.
- Avoid exposing the embedding endpoint to the network.

### Suggested startup dependency

```text
Lantern Startup
      |
      +-- Configuration Validation
      |
      +-- SQLite Initialization
      |
      +-- Meilisearch Startup
      |
      +-- Embedding Model Initialization
      |
      +-- Search Index Validation
      |
      +-- Crawler Job Recovery
      |
      +-- Application Ready
```

The exact order may differ based on existing architecture.

Do not impose artificial ordering where services can initialize safely in parallel.

### Readiness

Distinguish:

- Web application reachable.
- SQLite ready.
- Meilisearch ready.
- Embedding server ready.
- Keyword search ready.
- Hybrid search ready.

Do not mark Hybrid Search ready merely because the HTTP listener is running.

## 14. Console Execution Mode

Support interactive execution without Windows Service installation.

Example:

```powershell
.\scripts\Start-Lantern.ps1
```

### Requirements

- Start the Lantern application.
- Start or connect to the approved bundled Meilisearch process.
- Load persistent configuration.
- Initialize local embedding.
- Display the application URL.
- Show actionable startup errors.
- Support Ctrl+C.
- Shut down managed components safely.

### Console operation

The application must not require an active browser session.

Background crawler jobs must remain operational while the application runs.

### Working directory

Do not assume the current directory is the application installation directory.

Resolve paths from explicit configuration and the application base directory.

## 15. Windows Service Support

Support running Lantern as a Windows Service.

Use standard .NET Windows Service integration.

Prefer:

```text
Microsoft.Extensions.Hosting.WindowsServices
```

or the established equivalent.

### Requirements

- Support the Windows Service Control Manager.
- Start automatically when configured.
- Handle stop requests.
- Handle system shutdown.
- Preserve background jobs.
- Preserve application logs.
- Use a configured service identity.
- Avoid interactive desktop dependencies.

### Service behavior

The same application functionality must work in console and service modes.

Do not create two separate Lantern backends.

### Working directory

Windows Services may start with a system working directory.

Do not rely on relative paths.

### Service readiness

The service may be considered started before all optional subsystems are ready.

Expose accurate readiness diagnostics.

Avoid pretending that a failed Meilisearch or embedding initialization succeeded.

## 16. Service Installation Scripts

Provide a documented PowerShell installation script.

Suggested name:

```text
Install-LanternService.ps1
```

### Requirements

- Validate administrator privileges where required.
- Validate the installation directory.
- Validate required files.
- Validate configured data paths.
- Validate service name availability.
- Configure the Windows Service.
- Set the executable path safely.
- Configure startup behavior.
- Apply appropriate filesystem permissions.
- Report installation success or failure.

### Safety

Use correct PowerShell argument handling.

Avoid command injection through paths or arguments.

Handle paths containing spaces.

Do not embed secrets directly into service command-line arguments.

### Idempotency

Repeated installation attempts should not create duplicate services.

If the service already exists, report the existing configuration and require an explicit supported update operation.

Do not silently overwrite unrelated services.

## 17. Service Removal Scripts

Provide:

```text
Uninstall-LanternService.ps1
```

### Requirements

- Verify the intended service identity.
- Stop the service safely.
- Remove its registration.
- Preserve persistent application data by default.
- Preserve backups.
- Avoid deleting unrelated files.
- Report success or failure.

### Important distinction

Removing the Windows Service is not equivalent to deleting Lantern data.

Do not automatically delete:

- SQLite.
- Meilisearch data.
- Crawler configurations.
- Search categories.
- Job history.
- Backups.
- Protected secrets.

Provide separate, explicit instructions for complete data removal.

## 18. First-Run Initialization

Implement a safe first-run initialization workflow.

### Required operations

1. Detect whether Lantern has already been initialized.
2. Validate installation files.
3. Determine the persistent data directory.
4. Create required data directories.
5. Apply filesystem permissions.
6. Generate required secrets.
7. Initialize SQLite.
8. Apply supported EF Core migrations.
9. Prepare Meilisearch persistent storage.
10. Validate the embedding model.
11. Provision the initial administrator.
12. Save initialization state.
13. Start the application.

### Existing installation detection

Do not recreate the administrator or reset secrets when existing state is detected.

Do not overwrite existing configuration without explicit approval.

### Partial initialization

If initialization fails halfway through:

- Preserve diagnostics.
- Avoid destructive cleanup.
- Allow safe retry.
- Avoid generating inconsistent replacement secrets.
- Do not claim initialization succeeded.

### Idempotency

Running initialization again should not corrupt an existing installation.

## 19. Initial Administrator Provisioning

Provide a secure initial administrator setup process.

### Requirements

- Do not ship a default administrator password.
- Do not use hardcoded credentials.
- Do not expose credentials in logs.
- Do not include administrator credentials in the release ZIP.
- Require deliberate initial provisioning.
- Store credentials using established password hashing.
- Prevent repeated unauthorized administrator creation.

### Recommended approach

Use a local interactive PowerShell setup or equivalent one-time provisioning mechanism.

Ensure the workflow operates under an appropriately authorized local identity.

### Remote access

Do not expose an unauthenticated administrator creation endpoint to the intranet.

If browser-based first-run setup is supported, it must be secured against remote takeover.

### Configuration recovery

Document how administrators can recover access using a controlled local maintenance process.

Do not create a permanent unauthenticated recovery endpoint.

## 20. Network Configuration

Support explicit configuration of the Lantern web endpoint.

### Defaults

Prefer loopback binding for initial setup.

An intranet-accessible binding must require explicit configuration.

### Requirements

- Validate addresses and ports.
- Detect port conflicts.
- Avoid exposing Meilisearch.
- Avoid exposing the embedding server.
- Support HTTPS configuration where appropriate.
- Document certificates and reverse-proxy operation.
- Preserve authentication cookie security.

### Important security rule

Self-contained packaging does not make HTTP connections secure.

For remote intranet administration, document and support a secure HTTPS deployment.

Do not silently downgrade cookie or transport security.

### Firewall

Do not automatically create broad firewall rules.

If firewall configuration is supported, require an explicit administrator action and narrowly scoped rules.

## 21. Service Account and Filesystem Permissions

Support least-privilege execution.

### Required access

The service identity may need:

- Read access to Lantern binaries.
- Read access to model files.
- Read/write access to application data.
- Read/write access to Meilisearch data.
- Read/write access to logs.
- Read access to configured file crawler roots.
- Network access to configured web crawler targets.

### UNC paths

Access to network shares depends on the service identity.

Document that `LocalSystem` is not equivalent to the interactive user's credentials.

Do not assume UNC shares work merely because the administrator can browse them interactively.

### Permissions

Do not grant broad write permissions to every local user.

Do not require the service to run as a full local administrator without a documented necessity.

## 22. Persistent Configuration

Separate default release configuration from active installation configuration.

### Requirements

- Ship documented configuration templates.
- Create active configuration during initialization.
- Preserve active settings during upgrades.
- Apply supported configuration migrations.
- Validate settings at startup.
- Avoid overwriting administrator changes.
- Keep secrets out of public template files.

### Versioned settings

If configuration formats change:

- Detect the existing format version.
- Apply supported transformations.
- Preserve a backup before migration.
- Reject unsupported transitions.
- Report actionable errors.

Do not silently discard unknown critical settings.

## 23. Application Upgrade Strategy

Implement a safe upgrade workflow.

### Recommended procedure

```text
Existing Lantern
      |
      v
Verify Target Release
      |
      v
Check Compatibility
      |
      v
Stop Lantern Service
      |
      v
Back Up Persistent Data
      |
      v
Install New Application Files
      |
      v
Apply Supported Migrations
      |
      v
Start Updated Lantern
      |
      v
Verify Health and Search
```

### Requirements

- Preserve SQLite data.
- Preserve crawler definitions.
- Preserve schedules.
- Preserve job history.
- Preserve category assignments.
- Preserve Meilisearch indexes.
- Preserve secrets.
- Preserve backups.
- Preserve model configuration.

### Release versioning

Use explicit release versions.

Detect installed and target versions.

Reject unsupported downgrade paths.

Do not silently install an older application over a newer database schema.

## 24. Meilisearch Upgrade and Migration

Treat Meilisearch upgrades as a separate compatibility concern.

### Requirements

- Record source and target Meilisearch versions.
- Check official compatibility requirements.
- Determine whether a migration is required.
- Preserve the existing index data before migration.
- Use supported snapshot, dump, or migration procedures.
- Validate the migrated index.
- Verify keyword and hybrid search.
- Preserve category filter settings.
- Preserve document and chunk identities.

### Safety

Do not copy an incompatible Meilisearch data directory into a new version and assume it works.

Do not automatically delete indexes when startup fails.

Do not silently rebuild the entire corpus.

### Recovery

If migration fails, preserve the original data and the corresponding compatible Meilisearch version.

Document how to return to the original application version safely.

## 25. SQLite Migration During Upgrade

Use established EF Core migrations.

### Requirements

- Detect current schema version.
- Verify compatibility.
- Back up the database.
- Apply supported migrations.
- Report migration failures.
- Preserve job history.
- Preserve category relationships.
- Preserve crawler state.

### Failure handling

If migration fails:

- Stop the upgrade.
- Preserve original data.
- Preserve diagnostic logs.
- Do not continue with partially initialized services.
- Provide a documented recovery path.

Do not delete or recreate the SQLite database automatically.

## 26. Rollback Strategy

Implement a documented rollback procedure.

### Requirements

- Keep the previous application version available until the upgrade is verified.
- Preserve a pre-upgrade backup.
- Verify rollback compatibility.
- Restore matching SQLite state when necessary.
- Restore matching Meilisearch data when necessary.
- Restore matching configuration and secrets.
- Verify service operation after rollback.

### Important distinction

Binary rollback and data rollback are not equivalent.

An older application may not understand a newer SQLite schema.

An older Meilisearch executable may not understand newer index storage.

Do not claim rollback is safe without verifying the complete state combination.

### Operational rule

Never perform an automatic destructive rollback based solely on a failed health check.

Prefer an explicit controlled recovery operation with clear diagnostics.

## 27. Backup and Restoration Integration

Reuse Phase 10 backup and restoration mechanisms.

### Requirements

- Expose documented backup commands or scripts.
- Include SQLite data.
- Include Meilisearch state.
- Include configuration.
- Include required secrets and Data Protection keys.
- Include compatibility metadata.
- Exclude unnecessary temporary files.

### Restore validation

Perform isolated restoration tests.

Verify:

- Administrator authentication.
- Crawler configurations.
- Schedules.
- Categories.
- Job history.
- Keyword search.
- Hybrid search.
- Source metadata.
- Document and chunk identity consistency.

### Offline recovery

Restoration must not depend on external cloud services.

Do not require redownloading Meilisearch or the model if those assets are included in the release.

## 28. Clean Uninstallation

Document two different operations.

### Remove application

- Stop the service.
- Remove service registration.
- Remove installed application binaries.
- Preserve persistent data.
- Preserve backups.

### Remove all Lantern data

This must be a separate explicit operation.

Require clear confirmation.

Warn that deleting persistent data may remove:

- Search indexes.
- Crawler configurations.
- Categories.
- Scheduled jobs.
- Execution history.
- Administrator state.

Do not combine ordinary uninstall and irreversible data deletion into one implicit action.

### Safety

Deletion scripts must validate paths carefully.

Never recursively delete a user-provided directory without confirming that it is the intended Lantern-managed data directory.

## 29. Offline Operation Verification

This is a mandatory release test.

### Test environment

Use a clean Windows x64 virtual machine or equivalent isolated environment.

Disconnect external network access after transferring the release archive.

### Verify

1. Extract the ZIP.
2. Initialize Lantern.
3. Create administrator credentials.
4. Start the application.
5. Start bundled Meilisearch.
6. Load the Granite model.
7. Open the search UI.
8. Access the administration UI.
9. Create a local file crawler.
10. Index a test document.
11. Perform keyword search.
12. Perform hybrid search.
13. Verify local document metadata.
14. Restart Lantern.
15. Verify persistent state.

### Constraints

The system must not require:

- Docker installation.
- Java installation.
- Node.js installation.
- .NET SDK installation.
- .NET runtime installation.
- External embedding API calls.
- Internet-based model downloads.
- Remote license activation.

### Web crawling

Web crawling naturally requires connectivity to its target websites.

Offline operation means that Lantern itself does not require internet access.

An offline environment may still crawl reachable local intranet HTTP services.

Do not claim that disconnected public websites remain crawlable.

## 30. Clean Installation Testing

Test on a Windows environment that has not previously installed Lantern.

### Verify

- No dependency on developer-machine paths.
- No dependency on globally installed Meilisearch.
- No dependency on globally installed .NET.
- No dependency on Visual Studio.
- No dependency on rustup.
- No dependency on Python.
- No dependency on an external embedding service.
- Native libraries load successfully.
- First-run provisioning works.
- Directories are created correctly.
- Application startup succeeds.

### Supported operating systems

Determine and document the actual supported Windows versions based on native library requirements.

Do not claim compatibility with every Windows release without testing.

Verify CPU architecture and instruction-set compatibility for the bundled native binaries.

## 31. Windows Service Testing

Test actual service installation and operation.

### Scenarios

1. Install service.
2. Start service.
3. Verify HTTP availability.
4. Verify Meilisearch startup.
5. Verify embedding model readiness.
6. Create a crawler.
7. Schedule a crawl.
8. Close the browser.
9. Verify the background job still runs.
10. Stop the service.
11. Verify controlled shutdown.
12. Restart the service.
13. Verify recovery.
14. Uninstall service.
15. Verify data preservation.

### Network shares

Where practical, test the service under an identity with access to a controlled UNC share.

Do not assume the interactive console tests validate service-account network permissions.

## 32. Upgrade and Rollback Testing

Use a representative existing installation.

### Upgrade test

1. Install a previous compatible Lantern build.
2. Create crawler configurations.
3. Index sample files.
4. Index sample web pages.
5. Configure categories.
6. Create job history.
7. Back up the installation.
8. Upgrade to the target build.
9. Run supported migrations.
10. Verify data preservation.
11. Verify search results.
12. Verify crawler scheduling.
13. Verify hybrid search.

### Rollback test

When rollback is supported:

1. Create a pre-upgrade backup.
2. Upgrade.
3. Simulate a controlled upgrade failure.
4. Restore the original compatible binaries and data.
5. Verify the original system operates.

Do not use production data for destructive upgrade tests.

## 33. Native Dependency Validation

Test the actual contents of the release ZIP.

### Required checks

- Lantern executable exists.
- All required managed assemblies exist.
- Native LlamaPure dependencies exist.
- Bundled Meilisearch executable exists.
- GGUF model exists.
- Model checksum matches.
- Static web assets exist.
- Configuration templates exist.
- Installation scripts exist.
- Third-party notices exist.

### Important

Do not test only the build directory.

Test the extracted release archive.

The goal is to catch files accidentally omitted during packaging.

### Runtime verification

Verify native dependencies from the installed release directory.

Do not rely on global `PATH` entries from the development machine.

## 34. Release Packaging Script

Create a reproducible packaging script.

Suggested name:

```text
scripts/Build-Release.ps1
```

### Responsibilities

1. Validate repository state and version.
2. Invoke the approved Release publish.
3. Validate required build outputs.
4. Copy approved native dependencies.
5. Copy the pinned Meilisearch executable.
6. Copy the validated GGUF model.
7. Copy configuration templates.
8. Copy administration scripts.
9. Copy documentation and licenses.
10. Generate the release manifest.
11. Calculate checksums.
12. Create the ZIP archive.
13. Verify archive contents.
14. Report the output location.

### Requirements

- Fail on missing required files.
- Fail on incorrect checksums.
- Fail on unsupported component combinations.
- Avoid downloading unverified binaries.
- Avoid including development secrets.
- Avoid including test databases.
- Avoid including temporary build files.
- Avoid including production configuration.

### Repeatability

Building the same source and dependency set should produce equivalent release contents.

If byte-for-byte archive reproducibility is not achieved, document the remaining variable inputs such as timestamps.

Do not falsely claim deterministic builds.

## 35. GitHub Actions Release Integration

Extend Phase 11 CI with release packaging.

### Recommended workflow

```text
Release Tag
     |
     v
Verify Source Revision
     |
     v
Required Build and Tests
     |
     v
Release Qualification
     |
     v
Windows Publish
     |
     v
Package Dependencies
     |
     v
Generate ZIP and Checksums
     |
     v
Validate Artifact
     |
     v
Release Candidate
```

### Requirements

- Use the validated source commit.
- Preserve build provenance.
- Verify native dependencies.
- Verify the GGUF checksum.
- Include license notices.
- Verify the ZIP contents.
- Avoid uploading secrets.
- Retain release qualification results.

### Publication

If repository access and release permissions are available, support a documented GitHub Releases publishing workflow.

Do not claim a public release has been published unless an actual release was created and verified.

Do not bypass mandatory qualification checks merely because a release tag exists.

### Signing

If a trusted Windows code-signing certificate is available, integrate signing using secure key handling.

Do not invent a certificate.

Do not embed private signing keys in the repository.

Unsigned releases may remain supported initially, but document Windows SmartScreen and trust-related user experience limitations.

## 36. Versioning and Compatibility

Introduce or validate consistent application versioning.

### Requirements

- Use one authoritative Lantern version.
- Include the version in the release archive name.
- Include the version in the release manifest.
- Include the version in application diagnostics.
- Preserve source commit identity.
- Record compatibility with the bundled Meilisearch version.
- Record relevant database schema compatibility.

### Example archive

```text
Lantern-1.0.0-win-x64.zip
```

The version is illustrative.

Use the actual release version.

### Compatibility documentation

Document:

- Supported Windows versions.
- Supported CPU architecture.
- Meilisearch version.
- GGUF model version.
- SQLite migration support.
- Upgrade paths.
- Rollback limitations.

## 37. Health Checks and Operational Diagnostics

Verify that packaged Lantern exposes useful operational status.

### Required status

- Application version.
- SQLite availability.
- Meilisearch process status.
- Meilisearch readiness.
- Embedding server status.
- Model readiness.
- Keyword search availability.
- Hybrid search availability.
- Crawler scheduler status.

### Security

Do not expose detailed internal configuration or secrets through public health endpoints.

Separate public health indicators from administrator-only diagnostics where appropriate.

### Startup failure

Provide actionable diagnostics for:

- Missing Meilisearch binary.
- Missing GGUF model.
- Invalid native DLL.
- Incorrect architecture.
- Invalid data directory permissions.
- Port conflicts.
- SQLite migration failure.
- Meilisearch startup failure.
- Embedding initialization failure.

Do not silently recreate data as a recovery strategy.

## 38. Documentation

Create or update:

```text
README.md
docs/INSTALL.md
docs/CONFIGURATION.md
docs/WINDOWS-SERVICE.md
docs/UPGRADE.md
docs/BACKUP-RESTORE.md
docs/TROUBLESHOOTING.md
docs/THIRD-PARTY-NOTICES.md
```

Adapt filenames to existing conventions.

### Installation documentation

Explain:

- System requirements.
- ZIP extraction.
- Data directory.
- First-run initialization.
- Administrator provisioning.
- Console startup.
- Windows Service installation.
- Accessing the browser interface.
- Stopping Lantern.

### Configuration documentation

Explain:

- HTTP/HTTPS binding.
- Meilisearch ports.
- Embedding model path.
- SQLite location.
- Data directories.
- Service identity.
- UNC permissions.
- Crawler limits.

### Upgrade documentation

Explain:

- Pre-upgrade checks.
- Backups.
- Stopping services.
- Replacing binaries.
- Applying migrations.
- Verifying operation.
- Rollback.

### Recovery documentation

Explain:

- Backup contents.
- Restore procedures.
- SQLite recovery.
- Meilisearch migration.
- Reindexing when truly necessary.
- Interrupted jobs.
- Category synchronization.

### Security documentation

Explain:

- Administrator authentication.
- HTTPS configuration.
- Internal service isolation.
- Filesystem permissions.
- Network restrictions.
- The v1.0 document-access model.

## 39. Out of Scope

Do not implement:

- MSI installers.
- MSIX packaging.
- WiX installers.
- A custom graphical installer.
- Automatic in-application updates.
- Cloud deployment.
- Docker images.
- Linux packaging.
- macOS packaging.
- Kubernetes.
- Distributed search.
- Distributed crawling.
- Active Directory integration.
- Per-document Windows ACL synchronization.
- OCR.
- RAG.
- Chat interfaces.
- New embedding models.
- GPU-specific distributions.
- New crawler types.
- Search engine replacement.

These are deferred enhancements.

Do not add major new product features during release packaging.

## 40. Acceptance Criteria

Phase 12 is complete only when all required conditions are satisfied.

### Windows distribution

- A Windows x64 ZIP is produced.
- Lantern is self-contained.
- All required native libraries are included.
- Meilisearch Community Edition is bundled.
- The validated Granite GGUF model is included.
- No Docker, Java, Node.js, or separately installed .NET runtime is required.
- The archive includes installation instructions and licenses.

### Installation

- A clean Windows installation succeeds.
- First-run initialization succeeds.
- Administrator provisioning is secure.
- SQLite initializes correctly.
- Meilisearch starts successfully.
- The embedding model loads successfully.
- Search and administration pages are accessible.
- No internet connection is required for initial application operation.

### Runtime

- Console mode works.
- Windows Service mode works.
- Service shutdown is handled.
- Meilisearch process ownership is correct.
- Health and readiness reporting is accurate.
- File crawling works.
- Web crawling works with reachable targets.
- Scheduled crawler jobs work.
- Keyword and hybrid search work.

### Persistence

- SQLite remains outside the versioned binary directory.
- Meilisearch data remains persistent.
- Crawler definitions survive restart.
- Categories survive restart.
- Job history survives restart.
- Administrator credentials survive restart.
- Search indexes survive restart.

### Upgrade

- A supported upgrade path exists.
- Application files can be replaced safely.
- SQLite migrations are controlled.
- Meilisearch compatibility is checked.
- Data is backed up before risky changes.
- Search data and configuration remain usable after upgrade.

### Rollback and recovery

- Rollback behavior is documented.
- Compatible rollback scenarios are tested.
- Backup restoration is verified.
- Failed migrations do not cause silent data deletion.
- Interrupted operations remain recoverable.

### Uninstallation

- Windows Service removal works.
- Application removal works.
- Persistent data is preserved by default.
- Complete data deletion requires explicit action.
- Uninstallation cannot accidentally delete unrelated directories.

### Release quality

- Phase 11 release qualification checks pass.
- Release binary checksums are verified.
- Native dependency versions are recorded.
- Model integrity is verified.
- Third-party notices are included.
- The extracted release archive is smoke-tested.
- Offline installation is tested.
- Windows Service operation is tested.
- Upgrade and restoration scenarios are tested.

Do not mark Phase 12 complete if mandatory release qualification or clean installation tests have not actually been executed.

## 41. Deliverables

Provide:

1. Windows x64 self-contained application publish.
2. Complete release directory structure.
3. Bundled Meilisearch Community Edition.
4. Bundled LlamaPure native dependencies.
5. Bundled Granite GGUF model.
6. Release manifest.
7. SHA-256 checksums.
8. Reproducible release packaging script.
9. First-run initialization.
10. Secure administrator provisioning.
11. Meilisearch process management.
12. Console startup script.
13. Windows Service integration.
14. Windows Service installation script.
15. Windows Service removal script.
16. Persistent data directory configuration.
17. Secure secret management.
18. Backup integration.
19. Upgrade procedure.
20. Rollback procedure.
21. Safe uninstallation procedure.
22. Offline installation tests.
23. Clean Windows smoke tests.
24. Windows Service tests.
25. Upgrade and recovery tests.
26. GitHub Actions release integration.
27. Third-party license notices.
28. Installation and operations documentation.

## 42. Required Completion Report

At the end of implementation, provide the following.

### A. Implementation summary

Describe the Windows distribution and release capabilities completed.

### B. Files changed

List important files created or modified.

### C. Release artifacts

Report:

- Archive filename.
- Archive size.
- SHA-256 checksum.
- Lantern version.
- Source commit.
- Meilisearch version.
- LlamaPure revision.
- Model checksum.

Report only actual values.

### D. Packaging architecture

Explain:

- Directory layout.
- Persistent data location.
- Native dependency loading.
- Meilisearch lifecycle.
- Embedding lifecycle.
- Service integration.
- Initialization.
- Configuration handling.

### E. Installation validation

Report actual clean-installation testing.

Identify the Windows version and environment used.

### F. Runtime validation

Report:

- Console mode.
- Service mode.
- SQLite readiness.
- Meilisearch readiness.
- Model loading.
- Keyword search.
- Hybrid search.
- File crawling.
- Web crawling.
- Scheduling.

### G. Upgrade and rollback verification

Describe tested scenarios and observed outcomes.

### H. Release qualification

Report actual CI and qualification results.

Do not claim a workflow passed without a verified run.

### I. Security and license review

Report:

- Remaining security findings.
- Native dependency provenance.
- Model license compliance.
- Third-party notices.
- Signing status.

### J. Known limitations

Document:

- Supported Windows versions.
- CPU compatibility.
- Unsigned binary warnings where applicable.
- Windows Service account requirements.
- UNC permissions.
- HTTPS deployment requirements.
- Meilisearch migration limitations.
- Rollback limitations.
- Offline model size and distribution considerations.

### K. Remaining work

Identify incomplete acceptance criteria and their impact on the release.

Do not claim production readiness when essential installation or recovery tests remain unverified.

---

## Final Instructions

Implement Phase 12 directly in the existing Lantern repository.

Treat this as the final packaging and release phase of the current roadmap.

Inspect and preserve all functionality from Phases 0–11.

Publish Lantern as a self-contained Windows x64 application.

Bundle the approved native Meilisearch Community Edition executable, validated LlamaPure libraries, and Granite Embedding 97M GGUF model.

Use a straightforward ZIP-based distribution.

Do not require Docker, Java, Node.js, externally installed .NET runtimes, or cloud inference services.

Implement secure first-run initialization, persistent data directories, administrator provisioning, and managed Meilisearch startup.

Support both console execution and Windows Service operation.

Protect SQLite, Meilisearch indexes, crawler definitions, categories, job history, and secrets during restart, upgrade, rollback, and uninstallation.

Use the established backup and restoration procedures.

Verify that the distribution works on a clean Windows x64 environment without developer-machine dependencies.

Verify actual keyword and hybrid search using the packaged native components and model.

Integrate release artifact generation with the Phase 11 quality assurance workflows.

Generate versioned ZIP archives, manifests, checksums, and third-party notices.

Do not claim a successful release without executing the mandatory release qualification and clean installation tests.

**The primary success condition is a complete Windows x64 Lantern distribution that an administrator can extract, initialize, run, install as a Windows Service, operate offline, upgrade safely, recover from failures, and uninstall without losing persistent data—while retaining fully functional file crawling, web crawling, category-based search, keyword retrieval, and local Granite-based hybrid search.**

Do not introduce additional product features beyond the Phase 12 release scope.
