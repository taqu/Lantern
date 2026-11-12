# Lantern — Comprehensive Meilisearch Security, API Key Lifecycle, Setup, and Documentation Refactoring

## Mission

Perform a comprehensive refactoring of Lantern's Meilisearch authentication, credential management, authorization boundaries, installation procedures, setup scripts, and documentation.

The current implementation has evolved through several development phases and may contain inconsistent assumptions about how Meilisearch credentials are generated, persisted, loaded, and used.

Previously observed problems include:

- `missing_authorization_header` during document indexing.
- API key values generated locally without being registered through Meilisearch.
- Confusion between the Meilisearch master key, scoped API keys, and ASP.NET Core Data Protection key rings.
- Incorrect handling or interpretation of DPAPI-protected XML files.
- Different secret-loading behavior between Windows and Linux.
- Administrator provisioning inconsistencies.
- Development setup scripts producing credentials that do not work at runtime.
- Configuration mismatches between setup, IDE debugging, console execution, and service execution.
- Incomplete documentation of initial installation, API key management, and administrator recovery.

These are indications of architectural inconsistency, not isolated bugs.

**The objective is to establish one coherent, secure, cross-platform credential and authentication architecture and update all related code, scripts, tests, packaging, and documentation accordingly.**

Do not limit the work to individual error messages.

Do not preserve obsolete behavior merely for compatibility with earlier implementation assumptions.

However, preserve existing installations, databases, indexes, and credentials through a safe migration strategy.

---

# Phase 1 — Audit the Existing Architecture

## 1. Inspect the Entire Relevant Codebase

Before changing implementation, examine all Meilisearch-related code.

Include:

- Meilisearch client registration.
- Dependency injection.
- Meilisearch process management.
- API key generation.
- API key provisioning.
- Secret storage.
- Credential loading.
- Search services.
- Document indexing.
- Chunk indexing.
- Document deletion.
- Index initialization.
- Index settings synchronization.
- Category synchronization.
- Crawler jobs.
- Background services.
- Administrator authentication.
- Administrator authorization.
- Application startup and shutdown.
- Setup and maintenance commands.

Also examine:

- Root README.
- English documentation.
- Japanese documentation.
- Development setup scripts.
- Release setup scripts.
- Startup scripts.
- Windows Service installation.
- Linux systemd integration.
- Configuration templates.
- Release packaging.
- CI workflows.
- Existing tests.

Do not assume any previously proposed CLI commands or scripts are actually implemented.

Use the repository as the source of truth.

## 2. Create a Credential Usage Inventory

Identify every credential used by Lantern.

Classify them into:

| Credential | Purpose |
|---|---|
| Meilisearch Master Key | Meilisearch bootstrap and key management |
| Search API Key | Search operations |
| Indexing API Key | Background document indexing |
| Admin API Key | Privileged index and configuration management |
| Internal Maintenance Credential | Unattended privileged maintenance, if required |
| Administrator Password | Lantern web administrator authentication |
| ASP.NET Core Data Protection Keys | Cookie and application data protection |

For each credential, determine:

- Where it is created.
- Where it is stored.
- How it is protected.
- When it is loaded.
- Who can access it.
- How long it remains in memory.
- When it expires.
- How it is rotated.
- How it is recovered.
- Whether it is used by multiple components.

Produce an inventory before implementing changes.

## 3. Audit Every Meilisearch Client

Find every instantiation of:

```csharp
new MeilisearchClient(...)
```

and all equivalent HTTP-based clients.

Record:

- Credential source.
- Authorized actions.
- Required indexes.
- Service lifetime.
- Startup ordering.
- Error handling.
- Key rotation behavior.

Identify unintended anonymous clients and clients using the master key for routine operations.

Remove duplicate credential-loading logic.

---

# Phase 2 — Define the Target Security Architecture

## 4. Separate Meilisearch Credentials by Role

Implement the following logical roles.

### Master Key

Purpose:

- Secure the Meilisearch server.
- Create and manage API keys.
- Recover from missing or expired API keys.
- Perform explicit credential maintenance.

Restrictions:

- Never use for normal search.
- Never use for normal indexing.
- Never expose to the browser.
- Never include in cookies or sessions.
- Never log.
- Never embed in client-side JavaScript.

Load it only within authorized bootstrap or credential-management operations when practical.

The master key must remain persistently available to authorized maintenance processes so unattended recovery and rotation can work.

### Search API Key

Purpose:

- Keyword search.
- Hybrid search.
- Other search operations supported by the configured Meilisearch version.

Permissions:

```json
{
  "actions": ["search"],
  "indexes": ["lantern-*"],
  "expiresAt": null
}
```

This is an illustrative policy, not a fixed schema or exact index naming requirement.

Use the actual Lantern index names and required action permissions.

Keep this credential server-side even if Meilisearch supports frontend search keys.

Do not expose it directly to the browser unless a separate explicit security design authorizes that.

### Indexing API Key

