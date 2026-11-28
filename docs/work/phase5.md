# Lantern — Phase 5 Implementation Instructions
## Local File Crawler and Incremental Index Synchronization

You are implementing **Phase 5: Local File Crawler** of the Lantern project.

Lantern is a lightweight, self-hosted full-text search platform designed primarily for Windows and intranet environments.

The application uses:

- .NET 10 and ASP.NET Core.
- Blazor Interactive Server.
- Meilisearch Community Edition.
- SQLite with Entity Framework Core.
- LlamaPure with IBM Granite Embedding 97M Multilingual R2.
- Local REST-based embedding and hybrid search.
- Document extraction and structure-aware chunking.

Phases 0–4 have established the search interface, indexing infrastructure, administration foundation, embedding server, document chunking, and document extraction pipeline.

**Your task is to implement a reliable local filesystem crawler that discovers documents, detects changes, processes supported files, synchronizes documents and chunks with Meilisearch, and persists crawl state in SQLite.**

The crawler must support both local Windows directories and UNC network shares.

Do not implement browser-based crawler configuration, scheduling, or the persistent job execution framework planned for subsequent phases.

---

## 1. Review the Existing Implementation

Before changing the repository:

1. Read `roadmap.md` and the existing architecture documentation.
2. Inspect the solution and project structure.
3. Review the Phase 2 document identity and indexing services.
4. Review the Phase 3 SQLite persistence infrastructure.
5. Review the Phase 3.5 embedding, chunk indexing, and hybrid search services.
6. Review the Phase 4 document extraction and structure-aware chunking pipeline.
7. Inspect existing crawler-related models or interfaces.
8. Review dependency injection, configuration, and logging conventions.
9. Run existing builds and tests to establish a baseline.

The actual implementation is the source of truth.

Reuse existing components wherever possible.

Do not introduce duplicate indexing, extraction, chunking, or configuration frameworks.

Preserve all functionality implemented in previous phases.

## 2. Phase Objectives

Implement:

1. Local directory crawling.
2. Recursive file discovery.
3. Windows UNC network share support.
4. File extension filtering.
5. Directory exclusion rules.
6. File size limits.
7. Document extraction integration.
8. Structure-aware chunk generation.
9. Meilisearch document and chunk synchronization.
10. SQLite-backed crawl state.
11. Incremental change detection.
12. Safe deleted-file reconciliation.
13. Cancellation and bounded concurrency.
14. Controlled error handling.
15. Deterministic unit and integration tests.

The crawler must support repeated execution against the same source without generating duplicate documents or chunks.

It must not require the Blazor administration UI to execute.

## 3. Architecture

Use the existing components to implement the following pipeline:

```text
Configured File Crawler
        |
        v
Directory Enumerator
        |
        v
File Filtering
        |
        v
File Fingerprint Comparison
        |
        +-- Unchanged --> Skip
        |
        +-- Changed / New
                |
                v
        Document Extraction
                |
                v
        Structure-Aware Chunking
                |
                v
        Document / Chunk Indexing
                |
                v
        Meilisearch Task Completion
                |
                v
        SQLite Crawl State Update
                |
                v
        Reconciliation
```

### Responsibility boundaries

**File crawler**

Responsible for filesystem discovery, filtering, change detection, and synchronization.

**Document extraction pipeline**

Responsible for reading supported documents and extracting structured text.

**Chunking service**

Responsible for creating searchable text chunks.

**Meilisearch indexing services**

Responsible for document and chunk upsert, deletion, and task completion.

**SQLite**

Responsible for crawler definitions, fingerprints, synchronization state, and reconciliation metadata.

**Embedding server**

Responsible only for generating embeddings requested by Meilisearch.

Do not call LlamaPure directly from the filesystem crawler.

Do not duplicate embedding generation in the crawler.

## 4. File Crawler Configuration

Use the existing crawler configuration infrastructure from Phase 3.

Extend it with the minimum fields required to execute a local file crawler.

Suggested configuration:

```json
{
  "id": "crawler-id",
  "name": "Company Documents",
  "type": "file",
  "enabled": true,
  "rootPath": "C:\\Documents",
  "recursive": true,
  "includedExtensions": [
    ".pdf",
    ".docx",
    ".xlsx",
    ".pptx",
    ".txt",
    ".md",
    ".html",
    ".htm"
  ],
  "excludedDirectories": [
    ".git",
    "node_modules",
    "bin",
    "obj"
  ],
  "maxFileSizeMB": 100,
  "maxConcurrentFiles": 2
}
```

These values are illustrative.

Follow the current configuration model rather than introducing a competing schema.

### Requirements

- Support local absolute directory paths.
- Support UNC paths.
- Support recursive and nonrecursive enumeration.
- Support included and excluded file extensions.
- Support excluded directory names or paths.
- Support maximum file sizes.
- Support configurable bounded processing concurrency.
- Validate configuration before execution.
- Reject invalid or unsupported crawler settings.

Keep filtering rules simple and predictable.

Do not implement a complex globbing language unless one is already available and suitable.

### Scope restriction

Do not implement crawler creation or editing forms in this phase.

These belong to Phase 6.

Crawler configuration may be supplied programmatically, through test fixtures, or through existing persistence infrastructure.

## 5. Windows Filesystem and UNC Support

Support standard Windows paths:

```text
C:\Documents
D:\Shared\Reports
\\fileserver\shared\documents
```

### Requirements

- Normalize paths consistently.
- Support drive-letter and UNC paths.
- Preserve original paths for user-facing metadata.
- Handle Windows filesystem case-insensitive behavior appropriately.
- Avoid generating duplicate document identities due to path formatting differences.
- Handle inaccessible directories.
- Handle locked files.
- Handle permission errors.
- Handle temporarily unavailable network shares.
- Handle files disappearing during enumeration or processing.

### Filesystem boundaries

The crawler must remain within its configured root.

Do not traverse directory links, junctions, or symbolic links by default.

If existing code supports following links, enforce root-boundary validation and cycle detection.

Do not allow path traversal through untrusted crawler configuration.

Do not accidentally scan the entire filesystem because of a malformed root path.

### Network shares

UNC crawling must use the permissions of the Lantern process or service identity.

Do not implement credential impersonation or Windows authentication management in this phase.

Document the required filesystem permissions.

A disconnected share must produce an explicit failure or incomplete-scan result.

It must not be interpreted as an empty directory.

## 6. Directory Enumeration

Implement a filesystem enumeration component.

### Requirements

1. Start from a validated root directory.
2. Enumerate files.
3. Recursively traverse subdirectories when configured.
4. Apply directory exclusions before descending.
5. Apply extension and file-size filters.
6. Avoid loading the entire directory tree into memory.
7. Respect cancellation between enumeration operations.
8. Report inaccessible directories.
9. Prevent traversal loops.
10. Preserve sufficient information to determine whether a scan completed successfully.

Use `System.IO` APIs.

Prefer streaming enumeration.

Avoid converting millions of discovered paths into one large in-memory collection.

### Error handling

Distinguish:

- Root unavailable.
- Directory access denied.
- Directory enumeration failure.
- File access denied.
- File disappeared.
- Unsupported filesystem entry.

Errors must be recorded with enough context for troubleshooting.

Do not terminate the entire crawl because one ordinary file cannot be read.

However, a root-level failure must prevent successful reconciliation.

## 7. File Fingerprints and Change Detection

Implement incremental crawling.

The crawler must avoid extracting and embedding unchanged files.

### Initial fingerprint strategy

Use filesystem metadata such as:

```text
CanonicalPath
FileSize
LastWriteTimeUtc
```

Associate this information with the configured crawler identity.

Use the existing deterministic document ID generator.

### Requirements

- New files must be indexed.
- Changed files must be reprocessed.
- Unchanged files must be skipped.
- Deleted files must eventually be removed.
- Repeated scans must not create duplicate documents.
- Interrupted processing must not mark a file as successfully indexed.

### Fingerprint reliability

Filesystem timestamps and sizes are not perfect content identifiers.

For the initial implementation, metadata-based change detection is acceptable.

Do not hash every file on every scan by default.

If hashing is already supported, reuse it where beneficial.

Document limitations involving:

- Timestamp granularity.
- Files changed without metadata updates.
- Network filesystem behavior.
- Files modified while being processed.

### Concurrent modification

A file may change during extraction.

Where practical, compare relevant filesystem metadata before and after processing.

If a file changes during processing, do not treat the resulting index as a verified representation of the latest file version.

Report or mark it for retry.

## 8. SQLite Crawl State

Extend the Phase 3 database infrastructure to store per-file synchronization state.

Suggested logical entity:

```text
CrawledFileState
    |
    +-- CrawlerId
    +-- DocumentId
    +-- CanonicalPath
    +-- FileSize
    +-- LastWriteTimeUtc
    +-- Fingerprint
    +-- LastSeenScanId
    +-- LastIndexedAt
    +-- SyncStatus
    +-- LastError
```

Adapt this structure to the actual repository.

Do not create redundant columns without a concrete requirement.

### Requirements

- Persist fingerprints.
- Track successfully indexed files.
- Track files discovered in the current scan.
- Distinguish indexing success from failure.
- Preserve state across application restarts.
- Support reconciliation after a completed scan.
- Avoid duplicate state records.
- Use EF Core migrations.

### State correctness

Do not update a file's successful fingerprint before Meilisearch confirms indexing success.

Meilisearch and SQLite do not share a transaction.

Design state transitions so interrupted operations can be retried safely.

Prefer idempotent indexing and repairable synchronization state over complex distributed transaction mechanisms.

### SQLite concurrency

Reuse existing DbContext factory or scoped lifetime conventions.

Do not share one DbContext concurrently across processing workers.

Batch state updates where appropriate.

Avoid excessive SQLite write contention.

## 9. Crawl Execution Lifecycle

Implement a single-run crawler execution service.

Conceptual interface:

```csharp
public interface IFileCrawler
{
    Task<FileCrawlResult> CrawlAsync(
        FileCrawlerConfiguration configuration,
        CancellationToken cancellationToken);
}
```

The signature is illustrative.

### Suggested lifecycle

```text
Initialize
    |
    v
Validate configuration
    |
    v
Create scan identifier
    |
    v
Enumerate files
    |
    v
Process new / changed files
    |
    v
Verify indexing completion
    |
    v
Reconcile deleted files
    |
    v
Persist scan result
```

### Execution result

Return structured information such as:

```text
DiscoveredFiles
ProcessedFiles
IndexedFiles
UnchangedFiles
SkippedFiles
FailedFiles
DeletedFiles
StartedAt
FinishedAt
Status
```

Suggested statuses:

```text
Completed
CompletedWithErrors
Failed
Canceled
```

A scan containing recoverable errors must not be reported as entirely successful.

### Important scope restriction

Do not implement a persistent job queue or scheduler.

Those belong to Phase 7.

A single-run execution result is sufficient for Phase 5.

## 10. Document Extraction Integration

Reuse the Phase 4 document processing pipeline.

For each new or changed supported file:

1. Open the file safely.
2. Validate the applicable file-size limit.
3. Pass it to the document processing service.
4. Extract text and metadata.
5. Generate structure-aware chunks.
6. Associate chunks with the deterministic parent document ID.
7. Preserve available source location metadata.
8. Return an indexing-ready representation.

### Requirements

Do not duplicate PDF or Office extraction logic.

Do not reimplement chunking inside the crawler.

Preserve:

- Source path.
- Document URI.
- Document title.
- File extension.
- File size.
- Modification timestamp.
- Page numbers.
- Slide numbers.
- Worksheet names.
- Section headings.

### Extraction failures

A failed extraction must not delete a previously indexed valid version of the document.

Preserve the last successfully indexed state.

Record the failure so the file can be retried.

### Empty documents

Distinguish:

- A genuinely empty document.
- A document containing no extractable text.
- An extraction failure.

Do not silently treat all three cases as successful empty content.

Use the documented Phase 4 behavior.

## 11. Document and Chunk Synchronization

Integrate with the existing Phase 2 and Phase 3.5 indexing services.

### New document

For a newly discovered file:

1. Generate its deterministic document ID.
2. Extract and chunk the document.
3. Index the parent document where required.
4. Index its chunks.
5. Wait for Meilisearch operations to complete.
6. Persist successful synchronization state.

### Updated document

For a changed file:

1. Read the previous successful state.
2. Extract and chunk the new version.
3. Determine the new chunk set.
4. Upsert the new content.
5. Reconcile obsolete chunks.
6. Confirm indexing and deletion tasks.
7. Update the successful fingerprint.

### Unchanged document

Skip extraction, chunking, and embedding generation.

Do not perform redundant Meilisearch upserts merely to update crawl timestamps.

### Failed update

If extraction or indexing fails, preserve the previous valid searchable version wherever possible.

Do not mark the new fingerprint as synchronized.

Allow the next crawl to retry.

### Nontransactional update limitations

Meilisearch does not provide a transaction spanning all parent and chunk updates.

Implement a safe, documented synchronization strategy.

If available, reuse the Phase 3.5 safe chunk replacement mechanism.

Do not delete previous chunks before the replacement is known to be usable.

Do not report successful synchronization when only some required operations have succeeded.

Ensure an interrupted update can be repaired by a subsequent crawl.

## 12. Deleted-File Reconciliation

Implement safe removal of indexed documents whose source files no longer exist.

**This is a critical reliability requirement.**

### Recommended approach

Assign a unique scan identifier to each crawl.

Track which previously indexed files were observed during that scan.

After a scan completes successfully, identify candidates not observed during the scan.

Only reconcile deletions when the relevant filesystem enumeration completed reliably.

### Requirements

- Remove deleted parent documents.
- Remove associated searchable chunks.
- Confirm Meilisearch deletion completion.
- Update or remove corresponding SQLite state.
- Preserve retry information when deletion fails.
- Prevent accidental deletion of documents belonging to another crawler.

### Safety rules

Do not perform deletion reconciliation when:

- The crawler root is unavailable.
- The scan was canceled.
- Directory enumeration was incomplete.
- A required subtree could not be enumerated reliably.
- The network share disconnected.
- The application terminated unexpectedly.

A permission error must not be interpreted as file deletion.

### Recoverable file errors

Individual extraction failures do not necessarily invalidate deletion reconciliation when filesystem enumeration itself completed successfully.

Distinguish discovery completeness from document processing success.

### Deleted parent documents

Ensure that all child chunks associated with a deleted parent are removed.

Do not leave orphaned search chunks.

### Destructive operations

Avoid accidental mass deletion caused by:

- Root path changes.
- Invalid configuration.
- Empty or unavailable network shares.
- Failed enumeration.
- Interrupted scans.

Treat changing a crawler root as an explicit configuration change requiring a documented reconciliation strategy.

## 13. Cancellation

Support cooperative cancellation throughout the crawler.

Cancellation must propagate through:

- Directory traversal.
- File processing.
- Document extraction.
- Chunk generation where supported.
- Meilisearch indexing.
- SQLite operations.

### Requirements

- Stop scheduling new work after cancellation.
- Release file handles.
- Allow safe completion or interruption of in-flight operations.
- Preserve successfully completed synchronization state.
- Do not perform deleted-file reconciliation after cancellation.
- Return a canceled execution result.

Do not assume that cancellation immediately interrupts synchronous extraction or native operations.

Document any limitations.

Do not forcibly terminate the Lantern process to cancel a crawl.

## 14. Bounded Concurrency and Backpressure

Large document collections must not cause unbounded memory growth.

### Recommended initial settings

```text
MaxConcurrentFiles = 2
FileQueueCapacity = 32
```

These are development defaults, not performance guarantees.

### Requirements

- Bound active file processing.
- Bound pending work.
- Avoid loading all file contents into memory.
- Stream file enumeration.
- Avoid simultaneous processing of excessive large documents.
- Respect embedding server capacity indirectly through bounded indexing.
- Avoid overwhelming SQLite with concurrent writes.

Use simple .NET concurrency primitives.

A bounded `Channel<T>` or `SemaphoreSlim` may be appropriate.

Do not create one task per discovered file in a large directory tree.

### Embedding integration

Meilisearch communicates with the embedding REST server.

