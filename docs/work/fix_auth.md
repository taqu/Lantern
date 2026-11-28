# Lantern — Audit and Fix Initial Administrator Provisioning and Setup Documentation

## Objective

Lantern's installation and development documentation does not clearly explain how users should establish the initial administrator username and password.

This creates a fundamental usability problem: users may be able to start Lantern but have no documented, reliable way to authenticate to the administration interface.

Previous development testing also revealed that credentials displayed by `scripts/setup-dev.ps1` did not necessarily allow successful administrator login.

**Your task is to audit the complete initial administrator provisioning workflow, correct any implementation or configuration defects, and update the documentation so that both end users and developers can reliably create and use their initial administrator account.**

Do not assume that documenting environment variables is sufficient.

The documented procedure must work with the actual application.

---

## 1. Inspect the Existing Implementation

Before making changes, inspect:

- Root `README.md`.
- Release installation documentation.
- Developer setup documentation.
- `scripts/setup-dev.ps1`.
- Release startup scripts.
- First-run initialization code.
- Administrator persistence entities.
- Administrator repositories and services.
- Password hashing and verification.
- Login endpoints and Blazor components.
- Authentication and authorization configuration.
- SQLite initialization and EF Core migrations.
- Development and production configuration.
- Application startup and Windows Service hosting.

Also search for:

```text
LANTERN_ADMIN_USERNAME
LANTERN_ADMIN_PASSWORD
Administrator
AdminUser
PasswordHasher
PasswordHash
InitialAdmin
BootstrapAdmin
EnsureCreated
MigrateAsync
```

Use actual repository names and structures.

Determine exactly how Lantern currently creates its initial administrator.

Do not assume the Phase 12 roadmap was implemented as originally described.

## 2. Identify the Current Administrator Provisioning Behavior

Answer the following questions from the implementation:

1. Does Lantern create an administrator automatically?
2. Is administrator creation performed during first startup?
3. Does it depend on environment variables?
4. Is there a command-line provisioning mechanism?
5. Is there an interactive setup mechanism?
6. Is there a default username?
7. Is there a default password?
8. Where is the administrator record persisted?
9. What happens when the database already contains an administrator?
10. What happens if initial credentials are missing?
11. What happens if initial provisioning fails?
12. Can an administrator password be reset safely?
13. Does the Windows Service startup process support provisioning?
14. Do development and production installations use the same authentication logic?

Document the confirmed current behavior before implementing changes.

## 3. Define a Clear First-Run Administrator Workflow

The intended behavior is:

```text
Fresh Lantern Installation
          |
          v
Initialize Application Data
          |
          v
Initialize SQLite
          |
          v
Check Administrator State
          |
          +-- Administrator Exists
          |        |
          |        v
          |   Preserve Existing Account
          |
          +-- No Administrator Exists
                   |
                   v
          Require Initial Provisioning
                   |
                   v
          Create Administrator Securely
                   |
                   v
          Verify Account Creation
                   |
                   v
          Enable Administrator Login
```

### Requirements

- A fresh installation must have a documented administrator provisioning procedure.
- No undocumented default password may be required.
- No insecure default administrator password may be shipped.
- Provisioning must be performed only through an explicitly authorized local setup mechanism.
- The administrator must be persisted in SQLite.
- Existing administrators must not be silently overwritten.
- Setup failures must be reported accurately.

Do not expose an unrestricted administrator registration endpoint over HTTP.

## 4. Choose the Simplest Supported Provisioning Method

Prefer reusing the existing administrator provisioning service.

If a working local setup mechanism already exists, preserve it and document it.

If the implementation is incomplete, introduce a minimal, secure provisioning command.

The preferred interface is one of:

```powershell
.\scripts\Initialize-Admin.ps1
```

or:

```powershell
.\app\Lantern.Web.exe admin init
```

These are examples, not existing verified commands.

Choose whichever fits the current architecture.

Avoid implementing multiple overlapping provisioning mechanisms.

### Important

The provisioning command must use the same:

- Configuration loading.
- SQLite database path.
- Administrator entity.
- Password hashing implementation.
- Authentication requirements.

as the running Lantern application.

Do not implement password hashing independently in PowerShell.

## 5. Username and Password Requirements

Define and document the actual username and password validation rules.

### Username

- Require a nonempty username.
- Validate its length.
- Normalize consistently with login.
- Reject invalid characters where appropriate.
- Enforce uniqueness.
- Avoid accidental case-sensitivity inconsistencies.

### Password

- Require a strong initial password.
- Preserve the existing password length policy unless a change is justified.
- Never store plaintext passwords.
- Use the established password hashing implementation.
- Do not trim or transform passwords unexpectedly.
- Do not write passwords to logs.
- Do not include passwords in configuration templates.

### Interactive provisioning

If a PowerShell script prompts for a password, prefer secure input mechanisms.

Avoid displaying entered passwords on the console.

