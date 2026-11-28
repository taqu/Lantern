# Lantern — Phase 11 Implementation Instructions
## Continuous Integration, Quality Assurance, and Reproducible Windows Builds

You are implementing **Phase 11: CI and Quality Assurance** of the Lantern project.

Lantern is a lightweight, self-hosted full-text search platform primarily targeting Windows x64 and intranet environments.

The application uses:

- .NET 10 and ASP.NET Core.
- Blazor Web App with Interactive Server.
- Meilisearch Community Edition.
- SQLite with Entity Framework Core.
- LlamaPure and llama.cpp.
- IBM Granite Embedding 97M Multilingual R2.
- Local REST-based embedding and hybrid search.
- Document extraction and structure-aware chunking.
- Incremental file and web crawlers.
- Persistent crawler job management and scheduling.
- Administrator-managed search categories.
- Security and recovery infrastructure.

Phases 0–10 have established the application functionality and security foundations.

**Your task is to establish reliable, automated, reproducible build and test workflows using GitHub Actions, with Windows x64 as the primary target.**

Do not implement Phase 12 Windows distribution packaging or public release automation.

---

## 1. Review the Existing Repository

Before modifying anything:

1. Read `roadmap.md` and existing architecture documentation.
2. Inspect the .NET solution, project files, and build configuration.
3. Review all existing test projects.
4. Review `.github/workflows/`.
5. Review the current Meilisearch integration test infrastructure.
6. Review SQLite integration tests and migrations.
7. Review LlamaPure native dependencies.
8. Review the Granite GGUF model loading mechanism.
9. Review document extraction fixtures.
10. Review file and web crawler integration tests.
11. Review Phase 7 job execution and recovery tests.
12. Review Phase 9 category synchronization tests.
13. Review Phase 10 security and fault-injection tests.
14. Review existing dependency and license documentation.
15. Execute the current local build and tests.

The actual repository is the source of truth.

Do not assume that every previously planned test already exists.

Identify missing coverage and distinguish it from failing tests.

Preserve completed functionality.

Avoid unrelated architectural changes.

## 2. Phase Objectives

Implement:

1. GitHub Actions CI workflows.
2. Automated .NET 10 restore and build.
3. Automated unit tests.
4. Automated SQLite integration tests.
5. Real Meilisearch integration tests.
6. Document extraction regression tests.
7. File and web crawler integration tests.
8. Persistent job and recovery tests.
9. Hybrid search integration tests.
10. Security regression tests.
11. Formatting and static analysis checks.
12. Dependency vulnerability auditing.
13. Third-party license validation.
14. Windows x64 publishing validation.
15. Test and build artifact collection.
16. CI documentation and troubleshooting guidance.

The CI pipeline must run without Docker.

The application must remain independent of cloud services during normal operation.

Using GitHub Actions for development automation does not introduce a cloud runtime requirement for Lantern.

## 3. CI Design Principles

Follow these principles:

1. Prefer deterministic tests.
2. Prefer Windows runners for Windows-specific behavior.
3. Keep all required CI checks automatic.
4. Separate fast tests from expensive integration tests.
5. Avoid unnecessary external dependencies.
6. Avoid hiding failures.
7. Avoid automatically downloading unverified native binaries.
8. Cache dependencies where safe.
9. Keep workflow permissions minimal.
10. Ensure every artifact is associated with a source commit.

### Critical requirement

**A workflow must never report success by silently skipping tests that it claims to validate.**

If a required dependency is unavailable, the relevant required CI job must fail.

Optional expensive tests may run separately, but their absence must be explicit.

## 4. GitHub Actions Workflow Structure

Use GitHub Actions.

Recommended logical workflow structure:

```text
.github/workflows/
    |
    +-- ci.yml
    |
    +-- integration.yml
    |
    +-- native-embedding.yml
    |
    +-- security.yml
    |
    +-- publish-validation.yml
```

This is illustrative.

Do not split workflows unnecessarily.

A smaller number of well-organized workflows is acceptable.

### Trigger policy

Run required checks on:

- Pull requests targeting the primary development branch.
- Pushes to the primary development branch.
- Manual workflow dispatch where useful.

Avoid automatically executing expensive model inference tests for every trivial documentation change unless justified.

### Concurrency

Use GitHub Actions concurrency controls where appropriate.

Cancel superseded pull-request runs to avoid wasting runner resources.

Do not accidentally cancel release validation or other intentionally independent executions.

## 5. .NET SDK and Build Environment

Use .NET 10.

Prefer the official `actions/setup-dotnet` action.

### Requirements

