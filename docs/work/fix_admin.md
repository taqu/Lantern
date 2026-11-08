# Lantern — Investigate and Fix Development Administrator Login Failure

## Objective

Investigate and fix an administrator authentication failure in Lantern's development environment.

The developer executed:

```powershell
.\scripts\setup-dev.ps1
```

The script displayed development administrator credentials:

```text
Development administrator credentials
  Username : admin
  Password : <generated-password>
```

However, attempting to sign in through the Lantern web interface produces:

```text
administrator login is failed.
```

The administrator credentials generated or configured during development setup do not appear to authenticate successfully.

**Your task is to trace the complete development administrator provisioning and authentication workflow, identify the actual failure, and implement a reliable fix.**

Do not assume that the password is incorrect.

Do not delete the development database or regenerate credentials as the first troubleshooting step.

Do not expose administrator passwords, hashes, or authentication secrets in diagnostic logs.

---

## 1. Inspect the Existing Implementation

Before changing code, inspect:

- `scripts/setup-dev.ps1`
- The development configuration files
- `Properties/launchSettings.json`
- `.vscode/launch.json`, if present
- `Program.cs`
- Application startup and initialization services
- Administrator provisioning services
- Administrator persistence models
- SQLite database configuration
- Authentication configuration
- Password hashing and verification
- Login endpoints or Blazor components
- Cookie authentication configuration
- Authorization policies
- Existing authentication tests

Also inspect any existing commands or services responsible for:

- Creating an administrator.
- Checking whether an administrator already exists.
- Updating administrator credentials.
- Initializing development databases.
- Loading development secrets.
- Restoring persisted configuration.

The repository is the source of truth.

Do not assume the setup script and application use the same credential storage mechanism.

## 2. Reproduce the Failure

Reproduce the issue using the supported development workflow.

Expected sequence:

```text
Run setup-dev.ps1
        |
        v
Development configuration initialized
        |
        v
Administrator credentials provisioned
        |
        v
Launch Lantern through IDE
        |
        v
Open administrator login page
        |
        v
Submit credentials
        |
        v
Authentication succeeds
```

Determine exactly where this sequence fails.

### Required investigation

Identify:

1. Where the username is stored.
2. Where the password or password hash is stored.
3. Whether the setup script creates an actual administrator record.
4. Whether provisioning is deferred until application startup.
5. Whether environment variables are required.
6. Whether the IDE supplies those environment variables.
7. Which SQLite database is opened.
8. Whether the administrator record exists.
9. Whether password verification succeeds.
10. Whether authentication cookies are issued.
11. Whether authorization succeeds after authentication.

Do not modify credentials until the failure point has been identified.

## 3. Trace Credential Provisioning

Inspect how `setup-dev.ps1` handles administrator credentials.

Determine whether it:

- Generates a password.
- Prompts for a password.
- Stores credentials in environment variables.
- Writes ASP.NET Core User Secrets.
- Writes a development configuration file.
- Directly initializes a SQLite administrator record.
- Invokes an application initialization command.
- Merely prints credentials without persisting them.

### Critical requirement

**Displaying a username and password is not proof that the administrator account was successfully provisioned.**

Verify that the generated credentials are actually registered with the authentication system.

### Setup success criteria

The script must not report successful administrator provisioning unless the relevant initialization operation has completed successfully.

If provisioning is deferred until first application startup, the script must explicitly distinguish:

```text
Credentials configured for initial provisioning
```

from:

```text
Administrator account successfully created
```

Do not claim successful account creation before it occurs.

## 4. Investigate Development Configuration Loading

Inspect configuration precedence.

Relevant configuration sources may include:

```text
appsettings.json
appsettings.Development.json
Environment Variables
ASP.NET Core User Secrets
Command-Line Arguments
Development Setup Files
```

Determine the actual precedence used by Lantern.

### Verify