The crawler must wait for relevant Meilisearch indexing tasks when determining successful synchronization.

Do not assume successful document submission means embedding generation has completed.

Avoid re-embedding unchanged documents.

## 15. Logging and Diagnostics

Use the existing logging infrastructure.

Record meaningful crawl events, including:

- Crawl start.
- Crawl completion.
- Root unavailable.
- Directory access failure.
- File extraction failure.
- Indexing failure.
- Deletion failure.
- Cancellation.
- Summary counts.

### Logging requirements

- Include crawler identity where applicable.
- Include document identity for document-specific failures.
- Use structured logging.
- Avoid logging full extracted content.
- Avoid logging embedding vectors.
- Avoid logging credentials.
- Avoid excessively verbose per-file success logs by default.

### Error isolation

A malformed PDF must not crash the crawler.

A locked DOCX file must not terminate unrelated file processing.

A temporary Meilisearch outage must produce a recoverable synchronization failure.

The crawler should return useful diagnostics rather than silently ignoring failures.

## 16. Configuration and Dependency Injection

Integrate the crawler using the existing dependency injection architecture.

Suggested logical components:

```text
FileCrawler
DirectoryEnumerator
FileFilter
FileFingerprintService
CrawlStateRepository
FileSynchronizationService
DeletionReconciler
```

These names are illustrative.

Reuse existing interfaces and services.

Avoid unnecessary architectural layers.

### Configuration

Possible options:

```json
{
  "FileCrawling": {
    "MaxConcurrentFiles": 2,
    "QueueCapacity": 32,
    "MaxFileSizeMB": 100,
    "FollowDirectoryLinks": false
  }
}
```

Respect existing application settings and persistence conventions.

Do not introduce a second configuration system.

## 17. Manual Execution for Development

Provide a straightforward way to execute a crawler without the administration UI.

A small development command or test harness is sufficient.

Example conceptual usage:

```text
lantern crawl --path "C:\Documents"
```

This is illustrative.

Do not introduce a full CLI framework unless the repository already uses one.

The execution method should:

1. Accept or resolve a crawler configuration.
2. Start a single crawl.
3. Report summary counts.
4. Report failures.
5. Exit or return an appropriate result.

Do not implement recurring schedules.

Do not implement browser-based manual execution controls yet.

## 18. Unit Tests

Add tests for:

1. Windows path normalization.
2. UNC path normalization.
3. Deterministic document identities.
4. Extension filtering.
5. Directory exclusions.
6. File-size filtering.
7. Recursive enumeration.
8. Nonrecursive enumeration.
9. Symbolic link and junction handling.
10. Fingerprint comparison.
11. New-file detection.
12. Changed-file detection.
13. Unchanged-file detection.
14. Deleted-file candidate detection.
15. Cancellation behavior.
16. Incomplete-scan handling.
17. Error isolation.
18. Bounded concurrency behavior.

Use temporary directories where practical.

Do not depend on arbitrary user files.

## 19. SQLite Integration Tests

Use actual SQLite databases.

Test:

1. Initial crawl-state migration.
2. Fingerprint persistence.
3. State persistence across DbContext instances.
4. Successful synchronization updates.
5. Failed synchronization preserving previous state.
6. Repeated indexing idempotency.
7. Scan identifier tracking.
8. Deleted-file reconciliation.
9. Interrupted scan recovery.
10. Concurrent state update correctness.

Use isolated temporary databases.

Do not modify application production data during tests.

## 20. End-to-End Crawler Tests

Create deterministic filesystem fixtures.

Include:

```text
TestDocuments/
    |
    +-- manual.pdf
    +-- report.docx
    +-- presentation.pptx
    +-- workbook.xlsx
    +-- notes.txt
    +-- README.md
    |
    +-- nested/
    |      +-- document.pdf
    |
    +-- excluded/
           +-- ignored.txt
```

Test against a real Meilisearch instance where available.

### Required scenarios

**Initial crawl**

- Discover supported files.
- Extract content.
- Generate chunks.
- Index documents and chunks.
- Verify keyword search.
- Verify hybrid search when enabled.

**Second crawl without changes**