- Use a supported GitHub Actions runner.
- Install or select the required .NET SDK.
- Respect `global.json` if present.
- Preserve deterministic SDK selection.
- Restore dependencies.
- Build in Release configuration.
- Capture build failures.
- Avoid relying on undocumented runner-installed SDK versions.

### Suggested commands

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
```

Adapt commands to the repository.

If the solution uses a specific SDK feature band, preserve it.

### Reproducibility

Prefer:

- Explicit package versions.
- Consistent central package management where already used.
- NuGet lock files where appropriate.
- Stable native dependency versions.
- Deterministic build properties.

Do not update dependencies automatically merely because newer versions exist.

## 6. Windows-First CI

Windows x64 is Lantern's primary deployment target.

Use Windows runners for required validation of:

- Native LlamaPure dependencies.
- Windows filesystem behavior.
- Windows path normalization.
- UNC path handling.
- Symbolic links and reparse points.
- Native Meilisearch execution.
- Self-contained publishing.

### Requirements

- Do not require Docker.
- Do not require a Java runtime.
- Do not require a Node.js build step for the application.
- Avoid requiring administrator privileges for normal tests.
- Clearly document tests that require special Windows capabilities.

### Optional cross-platform validation

Linux or other platforms may be used for portable unit tests.

However, do not treat a successful Linux build as proof that the Windows distribution works.

Native Windows functionality must be tested on Windows.

## 7. Test Classification

Classify tests into clear categories.

Suggested categories:

```text
Unit
Integration
Database
Meilisearch
Extraction
Embedding
Crawler
Security
Recovery
EndToEnd
```

Use xUnit traits or existing test categorization conventions.

### Required behavior

- Fast unit tests run on every pull request.
- SQLite integration tests run on every pull request.
- Required Meilisearch integration tests run automatically.
- Extraction regression tests run automatically.
- Security regression tests run automatically.
- Native embedding tests run in a clearly defined workflow.
- Expensive end-to-end tests use an explicit execution policy.

Do not create arbitrary test categories that make important tests easy to overlook.

### Test isolation

Each integration test must use isolated state.

Avoid dependence on test execution order.

Avoid global mutable state shared across tests unless properly synchronized.

Do not reuse production indexes, databases, or application data directories.

## 8. Automated Unit Tests

Run all existing unit tests.

Coverage should include:

- Document identity.
- URL normalization.
- File path normalization.
- Text encoding.
- Document metadata normalization.
- Chunking.
- Search request validation.
- Search filters.
- Search categories.
- Job state transitions.
- Scheduler calculations.
- Crawler configuration validation.
- Resource limits.
- Security boundary validation.

### Requirements

- Tests must be repeatable.
- Tests must not require external network access.
- Tests must not rely on wall-clock timing.
- Tests must not require administrator privileges.
- Tests must not modify repository files unexpectedly.

Use `TimeProvider` or equivalent controlled clocks where needed.

## 9. SQLite Integration Tests

Use real SQLite databases.

Do not rely exclusively on EF Core's in-memory provider.

### Required coverage

1. Database initialization.
2. EF Core migrations.
3. Application settings persistence.
4. Crawler configuration persistence.
5. Crawl-state persistence.
6. File fingerprint synchronization.
7. Web URL state.
8. Job persistence.
9. Job state transitions.
10. Schedule persistence.
11. Category persistence.
12. Crawler-category relationships.
13. Category synchronization state.
14. Restart recovery.
15. Database integrity constraints.

### Test isolation

Use temporary SQLite databases.

Each test or test collection should own its database lifecycle.

Clean up temporary resources.

Avoid concurrent access patterns that accidentally invalidate test assumptions.

### Migration testing

Test migrations against both:

- A newly created database.
- A representative database created using an older schema.

Do not validate only fresh database creation.

## 10. Native Meilisearch Integration Tests

Run integration tests against a real Meilisearch Community Edition process.

**Docker must not be required.**

### Native binary setup

Use a verified Meilisearch Windows x64 executable.

Requirements:

- Use a pinned Meilisearch version.
- Download only from a trusted official distribution source when downloading is necessary.
- Verify the downloaded artifact's SHA-256 checksum.
- Use the Community Edition distribution intended for Lantern.
- Keep the binary outside the repository unless existing project policy requires otherwise.
- Avoid silently substituting Enterprise Edition or another distribution.

### Process lifecycle

The test harness should:

1. Allocate an available loopback port safely.
2. Create an isolated temporary data directory.
3. Start the native Meilisearch process.
4. Wait for verified readiness.
5. Run integration tests.
6. Capture useful diagnostics.
7. Stop the process.
8. Remove temporary resources.

### Failure behavior

If Meilisearch fails to start, the integration job must fail.

Do not silently skip all tests.

If the process exits unexpectedly, capture its exit status and available logs.

### Required test coverage

- Index creation.
- Index settings.
- Document indexing.
- Chunk indexing.
- Search.
- Updating documents.
- Deletion.
- Task completion.
- Task failure handling.
- Index rebuilding.
- Filtering.
- Sorting.
- Category metadata.
- Repeated indexing idempotency.

### Security

Never use a production Meilisearch instance for CI.

Do not expose the CI test instance publicly.

Use isolated credentials and test-only indexes.

## 11. Document Extraction Regression Tests

Run the Phase 4 extraction regression corpus automatically.

### Required formats

- TXT.
- Markdown.
- HTML.
- PDF.
- DOCX.
- XLSX.
- PPTX.
- General text files.
- Source code.
- Configuration files.

Include Japanese and English content.

### Required behavior

Verify:

- Text extraction.
- Metadata extraction.
- Structural boundaries.
- Unicode preservation.
- UTF-8 output.
- Page numbers.
- Slide numbers.
- Worksheet names.
- Source-code line numbers.
- Malformed document handling.
- Unsupported format handling.

### Text encoding

Include fixtures for:

- UTF-8.
- UTF-8 with BOM.
- UTF-16 LE.
- UTF-16 BE.
- Common Japanese legacy encodings where supported.
- Invalid encoding sequences.
- Binary files disguised as text.

Do not allow silent text corruption.

### Fixture integrity

Use deterministic fixture files.

Avoid modifying fixtures during tests.

Do not download arbitrary external documents during every CI run.

## 12. Chunking and Search Integration Tests

Verify the full processing pipeline:

```text
Document Fixture
       |
       v