- `ASPNETCORE_ENVIRONMENT` is set to `Development`.
- The intended development configuration is loaded.
- The configured data directory is correct.
- Administrator provisioning settings are available at the correct stage.
- Settings are not accidentally overridden.
- The setup script and application use consistent configuration keys.

### IDE startup

Pay particular attention to the difference between:

```text
setup-dev.ps1 execution process
```

and:

```text
Visual Studio F5 process
```

PowerShell environment variables created in one process do not automatically persist into unrelated future IDE processes.

If administrator provisioning depends on environment variables that exist only during setup script execution, determine whether this is the root cause.

### Required behavior

The development workflow must not depend on undocumented environment variable propagation between separate processes.

## 5. Verify SQLite Database Identity

Inspect the actual development database path.

For example:

```text
<RepositoryRoot>/.lantern-dev/database/
```

The exact path must be determined from the implementation.

### Important

The setup script and Lantern application must use the same intended development database.

Investigate possible differences caused by:

- Current working directory.
- Application content root.
- Visual Studio launch configuration.
- Relative database paths.
- Environment-specific configuration.
- Multiple development data directories.
- Previous initialization.
- Windows Service configuration.

### Required verification

Record the effective database path used by:

1. Development setup.
2. IDE startup.
3. Administrator repository.
4. Authentication service.

Compare the canonical paths.

Do not log database credentials or other secrets.

### Existing database

If an administrator record already exists, determine whether the provisioning process intentionally skips account creation.

Do not assume newly generated credentials replace an existing administrator password.

## 6. Investigate Existing Administrator State

Inspect administrator persistence.

Determine:

- Whether an administrator account exists.
- Whether its normalized username matches the configured username.
- Whether its password hash is populated.
- Whether the account is enabled.
- Whether multiple administrator records exist unexpectedly.
- Whether the account was created by an earlier development run.

### Existing account handling

If the database already contains an administrator, the setup script must not generate and display a new password while silently leaving the old password unchanged.

Choose a clear, consistent behavior.

Recommended options:

**Initial provisioning**

Create an administrator only when the account does not exist.

**Existing administrator**

Preserve the account and inform the developer that provisioning was skipped.

**Explicit credential reset**

Provide a separate authenticated or locally authorized development reset operation.

Do not automatically overwrite existing credentials during every setup execution.

## 7. Audit Password Hashing and Verification

Inspect the password hashing implementation.

Verify:

- A supported password hashing mechanism is used.
- Passwords are not stored in plaintext.
- Password hashing and verification use compatible formats.
- Password normalization is consistent.
- Input is not accidentally truncated.
- No extra newline characters are included.
- No unexpected whitespace is appended.
- Password verification handles existing stored hashes correctly.

### PowerShell considerations

Inspect whether the script introduces:

- Trailing newlines.
- Quoting problems.
- Encoding differences.
- Incorrect environment variable values.
- Unexpected string transformations.

### .NET considerations

Inspect whether application code:

- Hashes the password twice.
- Compares plaintext with a hash.
- Uses inconsistent password hashing options.
- Uses incorrect hash serialization.
- Accidentally reads a different credential field.

Do not replace the password hashing implementation without identifying a genuine defect.

Preserve compatibility with existing valid administrator records.

## 8. Audit the Login Flow

Trace the login request through the application.

Conceptually:

```text
Login Form
    |
    v
Authentication Endpoint / Handler
    |
    v
Administrator Lookup
    |
    v
Password Verification
    |
    v
Authentication Ticket
    |
    v
Cookie Issuance
    |
    v
Authorized Administration Page
```

Determine whether the failure occurs during:

1. Input validation.
2. Administrator lookup.
3. Password verification.
4. Account state validation.
5. Authentication ticket creation.
6. Cookie issuance.
7. Cookie transmission.
8. Cookie validation.
9. Authorization.

### Important

Do not treat every authentication or authorization failure as an incorrect password.

Inspect the precise failure location.

### Cookie authentication

Verify:

