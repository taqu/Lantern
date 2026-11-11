# Lantern — Cross-Platform Setup, Secret Management, and Linux Support

## Objective

Extend Lantern from a Windows-first application into a self-hosted search platform that supports both Windows x64 and Linux x64.

The existing Windows implementation must remain fully functional.

A Linux development and testing environment is available. Therefore, Linux functionality must be implemented and verified on an actual Linux system, not merely inferred from cross-platform .NET compatibility.

The project currently uses:

- .NET 10 and ASP.NET Core.
- Blazor Web App with Interactive Server.
- SQLite and Entity Framework Core.
- Meilisearch Community Edition.
- LlamaPure and native llama.cpp libraries.
- IBM Granite Embedding 97M Multilingual R2.
- Local REST-based embedding and hybrid search.
- File and web crawlers.
- Persistent crawler jobs and scheduling.
- Administrator authentication.
- Search category management.

The existing setup has platform-specific scripts and configuration handling.

There have also been issues involving Meilisearch authentication, administrator provisioning, process lifecycle management, and differences between development and release startup.

**Your task is to consolidate initialization and credential management into a shared .NET CLI, introduce Linux-native execution and packaging, and provide reliable startup and service integration on both Windows and Linux.**

Do not create two independent implementations of the setup process.

---

# Part 1 — Architecture and Existing Implementation

## 1. Inspect the Repository

Before making changes, investigate:

1. Solution and project structure.
2. `Program.cs` and application startup.
3. Application configuration and dependency injection.
4. Existing setup and startup scripts.
5. `scripts/setup-dev.ps1`.
6. Meilisearch startup scripts.
7. Meilisearch process management.
8. Meilisearch authentication configuration.
9. Administrator provisioning and password hashing.
10. SQLite initialization and migrations.
11. Embedding server initialization.
12. LlamaPure native dependency loading.
13. GGUF model configuration.
14. Windows Service hosting.
15. Release packaging.
16. GitHub Actions workflows.
17. Existing developer and installation documentation.

Identify which components are already platform-independent and which currently assume Windows.

Inspect the actual implementation rather than relying solely on previous roadmap documents.

Preserve existing functionality.

## 2. Target Architecture

The intended structure is:

```text
Windows                           Linux
   |                                |
   +-- setup-lantern.ps1             +-- setup-lantern.sh
   +-- start-lantern.ps1             +-- start-lantern.sh
   +-- Windows Service              +-- systemd
   |                                |
   +---------------+----------------+
                   |
                   v
          Shared Lantern CLI
                 .NET 10
                   |
         +---------+---------+
         |                   |
         v                   v
    Configuration         Credentials
    Initialization        Management
         |                   |
         +---------+---------+
                   |
                   v
          Shared Application Host
                   |
        +----------+----------+
        |          |          |
        v          v          v
      SQLite   Meilisearch  Embedding
                   |          |
                   v          v
              Native Binary  LlamaPure
```

### Design principles

- Share application initialization logic.
- Share authentication provisioning.
- Share secret generation.
- Share SQLite initialization.
- Share Meilisearch configuration.
- Share startup validation.
- Keep operating-system-specific code isolated.
- Avoid separate business logic in PowerShell and Bash.
- Avoid unnecessary external runtime dependencies.

---

# Part 2 — Shared .NET CLI

## 3. Introduce a Shared Lantern CLI

Provide a consistent command-line interface.

Suggested commands:

```bash
Lantern setup
Lantern start
Lantern status
Lantern admin reset-password
Lantern meilisearch rotate-key
```

These are desired interfaces, not assumptions about existing commands.

Inspect the existing project structure before deciding whether to:

- Extend the current Lantern executable with subcommands.
- Introduce a separate `Lantern.Cli` executable.
- Reuse an existing command-line infrastructure.

Prefer the smallest maintainable design.

### Requirements

- Work on Windows and Linux.
- Support interactive and noninteractive execution where appropriate.
- Return meaningful exit codes.
- Produce actionable error messages.
- Avoid printing secrets.
- Avoid platform-specific paths in shared logic.
- Use the existing application services.

Do not implement credential hashing or SQLite schema management independently in shell scripts.

## 4. Command-Line Design

Provide clear command behavior.

### `Lantern setup`

Initialize a new installation or validate an existing installation.

### `Lantern start`

Start the Lantern application using the configured environment.