Extraction
       |
       v
Structured Document
       |
       v
Structure-Aware Chunking
       |
       v
Meilisearch
```

### Required coverage

- Deterministic parent IDs.
- Deterministic chunk IDs.
- Token limits.
- Chunk overlap.
- Source metadata preservation.
- Repeated processing.
- Safe chunk replacement.
- Deleted chunk reconciliation.

### Search verification

Confirm that extracted content can be found through keyword search.

Do not substitute mocked search results for required integration tests.

## 13. LlamaPure and Native Embedding Tests

Lantern uses the LlamaPure library and a local GGUF embedding model.

The development model is:

```text
resources/granite-embedding-97M-multilingual-r2.gguf
```

The model produces 384-dimensional embeddings.

### Native dependency validation

Verify:

- Native llama.cpp library loading.
- Required Windows runtime dependencies.
- Model file loading.
- Model disposal.
- Correct embedding dimensions.
- Finite vector values.
- Unicode input handling.

### Output compatibility

The project author previously validated LlamaPure against the official `llama-embedding` utility.

Preserve any established reference tests or golden vectors.

If the current repository includes validated reference outputs, use them to detect regressions.

Do not regenerate expected vectors from the implementation under test.

### Test inputs

Include:

- Short English text.
- Japanese text.
- Mixed-language text.
- Repeated identical input.
- Empty input.
- Overlong input.

### Model distribution

Avoid downloading the complete GGUF model during every ordinary CI job.

The model may be:

- Available through a controlled CI cache.
- Supplied as a verified test asset.
- Downloaded by a separate explicitly configured workflow.
- Available on an authorized self-hosted runner.

Validate integrity using a known checksum.

Do not assume the model exists on every GitHub-hosted runner.

### Required versus optional tests

Keep lightweight embedding service tests in the normal pipeline.

Run real-model inference in a clearly defined native integration workflow.

Document which workflow is required for release qualification.

Do not claim end-to-end embedding coverage when only mocked inference was executed.

## 14. Hybrid Search Integration Tests

Use real Meilisearch with the local REST embedding server.

### Required coverage

1. REST embedder configuration.
2. Embedding endpoint readiness.
3. Document embedding generation.
4. Chunk indexing.
5. Keyword search.
6. Hybrid search.
7. Semantic ratio behavior.
8. Japanese retrieval.
9. English retrieval.
10. Mixed-language retrieval.
11. Category filtering.
12. Embedding server failure.
13. Recovery after service restoration.

### Resource management

Keep the integration corpus small and deterministic.

Bound model inference concurrency.

Avoid unnecessary repeated model loading.

### Model availability

If real-model tests are part of the required release qualification suite, missing model resources must fail qualification.

Do not silently replace real embedding tests with mock tests.

## 15. File Crawler Integration Tests

Run deterministic file crawler scenarios.

### Required coverage

- Initial crawl.
- Incremental crawl.
- Modified files.
- Deleted files.
- Unchanged file skipping.
- Excluded directories.
- General text and source code.
- UNC path normalization.
- File access failures.
- Incomplete enumeration.
- Cancellation.
- Safe deletion reconciliation.

### Windows behavior

Run Windows-specific tests on Windows.

Where genuine network share testing requires special infrastructure, use an explicit test environment or classify the test separately.

Do not claim full UNC integration coverage based only on path normalization tests.

### Test isolation

Use temporary directories.

Do not crawl arbitrary runner directories.

Do not use real production shares.

## 16. Web Crawler Integration Tests

Use a deterministic local HTTP fixture server.

Do not rely on public websites.

### Required scenarios

- Initial HTML crawling.
- Link discovery.
- URL deduplication.
- Relative URLs.
- Redirects.
- robots.txt.
- Rate limiting.
- HTTP 304 handling.
- ETag handling.
- Last-Modified handling.
- PDF and Office downloads.
- General text downloads.
- Source code downloads.
- Crawl limits.
- Cancellation.
- Incremental updates.
- Safe missing-page reconciliation.

### Security

Use local controlled fixtures to exercise destination validation.

Do not weaken SSRF protections merely to make integration tests work.

Provide explicitly approved test-network settings when necessary.

## 17. Job Execution and Scheduling Tests

Run Phase 7 job management tests.

### Required coverage

- Job submission.
- Job persistence.
- Job claiming.
- Duplicate claim prevention.
- Per-crawler execution exclusion.
- Manual execution.
- Scheduled execution.
- Cancellation.
- Retry.
- Progress persistence.
- History persistence.
- Startup recovery.
- Missed schedule handling.
- Retention.

### Determinism

Use controlled clocks where possible.

Avoid tests that sleep for minutes waiting for schedule execution.

Test scheduler logic independently from actual wall-clock time.

### Browser independence

Verify that jobs continue independently of Blazor circuit lifetime.

Do not require a browser session to remain connected during background execution tests.

## 18. Search Category Tests

Run tests for Phase 9 category management.

### Required coverage

- Category creation.
- Category editing.
- Category ordering.
- Category enabling/disabling.
- Category deletion or archival.
- Many-to-many crawler relationships.
- Existing crawler migration.
- Metadata synchronization.
- Category filtering.
- Keyword category search.
- Hybrid category search.
- Category URL state.
- Concurrent indexing and category changes.

### Embedding preservation

Verify that category-only metadata changes do not unnecessarily regenerate embeddings.

Use actual Meilisearch behavior where required.

Do not assume this property without tests.

### Failure recovery

Test interrupted category synchronization.

Ensure retry or reconciliation restores consistency.

## 19. Security Regression Tests

Automate the security tests introduced during Phase 10.

### Required categories

**Authentication and authorization**

- Unauthorized administration access.
- Invalid credentials.
- Unauthorized configuration changes.
- Administrative endpoint protection.
- CSRF protection.

**Web crawler security**

- SSRF.
- Unsafe redirects.
- DNS rebinding.
- Prohibited IP destinations.
- Unapproved ports.
- Approved intranet targets.
- Resource exhaustion boundaries.

**Filesystem security**

- Path traversal.
- Root boundary escape.
- Symbolic links.
- Junctions.
- UNC access failures.
- Incomplete scan protection.

**Document extraction security**

- Malformed files.
- ZIP expansion limits.
- XML external entities.
- Oversized input.
- Extraction resource limits.

**Search security**

- Unsafe filter input.
- Untrusted HTML.
- Unsafe source URLs.
- Invalid category identifiers.
- Excessive pagination.

**Embedding security**

- Oversized input.
- Queue saturation.
- Native worker failure.
- Listener isolation.

### Failure policy

Security regressions must fail required CI checks.

Do not mark security tests as optional simply because they are inconvenient to execute.

If some tests require a dedicated environment, document that environment and make the corresponding release qualification check mandatory.

## 20. Backup and Recovery Tests

Automate feasible portions of the Phase 10 recovery procedures.

### SQLite

Test:

- Backup creation.
- Restore.
- Schema integrity.
- Configuration preservation.
- Job history preservation.
- Category relationship preservation.

### Meilisearch

Test:

- Snapshot or supported backup creation.
- Restore into isolated storage.
- Index availability.
- Document count and identity consistency.
- Search functionality.

### Cross-system recovery

Where practical, validate a coordinated recovery scenario.

Do not assume SQLite and Meilisearch backups are automatically transactionally consistent.

Test documented reconciliation behavior.

### Failure simulation

Use controlled fault injection.

Do not intentionally corrupt production files or indexes.

## 21. Formatting and Static Analysis

Introduce or validate formatting and static analysis checks.

### Recommended tools

Use built-in .NET tooling where practical:

```powershell
dotnet format --verify-no-changes
```

Use existing Roslyn analyzers and repository configuration.

### Requirements

- Preserve `.editorconfig`.
- Apply consistent formatting.
- Run analyzers during CI.
- Report diagnostics clearly.
- Avoid broad formatting changes unrelated to necessary fixes.

### Warning policy

Define an explicit policy for build warnings.

Treat newly introduced serious warnings as failures.

Avoid hiding warnings globally.

Do not enable `TreatWarningsAsErrors` blindly if it causes unrelated historical warnings to block development without a documented migration plan.

Prefer targeted, justified analyzer configuration.

## 22. Dependency Vulnerability Auditing

Audit direct and transitive NuGet dependencies.

Use .NET's supported package audit capabilities.

### Suggested command

```powershell
dotnet package list --vulnerable --include-transitive
```

Verify command compatibility with the selected .NET SDK.

### Requirements

- Inspect direct dependencies.
- Inspect transitive dependencies.
- Report known vulnerabilities.
- Define severity thresholds for CI failure.
- Treat critical and high-severity unresolved vulnerabilities as release blockers unless explicitly assessed and documented.
- Avoid automatically updating packages without compatibility validation.

### NuGet Audit

Use NuGet's supported restore-time auditing where appropriate.

Ensure the workflow actually detects and evaluates vulnerability reports.

Do not assume a command returning exit code zero necessarily means no vulnerable packages were reported.

Parse or inspect structured results where necessary.

### Network availability

Vulnerability auditing may require access to online advisory data.

If advisory sources are unavailable, report that the audit could not complete.

Do not report a clean security result when the audit was not performed.

## 23. Native Dependency and Supply-Chain Validation

Lantern includes native components.

Review:

- Meilisearch executable.
- llama.cpp native libraries.
- LlamaPure.
- GGUF model resources.
- Document extraction dependencies.
- Other bundled runtime components.

### Requirements

- Record component versions.
- Record source repositories.
- Verify checksums for downloaded binaries.
- Identify applicable licenses.
- Avoid untrusted binary download sources.
- Document native runtime requirements.
- Verify Windows x64 compatibility.

### Binary provenance

Prefer official release artifacts or documented reproducible builds.

Do not silently replace a dependency with an unknown fork.

If the validated local LlamaPure implementation differs from the public repository, document its provenance and version.

## 24. License Compliance Checks

Review third-party license obligations.

### Components

Include:

- Meilisearch Community Edition.
- LlamaPure.
- llama.cpp.
- Granite Embedding model.
- Toxy.
- PDF extraction libraries.
- Office document libraries.
- Other NuGet packages.
- Native transitive dependencies.

### Requirements

- Maintain an up-to-date dependency inventory.
- Record license identifiers.
- Record third-party notices.
- Identify redistributable components.
- Identify license restrictions.
- Flag unknown licenses for manual review.
- Ensure release-blocking license issues fail qualification.

### Model license

Verify the license associated with the actual Granite model artifact.

Do not assume that the model inherits the license of the inference library.

### Automated validation

Automate license inventory generation or validation where practical.

Manual review may remain necessary for ambiguous dependencies.

Do not claim automated license scanning provides definitive legal clearance.

## 25. Build and Test Artifacts

Collect useful artifacts from CI.

Suggested artifacts:

```text
artifacts/
    |
    +-- test-results/
    |
    +-- coverage/
    |
    +-- build-logs/
    |
    +-- security-reports/
    |
    +-- dependency-inventory/
    |
    +-- publish-validation/