- Detect unchanged files.
- Skip unnecessary extraction.
- Skip unnecessary indexing.
- Preserve document and chunk counts.

**Modified file**

- Detect the change.
- Update extracted content.
- Update searchable chunks.
- Remove obsolete chunks.
- Preserve the deterministic parent document ID.

**Deleted file**

- Detect a deleted file after a complete scan.
- Remove the parent document.
- Remove associated chunks.
- Update SQLite state.

**Extraction failure**

- Preserve the last successfully indexed version.
- Record the failure.
- Allow retry on a later scan.

**Canceled scan**

- Stop safely.
- Preserve completed state.
- Avoid deletion reconciliation.

**Incomplete enumeration**

- Report incomplete discovery.
- Avoid deleting unseen documents.

**Repeated execution**

- Do not accumulate duplicate documents or chunks.

### UNC tests

Where a suitable Windows network share is available, verify UNC crawling.

If no UNC test environment is available:

- Test path normalization and boundary behavior independently.
- Document the missing environment.
- Do not claim real UNC integration testing was completed.

## 21. Regression Testing

Run existing tests from Phases 0–4.

Verify that:

- Search UI remains functional.
- Administration authentication remains functional.
- SQLite configuration persists.
- Document extraction remains functional.
- Generic and structure-aware chunking still work.
- Embedding server remains functional.
- Hybrid search remains functional.
- Existing indexing behavior is preserved.

Do not modify unrelated features to simplify crawler implementation.

Do not claim tests passed unless they actually ran.

## 22. Performance Considerations

Prioritize correctness before throughput optimization.

Measure representative behavior when possible:

- Files discovered per second.
- Files processed per second.
- Initial crawl duration.
- Incremental crawl duration.
- Number of skipped unchanged files.
- Document extraction throughput.
- Indexing throughput.
- Peak memory usage.
- Meilisearch task completion latency.

Do not invent benchmark results.

### Important optimization

Incremental crawling should avoid expensive work for unchanged files.

The expected sequence is:

```text
File discovered
       |
       v
Fingerprint comparison
       |
       +-- Unchanged
       |       |
       |       v
       |      Skip
       |
       +-- Changed
               |
               v
             Extract
               |
               v
              Chunk
               |
               v
              Index
               |
               v
           Synchronize state
```

Do not perform document extraction or embedding generation before checking whether the file changed.

## 23. Architecture Guidelines

Suggested logical organization:

```text
Lantern.Core
    |
    +-- FileCrawlerConfiguration
    +-- FileCrawlResult
    +-- FileFingerprint
    +-- CrawlState models
    +-- Crawler interfaces

Lantern.Crawling
    |
    +-- Files/
    |     +-- Enumeration
    |     +-- Filtering
    |     +-- Fingerprinting
    |     +-- Synchronization
    |     +-- Reconciliation
    |
    +-- FileCrawler

Lantern.Infrastructure
    |
    +-- SQLite crawl state
    +-- EF Core migrations
    +-- Existing Meilisearch integration

Lantern.Web
    |
    +-- Existing administration
    +-- Dependency injection
    +-- Configuration
```

This structure is illustrative.

Use the existing repository architecture.

Do not create unnecessary projects or assemblies.

## 24. Out of Scope

Do not implement:

- Browser-based crawler creation.
- Browser-based crawler editing.
- Crawler administration forms.
- Persistent execution queues.
- Recurring scheduling.
- Cron expressions.
- Job history UI.
- Manual execution buttons in the administration interface.
- Web crawling.
- HTTP page discovery.
- Filesystem watchers.
- Real-time indexing notifications.
- OCR.
- Legacy Office document support.
- Per-document Windows ACL synchronization.
- Windows service installation.
- Distributed crawler workers.
- RAG or chat interfaces.

These features belong to subsequent phases or deferred enhancements.

## 25. Acceptance Criteria

Phase 5 is complete only when the following conditions are satisfied.

### Filesystem discovery

- Local directories can be crawled.
- Recursive enumeration works.
- File and directory filtering works.
- UNC paths are supported.
- Invalid roots are rejected safely.
- Filesystem errors are handled.

### Incremental synchronization