Do not pass plaintext passwords through command-line arguments.

Use secure local input and transfer the credential to the provisioning component without unnecessarily exposing it through process listings or persistent files.

Do not invent a hardcoded fallback password.

## 6. Initial Administrator State

Handle these cases explicitly.

### Fresh installation

No administrator exists.

The setup process must provide a reliable way to create one.

### Existing installation

An administrator already exists.

Do not create another account or reset its password silently.

### Partially initialized installation

SQLite exists, but administrator provisioning did not complete.

Allow safe retry without deleting unrelated data.

### Existing database with inaccessible administrator

Provide a controlled recovery mechanism.

Do not automatically reset administrator credentials merely because login fails.

### Multiple administrators

If Lantern supports multiple administrator accounts, preserve the existing account management policy.

Do not redesign the entire identity system for this task.

## 7. Initial Provisioning and Windows Service Operation

Windows Service startup must be noninteractive.

Do not require the Windows Service to prompt for administrator credentials.

### Recommended sequence

1. Extract Lantern.
2. Initialize application configuration.
3. Provision the initial administrator through a local setup command.
4. Install the Windows Service.
5. Start the Windows Service.
6. Open the administration interface.
7. Log in with the provisioned credentials.

If Lantern already supports a different secure first-run sequence, adapt the documentation to that implementation.

### Important

Do not store the administrator password in the Windows Service command line.

Do not require persistent administrator password environment variables.

The password should be needed during provisioning and login, not on every application startup.

## 8. Development Administrator Provisioning

Review:

```powershell
.\scripts\setup-dev.ps1
```

Ensure the development workflow is correct.

### Requirements

- Development uses an isolated SQLite database.
- Setup and IDE startup use the same database.
- Administrator provisioning is verified.
- Generated credentials are not silently ignored.
- Existing credentials are not silently overwritten.
- F5 debugging does not require repeating initial provisioning.
- The development setup process reports accurately whether provisioning occurred.

### Existing development account

If the administrator already exists, do not generate a new password and imply that it is valid.

Instead, clearly report that the existing account was preserved.

If password reset is supported, provide an explicit development-only option.

### Production isolation

Development credential reset mechanisms must not operate on production databases.

Do not weaken production authentication to simplify debugging.

## 9. Credential Recovery

Investigate whether an administrator password recovery mechanism already exists.

If not, implement a minimal, secure local recovery command where appropriate.

### Requirements

- Require explicit local authorization.
- Validate the intended Lantern data directory.
- Identify the correct administrator account.
- Reuse the existing password hashing service.
- Require explicit reset confirmation.
- Preserve unrelated application data.
- Prevent remote unauthenticated resets.
- Avoid logging the new password.

### Windows Service

Document whether the service must be stopped before modifying administrator credentials.

Prevent concurrent database modification problems.

### Data preservation

Password recovery must not delete:

- Crawler configurations.
- Search categories.
- Job history.
- Schedules.
- SQLite database.
- Meilisearch indexes.

## 10. Audit Authentication End-to-End

Verify the complete authentication flow:

```text
Administrator Provisioning
          |
          v
SQLite Administrator Record
          |
          v
Login Form
          |
          v
Username Lookup
          |
          v
Password Verification
          |
          v
Authentication Cookie
          |
          v
Authorized Administrator Page
```

### Required verification

- The account is created in the expected SQLite database.
- The username is stored and looked up consistently.
- The password hash verifies.
- The account is enabled.
- Authentication cookies are issued correctly.
- The browser sends the cookie.
- Authorization recognizes the authenticated administrator.

Do not declare provisioning successful merely because a database record was inserted.

Verify actual login.

## 11. Improve Setup Diagnostics

Provide clear messages for:

- Administrator successfully created.
- Administrator already exists.
- Administrator provisioning skipped.
- Password validation failure.
- Database initialization failure.
- Configuration mismatch.
- Incorrect database path.
- Credential reset completed.
- Credential reset rejected.

### Security

Do not log:

- Plaintext passwords.
- Password hashes.
- Authentication cookies.
- Session tokens.
- Secret keys.

The setup command should not print sensitive credentials unnecessarily.

Authentication error messages displayed through the web interface must remain generic.

## 12. Update Release Installation Documentation

Review the actual release layout and scripts.

Update the release installation guide to explain:

1. When the initial administrator is created.
2. How the administrator username is chosen.
3. How the password is entered.
4. What command is used for provisioning.
5. Whether the application must be running.
6. Where the credentials are persisted.
7. How to start Lantern after provisioning.
8. How to log in.
9. What happens on subsequent launches.
10. How to recover administrator access.

### Requirements

Use only commands implemented and validated in the repository.

Do not reference nonexistent initialization scripts.

Do not instruct users to regenerate credentials during every startup.

Do not require the administrator password to be stored in plaintext configuration files.

## 13. Update README Quick Start

Update the root README Quick Start.

It must explicitly cover initial administrator provisioning.