```

This structure is illustrative.

### Requirements

- Publish test results.
- Preserve failure diagnostics.
- Publish useful coverage reports.
- Preserve build logs where appropriate.
- Include source commit identifiers.
- Avoid exposing secrets.
- Avoid uploading complete internal document contents.
- Avoid uploading sensitive test databases unnecessarily.

### Artifact retention

Use appropriate retention periods.

Avoid unbounded artifact storage.

Do not upload large GGUF model files as ordinary test artifacts.

## 26. Test Coverage Reporting

Generate code coverage reports where practical.

### Requirements

- Report meaningful coverage.
- Distinguish unit and integration coverage where useful.
- Identify critical untested components.
- Preserve reports as CI artifacts.
- Avoid treating a single coverage percentage as proof of correctness.

### Priority areas

Emphasize coverage for:

- Crawler boundary validation.
- Synchronization state transitions.
- Job claiming and recovery.
- Category synchronization.
- Search validation.
- Authentication and authorization.
- Backup and restoration.

Do not introduce arbitrary high coverage thresholds that encourage meaningless tests.

If thresholds are used, base them on the current baseline and documented goals.

## 27. Windows x64 Publish Validation

Verify that Lantern can be published as a self-contained Windows x64 application.

### Suggested command

```powershell
dotnet publish `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output artifacts/publish/win-x64
```