- New files are indexed.
- Modified files are updated.
- Unchanged files are skipped.
- Deleted files are removed after safe reconciliation.
- Repeated scans do not create duplicates.
- Failed updates preserve previous successful state.

### Document processing

- Supported documents use the Phase 4 extraction pipeline.
- Extracted content is chunked correctly.
- Chunk metadata is preserved.
- Parent documents and child chunks remain associated.

### Meilisearch

- Parent documents are indexed where required.
- Searchable chunks are indexed.
- Old chunks are reconciled.
- Relevant asynchronous indexing tasks are tracked.
- Indexing failures are surfaced.
- Keyword and hybrid search remain functional.

### SQLite

- Fingerprints are persisted.
- Crawl state survives application restart.
- Failed operations can be retried.
- Interrupted scans do not cause accidental deletions.
- Migrations are tracked.

### Reliability

- Cancellation is supported.
- Concurrency is bounded.
- Resource use is controlled.
- Ordinary file errors do not crash the crawler.
- Unavailable roots do not trigger mass deletion.
- Incomplete enumeration prevents unsafe reconciliation.

### Testing

- `dotnet build` succeeds.
- Existing tests pass.
- New unit tests pass.
- SQLite integration tests pass.
- Real Meilisearch integration tests pass where available.
- UNC integration limitations are documented if testing is unavailable.

Clearly identify unmet or unverified criteria.

## 26. Deliverables

Provide:

1. File crawler implementation.
2. Directory enumeration service.
3. File filtering and path normalization.
4. File fingerprint and change detection.
5. SQLite crawl-state entities and migration.
6. Incremental synchronization service.
7. Document extraction pipeline integration.
8. Chunk indexing integration.
9. Safe deleted-file reconciliation.
10. Cancellation and bounded concurrency.
11. Structured crawl results and diagnostics.
12. Development execution mechanism.
13. Unit and integration tests.
14. Updated architecture and developer documentation.

Document:

- How to configure a file crawler.
- How to execute a single crawl.
- How incremental synchronization works.
- How changed and deleted files are handled.
- How crawl state is persisted.
- How failure recovery works.
- UNC permission requirements.
- Known limitations.

## 27. Required Completion Report

At the end of implementation, provide:

### A. Implementation summary

Describe the completed crawler functionality.

### B. Files changed

List created and modified files.

### C. Architectural decisions

Explain:

- Directory enumeration strategy.
- Path normalization.
- Fingerprint strategy.
- SQLite state representation.
- Document synchronization.
- Chunk replacement.
- Deleted-file reconciliation.
- Concurrency and cancellation.
- Failure recovery.

### D. Build and test commands

List commands actually executed.

### E. Test results

Clearly distinguish:

- Passed tests.
- Failed tests.
- Skipped tests.
- Tests not executed.

### F. Performance observations

Report any measured crawl performance.

Do not fabricate performance data.

### G. Known limitations

Document limitations, especially:

- Metadata-based fingerprint accuracy.
- Network share availability.
- Filesystem permissions.
- Nontransactional Meilisearch synchronization.
- Native extraction cancellation.
- Deferred administration and scheduling features.

### H. Remaining work

Identify incomplete acceptance criteria.

Do not begin Phase 6 automatically.

---

## Final Instructions

Implement Phase 5 directly in the existing Lantern repository.

Inspect the completed Phases 0–4 before modifying code.

Reuse the existing document extraction, structure-aware chunking, indexing, and SQLite infrastructure.

Support local Windows directories and UNC network shares.

Use incremental file fingerprinting to avoid unnecessary extraction and embedding work.

Ensure that new, changed, and deleted files are reflected correctly in Meilisearch.

Preserve the last valid searchable version when extraction or indexing fails.

Never interpret incomplete filesystem enumeration as evidence that documents were deleted.

Keep execution bounded, cancelable, and recoverable.

Do not implement browser-based crawler management or scheduling.

**The primary success condition is a reliable and repeatable local file crawler that synchronizes filesystem documents and their searchable chunks with Meilisearch while preserving consistency across failures, cancellations, and subsequent scans.**

Do not implement Phase 6 or later features automatically.
