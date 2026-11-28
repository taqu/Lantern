# Lantern — Phase 2 Implementation Instructions
## Search and Indexing Foundation

You are implementing **Phase 2: Search and Indexing Foundation** of the Lantern project.

Lantern is a lightweight, self-hosted full-text search platform built with .NET 10, Blazor, and Meilisearch Community Edition. Its primary target is Windows and intranet environments, without requiring Docker, Java, Node.js, or cloud services.

Phase 0 established the technical foundation and validated critical dependencies.

Phase 1 implemented a working search UI against an existing Meilisearch index.

**Your task is to establish the shared document model, indexing operations, index configuration, and integration tests required by future file and web crawlers.**

Do not implement features from subsequent phases.

---

## 1. Review the Existing Implementation

Before modifying the code:

1. Read `roadmap.md`, if present, and review the existing architecture documentation.
2. Inspect the solution structure and projects.
3. Review the Phase 1 search service, search UI, and Meilisearch configuration.
4. Examine the existing sample index and development seeding utility.
5. Identify reusable types, services, and dependency injection registrations.
6. Run the existing build and tests to establish a baseline.

Reuse existing functionality wherever practical.

Do not introduce parallel implementations of services that already exist.

Do not restructure the entire solution merely to accommodate Phase 2.

## 2. Scope and Objectives

Implement the following capabilities:

1. A normalized searchable document model.
2. Deterministic document identification.
3. File and web source representations.
4. Document upsert operations.
5. Document deletion operations.
6. Meilisearch index creation and configuration.
7. Text and metadata normalization.
8. Bounded batch ingestion.
9. Reliable asynchronous task completion handling.
10. A basic full-index rebuild operation.
11. Unit tests and integration tests against a real Meilisearch instance.

The resulting indexing services must be reusable by future crawlers without depending on the Blazor UI.

## 3. Document Model

Create a shared document model representing searchable content independently of its origin.

Use the following initial Meilisearch document schema:

```json
{
  "id": "stable-document-id",
  "sourceId": "source-identifier",
  "sourceType": "file",
  "title": "Example Document",
  "uri": "file:///C:/Documents/example.pdf",
  "path": "C:\\Documents\\example.pdf",
  "content": "Extracted full text",
  "extension": ".pdf",
  "mimeType": "application/pdf",
  "size": 12345,
  "modifiedAt": "2026-10-01T10:00:00Z",
  "indexedAt": "2026-10-01T10:05:00Z"
}
```

### Requirements

- `id` must uniquely and deterministically identify a document.
- `sourceId` identifies the originating source or crawler.
- `sourceType` distinguishes file and web documents.
- `title` is the human-readable display title.
- `uri` is the canonical source URI.
- `path` is the original or normalized filesystem path for file documents.
- `content` contains normalized searchable text.
- `extension` stores the normalized file extension when applicable.
- `mimeType` stores the document MIME type when known.
- `size` stores the source size in bytes when known.
- `modifiedAt` stores the source modification time when available.
- `indexedAt` stores the indexing timestamp.

Use nullable fields where information is genuinely unavailable.

Keep UTC timestamps consistent.

Avoid adding speculative metadata fields that are not needed in this phase.

The document model should be serializable to Meilisearch using predictable field names.

## 4. Deterministic Document Identification

Implement a reusable document ID generator.

The ID must be derived from:

- Source identity.
- Source type.
- Canonical document location.

Use a deterministic hash, such as SHA-256, encoded as a lowercase hexadecimal string.

Conceptually:

```text
Document ID =
    SHA256(
        sourceType +
        sourceId +
        canonicalLocation
    )
```

Use an unambiguous encoding of the individual components to avoid collisions caused by concatenation.

### File source identity

For file documents:

- Normalize path separators appropriately.
- Normalize absolute paths.
- Handle Windows drive-letter paths.
- Handle UNC paths.
- Preserve the distinction between different source definitions.
- Apply Windows-appropriate case handling without corrupting the actual display path.

Do not depend on the existence of the file when generating its ID.

### Web source identity

For web documents:

- Normalize scheme and host casing.
- Remove URL fragments.
- Normalize default HTTP/HTTPS ports.
- Preserve meaningful query parameters.
- Preserve path and query semantics unless equivalence is established.

Avoid aggressive URL normalization that might merge distinct resources.

### Required behavior