Adapt the project path and publish options to the repository.

### Required checks

- Publish succeeds.
- Output contains the Lantern executable.
- Required managed assemblies are present.
- Native LlamaPure dependencies are present.
- Configuration files are included appropriately.
- Required static web assets are included.
- No development-only secrets are packaged.
- Application startup is validated where practical.

### Embedding model

The large GGUF model does not necessarily need to be copied into every ordinary CI publish artifact.

However, the release asset policy must be explicit.

If the intended distribution bundles the model, validate the inclusion mechanism.

If the model is provisioned separately, validate model path resolution and startup behavior when it is absent.

### Meilisearch

Phase 11 should validate compatibility with the intended native Windows Meilisearch executable.

Do not implement final bundled process lifecycle management if that belongs to Phase 12.

### Important restriction

This phase validates publishing.

It does not create the final release ZIP, Windows service installer, or public GitHub release.

## 28. Publish Smoke Tests

Where practical, execute the published application in CI.

### Requirements

- Start the published Lantern executable.
- Use isolated temporary configuration and data directories.
- Verify startup.
- Verify a basic HTTP health endpoint.
- Verify SQLite initialization.
- Verify Meilisearch connectivity where configured.
- Verify clean shutdown.
- Capture application logs if startup fails.

### Native dependencies