### `Lantern status`

Report the health of Lantern and its managed dependencies.

### `Lantern admin reset-password`

Reset an administrator password through an explicitly authorized local operation.

### `Lantern meilisearch rotate-key`

Perform controlled rotation of Meilisearch credentials.

### Additional requirements

- Provide `--help`.
- Support configuration path overrides.
- Support data-directory overrides.
- Reject unknown arguments.
- Return nonzero exit codes on failures.
- Do not silently overwrite existing configuration.

Avoid implementing a large command framework unless it materially simplifies the code.

---

# Part 3 — Shared Configuration and Directories

## 5. Cross-Platform Data Directories

Support platform-appropriate default directories.

### Windows

Suggested defaults:

```text
C:\ProgramData\Lantern\
    config\
    database\
    meilisearch\
    secrets\
    logs\
    backups\
```

### Linux

Suggested system-service defaults:

```text
/etc/lantern/
    appsettings.json

/etc/lantern/secrets/
    meili-master-key.txt

/var/lib/lantern/
    database/
    meilisearch/
    backups/

/var/log/lantern/
```

These are suggested defaults.

Adapt them to the actual installation and service model.

### Requirements

- Support explicit directory overrides.
- Resolve paths independently of the current working directory.
- Preserve existing Windows installations.
- Support development-specific data directories.
- Never silently use production data in development.
- Create only directories required by the selected configuration.

### Rootless Linux execution

Support a user-writable development or console configuration.

For example:

```text
~/.local/share/lantern/
~/.config/lantern/
```

Do not require root privileges for ordinary development execution.

System installation under `/etc` and `/var/lib` may require administrative setup.

## 6. Configuration Loading

Use standard .NET configuration facilities.

Support:

- Application configuration files.
- Environment-specific configuration.
- Environment variables.
- Explicit command-line overrides.
- Protected secret files.

Define a predictable precedence.

### Requirements

- Use the same configuration model on both platforms.
- Validate security-critical settings.
- Avoid embedding absolute platform-specific paths.
- Preserve compatibility with existing Windows configuration.
- Do not silently discard existing settings.
- Do not place plaintext secrets in public configuration templates.

### Migration

If configuration keys or storage locations change:

1. Identify the existing format.
2. Validate the target format.
3. Preserve a backup.
4. Migrate supported settings.
5. Verify the migrated configuration.
6. Reject unsupported migration paths safely.

Do not reset existing Meilisearch credentials during migration.

---

# Part 4 — Cross-Platform Secret Management

## 7. Generate Meilisearch Credentials Securely

Implement shared secret generation in C#.

Use:

```csharp
System.Security.Cryptography.RandomNumberGenerator
```

or an equivalent cryptographically secure .NET API.

### Requirements

- Generate sufficient entropy.
- Use a supported string representation.
- Generate secrets only during explicit initialization or rotation.
- Preserve existing secrets.
- Never use `System.Random` or PowerShell `Get-Random` for security credentials.
- Never log secret values.
- Never include secrets in release artifacts.

### Important distinction

Meilisearch has:

- A master key used to secure the server.
- API keys that must be recognized by Meilisearch.

Do not assume that generating an arbitrary local `meili-api-key.txt` creates a valid Meilisearch API key.

Use the supported Meilisearch API when scoped keys are required.

## 8. Protected Secret Storage

Use persistent secret files protected by operating-system permissions.

### Windows

Use filesystem ACLs.

Restrict access to:

- The intended service identity.
- Authorized installation administrators.
- Other explicitly required principals.

Avoid granting access to all local users.

### Linux

Use restricted ownership and permissions.

Suggested directory permission:

```bash
chmod 700 /etc/lantern/secrets
```

Suggested secret file permission:

```bash
chmod 600 /etc/lantern/secrets/meili-master-key.txt
```

Set appropriate ownership for the Lantern service identity.

### Requirements

- Apply permissions during setup.
- Verify effective permissions where practical.
- Do not assume permissions are correct merely because the file was created.
- Avoid logging secret contents.
- Avoid writing secrets to temporary world-readable files.
- Do not use User or Machine environment variables as the primary permanent secret store.

### Important

The Linux permission examples apply to system installations.

Rootless development setups must use suitable user-owned directories instead.

## 9. Console Display of Keys

Support optional explicit display of a generated Meilisearch master key.

For example:

```bash
Lantern setup --show-master-key
```

