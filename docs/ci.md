# CI Infrastructure

This document describes the GitHub Actions CI configuration for Lantern.

## Workflow Overview

| Workflow | File | Trigger | Purpose |
|---|---|---|---|
| CI | `.github/workflows/ci.yml` | Every PR and push to `main` | Build, format, unit tests, integration tests, audit, publish |
| Native Embedding | `.github/workflows/native-embedding.yml` | Manual (`workflow_dispatch`) | GGUF model inference and hybrid search |

### ci.yml jobs

| Job | Depends on | Windows runner | Description |
|---|---|---|---|
| `build` | — | yes | Restore NuGet packages, build in Release configuration |
| `format` | `build` | yes | `dotnet format --verify-no-changes` |
| `unit-tests` | `build` | yes | Run `Lantern.Core.Tests` (no external dependencies) |
| `integration-tests` | `build` | yes | Start Meilisearch, run `Lantern.IntegrationTests` |
| `dependency-audit` | `build` | yes | `dotnet list package --vulnerable --include-transitive` |
| `publish-validation` | `unit-tests` + `integration-tests` | yes | `dotnet publish --runtime win-x64 --self-contained`, startup smoke test |

`publish-validation` only runs when both test jobs pass.

## Trigger Policy

```
ci.yml triggers on:
  push → main
  pull_request → main
  workflow_dispatch

native-embedding.yml triggers on:
  workflow_dispatch only
```

Superseded pull-request runs are cancelled automatically. Pushes to `main` are never cancelled.

## Windows Runners

All jobs use `windows-latest` (currently Windows Server 2022, x64).

Native validation requirements:
- LlamaPure DLL loading
- Windows filesystem behavior (path normalization, UNC paths, reparse points)
- Native Meilisearch process execution
- Self-contained win-x64 publishing

Linux runners are not used. A passing Linux build does not validate the Windows distribution.

## .NET SDK

The SDK version is pinned via `global.json` to .NET 10 with `rollForward: latestFeature`.

This allows any 10.x feature-band SDK while preventing accidental upgrades to .NET 11+.

```json
{ "sdk": { "version": "10.0.0", "rollForward": "latestFeature" } }
```

The `actions/setup-dotnet` action installs the SDK matching `global.json`.

## Meilisearch Setup

Integration tests run against a real Meilisearch Community Edition process. Docker is not required.

### Binary

| Property | Value |
|---|---|
| Version | 1.55.0 |
| Source | Official GitHub release (`meilisearch/meilisearch`) |
| File | `meilisearch-windows-amd64.exe` |
| SHA-256 | `948535579776EA8AEEFFD166AC824F26624468B4F82D6E46A613230D8C91E62A` |
| Cache key | `meilisearch-windows-amd64-{version}` |

The binary is cached in GitHub Actions by version. A cache miss downloads from the official release URL and verifies the SHA-256 before proceeding. A checksum mismatch fails the job.

### Lifecycle in CI

1. Download and verify binary (or restore from cache).
2. Generate an ephemeral master key (random 48-character alphanumeric string, not a secret).
3. Create an isolated temporary data directory under `$RUNNER_TEMP`.
4. Start the process bound to `127.0.0.1:17700 --no-analytics`.
5. Poll `/health` up to 60 seconds. Fail if Meilisearch does not become ready.
6. Export `MEILI_MASTER_KEY` and `MEILI_HOST` for the test runner.
7. Run tests.
8. Stop and clean up the process and data directory (runs even on failure).

Meilisearch stdout and stderr are captured to `$RUNNER_TEMP/meili-out.log` and `meili-err.log`. On failure these are uploaded as artifacts.

### Failure Policy

If Meilisearch fails to start, the `integration-tests` job fails. Tests are not silently skipped.

The `SkippableFact`/`Skip.If` pattern in tests checks for the `MEILI_MASTER_KEY` environment variable. Because CI sets this variable before running the tests, no Meilisearch-dependent test will be skipped. If Meilisearch is not actually reachable, the tests will fail with a connection error.

## NuGet Caching

NuGet packages are cached using:

```
key: {runner.os}-nuget-{hash of all .csproj files and Directory.Build.props}
```

Cache is shared across all jobs in the same workflow run that use the same OS.

## GGUF Model (native-embedding.yml)

The IBM Granite Embedding 97M Multilingual R2 model (`granite-embedding-97M-multilingual-r2.gguf`) is not present on standard GitHub-hosted runners. The `native-embedding.yml` workflow:

1. Resolves the model path (default: `resources/granite-embedding-97M-multilingual-r2.gguf`).
2. **Fails** if the file is not found. Missing model resources are not silently skipped.
3. Runs the full test suite with `Embedding__Enabled=true` and the model path configured.

Options for making the model available:
- **Self-hosted runner**: place the model in the repository's `resources/` directory.
- **GitHub Actions cache**: seed the cache in a separate workflow and restore it here.
- **Alternative path**: pass `model_path` when triggering via `workflow_dispatch`.

## Artifact Collection

| Artifact name | Contents | Retention |
|---|---|---|
| `unit-test-results` | TRX + coverage from `Lantern.Core.Tests` | 30 days |
| `integration-test-results` | TRX + coverage from `Lantern.IntegrationTests` | 30 days |
| `dependency-audit` | `nuget-audit.txt` | 30 days |
| `publish-win-x64` | `Lantern.Web.exe` + `wwwroot/` | 7 days |
| `meilisearch-logs` | stdout/stderr (only on failure) | 7 days |
| `smoke-test-logs` | stdout/stderr (only on failure) | 7 days |

Secrets and full document content are never written to logs or artifacts.

## CI Security

- Workflows use `permissions: contents: read` (minimal).
- The Meilisearch master key is ephemeral, generated per run, and is not a secret (it controls only the temporary CI test instance).
- `MEILI_MASTER_KEY` is written to `GITHUB_OUTPUT` (not `GITHUB_ENV`) so it is masked in logs when referenced with `${{ steps.meili-key.outputs.key }}`.
- External binary checksums are verified before execution.
- No production credentials are required for CI.

### Pinning third-party actions to SHA

The current workflows use version tags (`@v4`). Before shipping a release, pin each action to an immutable commit SHA:

```yaml
# Example:
uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683  # v4.2.2
```

Review the action source at that commit before pinning.

## Concurrency Controls

`ci.yml` uses:

```yaml
concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: ${{ github.ref != 'refs/heads/main' }}
```

- Pull-request runs are cancelled when a newer push supersedes them.
- Runs on `main` are never cancelled.

## Upgrading Meilisearch

1. Download the new Windows x64 binary.
2. Compute its SHA-256: `certutil -hashfile meilisearch-windows-amd64.exe SHA256`
3. Update `MEILISEARCH_VERSION` and `MEILISEARCH_SHA256` in both workflow files.
4. Delete the old cache entry from the Actions cache UI or change the cache key.
5. Verify `MeiliSearch` .NET client compatibility and update the package version if needed.