The same source document must always produce the same ID.

Documents with distinct source identities must not accidentally share the same ID.

Re-indexing must update the existing record instead of creating duplicates.

Include deterministic ID unit tests.

## 5. Document Normalization

Implement a focused normalization component for searchable documents.

Responsibilities include:

- Trimming and validating titles.
- Normalizing line endings.
- Removing invalid control characters.
- Preserving meaningful whitespace in extracted content.
- Normalizing extensions to lowercase.
- Handling missing MIME types and metadata.
- Converting timestamps to UTC.
- Validating required fields.
- Rejecting structurally invalid documents.

Preserve Japanese and English Unicode text.

Do not remove meaningful punctuation or aggressively rewrite document content.

Do not implement document extraction in this phase.

The normalization component must accept already-extracted content.

Avoid unnecessary allocations and repeated transformations for large document bodies.

## 6. Meilisearch Index Configuration

Implement a service responsible for ensuring that the required Meilisearch index exists and has the expected settings.

Use a configurable index name.

Define `id` as the primary key.

### Searchable attributes

Initially configure:

```text
title
content
```

### Filterable attributes

Initially configure:

```text
sourceId
sourceType
extension
mimeType
```

### Sortable attributes

Initially configure:

```text
modifiedAt
indexedAt
size
```

### Displayed attributes

Ensure that the metadata required by the Phase 1 search UI remains available.

Avoid hiding required result fields unintentionally.

### Requirements

- Index creation must be idempotent.
- Settings updates must be idempotent.
- Existing documents must not be deleted by routine initialization.
- Index configuration must use the official Meilisearch .NET SDK where practical.
- Index initialization must correctly handle asynchronous Meilisearch tasks.
- Search operations must not race against incomplete initial index configuration.

Do not automatically recreate the index every time the application starts.

Document any settings that may require reindexing when modified.

## 7. Document Indexing Service

Implement a reusable indexing service with operations conceptually equivalent to:

```csharp
Task UpsertAsync(
    SearchDocument document,
    CancellationToken cancellationToken);

Task UpsertBatchAsync(
    IReadOnlyCollection<SearchDocument> documents,
    CancellationToken cancellationToken);

Task DeleteAsync(
    string documentId,
    CancellationToken cancellationToken);

Task DeleteBatchAsync(
    IReadOnlyCollection<string> documentIds,
    CancellationToken cancellationToken);
```

These signatures are illustrative. Adapt them to the repository's existing conventions.

### Upsert requirements

- Validate and normalize incoming documents.
- Generate or verify deterministic document IDs.
- Use Meilisearch document upsert semantics.
- Preserve existing records when unrelated documents are indexed.
- Report validation and indexing errors clearly.
- Support cancellation where available.

### Deletion requirements

- Support deleting a document by ID.
- Support bounded batch deletion.
- Handle deletion of nonexistent IDs gracefully.
- Ensure failures are not silently ignored.

### Batch processing

Introduce configurable limits such as:

```text
MaxDocumentsPerBatch
MaxPayloadBytesPerBatch
```

Use conservative defaults.

A batch must be split before it exceeds either configured limit.

Estimate or measure serialized payload size in a consistent way.

If an individual document exceeds the configured payload limit, report it explicitly rather than repeatedly retrying it.

Avoid loading an entire large corpus into memory merely to build batches.

Keep ingestion concurrency bounded.

Do not implement an elaborate background job queue in Phase 2.

## 8. Meilisearch Asynchronous Task Handling

Meilisearch indexing, deletion, index creation, and settings updates may return asynchronous task identifiers.

A successful HTTP response does not necessarily mean that the requested operation has completed successfully.

Implement a shared task completion mechanism.

### Required behavior

1. Submit the operation.
2. Obtain the Meilisearch task identifier.
3. Wait or poll for completion.
4. Detect successful completion.
5. Detect failed tasks.
6. Enforce a configurable timeout.
7. Support cancellation.
8. Surface useful failure details.

Conceptually:

```text
Submit operation
      |
      v
Receive task UID
      |
      v
Poll task status
      |
      +-- succeeded --> return success
      |
      +-- failed -----> raise/report failure
      |
      +-- timeout ----> report timeout
      |
      +-- canceled ---> stop waiting
```

Use the official SDK's task-waiting support if it provides the required behavior.

Avoid tight polling loops.