Purpose:

- Add documents.
- Update documents.
- Delete documents.
- Inspect indexing tasks.
- Support background crawler synchronization.

Permissions must cover only required operations.

Do not grant unrestricted administrative access to the crawler.

The crawler must operate independently of administrator login sessions.

### Admin API Key

Purpose:

- Authorized administrative changes.
- Index management.
- Index configuration.
- Settings updates.
- Administrative diagnostics where needed.

Use a scoped key with only the actions necessary for the actual administrative operation.

Do not automatically use the default unrestricted Admin API Key if a narrower custom key is sufficient.

### Internal Maintenance Credential

Some privileged operations may need to run without an administrator login, including:

- Startup index initialization.
- Index schema reconciliation.
- Embedder configuration.
- Background settings migration.
- Recovery after interrupted upgrades.

Audit these requirements.

If they require privileges beyond the Indexing API Key, create an explicit internal maintenance role with minimal necessary permissions.

Do not make background maintenance depend on an interactive administrator session.

Do not reuse a broad Admin API Key everywhere merely for convenience.

## 5. Separate Lantern Authentication From Meilisearch Authorization

Lantern administrator authentication and Meilisearch API authorization are distinct systems.

The browser must authenticate to Lantern through the existing ASP.NET Core authentication mechanism.

The browser must never receive:

- The Meilisearch Master Key.
- The Meilisearch Admin API Key.
- The Meilisearch Indexing API Key.
- Internal maintenance credentials.

A Lantern administrator session grants access to authorized application operations.

It must not directly become a Meilisearch administrative session.

Use server-side authorization checks before invoking privileged Meilisearch services.

## 6. Admin Key Lifetime

Prefer operation-scoped use of privileged credentials.

Expected flow:

```text
Administrator Login
        |
        v
ASP.NET Core Authentication
        |
        v
Administrative Operation Requested
        |
        v
Server-Side Authorization Check
        |
        v
Load Required Admin Credential
        |
        v
Create or Acquire Privileged Client
        |
        v
Perform Authorized Operation
        |
        v
Release Operation References
```

Requirements:

- Do not store Admin API Keys in authentication cookies.
- Do not store them in Blazor component state.
- Do not store them in browser storage.
- Do not attach them to user session claims.
- Avoid long-lived singleton Admin clients unless justified and documented.
- Do not use shared mutable global authorization state.

A logged-in administrator should not cause privileged credentials to remain unnecessarily loaded for the entire browser session.

### Security limitation

Releasing references to a .NET string does not guarantee immediate zeroization of the credential from memory.

Do not claim cryptographic memory erasure unless the implementation actually provides it.

The goal is to minimize credential exposure and unnecessary retention.

---

# Phase 3 — Shared Cross-Platform Credential Manager

## 7. Introduce a Shared Meilisearch Credential Manager

Create or refine a service responsible for the complete API key lifecycle.

Suggested conceptual interface:

```csharp
public interface IMeilisearchCredentialManager
{
    Task EnsureCredentialsAsync(
        CancellationToken cancellationToken);

    Task<MeilisearchCredential> GetCredentialAsync(
        MeilisearchCredentialRole role,
        CancellationToken cancellationToken);

    Task RotateCredentialAsync(
        MeilisearchCredentialRole role,
        CancellationToken cancellationToken);

    Task<MeilisearchCredentialStatus> GetStatusAsync(
        MeilisearchCredentialRole role,
        CancellationToken cancellationToken);
}
```

This is illustrative.

Adapt names and signatures to the existing application architecture.

### Responsibilities

- Discover existing managed API keys.
- Validate credential metadata.
- Validate permissions.
- Validate index scopes.
- Validate expiration.
- Load stored credential values.
- Create missing credentials.
- Rotate expiring credentials.
- Recover expired credentials.
- Support safe credential replacement.
- Record non-sensitive operational status.

Do not implement this separately for Windows and Linux.

## 8. Use the Meilisearch Keys API

Create API keys through the official Meilisearch API.

Conceptually:

```http
POST /keys
Authorization: Bearer <master-key>
Content-Type: application/json
```

```json
{
  "name": "Lantern Search",
  "actions": ["search"],
  "indexes": ["lantern-*"],
  "expiresAt": null
}
```

Adapt the key policy to the actual requirements.

### Requirements

- Never generate an arbitrary API key string locally and assume it is registered.
- Do not derive the credential value through undocumented algorithms.
- Use the supported SDK or REST API.
- Persist the actual API key returned by Meilisearch.
- Persist its UID and role.
- Preserve the expiration date.
- Preserve the intended permissions and index scopes.

Verify the precise API semantics against the pinned Meilisearch version.

## 9. Handle Existing Default API Keys

Meilisearch creates default API keys when configured with a master key.

Audit whether Lantern currently uses these keys.

The target design should favor purpose-specific custom keys where narrower permissions are practical.

Do not automatically delete existing default keys during migration.