- Authentication scheme.
- Cookie registration.
- Login endpoint behavior.
- `HttpOnly`.
- `SameSite`.
- `Secure`.
- Development HTTP compatibility.
- Cookie expiration.
- Correct redirect behavior.

If development uses HTTP on loopback, verify that cookie security configuration is consistent with the intended development workflow.

Do not weaken production cookie security to fix local debugging.

### Blazor Interactive Server

If login is implemented through an interactive component, verify that authentication state and cookie issuance use a supported ASP.NET Core pattern.

Cookie sign-in must occur in a valid HTTP response context.

Do not assume that an established Blazor interactive circuit can always issue new authentication cookies.

## 9. Improve Authentication Diagnostics

Add useful development diagnostics.

### Requirements

- Log authentication subsystem failures.
- Identify whether provisioning occurred.
- Identify the development database location.
- Log whether administrator lookup succeeded, where safe.
- Distinguish password verification failure from cookie or authorization failure in protected diagnostics.
- Avoid logging sensitive values.

### Never log

- Plaintext passwords.
- Generated passwords.
- Password hashes.
- Authentication cookies.
- Session tokens.
- Secret keys.
- Complete authentication configuration objects.

### User-facing behavior

Keep login failure messages generic.

Do not reveal whether a username exists or whether a password is incorrect.

Detailed diagnostics belong in protected server-side logs.

## 10. Fix Development Administrator Provisioning

Implement the smallest reliable correction based on the identified root cause.

### Preferred behavior

The development setup should:

1. Resolve the intended development data directory.
2. Initialize development configuration.
3. Initialize or validate the development database.
4. Determine whether an administrator exists.
5. Provision the administrator through the application's established security service when required.
6. Verify provisioning succeeded.
7. Report the result accurately.

### Important

Do not duplicate password hashing logic in PowerShell.

Prefer invoking an application-level provisioning operation or reusing the existing credential management service.

### New development installation

Provide a clear, secure one-time administrator initialization workflow.

### Existing development installation

Do not silently overwrite the existing administrator.

If credentials must be reset, require an explicit action.

### Noninteractive IDE startup

Once the account has been provisioned, Visual Studio F5 must work without repeatedly supplying administrator credentials through environment variables.

## 11. Provide a Safe Development Credential Reset

If no supported credential recovery mechanism exists, implement a narrowly scoped development reset command.

Suggested invocation:

```powershell
.\scripts\setup-dev.ps1 -ResetAdmin
```

This is illustrative; adapt to existing script conventions.

### Requirements

- Explicit opt-in.
- Development environment only.
- Canonical database path validation.
- Protection against production database access.
- Reuse of the existing password hashing implementation.
- No database deletion.
- No modification of crawler or search data.
- No modification of unrelated administrator accounts.
- Clear success and failure messages.

### Security

Do not implement an unauthenticated web-based password reset endpoint.

Do not allow a development reset command to operate on production databases.

Do not introduce a hardcoded fallback password.

Do not print an existing password.

If a generated temporary password must be displayed, show it only in the authorized interactive setup context and avoid writing it to logs.

## 12. Preserve Existing Development Data

Do not solve the issue using:

```text
Delete .lantern-dev
Delete SQLite database
Drop administrator tables
Recreate database
Reset all application settings
```

These are not acceptable default fixes.

### Requirements

- Preserve crawler definitions.
- Preserve search categories.
- Preserve job history.
- Preserve schedules.
- Preserve indexed documents.
- Preserve Meilisearch state.

Credential repair must be isolated from unrelated application data.

## 13. Automated Tests

Add targeted regression tests.

### Provisioning tests

1. Fresh development database.
2. First administrator creation.
3. Successful password verification.
4. Repeated setup execution.
5. Existing administrator preservation.
6. Explicit password reset.
7. Invalid password input.
8. Missing configuration.
9. Incorrect database path.
10. Production database protection.

### Authentication tests

