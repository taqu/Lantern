# Testing

This document explains how to run Lantern's test suite locally and in CI.

## Test Projects

| Project | Type | External dependencies |
|---|---|---|
| `tests/Lantern.Core.Tests` | Unit | None |
| `tests/Lantern.IntegrationTests` | Integration | SQLite (temp files), Meilisearch (optional), filesystem |

## Quick Start

```powershell
# Run all unit tests (no external services required)
dotnet test tests/Lantern.Core.Tests

# Run all integration tests (Meilisearch skips if MEILI_MASTER_KEY is not set)
dotnet test tests/Lantern.IntegrationTests

# Run unit tests and integration tests together
dotnet test --configuration Release
```

## Unit Tests

`Lantern.Core.Tests` contains tests for core domain models and logic.

Coverage includes:
- Search document identity and normalization
- Search page item construction
- Administration model validation
- Document metadata normalization
- Search query validation

These tests have no external dependencies and run anywhere .NET 10 is installed.

```powershell
dotnet test tests/Lantern.Core.Tests --configuration Release
```

## Integration Tests

`Lantern.IntegrationTests` covers all infrastructure and integration scenarios.

### Without Meilisearch

Tests that require Meilisearch use `SkippableFact` and check for the `MEILI_MASTER_KEY`
environment variable. If the variable is not set, those tests are skipped.

All other tests — SQLite persistence, file crawling, extraction, web crawling,
HTTP administration, security — run without Meilisearch.

```powershell
dotnet test tests/Lantern.IntegrationTests --configuration Release
```

### With Meilisearch

To run the full integration suite:

1. Start Meilisearch (see below).
2. Set `MEILI_MASTER_KEY` to the master key you used when starting Meilisearch.
3. Optionally set `MEILI_HOST` if Meilisearch is not on the default `http://localhost:7700`.

```powershell
# Start Meilisearch (uses tools/meilisearch/meilisearch.exe)
$env:MEILI_MASTER_KEY = "your-local-dev-key"
./scripts/start-meilisearch.ps1

# In a separate terminal, run integration tests
$env:MEILI_MASTER_KEY = "your-local-dev-key"
dotnet test tests/Lantern.IntegrationTests --configuration Release
```

## Starting Meilisearch Locally

The `tools/meilisearch/meilisearch.exe` binary must exist. If it does not:

```powershell
# Download Meilisearch v1.55.0
./scripts/download-meilisearch.ps1

# Verify the SHA-256 (optional but recommended)
certutil -hashfile tools/meilisearch/meilisearch.exe SHA256
# Expected: 948535579776EA8AEEFFD166AC824F26624468B4F82D6E46A613230D8C91E62A
```

Then start the service:

```powershell
$env:MEILI_MASTER_KEY = "change-this-key"
./scripts/start-meilisearch.ps1
```

Stop it when done:

```powershell
./scripts/stop-meilisearch.ps1
```

## Test Categories

Tests are organized by namespace and file location:

| Category | Location | Meilisearch? | SQLite? |
|---|---|---|---|
| Unit | `Lantern.Core.Tests/` | No | No |
| SQLite persistence | `IntegrationTests/SqlitePersistenceTests.cs` | No | Yes |
| Meilisearch | `IntegrationTests/*.cs` (SkippableFact) | Yes | No |
| File crawling | `IntegrationTests/Crawling/FileCrawlerIntegrationTests.cs` | Optional | Yes |
| Filesystem security | `IntegrationTests/Crawling/FilesystemSecurityTests.cs` | No | No |
| Document extraction | `IntegrationTests/Extraction/` | No | No |
| Web crawling | `IntegrationTests/Web/` | No | No |
| HTTP administration | `IntegrationTests/AdministrationHttpTests.cs` | No | Yes |

`FileCrawlerIntegrationTests` uses `SkippableFact` for indexing steps (requires Meilisearch)
but crawl-state and file-detection steps run without it.

## SQLite Tests

SQLite integration tests use temporary databases created in the system temp directory.
No configuration is required. Each test creates and removes its own database.

## Extraction Tests

Extraction tests scan the `samples/documents/` directory for fixture files. If the
directory is absent or empty the tests return without failing. Sample files should
be present in a complete clone of the repository.

## Filesystem Security Tests

Security tests in `FilesystemSecurityTests.cs` create junctions and symbolic links
on Windows. If the process lacks the required privileges, individual tests skip
gracefully with a descriptive message rather than failing the run.

## Native Embedding Tests

Tests that require the IBM Granite Embedding GGUF model are controlled by the
`Embedding__Enabled` and `Embedding__ModelPath` configuration values.

The model file (`resources/granite-embedding-97M-multilingual-r2.gguf`) is large
and is not required for ordinary development. These tests are run via the
`native-embedding.yml` workflow as part of release qualification.

## Formatting

Check that the code matches `.editorconfig` rules:

```powershell
dotnet format --verify-no-changes --verbosity diagnostic
```

Apply formatting (for fixing violations):

```powershell
dotnet format
```

## Code Coverage

Coverage is collected during test runs when using `--collect:"XPlat Code Coverage"`:

```powershell
dotnet test --configuration Release --collect:"XPlat Code Coverage" --results-directory artifacts/coverage
```

Coverage reports are produced in Cobertura XML format by `coverlet.collector`.

## Troubleshooting

### Meilisearch tests are all skipped

Ensure `MEILI_MASTER_KEY` is set in the current shell:

```powershell
[System.Environment]::GetEnvironmentVariable("MEILI_MASTER_KEY")
```

### Meilisearch connection errors

Verify the service is running:

```powershell
Invoke-WebRequest http://localhost:7700/health
```

Check the port: if you started Meilisearch on a non-default port, set:

```powershell
$env:MEILI_HOST = "http://localhost:YOUR_PORT"
```

### SQLite migration errors

Delete the temporary test database and retry. Test databases are created fresh
each run, but leftover files from a crashed run can cause conflicts:

```powershell
Remove-Item -Recurse "$env:TEMP\lantern-*" -ErrorAction SilentlyContinue
```

### Extraction tests find no fixtures

Verify the `samples/documents/` directory contains the expected files:

```powershell
Get-ChildItem samples/documents -Recurse
```

If the directory is empty after cloning, the fixture files may need to be added
to the repository.

### Native DLL load failures (LlamaPure)

The `Lantern.Embedding` project requires native llama.cpp DLLs for x64 Windows.
The NuGet package includes these DLLs. If they are missing from the build output:

```powershell
dotnet build src/Lantern.Embedding --configuration Release
Get-ChildItem src/Lantern.Embedding/bin/Release/net10.0 -Filter "*.dll"
```

Native embedding tests should only be run on x64 Windows.