Do not assume the default Admin API Key can perform API key management.

Use supported key-management permissions and the appropriate trusted credential.

## 10. Avoid Duplicate API Key Creation

Credential provisioning must be idempotent.

Ensure:

- Repeated setup does not create duplicate managed keys.
- Repeated application startup does not create duplicate keys.
- Concurrent startup attempts cannot independently create conflicting keys.
- Existing valid keys are reused.
- Partially completed provisioning can be reconciled safely.

Use deterministic role identity and appropriate synchronization.

### Important

An API key may have been successfully created in Meilisearch while the local process failed before persisting it.

Design recovery for this partial-success scenario.

Do not rely only on a local file-existence check.

Do not automatically delete unidentified keys.

If the key value cannot be safely recovered using the supported API behavior, create a replacement through an explicit, controlled reconciliation procedure and retire the old managed key only after successful cutover.

---

# Phase 4 — Credential Expiration and Rotation

## 11. Define Expiration Policies

Make expiration policies explicit and configurable.

For example:

```text
Search API Key:
  Configurable expiration

Indexing API Key:
  Configurable expiration

Admin API Key:
  Configurable expiration

Internal Maintenance API Key:
  Configurable expiration
```

Do not hardcode a universal expiration period without considering unattended operation.

Support non-expiring internal keys only when the operator explicitly selects or the documented installation policy allows them.

Document the security tradeoffs.

## 12. Rotate Keys Before Expiration

Do not wait for credentials to expire before starting rotation.

Implement:

- Startup validation.
- Periodic expiration checks.
- Configurable rotation thresholds.
- Replacement key creation.
- Replacement credential persistence.
- Client cutover.
- Old key revocation after successful cutover.

Example lifecycle:

```text
Existing Key
     |
     v
Expiration Threshold Reached
     |
     v
Create Replacement Key
     |
     v
Persist Replacement
     |
     v
Validate Authentication
     |
     v
Switch Active Clients
     |
     v
Retire Old Key
```

Use UTC consistently.

Avoid depending on a user login to perform scheduled rotation.

## 13. Make Rotation Failure-Safe

Handle:

- Meilisearch unavailable.
- Invalid master key.
- API key creation rejected.
- Key created but local persistence failed.
- Local persistence succeeded but validation failed.
- Application crash during rotation.
- Concurrent rotation requests.
- Existing key expired during recovery.
- Old key revocation failure.

### Requirements

- Preserve the last known working credential until replacement succeeds.
- Avoid deleting the old key prematurely.
- Record rotation status.
- Prevent repeated uncontrolled key creation.
- Allow safe restart and reconciliation.
- Never silently replace the master key.

### Important

Do not claim a transaction exists across local storage and Meilisearch unless it actually does.

Implement explicit recovery for partial states.

## 14. Rotate Client Credentials Correctly

Existing SDK clients may retain old credentials.

Ensure rotation updates the credentials used for new requests.

Review:

- Singleton clients.
- Scoped clients.
- Background service clients.
- Cached HTTP handlers.
- Indexing workers.
- Search services.
- Administrative operations.

Use an appropriate client factory or immutable client snapshot model.

Avoid mutating credentials in a shared client while requests are active unless the SDK explicitly supports it.

Consider in-flight requests during cutover.

---

# Phase 5 — Master Key Management

## 15. Separate Master Key From Managed API Keys

The master key must have its own lifecycle.

It is not created through `/keys`.

It is not the same as the Admin API Key.

It should not be rotated as part of ordinary API key expiration handling.

### Master key initialization

On a fresh installation:

1. Generate a cryptographically secure master key.
2. Persist it in protected storage.
3. Start Meilisearch using the stored value.
4. Verify the server is accessible with the intended master key.
5. Provision the required scoped API keys.
6. Verify authenticated operations.

### Existing installation

Do not regenerate the master key.

Validate that the stored master key corresponds to the intended Meilisearch instance.

If validation fails, stop credential provisioning and report a configuration error.

## 16. Explicit Master Key Rotation

Provide a separate maintenance operation.

Do not reuse ordinary API key rotation logic blindly.

The implementation must account for:

- Meilisearch process startup configuration.
- Existing managed API keys.
- Authentication effects of master key changes.
- Service restart requirements.
- Recovery after interrupted rotation.
- Preserving Meilisearch database and indexes.

Verify behavior against the installed Meilisearch version.

Do not assume master key rotation preserves all existing derived credentials.

---

# Phase 6 — Secret Storage on Windows and Linux

## 17. Shared Secret Store Abstraction

Use a common application interface for secret persistence.

Separate:

- Secret storage.
- Credential lifecycle.
- Meilisearch API access.
- Application authorization.

Avoid embedding Meilisearch role management directly into OS-specific file-handling code.

## 18. Windows Secret Protection

Audit the existing Windows implementation.

Determine whether it uses:

- Windows DPAPI.
- ASP.NET Core Data Protection.
- ACL-protected files.
- A custom encrypted XML format.

