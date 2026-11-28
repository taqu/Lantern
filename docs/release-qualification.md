# Release Qualification

This document defines the quality gate for Lantern release candidates.

A release candidate may only proceed to packaging (Phase 12) after all required
checks listed here have passed.

## Required Checks

All of the following must pass before a build is declared release-ready.

### 1. Release build

```powershell
dotnet build --configuration Release
```

### 2. Unit tests

```powershell
dotnet test tests/Lantern.Core.Tests --configuration Release
```

All tests must pass. No skipped tests are acceptable in the unit test project.

### 3. SQLite integration tests

Verified as part of the integration test suite.
Tests include: migration, settings persistence, crawler persistence, crawl-state,
fingerprint sync, job persistence, schedule persistence, category persistence,
restart recovery, and integrity constraints.

### 4. Meilisearch integration tests

Requires a real Meilisearch Community Edition process.
Verified via `ci.yml` `integration-tests` job.

### 5. Document extraction regression tests

Verified as part of `Lantern.IntegrationTests`.
Fixtures must be present in `samples/documents/`.

### 6. File crawler tests

Verified as part of `Lantern.IntegrationTests`.
Tests include: initial crawl, incremental crawl, modification detection,
deletion, exclusions, UNC path normalization, cancellation, error handling.

### 7. Web crawler tests

Verified as part of `Lantern.IntegrationTests`.
Tests include: HTML crawling, link discovery, redirects, robots.txt,
rate limiting, ETag/Last-Modified handling, safe deletion reconciliation.

### 8. Job and scheduler tests

Verified as part of `Lantern.IntegrationTests`.
Tests include: submission, persistence, claiming, duplicate prevention,
cancellation, retry, progress, startup recovery, retention.

### 9. Category synchronization tests

Verified as part of `Lantern.IntegrationTests`.
Tests include: creation, editing, ordering, crawler relationships,
metadata synchronization, category filtering.

### 10. Security regression tests

Verified as part of `Lantern.IntegrationTests`.
Categories: authentication/authorization, web crawler SSRF, filesystem
security, extraction security, search security, embedding security.

### 11. Native embedding tests

**Requires GGUF model. Run via `native-embedding.yml`.**

```
Model: resources/granite-embedding-97M-multilingual-r2.gguf
Workflow: .github/workflows/native-embedding.yml
```

This workflow must be triggered and must pass before a release candidate
is tagged. A successful `ci.yml` run does not satisfy this requirement.

The workflow fails if the model file is absent. Do not substitute mock
inference for real model tests at release qualification time.

### 12. Hybrid search integration tests

Verified by `native-embedding.yml`.
Requires: real Meilisearch, real embedding model, REST embedder configured.

### 13. Backup and recovery verification

Manual verification required.
Follow the procedures in `docs/backup-recovery.md`.

Automated portions (SQLite backup/restore, schema integrity) are covered
by the integration test suite.

### 14. Dependency audit

Verified by `ci.yml` `dependency-audit` job.
No critical or high severity NuGet vulnerabilities may be unresolved.

See `docs/dependencies.md` for the documented list of known findings
and their assessment.

### 15. License review

The license inventory in `docs/dependencies.md` must be current and reviewed.

Key items:
- Meilisearch Community Edition — SSPL v1 (bundling policy documented)
- LlamaPure — verify current license
- IBM Granite Embedding 97M — Apache 2.0 (model artifact license)
- All NuGet packages — MIT or Apache 2.0

Any unknown or unusual license must be assessed before release.

### 16. Windows x64 publish validation

Verified by `ci.yml` `publish-validation` job.

```powershell
dotnet publish src/Lantern.Web/Lantern.Web.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output artifacts/publish/win-x64
```

Required outputs:
- `Lantern.Web.exe` present
- `wwwroot/` present
- Native LlamaPure DLLs present
- No development secrets in output

### 17. Publish smoke test

Verified by `ci.yml` `publish-validation` job.

The published executable must start and respond to HTTP requests using an
isolated temporary configuration. The Meilisearch connectivity and admin
account provisioning must succeed.

## Failure Policy

A required check may not be waived without documenting:
- The specific check that failed or was not run.
- The reason it was waived.
- The risk accepted.
- The person who approved the waiver.

A release is not declared ready when required checks have not run.

## Optional Checks (recommended before release)

These are not currently automated but are recommended:

| Check | Location |
|---|---|
| Extended recovery tests | Manual, `docs/backup-recovery.md` |
| UNC share integration | Requires network share environment |
| Full end-to-end browser test | Manual |

## Branch Protection

Recommended required status checks for the `main` branch:

- `build`
- `format`
- `unit-tests`
- `integration-tests`
- `dependency-audit`

Configure these in the GitHub repository Settings → Branches → Branch protection rules.

The `publish-validation` job is recommended as an additional required check for
pull requests targeting a release branch.

The `native-embedding.yml` workflow is required for release qualification but
is not a required PR check due to GGUF model availability constraints.

## Native Model Availability

The GGUF model is not committed to the repository. Before running release
qualification:

1. Ensure the model is present at `resources/granite-embedding-97M-multilingual-r2.gguf`.
2. Verify model integrity (SHA-256 and source documented in `docs/dependencies.md`).
3. Trigger `native-embedding.yml` via workflow_dispatch.
4. Confirm the run succeeded before tagging the release candidate.

## Known Limitations

- UNC share integration tests require a network share environment not available
  on standard GitHub-hosted runners. Full UNC coverage relies on path normalization
  tests; real share access must be verified manually.

- The GGUF model is not available on GitHub-hosted runners. The `native-embedding.yml`
  workflow requires either a self-hosted runner with the model installed, or the model
  must be provided another way (see `docs/ci.md`).

- Backup/restore cross-system consistency testing requires coordinated
  Meilisearch and SQLite backup state, which is partially manual.
