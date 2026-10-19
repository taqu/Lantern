# Phase 2 implementation report

## A. Implementation summary

Implemented the normalized file/web document schema, deterministic identity,
normalization, idempotent index configuration, document upsert/delete, bounded lazy
batch ingestion, checked asynchronous task completion, and safe full-index rebuild.
Services are registered as singletons and reusable without Blazor. Search waits for
configuration and returns normalized source metadata while retaining old-index read
compatibility. The development seeder uses production upsert behavior without dropping
the index. No Phase 3 features were added.

## B. Files changed

Created:

- `src/Lantern.Core/Search/DocumentNormalizer.cs`
- `src/Lantern.Core/Search/IDocumentIndexingService.cs`
- `src/Lantern.Infrastructure/Search/DocumentBatcher.cs`
- `src/Lantern.Infrastructure/Search/MeilisearchIndexingService.cs`
- `tests/Lantern.Core.Tests/DocumentNormalizationTests.cs`
- `tests/Lantern.IntegrationTests/DocumentBatcherTests.cs`
- `tests/Lantern.IntegrationTests/IndexingIntegrationTests.cs`
- `tests/Lantern.IntegrationTests/TaskCompletionTests.cs`
- `docs/indexing.md`
- `docs/work/phase2_recap.md`

Modified:

- `src/Lantern.Core/Search/SearchDocument.cs`
- `src/Lantern.Core/Search/SearchPageItem.cs`
- `src/Lantern.Infrastructure/Search/MeilisearchOptions.cs`
- `src/Lantern.Infrastructure/Search/MeilisearchSearchService.cs`
- `src/Lantern.Infrastructure/ServiceCollectionExtensions.cs`
- `src/Lantern.Web/appsettings.json`
- `tools/Lantern.Feasibility/Lantern.Feasibility.csproj` (Infrastructure reference only)
- `tools/Lantern.Feasibility/Seeder.cs`
- `docs/architecture.md`

Pre-existing changes in `.editorconfig`, `docs/work/phase2.md`,
`scripts/download-meilisearch.ps1`, and the feasibility project's assembly/package
settings were preserved.

## C. Architectural decisions

- Identity: SHA-256 over a JSON array containing source type, source ID and canonical
  location. The component boundaries are unambiguous; caller-provided IDs are verified.
- Canonicalization: absolute Windows drive/UNC paths, case-folded only for identity;
  HTTP(S) URI canonicalization removes fragments while retaining query semantics.
- Settings: searchable title/content; filterable source/type/extension/MIME; sortable
  modification/index timestamps and size; all fields displayed. Existing records survive
  initialization and reinitialization.
- Batching: defaults of 100 documents and 4 MiB estimated JSON payload, including
  delimiters. Lazy enumeration and sequential submission bound memory and concurrency.
- Task completion: official SDK polling, explicit terminal success check, failure
  diagnostics, cancellation and a 60-second default deadline. No automatic retries.
- Rebuild: temporary index followed by awaited index swapping. A singleton gate
  serializes writes/rebuilds. Unknown promotion outcomes retain the temporary index;
  cleanup problems are logged. Pre-promotion failures preserve the active corpus.

See [the indexing guide](../indexing.md) for API usage and operational details.

## D. Build and test commands executed

```powershell
dotnet build Lantern.slnx --no-restore
dotnet test Lantern.slnx --no-build
dotnet build src\Lantern.Infrastructure --no-restore
dotnet test tests\Lantern.Core.Tests --no-restore
dotnet test tests\Lantern.IntegrationTests --no-restore
dotnet build Lantern.slnx --artifacts-path artifacts\phase2
dotnet build Lantern.slnx --artifacts-path artifacts\phase2 --no-restore
dotnet build Lantern.slnx --artifacts-path artifacts\phase2 --no-restore --verbosity quiet
dotnet test Lantern.slnx --artifacts-path artifacts\phase2 --no-build
git diff --check
```

## E. Results

- Baseline: 34 Core tests and 8 existing real-server integration tests passed.
- Baseline default-output build failed because the running `Lantern.Web` process locked
  its executable. It was left running; builds were moved to `artifacts/phase2`.
- Final full solution build: passed, zero errors. Five pre-existing ImageSharp
  vulnerability warnings remain in the feasibility tool's extraction dependencies.
- Final Core tests: 47 passed, zero failed, zero skipped.
- Final integration project: 17 passed, zero failed, zero skipped. This includes 11
  real-server tests (8 existing and 3 new), 4 batch tests, and 2 controlled waiting tests.
- Real Meilisearch operations ran against the already available native server using
  isolated test indexes. New indexing tests use GUID-suffixed names. No production
  corpus was rebuilt or cleared.
- An intermediate integration run found one test cleanup failure when a cancellation
  test had never created its index; cleanup now checks existence and the final run passes.
- `git diff --check`: passed.
- Not executed: manual browser interaction, production seeding, or destructive production
  migration. UI compatibility is covered by the unchanged UI, solution build, existing
  search tests and normalized-result service integration assertions.

## F. Known limitations

Only one process should own indexing; the gate does not coordinate external writers.
Normal batches can partially complete before later validation fails. Server tasks may
continue after caller cancellation or timeout. Operators must reconcile an uncertain
swap and inspect retained temporary indexes. Whole-index deletion remains an explicit
legacy API outside the new indexing gate. Windows case-sensitive directories are not
supported by the case-insensitive file identity policy. The development seeder does not
delete old Phase 1 IDs automatically. Existing extraction dependency warnings are
outside this phase.

## G. Remaining work

No outstanding Phase 2 implementation or automated acceptance checks. Manual browser
smoke testing remains an optional additional check. Crawling, extraction integration,
persistence, administration and multi-process coordination were not implemented.