### Critical distinction

ASP.NET Core Data Protection `key-*.xml` files are cryptographic key-ring entries.

They are not serialized Meilisearch API credentials.

Do not parse or Base64-decode those XML files to obtain Meilisearch API keys.

If Lantern protects credentials using `IDataProtector`, locate and decrypt the separately stored protected credential payload using the matching protection configuration.

### Requirements

- Correct DPAPI scope.
- Correct Windows identity.
- Correct filesystem ACLs.
- Correct service-account access.
- Compatibility with existing protected credentials.
- No plaintext logging.
- No automatic regeneration after decryption failure.

## 19. Linux Secret Protection

Use the existing Linux secret-storage implementation where appropriate.

A filesystem-permission-based implementation is acceptable if it satisfies the documented threat model.

For example:

```text
/etc/lantern/secrets/
```

with appropriate ownership and permissions.

### Requirements

- Restrict secret directory access.
- Restrict individual file access.
- Validate the service identity.
- Preserve exact credential values.
- Avoid unintended plaintext temporary files.
- Avoid exposing credentials through command lines or logs.
- Ensure systemd can read required secrets.
- Support rootless development.

### Important

Linux does not need a custom replacement for Windows DPAPI.

Do not implement unnecessary homegrown encryption.

However, do not conclude that Linux is correct merely because a file has mode `0600`.

Verify ownership, path resolution, permissions, credential content, and actual authenticated API access.

## 20. Atomic Secret Persistence

When creating or replacing credential files:

- Write to a securely created temporary file in the appropriate protected location.
- Restrict access before exposing the contents.
- Flush and validate as appropriate.
- Atomically replace the target where supported.
- Preserve recovery information without creating unsecured secret copies.
- Handle failures safely.

Avoid writing partial credential files that the application may subsequently interpret as valid.

---

# Phase 7 — Client Factory and Authorization Boundaries

## 21. Introduce Role-Aware Meilisearch Clients

Prefer a shared factory that can obtain clients for specific internal roles.

Conceptual interface:

```csharp
public interface IMeilisearchClientFactory
{
    Task<MeilisearchClient> CreateSearchClientAsync(
        CancellationToken cancellationToken);

    Task<MeilisearchClient> CreateIndexingClientAsync(
        CancellationToken cancellationToken);

    Task<MeilisearchClient> CreateAdminClientAsync(
        CancellationToken cancellationToken);
}
```

This is illustrative.

Use the actual SDK type and code conventions.

### Requirements

- Search services cannot accidentally receive Admin credentials.
- Indexing services cannot accidentally receive Master credentials.
- Admin services enforce Lantern authorization before privileged operations.
- SDK clients use the correct Bearer token.
- Missing credentials produce clear errors.
- Rotation can replace credentials without restarting the entire application where practical.

Do not make the factory a universal privileged service accessible from arbitrary user-facing components.

## 22. Administrator Operations

Audit the complete administrative API and Blazor UI.

Identify operations that require:

- Search permission.
- Indexing permission.
- Index settings permission.
- General administration permission.
- API key management permission.

Apply ASP.NET Core authorization at the server-side operation boundary.

Do not rely solely on UI visibility.

### Admin key use

Acquire privileged credentials only after authorization.

Avoid persistent per-user Meilisearch Admin clients.

Do not expose keys to the browser.

Do not treat an administrator login as permission to retrieve or display Meilisearch credentials.

## 23. Background Service Authorization

Review:

- File crawler.
- Web crawler.
- Scheduler.
- Job dispatcher.
- Incremental synchronization.
- Category reconciliation.
- Embedder configuration.
- Index maintenance.

Ensure each subsystem uses a role with appropriate Meilisearch permissions.

### Requirements

- Background services continue when no administrator is logged in.
- Expired credentials trigger controlled recovery.
- Unauthorized operations do not retry indefinitely.
- Search remains available when indexing is temporarily unavailable, if the necessary search infrastructure remains healthy.

---

# Phase 8 — Setup and Initialization Refactoring

## 24. Audit All Setup Scripts

Inspect every installation and development script.

Examples may include:

```text
scripts/setup-dev.ps1
scripts/start-meilisearch.ps1
scripts/download-meilisearch.ps1
start-lantern.ps1
start-lantern.sh
```

Search the entire repository for additional scripts.

Identify:

- Duplicate key generation.
- Local random API key generation.
- Hardcoded secrets.
- Plaintext credential persistence.
- Environment variable assumptions.
- Incorrect file paths.
- Missing API key registration.
- Inconsistent initialization.
- Production/development overlap.

## 25. Consolidate Setup Into Shared .NET Logic

Prefer a shared .NET CLI or application-level setup service.

Platform scripts should be thin wrappers.

Suggested conceptual commands:

```text
Lantern setup
Lantern start
Lantern status
Lantern credentials status
Lantern credentials rotate
Lantern admin reset-password
```