Do not classify timeout or cancellation as confirmed indexing success.

Do not automatically retry non-idempotent operations without verifying their safety.

Provide sufficient diagnostics to identify the failed operation and task.

Avoid logging credentials, API keys, or full document contents.

## 9. Full-Index Rebuild

Implement a basic mechanism for rebuilding the complete search index from an explicitly supplied document collection.

The rebuild operation should:

1. Validate the incoming document source.
2. Create and configure an isolated temporary index.
3. Populate the temporary index in bounded batches.
4. Wait for indexing tasks to complete.
5. Verify successful completion before promotion.
6. Promote the rebuilt index using a supported Meilisearch mechanism.
7. Clean up obsolete temporary resources when safe.

### Safety requirements

- Do not delete the active index before successfully building its replacement.
- A failed rebuild must not destroy the active index.
- Do not report success before the final promotion operation completes.
- Protect against accidental concurrent rebuilds within the Lantern process.
- Report incomplete cleanup without hiding the primary failure.

Prefer Meilisearch index swapping if supported by the deployed version.

If index swapping is unavailable or unsuitable, document and implement an alternative with equivalent safety goals where practical.

If the existing search service assumes a fixed physical index name, preserve that logical contract.

Document how the rebuild interacts with concurrent searches and indexing operations.

For this phase, it is acceptable to serialize rebuilds and ordinary indexing operations rather than introducing complex concurrent-write reconciliation.

### Important limitation

Phase 2 does not yet have a crawler framework or persistent crawl-state database.

Therefore, the rebuild operation only needs to accept an explicitly supplied collection or stream of normalized documents.

Do not implement automatic rediscovery of source files or websites.

Do not implement SQLite-backed rebuild jobs.

## 10. Preserve Phase 1 Search Functionality

The existing search UI must continue to work.

Review its expectations for:

- Document titles.
- Result URLs.
- File paths.
- Excerpts.
- Document types.
- Pagination.
- Error handling.

Update the search service only as necessary to support the normalized schema.

If the Phase 1 development seeding utility creates documents using an older format, migrate it to the shared document model.

Ensure that:

- Seeded documents use deterministic IDs.
- Seed data is compatible with the production indexing service.
- Search results expose required metadata.
- Meilisearch credentials remain server-side.
- Search errors continue to produce useful messages.

Do not redesign the search page.

## 11. Configuration and Dependency Injection

Reuse existing ASP.NET Core configuration patterns.

Introduce only the options required by Phase 2, such as:

```json
{
  "Search": {
    "IndexName": "lantern-documents",
    "MaxDocumentsPerBatch": 100,
    "MaxPayloadBytesPerBatch": 4194304,
    "TaskTimeoutSeconds": 60
  }
}
```

These values are suggested initial defaults, not mandatory constants.

Follow existing repository naming and configuration conventions.

Register services using dependency injection.

Avoid static global service instances.

Keep Meilisearch master API keys and connection credentials on the server.

Do not introduce SQLite in this phase.

Persistent crawler settings and execution history belong to later phases.

## 12. Testing Requirements

Add meaningful unit and integration tests.

### Unit tests

Cover at least:

1. Deterministic document ID generation.
2. File path canonicalization.
3. UNC path handling.
4. Web URL canonicalization.
5. Source identity separation.
6. Document metadata normalization.
7. Japanese and English Unicode preservation.
8. Invalid document rejection.
9. Batch splitting by document count.
10. Batch splitting by payload size.
11. Oversized document handling.

### Meilisearch integration tests

Use a real Meilisearch Community Edition instance.

Do not rely exclusively on mocks.

Cover:

1. Index creation.
2. Index settings configuration.
3. Index initialization idempotency.
4. Single-document upsert.
5. Batch upsert.
6. Updating an existing document.
7. Verifying no duplicates after repeated indexing.
8. Searching indexed content.
9. Retrieving source metadata.
10. Single-document deletion.
11. Batch deletion.
12. Successful full-index rebuild.
13. Failed rebuild preserving the active index.
14. Asynchronous task failure reporting.

Use an isolated test index or dedicated test instance.

Do not run destructive tests against the user's production index.

If integration tests require an external Meilisearch executable, document the setup.

Prefer a native Meilisearch process without Docker.

Do not claim that integration tests passed unless they were actually executed.

## 13. Architecture Guidelines

Follow the existing project structure.