Validate that required native libraries load successfully.

Do not assume a successful `dotnet publish` proves runtime compatibility.

### Environment isolation

Do not connect the smoke test to production databases or indexes.

Use test-only configuration.

Do not expose the test application externally.

## 29. CI Security

GitHub Actions workflows are part of the software supply chain.

### Required safeguards

- Use minimal workflow permissions.
- Avoid unnecessary write permissions.
- Pin actions to reviewed immutable commit SHAs for production or release-critical workflows.
- Validate third-party actions before adoption.
- Avoid exposing secrets to untrusted pull-request code.
- Avoid executing arbitrary commands from issue or pull-request text.
- Keep tokens out of logs and artifacts.
- Verify external binary checksums.

### Pull requests

Ensure untrusted fork pull requests cannot access privileged deployment credentials.

Do not use `pull_request_target` to execute untrusted repository code with elevated permissions.

### Dependencies

Do not execute unreviewed downloaded scripts merely to install test dependencies.

Prefer verified binaries and standard package managers.

## 30. CI Performance

Keep CI practical for regular development.

### Recommended approach

Separate checks by cost:

| Test group | Intended frequency |
|---|---|
| Build and formatting | Every PR |
| Unit tests | Every PR |
| SQLite tests | Every PR |
| Meilisearch core integration | Every PR |
| Extraction regression | Every PR |
| Security regression | Every PR |
| Real GGUF inference | Release qualification and explicit runs |
| Full hybrid end-to-end | Release qualification and explicit runs |
| Extended recovery tests | Release qualification |
| Windows publish validation | Every PR or release qualification |

These frequencies are recommendations.

Ensure every required release-qualification check is actually enforced before release.

### Important rule

Do not remove meaningful tests merely to reduce CI runtime.

Instead:

- Reuse test infrastructure.
- Cache safe dependencies.
- Avoid repeated model initialization.
- Reduce unnecessary fixture size.
- Separate expensive workflows.
- Keep test resources isolated.

## 31. Release Qualification Policy

Introduce a documented quality gate for Phase 12.

A release candidate may proceed only when the required checks pass.

### Required checks

1. Release build.
2. Unit tests.
3. SQLite integration tests.
4. Meilisearch integration tests.
5. Document extraction regression tests.
6. File crawler tests.
7. Web crawler tests.
8. Job and scheduler tests.
9. Category synchronization tests.
10. Security regression tests.
11. Native embedding tests.
12. Hybrid search tests.
13. Backup and recovery verification.
14. Dependency audit.
15. License review.
16. Windows x64 publish validation.
17. Publish smoke tests.