Use existing CLI conventions where possible.

Do not introduce commands into documentation before implementing them.

### Setup responsibilities

The setup process should:

1. Resolve the intended installation.
2. Validate data directories.
3. Initialize configuration.
4. Initialize SQLite.
5. Provision the initial Lantern administrator.
6. Generate or load the Meilisearch master key.
7. Start or connect to the intended Meilisearch instance.
8. Provision required scoped API keys through `/keys`.
9. Persist credentials securely.
10. Validate authenticated access.
11. Report setup completion accurately.

## 26. Setup Must Be Idempotent

Repeated execution must:

- Preserve the master key.
- Reuse valid API keys.
- Preserve the administrator account.
- Preserve existing databases.
- Preserve search indexes.
- Avoid duplicate keys.
- Avoid changing credentials unnecessarily.

Do not report success until required provisioning and validation have completed.

## 27. Development Setup

Fix the development workflow on Windows and Linux.

Verify:

- Isolated development directories.
- Correct Meilisearch connection settings.
- Correct master key.
- Registered API keys.
- Correct SDK client initialization.
- Correct administrator credentials.
- Visual Studio F5.
- Linux CLI and VS Code startup.
- Restart without reprovisioning.

Do not rely on environment variables established only in the setup script's process.

## 28. Release Setup

Support Windows and Linux distributions.

### Windows

- Self-contained release.
- Protected configuration and secret files.
- Windows Service compatibility.

### Linux

- Self-contained release.
- Restricted secret file permissions.
- systemd compatibility.

Both platforms must use the same credential lifecycle logic.

The OS-specific scripts should handle paths, permissions, and service integration, not duplicate Meilisearch provisioning logic.

---

# Phase 9 — Migration of Existing Installations

## 29. Inventory Legacy Credential Formats

Identify all previously supported configurations.

Potential cases:

- A single master key used for every operation.
- A locally generated but unregistered API key.
- Default Meilisearch API keys.
- Custom registered API keys.
- A plaintext credential file.
- A DPAPI-protected credential.
- A Data Protection-protected credential payload.
- Missing credential files.
- A partially initialized installation.

Do not assume every installation uses the same format.

## 30. Implement Non-Destructive Migration

Migration must preserve:

- SQLite databases.
- Administrator accounts.
- Crawler definitions.
- Scheduled jobs.
- Categories.
- Search indexes.
- Parent documents.
- Chunk documents.
- Embedding configuration.
- Meilisearch persistent data.

### Migration sequence

1. Identify the legacy configuration.
2. Validate the existing Meilisearch connection.
3. Locate the current master key if available.
4. Verify authorized credential access.
5. Create new role-based API keys.
6. Persist them securely.
7. Verify new clients can authenticate.
8. Switch application services to the new credentials.
9. Preserve recovery information.
10. Retire legacy credentials only when safe and explicitly justified.

Do not silently delete default keys or administrator credentials.

## 31. Migration Failure Handling

If migration fails:

- Preserve the original configuration.
- Preserve the original secrets.
- Preserve Meilisearch indexes.
- Report which step failed.
- Do not claim migration completed.
- Support safe retry.

If a key is permanently unrecoverable, report the limitation and require an authorized recovery procedure.

Never replace an inaccessible master key automatically.

---

# Phase 10 — Startup, Runtime, and Error Handling

## 32. Enforce Correct Startup Ordering

Review the application startup sequence.

Expected logical ordering:

```text
Configuration
      |
      v
Secret Store
      |
      v
Master Key Validation
      |
      v
Meilisearch Readiness
      |
      v
API Key Reconciliation
      |
      v
Role-Based Client Initialization
      |
      v
Search / Indexing Readiness
      |
      v
Crawler and Background Job Processing
```

Do not allow background indexing to begin before its required credentials are valid.

## 33. Distinguish Systemic Authentication Failures

Handle these failures distinctly:

```text
missing_authorization_header
invalid_api_key
```

Requirements:

- Report the affected credential role.
- Identify the failing subsystem.
- Avoid exposing credential contents.
- Avoid retrying identical failures for every document.
- Preserve pending indexing work.
- Avoid marking failed documents as indexed.

Authentication problems should not be presented as document extraction failures.

## 34. Handle Partial Startup Failure

Preserve correct hosted-service lifecycle behavior.

If startup fails:

- Stop newly started operations.
- Cancel background tasks.
- Await shutdown where applicable.
- Dispose resources safely.
- Stop only owned processes.
- Preserve the original exception.

Do not reintroduce `CancellationTokenSource` disposal races or orphaned Meilisearch processes.

## 35. Runtime Rotation Monitoring

Run credential maintenance independently of administrator sessions.

Requirements:

- Bounded scheduling.
- Cancellation support.
- No overlapping rotations.
- Explicit failure state.
- No uncontrolled retries.
- No duplicate key creation during service restart.
- Audit logs without secrets.

