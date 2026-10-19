# Search and indexing foundation (Phase 2)

`Lantern.Core.Search.SearchDocument` is the shared representation for already extracted
file and web content. No crawler, extraction pipeline or database is involved.
Resolve `IDocumentIndexingService` through `AddLanternInfrastructure`; it does not depend
on Blazor. Call `UpsertAsync`, `UpsertBatchAsync`, `DeleteAsync`, `DeleteBatchAsync`, or
`RebuildAsync`. Collection operations accept lazy `IEnumerable` sources and enumerate once.

## Schema and identity

The JSON fields are `id`, `sourceId`, `sourceType`, `title`, `uri`, `path`, `content`,
`extension`, `mimeType`, `size`, `modifiedAt`, and `indexedAt`.
Unknown metadata is null. Source types are the literal strings `file` and `web`.
Timestamps are UTC `DateTimeOffset` values; the caller can supply the indexing timestamp,
which otherwise defaults to creation time. IDs can be omitted on input; supplied IDs
must match the computed value.

`DocumentIdentity` hashes a JSON array of source type, source ID and canonical location
with SHA-256, producing 64 lowercase hexadecimal characters. Array encoding prevents
component-boundary collisions. Source IDs are case-sensitive, opaque identifiers;
surrounding whitespace and control characters are rejected. Separate source definitions
remain separate even when they reference the same physical document.

File locations must be absolute Windows drive or UNC share paths. `Path.GetFullPath`
resolves separators and dot segments without accessing the file. Identity uses invariant
uppercase, while display paths retain their case. Device paths and drive-relative paths
are rejected. This targets ordinary case-insensitive Windows filesystems, not NTFS
directories explicitly configured for case sensitivity. File URIs derive from the path.

Web locations must be absolute HTTP(S) URLs without embedded credentials. .NET URI
canonicalization normalizes scheme, host and default ports; fragments are removed.
Path case, query values and query ordering remain meaningful. No query parameter
stripping, sorting, redirect following or trailing-slash merging occurs.

Normalization trims titles, removes invalid control characters, changes CRLF/CR to LF,
and preserves tabs, indentation, punctuation, Japanese and English text. Extensions and
MIME types become lowercase. Negative sizes and inconsistent IDs are rejected. The
Phase 0 properties remain available to existing callers but are excluded from JSON;
new writes use the normalized fields. Search also reads old Phase 1 JSON fields.

## Configuration and completion

Use the existing `Meilisearch` section in server configuration:

```json
{
  "Meilisearch": {
    "Url": "http://127.0.0.1:7700",
    "IndexName": "lantern-documents",
    "MaxDocumentsPerBatch": 100,
    "MaxPayloadBytesPerBatch": 4194304,
    "TaskTimeoutSeconds": 60
  }
}
```

The application reads `MEILI_API_KEY` server-side. The development seeder and integration
tests retain their existing `MEILI_MASTER_KEY` / `MEILI_HOST` convention. Use a key with
index creation, settings, document, task, swap and cleanup permissions for indexing.
No credentials are delivered to browser clients.

Initialization creates a missing index with primary key `id`, rejects an incompatible
primary key and applies settings without clearing documents. Searchable attributes are
`title, content`; filterable attributes are `sourceId, sourceType, extension, mimeType`;
sortable attributes are `modifiedAt, indexedAt, size`. Displayed attributes remain `*`
to preserve UI metadata and legacy records. Settings are applied once per service
instance, with idempotent results across restarts. Changing searchable/filterable/sortable
settings can cause Meilisearch to reindex existing documents; initialization awaits this
work before permitting searches. No startup index deletion occurs.

Batches execute sequentially. The batcher accounts for serialized UTF-8 JSON item sizes,
array brackets and commas using camel-case System.Text.Json serialization. It bounds both
count and estimated payload bytes (conservative if the SDK uses more compact escaping).
An oversized individual item raises an explicit error. Only one bounded batch plus a
lookahead item is buffered, rather than an entire corpus. Ordinary batches are not
transactional: earlier completed batches remain if later input or indexing fails.

Every mutation awaits the official SDK task poller with a 100 ms polling interval,
checks terminal status and reports the task UID, operation and server failure details.
A linked cancellation deadline bounds waiting, including a stalled status request.
Timeout and cancellation do not confirm success: a submitted server operation may still
complete. No automatic retries occur. Callers should reconcile uncertain outcomes before
retrying. Upsert and delete are idempotent.

## Rebuild and concurrency

`RebuildAsync` accepts the complete explicitly supplied corpus; an empty collection
intentionally publishes an empty index. It builds a uniquely named temporary index,
applies settings, indexes every bounded batch and waits for completion. Only then does it
swap the temporary and logical active index via Meilisearch index swapping. Searches
keep the same logical index name and see the old or new corpus. The active corpus is
never deleted before replacement. A pre-promotion failure removes only the temporary
index. After confirmed promotion, cleanup removes the old corpus from the temporary name.

If promotion is submitted but its completion cannot be confirmed, the temporary index is
retained and its name logged for operator inspection. Do not blindly swap again: inspect
Meilisearch tasks and index contents first. Cleanup failures are logged without hiding
the primary failure or undoing a successful promotion; operators may remove the logged
temporary index after verifying its identity and the active corpus.

A singleton semaphore serializes initialization, ordinary writes and rebuilds within
the process. Searches wait for initial settings and may wait behind a running write or
rebuild at that readiness gate. Meilisearch processes already submitted tasks even after
client cancellation. Coordination across multiple Lantern processes or external writers
is outside Phase 2; use one indexing owner. Legacy `ISearchService.IndexDocumentAsync`
delegates to this service for the configured index. Explicit whole-index deletion remains
a legacy API and should not be used concurrently with indexing; future crawlers should
use only `IDocumentIndexingService`.

The seeder now upserts stable `development-seed` IDs without dropping the active index.
Old `doc-001`-style records are not silently deleted. To migrate an existing development
corpus completely, explicitly rebuild it from the desired normalized collection.

## Verification

Use a native Meilisearch executable (`scripts/download-meilisearch.ps1` and
`scripts/start-meilisearch.ps1`) and set `MEILI_MASTER_KEY`, optionally `MEILI_HOST`.
Keep that instance separate from production. New Phase 2 tests create unique
`lantern-phase2-test-<guid>` indexes and remove them afterwards; older integration tests
use their existing dedicated `integration-test` and `searchservice-test` names.
Integration tests skip when `MEILI_MASTER_KEY` is absent.

```powershell
dotnet build Lantern.slnx --artifacts-path artifacts\phase2
dotnet test Lantern.slnx --artifacts-path artifacts\phase2 --no-build
```

Core tests cover identity and normalization. The integration test project contains batch
unit tests and controlled timeout/cancellation tests, plus real-server tests for settings,
idempotent initialization, upserts, duplicate prevention, metadata, search, deletion,
successful rebuild, failed rebuild preservation and asynchronous task failure. The
controlled task handler is used only to make nonterminal waiting deterministic; actual
production and end-to-end integration operations use the official SDK and real server.