### Required versus optional checks

Explicitly identify which workflows are required for release qualification.

A workflow that is optional for ordinary pull requests may still be mandatory before release.

Do not label a build release-ready when required qualification workflows have not run.

### Branch protection

Document recommended required status checks.

Repository administrators may need to configure branch protection separately.

Do not claim that repository protection rules were changed unless they were actually changed and verified.

## 32. CI Failure Diagnostics

CI failures must be actionable.

### Requirements

- Identify failing projects and tests.
- Capture relevant Meilisearch logs.
- Capture embedding initialization failures.
- Capture database migration errors.
- Capture extraction fixture failures.
- Capture native dependency loading failures.
- Capture Windows publish failures.

### Log safety

Do not expose:

- API keys.
- Passwords.
- Authentication cookies.
- Full sensitive document content.
- Unnecessary embedding inputs.
- Production filesystem paths.

### Cleanup

Ensure test processes are terminated even when tests fail.

Avoid leaving orphaned Meilisearch or Lantern processes.

Use cleanup steps that run after failures.

## 33. Documentation

Create or update appropriate documentation.

Suggested files:

```text
docs/ci.md
docs/testing.md
docs/dependencies.md
docs/release-qualification.md
```

Adapt names to existing conventions.

### CI documentation

Explain:

- Workflow triggers.
- Required jobs.
- Optional expensive jobs.
- Windows runner requirements.
- Meilisearch setup.
- LlamaPure native dependencies.
- GGUF model provisioning.
- Caching.
- Artifact collection.

### Testing documentation

Explain:

- How to run unit tests.
- How to run SQLite tests.
- How to run Meilisearch tests.
- How to run extraction tests.
- How to run native embedding tests.
- How to run end-to-end hybrid tests.
- How to troubleshoot common failures.

### Release qualification

Explain:

- Required checks.
- Failure policies.
- Model availability requirements.
- Native binary verification.
- License review.
- Known limitations.

## 34. Suggested Workflow Execution

The following is a conceptual structure, not a mandatory YAML implementation.

```text
Pull Request
    |
    +-- Restore / Build
    |
    +-- Formatting / Static Analysis
    |
    +-- Unit Tests
    |
    +-- SQLite Integration Tests
    |
    +-- Native Meilisearch Tests
    |
    +-- Document Extraction Tests
    |
    +-- Crawler Tests
    |
    +-- Security Tests
    |
    +-- Windows Publish Validation
    |
    v
Required CI Status
```

Release qualification adds:

```text
Release Qualification
    |
    +-- All Required CI Checks
    |
    +-- Real GGUF Embedding Tests
    |
    +-- Full Hybrid Search Tests
    |
    +-- Extended Recovery Tests
    |
    +-- Backup / Restore Validation
    |
    +-- Dependency / License Review
    |
    +-- Publish Smoke Tests
    |
    v
Release Candidate Eligible
```

Do not automatically publish a release in Phase 11.

## 35. Out of Scope

Do not implement:

- Final Windows ZIP packaging.
- Windows service installation.
- GUI installers.
- MSI or MSIX installers.
- Public GitHub release creation.
- Release signing infrastructure.
- Automatic production deployment.
- New crawler types.
- New embedding models.
- OCR.
- RAG.
- Chat interfaces.
- Distributed CI workers.
- Distributed crawler execution.
- New application features unrelated to quality assurance.

These belong to Phase 12 or deferred enhancements.

## 36. Acceptance Criteria

Phase 11 is complete only when all required conditions are satisfied.

### Build automation

- GitHub Actions workflows exist.
- .NET 10 builds run automatically.
- Windows x64 is covered.
- Release builds succeed.
- Formatting and static analysis checks run.
- CI works without Docker.

### Automated tests

- Unit tests run automatically.
- SQLite tests run automatically.
- Real Meilisearch tests run automatically.
- Extraction regression tests run automatically.
- File crawler tests run automatically.
- Web crawler tests run automatically.
- Job and scheduling tests run automatically.
- Category synchronization tests run automatically.
- Security regression tests run automatically.

### Embedding and hybrid search

- Native embedding tests are defined.
- Real-model tests can execute using verified GGUF resources.
- Meilisearch REST Embedder integration is tested.
- Hybrid search is validated.
- Missing required model resources fail the relevant qualification job.
- Mock-only tests are not misrepresented as real inference coverage.

### Dependency and supply-chain safety

- Direct dependencies are audited.
- Transitive dependencies are audited.
- Native binary provenance is documented.
- Third-party licenses are reviewed.
- Release-blocking issues are reported.
- External binary checksums are verified.