Do not use a high-frequency polling loop when a scheduled check is sufficient.

---

# Phase 11 — Administrator Security

## 36. Preserve Administrator Authentication

The initial Lantern administrator is independent of Meilisearch credentials.

Do not store the administrator password as a Meilisearch API key.

Do not use the Meilisearch master key as an administrator password.

### First-run behavior

A fresh installation must allow an authorized operator to create the initial administrator.

Store the password using the existing supported password hashing mechanism.

### Existing installation

Preserve existing administrator accounts.

Do not regenerate passwords during ordinary setup or startup.

### Recovery

Provide a controlled local administrator recovery mechanism.

Do not implement an unauthenticated remote reset endpoint.

## 37. Authorization Tests

Verify:

- Anonymous users cannot access privileged operations.
- Ordinary users cannot access administrative endpoints.
- Administrators can perform permitted operations.
- Administrative requests use the proper Meilisearch role.
- Credentials never appear in HTTP responses or Blazor state.
- Background services operate without administrator sessions.

---

# Phase 12 — Cross-Platform Verification

## 38. Windows Validation

Use the actual Windows environment.

Test:

- Fresh setup.
- Existing setup.
- DPAPI secret loading, if used.
- Windows Service identity.
- Meilisearch authentication.
- API key provisioning.
- API key rotation.
- Search.
- Indexing.
- Administrator login.
- Restart.
- Recovery after partial failure.

## 39. Linux Validation

Use the available real Linux environment.

Test:

- Fresh setup.
- Existing setup.
- Secret file ownership.
- File permissions.
- systemd service identity.
- API key loading.
- API key provisioning.
- API key rotation.
- Search.
- Indexing.
- Administrator login.
- Restart.
- Recovery after partial failure.

Do not conclude Linux functionality is correct solely because plaintext files can be read.

Verify real authenticated Meilisearch operations.

## 40. Native Dependency Compatibility

Ensure the security refactoring does not break:

- Native Meilisearch startup.
- LlamaPure loading.
- Granite embedding inference.
- Hybrid search.
- PDF and Office extraction.
- File crawling.
- Web crawling.

Avoid changing unrelated search or extraction behavior.

---

# Phase 13 — Automated Tests

## 41. Credential Manager Tests

Add tests for:

1. Fresh API key provisioning.
2. Existing valid key reuse.
3. Missing key recovery.
4. Expired key replacement.
5. Expiring key rotation.
6. Invalid master key.
7. Incorrect permissions.
8. Incorrect index scope.
9. Duplicate provisioning attempts.
10. Concurrent startup.
11. Concurrent rotation.
12. Partial persistence failure.
13. Partial API creation failure.
14. Restart during rotation.
15. Old key revocation failure.
16. Recovery after interrupted migration.

## 42. Authorization Boundary Tests

Verify:

- Search services use search credentials.
- Indexing services use indexing credentials.
- Administration requires application authorization.
- Administrative services use only necessary privileged credentials.
- Master credentials are not used in ordinary operations.
- Credentials are not exposed to browser clients.

Use controlled fake clients for negative authorization tests where appropriate.

## 43. Real Meilisearch Integration Tests

Use a real authenticated Meilisearch instance.

Test:

- Key creation.
- Key retrieval.
- Key validation.
- Search permission.
- Indexing permission.
- Administrative permission.
- Denied operations for each restricted role.
- Expiration behavior.
- Rotation.
- Client cutover.
- Document creation.
- Document deletion.
- Index settings.
- Task inspection.

### Critical requirement

Test not only that permitted operations succeed, but also that forbidden operations are rejected.

A role separation design is incomplete if all clients accidentally possess administrative permissions.

## 44. Setup and Migration Tests

Test:

- Fresh Windows installation.
- Fresh Linux installation.
- Repeated setup.
- Existing master key.
- Existing API keys.
- Missing API key file.
- Unregistered legacy API key.
- Existing Data Protection key ring.
- Existing administrator account.
- Partially initialized installation.
- Interrupted migration.
- Rollback or recovery after failure.

Do not use production credentials or user documents in tests.

## 45. Release Smoke Tests

Verify released artifacts, not only development builds.

On both Windows and Linux:

1. Extract the release.
2. Run setup.
3. Create an administrator.
4. Initialize Meilisearch credentials.
5. Start Lantern.
6. Log in.
7. Perform search.
8. Index a document.
9. Restart.
10. Confirm credentials remain valid.
11. Verify service startup.
12. Verify key rotation behavior.

---

# Phase 14 — Complete Documentation Refactoring

## 46. Audit All Documentation

Search all documentation for outdated references to:

```text
MEILI_MASTER_KEY
MEILI_HOST
MEILI_API_KEY
meili-api-key.txt
meili-master-key.txt
Generate API Key
Default Admin API Key
Default Search API Key
Data Protection
DPAPI
setup-dev.ps1
Initialize-Lantern.ps1
```

Also search for:

- Plaintext password examples.
- Hardcoded development credentials.
- Environment variable instructions.
- Obsolete setup commands.
- Nonexistent scripts.
- Incorrect Windows/Linux paths.
- Claims that arbitrary generated strings are valid API keys.

Update every affected page.

Do not leave contradictory setup instructions in different documentation sections.

## 47. Rewrite README Quick Start

Provide separate Windows and Linux Quick Start instructions.

Each must explain:

- How to initialize Lantern.
- How the master key is generated.
- How API keys are provisioned.
- How the initial administrator is created.
- How to start Lantern.
- How to sign in.
- How credentials persist across restarts.

Keep the Quick Start concise.

Link to detailed security and installation documentation.

Do not require operators to manually create Meilisearch API keys unless they are configuring an explicitly external Meilisearch instance.

## 48. Document the Security Architecture

Create or update a dedicated security architecture document.

Explain:

- Master Key.
- Search API Key.
- Indexing API Key.
- Admin API Key.
- Internal Maintenance Credential, if implemented.
- Administrator authentication.
- Data Protection key rings.
- Windows DPAPI.
- Linux filesystem permissions.
- Credential expiration.
- Credential rotation.
- Authorized recovery.

Include a diagram of the trust boundaries.

Explain which credentials are persistent and which are acquired only for specific operations.

## 49. Document Credential Lifecycle

Include:

- First creation.
- Normal startup.
- Validation.
- Expiration.
- Rotation.
- Failure recovery.
- Administrative maintenance.
- Migration.
- Backup and restore.

Clearly distinguish API key rotation from master key rotation.

## 50. Update Setup and Development Documentation

Document exact commands for:

- Windows initial setup.
- Linux initial setup.
- Windows Visual Studio development.
- Linux development.
- Windows Service.
- systemd.
- Existing installation migration.
- Administrator password reset.
- Credential status inspection.
- API key rotation.
- Master key recovery.

Only document commands that are actually implemented and tested.

## 51. Update English and Japanese Documentation

Keep English and Japanese documentation consistent.

If the repository already has:

```text
docs/en/
docs/ja/
```

update both documentation trees.

Also update navigation, indexes, and links.

Ensure the README links to the correct pages.

Do not leave Japanese documentation describing the obsolete single-key architecture.

## 52. Update Configuration Templates

Review:

- Example appsettings files.
- Development configuration.
- Production configuration.
- Environment templates.
- Service configuration.
- Setup script defaults.

Remove obsolete credentials and unsafe examples.

Document configuration precedence.

Avoid requiring permanent environment variables containing secrets.

Ensure setup produces valid configuration for both Windows and Linux.

---

# Phase 15 — Packaging and CI

## 53. Update Release Packaging

Ensure the Windows and Linux packages contain:

- Shared setup CLI.
- Required startup wrappers.
- Configuration templates.
- Service integration files.
- Updated documentation.
- Required Meilisearch native executable.
- Required Lantern application binaries.

Do not include:

- Generated master keys.
- Generated API keys.
- Administrator passwords.
- Development credential files.
- Existing Data Protection key rings.
- Test databases containing credentials.

## 54. Update CI

Run appropriate tests on Windows and Linux.

Required coverage:

- Build.
- Unit tests.
- Credential manager tests.
- Authentication tests.
- Real Meilisearch integration tests.
- Setup tests.
- Migration tests.
- Windows secret storage tests.
- Linux secret storage tests.
- Release packaging smoke tests.

Ensure CI-generated credentials are isolated and never included in published artifacts.

---

# Phase 16 — Implementation Strategy

## 55. Implement in Controlled Stages

Use the following implementation order.

### Stage 1 — Audit

Map current credential storage, provisioning, and consumption.

### Stage 2 — Shared Credential Architecture

Implement role definitions, secret storage integration, and the credential manager.

### Stage 3 — Meilisearch Provisioning

Implement correct Keys API provisioning and authenticated client initialization.

### Stage 4 — Authorization Separation

Replace broad shared credentials with role-specific clients.

### Stage 5 — Expiration and Rotation

Implement validation, scheduled renewal, controlled cutover, and failure recovery.

### Stage 6 — Setup Refactoring

Update shared setup logic and Windows/Linux wrappers.

### Stage 7 — Existing Installation Migration

Implement non-destructive migration and compatibility handling.

### Stage 8 — Application Integration

Update search, crawling, indexing, maintenance, and administrator operations.

### Stage 9 — Documentation

Rewrite README, installation, development, and security documentation.

### Stage 10 — Verification

Run end-to-end tests on real Windows and Linux environments.

### Important

Each stage must leave the repository in a coherent state.

Do not remove the legacy credential-loading implementation before the migration path is functional and tested.

---

# Phase 17 — Acceptance Criteria

## 56. Security

The implementation is complete only when:

- Master Key is not used for routine search or indexing.
- Search API Key is limited to search operations.
- Indexing API Key has the required indexing permissions.
- Admin API Key is restricted to authorized administration.
- Background maintenance uses appropriate independent credentials.
- Browser clients never receive privileged Meilisearch credentials.
- Administrator authorization is enforced server-side.
- Existing encrypted secrets are preserved or safely migrated.
- Data Protection key rings are not mistaken for API key files.
- Secrets are not exposed through logs, command lines, cookies, or release packages.

## 57. Credential Lifecycle

- API keys are created through Meilisearch.
- Existing valid keys are reused.
- Missing keys are reconciled safely.
- Expired keys are recovered.
- Expiring keys are rotated before expiration.
- Rotation is safe across restarts.
- Concurrent provisioning does not create uncontrolled duplicates.
- Invalid master keys do not trigger automatic replacement.
- Credential status can be inspected without exposing secrets.

## 58. Setup

- Fresh Windows setup works.
- Fresh Linux setup works.
- Initial administrator provisioning works.
- Development setup works.
- Setup is idempotent.
- Existing data is preserved.
- Windows Service starts noninteractively.
- systemd starts noninteractively.
- Credentials survive restart.

## 59. Application

- Keyword search works.
- Hybrid search works.
- Parent document indexing works.
- Chunk indexing works.
- Document deletion works.
- Index initialization works.
- Category synchronization works.
- Background crawling works without an administrator session.
- Administrative operations require authorization.
- Systemic authentication failures are clearly reported.
- Startup failures shut down cleanly.

## 60. Documentation

- README reflects the new architecture.
- Windows installation instructions are correct.
- Linux installation instructions are correct.
- Development instructions are correct.
- Administrator provisioning is documented.
- Key management and rotation are documented.
- Recovery and migration are documented.
- English and Japanese content is consistent.
- All referenced commands exist.

---

# Phase 18 — Required Completion Report

Provide a detailed implementation report.

## A. Root Cause Analysis

Explain the previous authentication and credential-management defects.

Identify each incompatible or obsolete assumption.

## B. Final Credential Architecture

Describe:

- Master Key.
- Search API Key.
- Indexing API Key.
- Admin API Key.
- Internal Maintenance Credential.
- Credential storage.
- Client factory.
- Authorization boundaries.

## C. API Key Policies

Provide the actual Meilisearch actions, indexes, and expiration policies for each role.

Explain why each permission is required.

## D. Lifecycle Implementation

Describe:

- Initial provisioning.
- Existing key reuse.
- Expiration checks.
- Rotation.
- Recovery.
- Concurrent startup handling.

## E. Setup Refactoring

Describe changes to:

- Shared CLI.
- Windows scripts.
- Linux scripts.
- Development setup.
- Windows Service.
- systemd.

## F. Existing Installation Migration

Describe supported legacy states, migrations, backup requirements, and recovery procedures.

## G. Files Changed

List important created and modified files.

## H. Documentation

List README and documentation pages updated in English and Japanese.

## I. Tests

Report actual executed tests and results.

Separate:

- Windows.
- Linux.
- Unit.
- Integration.
- Migration.
- Packaging.
- Security.

Do not claim unexecuted tests passed.

## J. Security Review

Explain:

- Secret exposure boundaries.
- Privilege separation.
- Browser isolation.
- Background service credentials.
- Key rotation behavior.
- Remaining risks.

## K. Remaining Work

Identify incomplete acceptance criteria and unsupported scenarios.

---

# Final Instructions

Refactor Lantern's Meilisearch authentication and credential architecture comprehensively.

Treat this as an architectural correction, not a narrow bug fix.

Replace the single-key or shared-privilege assumptions with a clear separation between Master, Search, Indexing, Admin, and any necessary Internal Maintenance credentials.

Use the official Meilisearch Keys API for API key provisioning.

Implement secure key reuse, validation, expiration monitoring, controlled rotation, and recovery.

Use shared .NET services for the credential lifecycle and role-aware client creation.

Keep Windows and Linux secret protection platform-specific without duplicating API key management logic.

Do not confuse ASP.NET Core Data Protection key-ring XML with Meilisearch credentials.

Do not expose privileged Meilisearch keys through Lantern administrator sessions or browser state.

Use privileged Admin credentials only for authorized server-side operations.

Keep background crawlers and maintenance independent of interactive administrator sessions.

Refactor setup scripts, startup, service integration, configuration, release packaging, and existing installation migration.

Rewrite README and all relevant English/Japanese documentation to reflect the implemented architecture.

Test with real authenticated Meilisearch on Windows and Linux.

Preserve existing administrator accounts, databases, search indexes, crawler configurations, and credentials throughout migration.

Do not invent successful test results or claim security properties that have not been verified.

**The primary success condition is that Lantern has one consistent, least-privilege, cross-platform Meilisearch credential architecture, with secure first-run provisioning, reliable unattended operation, controlled API key rotation, correctly authorized administrative operations, safe migration, and documentation that accurately describes every supported installation and recovery workflow.**