### Requirements

- Display only when explicitly requested.
- Display only in an appropriate interactive context.
- Avoid exposing secrets in CI logs.
- Never write displayed secrets to ordinary application logs.
- Do not require the user to copy and re-enter the key during normal startup.

The default setup should generate and securely persist the key without displaying it.

### Initial-key input

Optionally support importing an existing master key through a secure local input mechanism.

Do not require manual entry unless explicitly requested.

Avoid putting secrets in command-line arguments.

## 10. Passing Credentials to Meilisearch

When Lantern starts Meilisearch, provide the required environment variables to the child process.

Prefer:

```csharp
var startInfo = new ProcessStartInfo
{
    FileName = meilisearchExecutablePath,
    UseShellExecute = false
};

startInfo.Environment["MEILI_MASTER_KEY"] = masterKey;
startInfo.Environment["MEILI_HTTP_ADDR"] = listenAddress;
```

Adapt the implementation to the actual process manager.

### Requirements

- Do not write the master key to a global environment variable.
- Do not persist it in Windows User or Machine environment variables.
- Do not place it in systemd `Environment=` directives.
- Avoid command-line secret arguments.
- Do not log the process environment.

### SDK authentication

Independently configure the Lantern Meilisearch SDK client with a valid key.

Verify the actual Authorization header and successful authenticated operations.

Do not assume that setting `MEILI_MASTER_KEY` for the Meilisearch process automatically authenticates Lantern's SDK clients.

---

# Part 5 — Meilisearch Key Rotation

## 11. Implement Controlled Credential Rotation

Provide:

```bash
Lantern meilisearch rotate-key
```

This must be an explicitly authorized maintenance operation.

### Requirements

- Identify the active Meilisearch instance.
- Validate the current credential state.
- Preserve the existing data directory.
- Create a recoverable pre-rotation state.
- Stop dependent indexing operations where necessary.
- Rotate the master key using a supported process.
- Reconcile or recreate scoped API keys as required.
- Update Lantern's protected credential storage.
- Restart or reconfigure affected components.
- Verify authenticated search and indexing.
- Preserve recoverability on partial failure.

### Important

Do not assume that replacing `meili-master-key.txt` is sufficient.

Changing the master key can affect API key validity and service authentication.

Verify behavior against the actual supported Meilisearch version.

### Failure handling

If rotation fails:

- Preserve diagnostics.
- Do not delete indexes.
- Do not leave conflicting active credentials without reporting the condition.
- Provide a documented recovery procedure.

---

# Part 6 — Administrator Provisioning

## 12. Shared Initial Administrator Setup

Implement or reuse a common administrator provisioning service.

The same mechanism must work on Windows and Linux.

### Requirements

- Initialize SQLite before provisioning.
- Check whether an administrator already exists.
- Require explicit initial credentials when creating the first administrator.
- Use the existing password hashing implementation.
- Persist the administrator record.
- Verify successful provisioning.
- Preserve existing accounts on subsequent starts.

### Password input

Use a secure interactive input mechanism.

Avoid echoing passwords to the console.

Do not pass plaintext passwords as ordinary command-line arguments.

Do not log passwords.

### Normal startup

After provisioning, the administrator password must not be required when starting Lantern.

This is essential for Windows Service and systemd operation.

## 13. Administrator Password Reset

Support:

```bash
Lantern admin reset-password
```

### Requirements

- Run only through an explicitly authorized local maintenance context.
- Validate the selected installation and database.
- Use the existing administrator repository and password hasher.
- Preserve unrelated application state.
- Avoid plaintext password persistence.
- Reject attempts against unauthorized installations.

### Security

Do not create an unauthenticated remote password reset endpoint.

Do not automatically reset administrator credentials when login fails.

### Platform differences

Use Windows ACLs and Linux filesystem permissions to restrict local maintenance access.

Document the trust model.

Do not assume every local shell user is authorized to reset the administrator password.

---

# Part 7 — Cross-Platform Process Lifecycle

## 14. Shared Application Startup

Ensure the application starts consistently on Windows and Linux.

The startup sequence should include:

1. Configuration loading.
2. Configuration validation.
3. Persistent storage initialization.
4. SQLite migrations.
5. Meilisearch process management.
6. Meilisearch readiness verification.
7. Meilisearch authentication verification.
8. Embedding initialization.
9. Search index validation.
10. Background job recovery.
11. Web application readiness.