1. Valid administrator credentials.
2. Invalid password.
3. Unknown username.
4. Disabled account.
5. Correct password hash verification.
6. Successful cookie issuance.
7. Authorized administration access.
8. Invalid session.
9. Logout.
10. Development HTTP behavior.

### Integration tests

Use real SQLite.

Test the complete provisioning and login flow through the actual application authentication services.

Do not rely exclusively on mocked repositories.

### IDE integration

Verify:

1. Run `setup-dev.ps1`.
2. Start Lantern using Visual Studio F5.
3. Open the login page.
4. Authenticate using the provisioned development administrator.
5. Access the administrator dashboard.
6. Restart Lantern.
7. Authenticate again without rerunning setup.

### Security regression

Verify that:

- Production accounts are unaffected.
- No plaintext password is persisted.
- Credential reset requires explicit authorization.
- Authentication failures do not leak account existence.
- Existing administrator sessions follow the documented behavior after password reset.

## 14. Documentation Updates

Update the development documentation.

Relevant files may include:

```text
docs/development.md
docs/en/development.md
docs/ja/development.md
README.md
```

Follow the existing repository documentation structure.

### Document

- How development administrator provisioning works.
- Where the development database is stored.
- Whether credentials are generated or entered.
- Whether setup must be run once.
- What happens when an administrator already exists.
- How to reset development credentials safely.
- How to start Lantern with Visual Studio.
- How to sign in to the administrator UI.
- How to troubleshoot login failures.

### Important

Do not document a reset command until it actually exists and has been tested.

Do not include real passwords in documentation.

## 15. Acceptance Criteria

The task is complete when:

- The root cause of the failed administrator login is identified.
- Development setup and application startup use the intended configuration.
- The effective SQLite database is consistent.
- First-run administrator provisioning works.
- The provisioned password successfully verifies.
- Login succeeds through the actual Web UI.
- Authentication cookies work.
- Authorized administration pages are accessible.
- Existing administrator credentials are not silently overwritten.
- Repeated setup execution is safe.
- Explicit development credential recovery works if implemented.
- Production authentication remains secure.
- Existing development data is preserved.
- Automated tests pass.
- Developer documentation is accurate.

Do not declare success based only on database insertion or password verification tests.

Verify actual web authentication.

## 16. Required Completion Report

After implementation, provide:

### A. Root cause

Explain exactly why the credentials produced by `setup-dev.ps1` failed to authenticate.

### B. Provisioning flow

Describe how the initial administrator is created and where its credentials are stored.

### C. Configuration and database verification

Report the effective configuration sources and development database path.

Do not reveal secrets.

### D. Authentication flow

Explain which stage failed and how it was corrected.

### E. Files changed

List created and modified files.

### F. Credential recovery

Describe any supported development administrator reset mechanism.

### G. Test results

Report actual commands executed and test outcomes.

Distinguish:

- Passed.
- Failed.
- Skipped.
- Not executed.

### H. IDE verification

Describe the actual Visual Studio login test.

Do not claim successful IDE login unless it was tested.

### I. Remaining limitations

Document unresolved authentication or provisioning issues.

---

## Final Instructions

Investigate the development administrator login failure in Lantern.

Trace the credentials from `scripts/setup-dev.ps1` through configuration, database provisioning, password hashing, login handling, cookie authentication, and authorization.

Determine whether the setup script actually creates the administrator account or merely configures credentials for later initialization.

Verify that setup and IDE startup reference the same development database.

Check for existing administrator accounts that cause new credentials to be ignored.

Correct configuration propagation, provisioning, password verification, or cookie handling defects as necessary.

Preserve existing development data.

Provide an explicit, safe development-only credential reset procedure if required.

Do not weaken production authentication.

Do not log or expose real credentials.

Update developer documentation and add regression tests.

**The primary success condition is that a developer can run `setup-dev.ps1`, start Lantern using Visual Studio F5, sign in successfully using the configured development administrator credentials, restart the application, and continue using the same account without repeating setup or resetting the database.**