Use `Lantern.Core` for domain models and suitable interfaces, and `Lantern.Infrastructure` for Meilisearch-specific implementations, if those projects already exist.

Keep responsibilities clear:

```text
Lantern.Core
    |
    +-- SearchDocument
    +-- Document identity / normalization contracts
    +-- Indexing service contracts
    |
Lantern.Infrastructure
    |
    +-- Meilisearch index configuration
    +-- Meilisearch document indexing
    +-- Meilisearch task completion
    +-- Index rebuild implementation
    |
Lantern.Web
    |
    +-- Existing search UI
    +-- Dependency injection / configuration
```

This is a logical layout, not a request to create unnecessary projects or layers.

Prefer straightforward .NET implementations.

Avoid introducing:

- Distributed job frameworks.
- Message brokers.
- Microservices.
- Repository-wide architectural refactoring.
- Complex event-driven abstractions.
- Unnecessary third-party libraries.

Ensure the indexing layer can eventually be used by both file and web crawlers.

## 14. Out of Scope

Do not implement any of the following:

- SQLite configuration persistence.
- Administration dashboard.
- Authentication or user management.
- Local filesystem crawler.
- Web crawler.
- Toxy document extraction pipeline.
- File change detection.
- Persistent crawler state.
- Crawl scheduling.
- Background job management.
- OCR.
- Embeddings, semantic search, or RAG.
- Search UI redesign.
- Windows service installation or release packaging.

These belong to subsequent phases.

Do not start Phase 3 automatically.

## 15. Acceptance Criteria

Phase 2 is complete only when all of the following are satisfied.

### Document model

- A normalized document schema is implemented.
- File and web sources are represented correctly.
- Document IDs are stable and deterministic.
- Re-indexing does not introduce duplicates.

### Indexing

- Documents can be added.
- Existing documents can be updated.
- Documents can be deleted.
- Bounded batch indexing works correctly.
- Index settings are configured correctly.

### Reliability

- Asynchronous tasks are tracked to completion.
- Failed tasks are reported reliably.
- Timeout and cancellation paths are handled.
- A failed rebuild preserves the active index.
- No destructive operation silently wipes the existing index.

### Compatibility

- The Phase 1 search UI remains functional.
- Existing search behavior is preserved.
- Search results include normalized source metadata.
- No Meilisearch credential is exposed to the browser.

### Testing

- The solution builds successfully.
- Existing tests still pass.
- New unit tests pass.
- Real Meilisearch integration tests pass.
- Index creation, indexing, search, update, deletion, and rebuild behavior are verified.

## 16. Deliverables

Provide:

1. Normalized `SearchDocument` model.
2. Deterministic document ID generator.
3. Document normalization implementation.
4. Index configuration service.
5. Document indexing service.
6. Bounded batching implementation.
7. Asynchronous task completion handling.
8. Basic safe full-index rebuild operation.
9. Updated development seeding functionality, if required.
10. Unit and Meilisearch integration tests.
11. Documentation explaining the schema, document IDs, indexing operations, and rebuild behavior.

Update architecture documentation where meaningful.

Avoid producing documentation that merely repeats the code without explaining important decisions.

## 17. Required Completion Report

At the end of implementation, provide:

### A. Implementation summary

Describe completed functionality.

### B. Files changed

List created and modified files.

### C. Architectural decisions

Explain the important decisions, particularly:

- Document identity strategy.
- Source canonicalization.
- Index settings.
- Batching limits.
- Task completion behavior.
- Rebuild safety.

### D. Build and test commands

List the actual commands executed.

### E. Test results

Clearly distinguish:

- Passed tests.
- Failed tests.
- Tests not executed.

### F. Known limitations

Identify any remaining technical limitations.

### G. Remaining work

List incomplete acceptance criteria, if any.

Do not declare Phase 2 complete if required acceptance criteria remain unverified.

---

## Final Instructions

Implement Phase 2 directly in the existing repository.

Inspect and preserve the Phase 0 and Phase 1 implementation before making changes.

Choose the simplest reliable implementation that satisfies the requirements.

Use real Meilisearch behavior rather than simulated production functionality.

Keep changes focused, testable, and maintainable.

**The primary success condition is a reusable, reliable indexing foundation that future Lantern file and web crawlers can consume without redesigning document identity, index configuration, or indexing operations.**

Do not implement Phase 3 or later features.