Reuse existing application infrastructure.

Do not duplicate this sequence in platform-specific shell scripts.

## 15. Meilisearch Process Management

Audit the current `MeilisearchProcessManager`.

Remove unjustified Windows-only assumptions.

### Requirements

- Resolve platform-specific executable paths.
- Support `.exe` on Windows.
- Support native Linux binaries.
- Use platform-appropriate file permissions.
- Preserve persistent data.
- Bind internal services to loopback by default.
- Manage only owned processes.
- Perform bounded readiness checks.
- Handle startup failure.
- Handle shutdown cancellation.
- Avoid orphaned processes.

### Linux-specific considerations

Verify:

- Executable permissions.
- Process signals.
- Child-process lifetime.
- Standard output and error capture.
- Shutdown grace periods.
- Exit codes.

Do not use Windows-specific process termination techniques on Linux.

### Important

Preserve the existing fixes for partial startup failures and disposed `CancellationTokenSource` errors.

A Kestrel port conflict must not leave an orphaned Meilisearch process.

## 16. Embedding Server and Native LlamaPure

Verify Linux support for the native embedding implementation.

### Requirements

- Build or obtain the approved Linux x64 LlamaPure native dependencies.
- Verify native llama.cpp compatibility.
- Resolve Linux `.so` libraries correctly.
- Preserve the existing embedding configuration.
- Load the same Granite GGUF model.
- Verify 384-dimensional output.
- Verify Japanese and English input.
- Verify deterministic or tolerance-based reference results, as appropriate.
- Dispose native resources correctly.

### Native dependencies

Do not assume a Windows DLL can be used on Linux.

Do not assume that successful .NET compilation proves native compatibility.

Test actual inference on Linux.

### CPU baseline

CPU-only execution must work.

Do not require CUDA or a discrete GPU.

GPU support may remain optional.

### Model compatibility

Use the existing validated model artifact.

Do not silently replace it with another embedding model.

## 17. Embedding Failure Behavior

If the embedding model cannot be initialized:

- Report the failure.
- Preserve keyword search where possible.
- Do not report Hybrid Search as ready.
- Do not automatically download another model.
- Do not delete existing vectors or indexes.

This behavior must be consistent on Windows and Linux.

---

# Part 8 — Cross-Platform Crawling

## 18. File Crawler Compatibility

Audit the existing file crawler for Windows-specific assumptions.

### Windows

Preserve:

- Drive-letter paths.
- UNC shares.
- Windows path normalization.
- Case-insensitive filesystem behavior where applicable.
- Reparse-point security.

### Linux

Support:

- Absolute POSIX paths.
- Case-sensitive filenames.
- Symbolic links.
- Unix permissions.
- Read-only mounts.
- File and directory access errors.

### Requirements

- Preserve approved root boundaries.
- Prevent symlink escapes.
- Avoid assuming path comparison is always case-insensitive.
- Avoid assuming Windows path separators.
- Preserve deterministic document identities.
- Preserve incremental crawling behavior.
- Prevent unsafe deletion after incomplete scans.

### Existing indexed content

Do not silently change Windows document identity semantics merely to support Linux.

Platform-specific path handling must be explicit and tested.

## 19. Web Crawler Compatibility

Preserve the Phase 8 web crawler on Linux.

Verify:

- HTTP and HTTPS requests.
- TLS validation.
- DNS resolution.
- Redirect policies.
- SSRF protection.
- Approved intranet networks.
- Rate limiting.
- robots.txt.
- Incremental crawling.
- ETag and Last-Modified behavior.

Ensure SSRF protection works with Linux networking APIs.

Do not weaken destination validation to achieve cross-platform compatibility.

## 20. Document Extraction Compatibility

Verify existing extraction libraries on Linux.

Test:

- PDF.
- DOCX.
- XLSX.
- PPTX.
- DOC.
- XLS.
- PPT.
- General text.
- Source code.
- HTML.
- Japanese text encodings.

The legacy Office formats should be tested if their support has been implemented.

### Requirements

- Identify Windows-only native dependencies.
- Replace or isolate unsupported dependencies where necessary.
- Preserve extraction accuracy.
- Preserve structural metadata.
- Preserve resource limits.
- Avoid requiring Microsoft Office or LibreOffice.
- Avoid requiring Java.