### Windows publishing

- Self-contained win-x64 publishing succeeds.
- Required native dependencies are validated.
- Published application startup is tested where practical.
- Development secrets are not included.
- Publish outputs are traceable to a source commit.

### CI reliability

- Required test failures block CI success.
- Missing test dependencies are not silently ignored.
- Integration tests use isolated resources.
- Test processes are cleaned up.
- Relevant diagnostics are retained.
- Workflows do not require privileged production credentials.

### Release qualification

- Required release checks are documented.
- Expensive checks have an explicit execution policy.
- No incomplete qualification is reported as successful.
- Phase 12 can consume the validated build and test workflow.

### Documentation

- CI instructions are documented.
- Local test execution is documented.
- Native dependency requirements are documented.
- Release qualification rules are documented.
- Known limitations are identified.

Do not declare Phase 11 complete if required checks have not actually been executed and verified.

## 37. Deliverables

Provide:

1. GitHub Actions CI workflows.
2. Automated .NET 10 build validation.
3. Windows x64 CI configuration.
4. Unit test automation.
5. SQLite integration test automation.
6. Native Meilisearch test infrastructure.
7. Document extraction regression automation.
8. File and web crawler test automation.
9. Job execution and recovery test automation.
10. Category synchronization tests.
11. Native embedding test workflow.
12. Hybrid search integration test workflow.
13. Security regression automation.
14. Formatting and static analysis checks.
15. NuGet vulnerability audit.
16. Native dependency inventory.
17. Third-party license review.
18. Windows x64 publish validation.
19. Publish smoke tests.
20. Test and build artifacts.
21. Release qualification documentation.
22. Updated testing documentation.

## 38. Required Completion Report

At the end of implementation, provide:

### A. Implementation summary

Describe the CI and quality assurance infrastructure implemented.

### B. Files changed

List workflow files, test infrastructure, scripts, and documentation created or modified.

### C. CI architecture

Explain:

- Workflow organization.
- Test classification.
- Windows runner selection.
- Meilisearch provisioning.
- GGUF provisioning.
- Native dependency validation.
- Artifact collection.
- Failure handling.

### D. Build and test commands

List actual commands executed locally and in CI.

### E. Test results

Clearly distinguish:

- Passed tests.
- Failed tests.
- Skipped tests.
- Tests not executed.

Include test counts where available.

### F. GitHub Actions results

Report:

- Workflows triggered.
- Jobs completed.
- Jobs failed.
- Jobs not executed.
- Workflow URLs or run identifiers when available.

Do not claim a GitHub-hosted workflow passed unless an actual run confirms it.

### G. Dependency and license findings

Report:

- Vulnerable dependencies.
- Unknown licenses.
- Native binary verification.
- Model license verification.
- Remaining release blockers.

### H. Windows publish validation

Describe the generated publish output and actual smoke-test results.

### I. Known limitations

Document limitations such as:

- Native model resource availability.
- GitHub-hosted runner constraints.
- UNC share integration requirements.
- Slow full-model testing.
- Platform-specific test coverage.
- Remaining manual release qualification steps.

### J. Remaining work

Identify incomplete acceptance criteria and their impact on Phase 12.

Do not begin Phase 12 automatically.

---

## Final Instructions

Implement Phase 11 directly in the existing Lantern repository.

Inspect the completed implementation from Phases 0–10 before making changes.

Establish GitHub Actions workflows that build and test Lantern automatically on Windows x64.

Use .NET 10 and the existing test infrastructure.

Run native Meilisearch integration tests without Docker.

Preserve and automate PDF, Office, general text, source-code, and Unicode extraction regression tests.

Validate file crawling, web crawling, persistent job execution, category synchronization, and recovery behavior.

Automate security regression checks.

Provide a verified approach for testing LlamaPure and IBM Granite Embedding 97M without repeatedly downloading or loading the large GGUF model during ordinary pull-request checks.

Ensure real-model embedding and hybrid search tests remain mandatory for release qualification.

Audit direct and transitive dependencies and review third-party license obligations.

Validate self-contained Windows x64 publishing and perform isolated startup smoke tests.

Collect useful test results and diagnostic artifacts while protecting secrets.

Keep CI deterministic, maintainable, and free from unnecessary infrastructure.

**The primary success condition is a reliable Windows-first CI and quality assurance system that automatically detects build failures, functional regressions, security issues, indexing inconsistencies, native dependency problems, and release-blocking defects before Lantern is packaged for distribution.**

Do not implement Phase 12 or later features automatically.