The final documentation should follow the actual supported workflow.

Conceptually:

```markdown
## Quick Start

1. Extract the Lantern Windows x64 release.
2. Run the supported initialization or startup command.
3. Complete initial administrator provisioning.
4. Start Lantern if it is not already running.
5. Open the application URL.
6. Sign in with the administrator credentials created during setup.
```

This is a conceptual example.

Replace it with exact, tested commands and paths.

### Important

Do not claim that the administrator is created automatically unless the implementation does so.

Do not claim an interactive password prompt exists unless it is implemented.

Keep Quick Start short, but do not omit essential authentication steps.

Link to detailed installation and administrator recovery documentation.

## 14. Update Developer Documentation

Update the developer guide.

Include:

- Development administrator initialization.
- Development database location.
- Development password configuration.
- Visual Studio F5 startup.
- VS Code debugging.
- Development account preservation.
- Explicit development password reset.
- Troubleshooting failed administrator login.

Clearly distinguish development setup from release installation.

Do not instruct developers to delete the database to reset credentials.

## 15. Automated Tests

Add regression tests using real SQLite.

### Provisioning tests

1. Fresh database without administrators.
2. Successful initial administrator creation.
3. Username uniqueness.
4. Password validation.
5. Password hashing.
6. Correct password verification.
7. Repeated provisioning attempts.
8. Existing administrator preservation.
9. Partial initialization recovery.
10. Invalid database configuration.

### Authentication integration tests

1. Login with provisioned credentials.
2. Login with invalid credentials.
3. Disabled administrator.
4. Cookie issuance.
5. Protected administration access.
6. Logout.
7. Session expiration.
8. Application restart followed by successful login.

### Recovery tests

1. Explicit password reset.
2. Correct administrator selection.
3. Incorrect authorization.
4. Production database protection.
5. Existing crawler data preservation.
6. Existing category data preservation.
7. Login with the new password.
8. Rejection of the previous password after reset.

### Development tests

1. Run `setup-dev.ps1`.
2. Launch Lantern in Development mode.
3. Verify the administrator account exists.
4. Log in through the actual authentication endpoint.
5. Restart Lantern.
6. Log in again without reprovisioning.

### Release tests

1. Extract the release ZIP.
2. Initialize a fresh data directory.
3. Provision the initial administrator.
4. Start Lantern.
5. Sign in successfully.
6. Restart Lantern.
7. Verify the administrator is preserved.

Do not rely exclusively on mocked authentication services.

## 16. Acceptance Criteria

The task is complete when:

- The initial administrator provisioning mechanism is identified and documented.
- Fresh installations can create an administrator securely.
- The username and password requirements are documented.
- No default insecure password is shipped.
- Administrator credentials are stored securely.
- The provisioning command uses the correct SQLite database.
- Existing administrators are preserved.
- Partial initialization is recoverable.
- Development setup produces valid administrator credentials.
- Visual Studio F5 login works.
- Release installation login works.
- Credentials remain valid after restart.
- Secure local credential recovery is available or the existing supported recovery process is clearly documented.
- README Quick Start explains initial administrator setup.
- Installation documentation contains exact working commands.
- Developer documentation is updated.
- Automated tests pass.

Do not claim successful end-to-end provisioning without verifying actual login.

## 17. Required Completion Report

Provide:

### A. Current behavior

Explain how administrator provisioning worked before the changes.

### B. Identified problems

Describe missing documentation and implementation defects.

### C. Final provisioning workflow

Explain the verified procedure for:

- Fresh Windows installations.
- Existing installations.
- Development environments.
- Administrator password recovery.

### D. Files changed

List modified scripts, application code, tests, and documentation.

### E. README Quick Start

Show the final Quick Start section.

### F. Authentication verification

Report actual login test results.

### G. Security

Explain password hashing, credential handling, and protection against unauthorized administrator creation.

### H. Build and tests

List commands actually executed and their results.

### I. Remaining limitations

Identify unsupported provisioning or recovery scenarios.

Do not report unexecuted tests as successful.

---

## Final Instructions

Audit and correct Lantern's initial administrator provisioning and documentation.

Determine exactly how the application expects the first administrator username and password to be established.

Ensure a fresh installation has a secure, reliable provisioning mechanism.

Reuse the existing administrator persistence and password hashing implementation.

Do not create insecure default credentials.

Do not rely on undocumented environment variable propagation.

Do not regenerate administrator passwords during ordinary startup.

Preserve existing administrator accounts and unrelated application data.

Provide safe, explicit administrator credential recovery where appropriate.

Verify that provisioned credentials allow successful authentication through the actual Lantern web interface.

Update README Quick Start, release installation instructions, and developer documentation with exact tested procedures.

**The primary success condition is that an administrator installing Lantern for the first time can follow the documentation, securely establish a username and password, start Lantern, sign in successfully, and continue using the same credentials after restarting the application—without guessing how initial authentication works.**