Do not claim that all extractors are Linux-compatible solely because their managed assemblies load.

---

# Part 9 — Platform-Specific Startup Scripts

## 21. Windows Wrapper

Provide or update:

```powershell
.\start-lantern.ps1
```

The script should:

- Resolve its installation directory.
- Find the correct Lantern executable.
- Invoke the shared CLI.
- Forward arguments.
- Preserve exit codes.
- Avoid duplicating initialization logic.

Use the existing PowerShell compatibility policy.

## 22. Linux Wrapper

Provide:

```bash
./start-lantern.sh
```

The script should:

- Resolve its own installation directory.
- Locate the Linux executable.
- Invoke the shared CLI.
- Forward arguments correctly.
- Preserve exit codes.
- Support graceful termination.

Use a POSIX-compatible shell or explicitly document a Bash requirement.

Avoid dependencies on uncommon shell utilities.

### Important

The shell script must not independently implement:

- Password hashing.
- SQLite initialization.
- Meilisearch API key creation.
- Configuration migrations.
- Administrator creation.

These belong to the shared .NET implementation.

---

# Part 10 — Windows Service and systemd

## 23. Preserve Windows Service Support

Retain existing Windows Service behavior.

Verify:

- Noninteractive startup.
- Persistent configuration.
- Credential loading.
- Meilisearch startup.
- Embedding initialization.
- Crawler scheduling.
- Controlled shutdown.
- Recovery after restart.

Do not break current Windows installations.

## 24. Implement Linux systemd Support

Provide a systemd unit file.

Suggested name:

```text
lantern.service
```

### Requirements

- Use the published Linux executable.
- Run under a dedicated service identity.
- Use absolute executable paths.
- Use a defined data directory.
- Support noninteractive startup.
- Restart according to a bounded, documented policy.
- Stop gracefully.
- Avoid passing plaintext secrets in unit files.
- Preserve persistent data.

### Example structure

```ini
[Unit]
Description=Lantern Search Service
After=network.target

[Service]
Type=exec
User=lantern
Group=lantern
ExecStart=/opt/lantern/app/Lantern.Web
WorkingDirectory=/opt/lantern/app
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

This is illustrative.

Adapt the executable and configuration paths to the actual package.

### Service security

Apply suitable systemd hardening options after verifying compatibility.

Do not enable restrictions that prevent configured file or web crawling.

Document required filesystem and network access.

### Shutdown

Verify that systemd stop requests terminate Lantern and its owned Meilisearch process cleanly.

Avoid orphaned child processes.

## 25. Linux Installation Permissions

Document and implement appropriate ownership.

Example:

```text
/opt/lantern       Application binaries
/etc/lantern       Configuration and secrets
/var/lib/lantern   Persistent data
/var/log/lantern   Logs
```

### Requirements

- Use a dedicated service account where practical.
- Restrict secret access.
- Permit writes only where required.
- Keep application binaries read-only during ordinary operation.
- Grant crawler access only to approved source directories.

Do not require Lantern to run as root.

---

# Part 11 — Linux Packaging

## 26. Linux x64 Self-Contained Distribution

Add a Linux x64 release target.

Use:

```text
Runtime Identifier: linux-x64
Deployment: Self-contained
Framework: .NET 10
```

Suggested command:

```bash
dotnet publish \
  src/Lantern.Web/Lantern.Web.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --output artifacts/publish/linux-x64
```

Adapt paths to the repository.

### Requirements

- Include the .NET runtime.
- Include required native `.so` libraries.
- Include the approved Meilisearch Linux executable.
- Include the Granite GGUF model.
- Include configuration templates.
- Include Linux startup scripts.
- Include the systemd unit.
- Include licenses and notices.

### Native dependency validation

Inspect ELF dependencies using appropriate Linux tools.

Verify actual compatibility with the supported Linux distribution and C library.

Do not assume every Linux distribution supports the same native binaries.

## 27. Linux Release Archive

Produce a versioned archive.

Example:

```text
Lantern-1.0.0-linux-x64.tar.gz
```

Preserve the existing Windows ZIP distribution.

### Requirements

- Include a release manifest.
- Include checksums.
- Record component versions.
- Preserve executable permissions.
- Exclude secrets.
- Exclude development databases.
- Exclude temporary files.
- Validate archive contents after extraction.

### Platform compatibility

Document the Linux distributions and versions actually tested.

Do not claim universal Linux compatibility.

## 28. Keep Windows Packaging Compatible

Preserve:

```text
Lantern-<version>-win-x64.zip
```

The Linux implementation must not require Windows users to adopt Linux-specific directory conventions or shell commands.

Use common packaging logic where practical, but preserve platform-specific binaries and scripts.

---

# Part 12 — Development Environment

## 29. Linux Developer Workflow

Provide a functional Linux development setup.

The developer should be able to:

1. Clone the repository.
2. Install the .NET 10 SDK.
3. Obtain or build the approved native dependencies.
4. Initialize development configuration.
5. Start Meilisearch.
6. Start Lantern.
7. Debug application code.
8. Run tests.

### Requirements

- Use a development-specific data directory.
- Avoid `/etc/lantern` and `/var/lib/lantern` for ordinary development.
- Avoid requiring root.
- Support command-line debugging.
- Document VS Code debugging where practical.
- Preserve existing Windows Visual Studio debugging.

## 30. Shared Development Setup

Prefer a common development setup command using the shared CLI.

Examples:

```bash
Lantern setup --environment Development
```

or an existing equivalent.

Windows and Linux developer scripts should invoke the same initialization services.

Do not maintain unrelated administrator provisioning logic in separate scripts.

---

# Part 13 — Testing

## 31. Cross-Platform Unit Tests

Run the existing test suite on Windows and Linux.

Cover:

- Configuration validation.
- Secret generation.
- Administrator provisioning.
- SQLite initialization.
- Schedule calculations.
- Search filters.
- Category relationships.
- Document identity.
- URL normalization.
- Path normalization.
- Cancellation behavior.

## 32. Real Linux Integration Tests

Use the available Linux environment.

**Actual Linux execution is mandatory.**

Verify:

1. Linux self-contained publish.
2. Application startup.
3. SQLite initialization.
4. Meilisearch startup.
5. Meilisearch authenticated API access.
6. Embedding model loading.
7. Native LlamaPure inference.
8. Keyword search.
9. Hybrid search.
10. File crawling.
11. Web crawling.
12. Document extraction.
13. Scheduler execution.
14. Application shutdown.
15. Persistent data across restart.

Do not replace Linux testing with Windows tests or mocked Linux platform behavior.

## 33. Credential Management Tests

Test:

- Initial master key generation.
- Existing key preservation.
- Invalid key detection.
- Valid Meilisearch SDK authentication.
- Missing Authorization header prevention.
- Scoped API key provisioning.
- Secret permissions.
- Credential rotation.
- Failed rotation recovery.
- Administrator creation.
- Administrator password reset.
- No plaintext password persistence.

Use real Meilisearch for authentication integration tests.

## 34. Linux Filesystem Security Tests

Test:

- Absolute paths.
- Relative path rejection.
- Symbolic links.
- Symlink escape attempts.
- Case-sensitive paths.
- Permission failures.
- Read-only directories.
- Deleted files.
- Interrupted scans.
- Incremental synchronization.

Ensure failed or incomplete scans do not trigger unsafe mass deletion.

## 35. Service Integration Tests

### Windows

Verify existing Windows Service tests still pass.

### Linux

Verify:

1. Install the systemd unit.
2. Start Lantern.
3. Check service status.
4. Verify application readiness.
5. Verify Meilisearch readiness.
6. Verify embedding readiness.
7. Execute a crawler job.
8. Restart the service.
9. Verify persistent state.
10. Stop the service.
11. Verify graceful child-process cleanup.
12. Remove the service without deleting data.

Run destructive installation tests only in a controlled environment.

## 36. Fresh Installation Tests

Test both platforms from extracted release archives.

### Windows

Use the Windows ZIP.

### Linux

Use the Linux tarball.

For each platform:

1. Extract the archive.
2. Initialize configuration.
3. Provision the initial administrator.
4. Start Lantern.
5. Verify authentication.
6. Verify Meilisearch.
7. Verify SQLite.
8. Verify the Granite model.
9. Perform keyword search.
10. Perform hybrid search.
11. Restart Lantern.
12. Verify preserved state.

### Offline testing

Verify the runtime does not require internet access after the release archive has been obtained.

Web crawling may still require access to configured target websites.

---

# Part 14 — CI and Release Integration

## 37. Extend GitHub Actions

Preserve Windows CI.

Add Linux validation.

Suggested matrix:

```text
windows-latest
ubuntu-latest
```

For Linux, use a runner compatible with the supported native dependencies.

### Required checks

- .NET restore.
- Release build.
- Unit tests.
- SQLite integration tests.
- Meilisearch integration tests.
- Extraction tests.
- Native dependency validation.
- Linux publish.
- Windows publish.
- Packaging smoke tests.

### Native embedding

Real GGUF inference must be part of release qualification on both operating systems.

Do not claim Linux hybrid search support based only on mocked embeddings.

### Release artifacts

Produce:

```text
Lantern-<version>-win-x64.zip
Lantern-<version>-linux-x64.tar.gz
```

Include corresponding checksums.

Do not publish a release unless the required platform-specific qualification checks pass.

---

# Part 15 — Documentation

## 38. Update Installation Documentation

Document both platforms.

Suggested structure:

```text
docs/en/installation/windows.md
docs/en/installation/linux.md
docs/en/development/windows.md
docs/en/development/linux.md
```

Follow the existing documentation organization.

### Windows documentation

Include:

- ZIP installation.
- Initial administrator setup.
- Meilisearch secret management.
- Console startup.
- Windows Service installation.
- Upgrade.
- Backup and recovery.

### Linux documentation

Include:

- Supported distributions.
- Native prerequisites.
- Tarball installation.
- File permissions.
- Initial administrator setup.
- Meilisearch secret management.
- Console startup.
- systemd installation.
- Upgrade.
- Backup and recovery.

### Shared documentation

Explain:

- Setup CLI commands.
- Administrator credentials.
- Meilisearch API key management.
- Secret rotation.
- Configuration precedence.
- Offline operation.
- Data directory layout.

## 39. Update README

The root README should clearly state:

```text
Supported Platforms

- Windows x64
- Linux x64
```

List only platforms actually validated.

Provide separate concise Quick Start instructions for Windows and Linux.

Do not document commands before they are implemented.

Do not include default administrator passwords.

## 40. Developer Documentation

Document:

- Windows Visual Studio debugging.
- Windows VS Code debugging.
- Linux VS Code debugging.
- Linux CLI debugging.
- Meilisearch setup.
- Native LlamaPure dependencies.
- Granite model placement.
- SQLite migrations.
- Development credential provisioning.
- Running tests.
- Troubleshooting.

Ensure all commands match the implementation.

---

# Part 16 — Scope and Acceptance

## 41. Out of Scope

Do not implement:

- Docker deployment.
- Kubernetes.
- macOS packaging.
- Linux ARM64.
- Windows ARM64.
- MSI installers.
- DEB or RPM packaging.
- GUI installation wizards.
- Cloud embedding services.
- New embedding models.
- GPU-specific releases.
- Distributed crawling.
- Distributed search.
- New authentication providers.
- LDAP or Active Directory integration.
- OCR.
- RAG.
- Chat interfaces.

These may be considered later.

Do not redesign Lantern's search architecture merely to add Linux support.

## 42. Acceptance Criteria

The implementation is complete when:

### Shared setup

- A shared .NET setup mechanism exists.
- PowerShell and Bash are thin wrappers.
- Initial configuration works on both platforms.
- SQLite initialization works.
- Administrator provisioning works.
- Normal startup is noninteractive.
- Existing installation data is preserved.

### Secret management

- Meilisearch secrets are generated securely.
- Secrets are stored in protected files.
- Windows ACLs are enforced.
- Linux file permissions are enforced.
- Existing secrets are preserved.
- API keys are valid and recognized by Meilisearch.
- Credentials survive restart.
- Key rotation is supported and verified.
- No plaintext secrets are committed to the repository.

### Application runtime

- Windows startup works.
- Linux startup works.
- Meilisearch process management works on both platforms.
- Authenticated indexing works.
- SQLite works.
- Keyword search works.
- Native LlamaPure inference works.
- Hybrid search works.
- File crawling works.
- Web crawling works.
- Scheduler and background jobs work.
- Shutdown does not leave orphaned owned processes.

### Service operation

- Windows Service behavior remains functional.
- systemd operation works on Linux.
- Service startup is noninteractive.
- Existing credentials are reused.
- Persistent data survives service restart.
- Graceful shutdown works.

### Packaging

- Windows x64 self-contained ZIP works.
- Linux x64 self-contained tarball works.
- Required native dependencies are included.
- Granite GGUF is included.
- Meilisearch is included.
- Release manifests and checksums are generated.
- Offline startup is verified.

### Testing

- Windows regression tests pass.
- Linux unit tests pass.
- Linux integration tests pass.
- Real Meilisearch authentication tests pass.
- Real LlamaPure inference tests pass.
- Keyword and hybrid search work on Linux.
- Linux installation and restart tests pass.
- systemd lifecycle tests pass.
- Documentation matches the actual implementation.

Do not claim Linux support is complete without execution in the real Linux environment.

## 43. Deliverables

Provide:

1. Shared .NET setup CLI.
2. Shared configuration initialization.
3. Cross-platform directory resolution.
4. Secure secret generation.
5. Windows secret permissions.
6. Linux secret permissions.
7. Meilisearch API key provisioning.
8. Meilisearch key rotation.
9. Shared administrator provisioning.
10. Shared administrator password reset.
11. Cross-platform Meilisearch process management.
12. Linux LlamaPure native integration.
13. Linux embedding verification.
14. Linux filesystem crawler compatibility.
15. Linux document extraction verification.
16. Windows startup wrapper.
17. Linux startup wrapper.
18. Windows Service regression verification.
19. Linux systemd service.
20. Linux self-contained packaging.
21. Windows packaging compatibility.
22. Cross-platform CI.
23. Installation and recovery tests.
24. Updated README.
25. Windows developer documentation.
26. Linux developer documentation.
27. Updated release documentation.

## 44. Required Completion Report

At the end of implementation, provide:

### A. Architecture summary

Explain how setup, secrets, authentication, and startup are shared between Windows and Linux.

### B. Existing Windows compatibility

Describe what was preserved and any required migrations.

### C. Linux implementation

Describe:

- Native binaries.
- Libraries.
- Process lifecycle.
- Filesystem behavior.
- Service management.
- Packaging.

### D. Secret management

Explain:

- Generation.
- Storage.
- Permissions.
- API key registration.
- Rotation.
- Recovery.

Do not reveal actual secrets.

### E. Administrator provisioning

Describe first-run setup and password recovery on both platforms.

### F. Files changed

List all important created and modified files.

### G. Build commands

List actual commands executed on Windows and Linux.

### H. Test results

Report:

- Windows tests.
- Linux tests.
- SQLite integration.
- Meilisearch integration.
- LlamaPure inference.
- Hybrid search.
- File crawler.
- Web crawler.
- systemd.
- Packaging.

Clearly distinguish passed, failed, skipped, and unexecuted tests.

### I. Release artifacts

Report actual:

- Archive names.
- Sizes.
- SHA-256 checksums.
- Target platforms.
- Native component versions.

### J. Known limitations

Document:

- Supported Linux distributions.
- Native dependency requirements.
- CPU instruction-set requirements.
- Filesystem compatibility.
- Service-account permissions.
- Meilisearch migration limitations.
- Unverified environments.

### K. Remaining work

Identify incomplete acceptance criteria.

Do not claim cross-platform production readiness when mandatory Linux validation remains incomplete.

---

# Final Instructions

Implement Windows and Linux support using a shared .NET initialization and command-line infrastructure.

Preserve the existing Windows functionality and persistent data.

Move administrator provisioning, Meilisearch credential management, configuration initialization, and migration logic into reusable .NET services.

Keep PowerShell and Bash scripts thin.

Use cryptographically secure secrets stored in protected files rather than permanent global environment variables.

Pass required Meilisearch settings through the managed child-process environment.

Ensure Lantern's Meilisearch SDK uses valid authenticated API credentials.

Provide explicit administrator initialization and credential recovery commands.

Support Linux x64 with native Meilisearch, native LlamaPure libraries, and the existing Granite GGUF model.

Verify actual embedding inference, keyword search, and hybrid search on Linux.

Implement systemd service support while preserving Windows Service behavior.

Provide a self-contained Linux x64 tarball alongside the Windows x64 ZIP.

Use the available Linux environment for real integration and packaging tests.

Update development, installation, security, and recovery documentation for both platforms.

**The primary success condition is that Lantern can be installed, initialized, started, authenticated, operated, and upgraded on both Windows x64 and Linux x64 using the same core application logic, with secure credential management, functional local hybrid search, and reliable background crawling—without Docker, Java, external embedding services, or duplicated platform-specific setup implementations.**
