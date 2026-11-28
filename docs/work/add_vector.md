# Lantern — Implement Missing Chunk Indexing and Hybrid Search

## Objective

The previous Meilisearch security and credential refactoring instructions incorrectly treated Chunk Indexing and Hybrid Search as already implemented features requiring regression verification.

**They are not currently implemented.**

Correct this assumption and implement both features as part of Lantern's core search architecture.

The implementation must integrate with the newly defined role-based Meilisearch credentials:

- Search API Key
- Indexing API Key
- Admin or Internal Maintenance API Key
- Master Key for credential management only

The target is a fully functional, offline-capable, cross-platform document search system that supports both keyword and hybrid retrieval.

Do not mark Chunk Indexing or Hybrid Search as completed merely because related configuration or interfaces exist.

---

## 1. Inspect the Existing Implementation

Before writing code, investigate:

- Document extraction pipeline.
- Document normalization.
- Existing chunking utilities, if any.
- Meilisearch document indexing.
- Meilisearch index settings.
- Search services and request models.
- Search result models.
- Local embedding REST server.
- LlamaPure integration.
- Granite embedding model.
- Category filtering.
- Crawler synchronization.
- Background jobs.
- Role-based Meilisearch client factory.
- Existing database and migration mechanisms.
- Web search UI.

Determine what is already functional and what is missing.

Distinguish between:

1. Implemented and tested.
2. Partially implemented.
3. Configured but nonfunctional.
4. Not implemented.

Do not reimplement working functionality unnecessarily.

## 2. Define the Index Architecture

Use two logical indexes.

### Parent Document Index

Suggested name:

```text
lantern-documents
```

Responsibilities:

- Document identity.
- Source location.
- Title.
- Document metadata.
- Categories.
- File type.
- Modification timestamps.
- Keyword search over appropriate parent content.
- Document-level filtering.

### Chunk Index

Suggested name:

```text
lantern-chunks
```

Responsibilities:

- Chunk identity.
- Parent document identity.
- Chunk text.
- Chunk order.
- Structural metadata.
- Source location metadata.
- Category information required for filtering.
- Embedding vectors.
- Hybrid and semantic retrieval.

Use the existing index names if they are already established.

Do not introduce incompatible names without a migration.

### Important

Do not assume Meilisearch performs joins between indexes.

Lantern must explicitly map chunk results to parent documents.

## 3. Implement Structure-Aware Chunking

Introduce or complete a reusable document chunking service.

Requirements:

- Preserve meaningful paragraph boundaries.
- Prefer section or heading boundaries.
- Support configurable chunk size.
- Support bounded overlap where beneficial.
- Use the embedding model's actual tokenizer or an appropriately validated token-counting strategy.
- Avoid arbitrary character-count assumptions.
- Avoid empty or whitespace-only chunks.
- Preserve Japanese and English content.
- Support cancellation.
- Bound memory consumption.

### Input formats

Use the normalized extraction output for:

- PDF.
- DOCX, XLSX, PPTX.
- Legacy DOC, XLS, PPT where implemented.
- ODT, ODS, ODP where implemented.
- HTML.
- General text.
- Source code.

Do not build separate incompatible chunking pipelines for each format.

### Structure preservation

Retain available:

- PDF page.
- Presentation slide.
- Spreadsheet sheet and cell region.
- Section heading.
- Paragraph.
- Source code file and line range.
- Web page section.

Do not invent metadata that the extractor did not provide.

## 4. Implement Chunk Identity

Each chunk must have a stable, deterministic identifier.

Use a design that supports:

- Parent document association.
- Idempotent recrawling.
- Document updates.
- Chunk replacement.
- Document deletion.
- Crash recovery.

For example:

```text
parentDocumentId
chunkId
chunkOrdinal
contentFingerprint
```

The exact identifier scheme must be documented.

Do not use random IDs that cause duplicate chunks on every crawl.

Be aware that chunk boundaries may change when document content changes.

Design synchronization to remove obsolete chunks safely.

## 5. Implement Chunk Indexing

Extend the existing crawler indexing pipeline:

```text
Document Discovered
        |
        v
Extract Content
        |
        v
Normalize Content
        |
        v
Generate Chunks
        |
        v
Synchronize Parent Document
        |
        v
Synchronize Chunk Documents
        |
        v
Verify Meilisearch Tasks
        |
        v
Mark Indexing Complete
```

### Requirements

- Create the chunk index through authorized internal maintenance operations.
- Configure primary keys and appropriate index settings.
- Insert chunk documents using the Indexing API Key.
- Update changed chunks.
- Remove obsolete chunks.
- Remove chunks when their parent document is deleted.
- Preserve category assignments.
- Preserve source metadata.
- Avoid duplicate chunks.
- Handle partial indexing failures.
- Respect Meilisearch asynchronous task completion.

### Critical requirement

Submitting a document update to Meilisearch does not mean indexing has succeeded.

Track the resulting task and handle failed tasks.

Do not mark a crawler document as successfully synchronized until the required indexing operations complete successfully.

## 6. Implement Incremental Synchronization

Integrate chunk indexing with existing crawler fingerprints.

Requirements:

- Skip unchanged content where safe.
- Avoid unnecessary extraction.
- Avoid unnecessary re-embedding.
- Update chunks when content changes.
- Reconcile obsolete chunks.
- Preserve correct parent-child relationships.
- Recover after interrupted indexing.
- Avoid deleting valid chunks after an incomplete crawl.

### Category changes

Category changes must update searchable metadata without unnecessarily regenerating embeddings.

Do not re-extract or re-embed unchanged content solely because its category assignment changed.

## 7. Integrate the Existing Embedding Server

Use the local LlamaPure-based embedding service.

The intended model is:

```text
IBM Granite Embedding 97M Multilingual R2
```

The model artifact is expected to be configured from the existing GGUF resources.

Inspect actual model configuration rather than hardcoding filenames unnecessarily.

### Requirements

- Verify the model loads on Windows and Linux.
- Verify output dimensionality.
- Validate the configured dimension against actual inference output.
- Verify Japanese and English embeddings.
- Handle empty input.
- Handle oversized input.
- Handle batching.
- Respect cancellation.
- Bound embedding concurrency.
- Avoid loading duplicate model instances.

The existing design expects 384-dimensional embeddings; confirm this against the actual configured model and runtime.

Do not silently replace the embedding model.

## 8. Choose the Meilisearch Embedding Integration

Evaluate the supported Meilisearch version and select one coherent integration strategy.

### Preferred candidate: REST Embedder

Configure Meilisearch to communicate with Lantern's local embedding REST service.

Use the supported `rest` embedder configuration.

Validate:

- Endpoint URL.
- Request format.
- Response format.
- Vector dimensions.
- Document text template.
- Search query embedding.
- HTTP authentication where required.
- Indexing task behavior.

### Alternative: User-Provided Vectors

If the existing architecture makes application-side embedding substantially more reliable, evaluate `userProvided` embeddings.

In that case, Lantern must explicitly generate and submit document and query vectors.

### Important

Do not mix these two embedding ownership models unintentionally.

Do not both generate vectors in Lantern and request Meilisearch to generate the same vectors unless a specific, tested design requires it.

Document the selected model clearly.

Use APIs and settings supported by the pinned Meilisearch version.

## 9. Configure the Chunk Embedder

Create the embedding configuration for the chunk index.

Requirements:

- Stable embedder name.
- Correct vector dimensions.
- Correct chunk text selection.
- No unintended embedding of large metadata blobs.
- Correct handling of Japanese text.
- Correct settings updates.
- Safe handling of existing indexes.
- No unnecessary re-embedding after application restart.

### Security

Index settings updates must use an appropriately authorized Admin or Internal Maintenance client.

Ordinary Search credentials must not modify embedder settings.

The embedding endpoint should remain local and protected according to the established trust model.

## 10. Implement Search Modes

Expose three logical search modes.

### Keyword

Traditional Meilisearch full-text search.

Must work without the embedding server.

### Semantic

Vector-based semantic retrieval.

Must use the configured embedding model.

### Hybrid

Combine full-text and semantic retrieval using Meilisearch's supported hybrid search API.

Support a configurable semantic ratio.

Conceptual request:

```json
{
  "q": "example query",
  "hybrid": {
    "embedder": "lantern-embedding",
    "semanticRatio": 0.5
  }
}
```

This is illustrative. Match the actual configured embedder name and pinned API version.

### Requirements

- Keyword mode must preserve existing search behavior.
- Semantic mode must genuinely use vectors.
- Hybrid mode must genuinely combine lexical and semantic retrieval.
- Do not label keyword-only results as hybrid.
- Do not silently ignore the requested search mode.

## 11. Implement Parent Document Result Aggregation

Chunk results are not final document results.

Implement a document-level retrieval and aggregation layer.

Conceptual flow:

```text
Search Query
      |
      v
Search Chunk Index
      |
      v
Ranked Chunk Hits
      |
      v
Group by Parent Document
      |
      v
Select Best Matching Chunks
      |
      v
Fetch Parent Metadata
      |
      v
Return Ranked Documents
```

### Requirements

- Group by stable parent document ID.
- Avoid duplicate parent results.
- Retain relevant chunk snippets.
- Preserve source location.
- Preserve category filters.
- Preserve document title and URL/path.
- Provide deterministic ranking behavior.
- Support pagination without losing expected results.

### Ranking

Define a clear and documented aggregation policy.

For example:

- Highest-ranked matching chunk determines initial document ranking.
- Additional matching chunks may provide supporting snippets.
- Repeated chunks from the same document must not overwhelm results unintentionally.

Do not invent score equivalence between independent indexes.

If combining parent keyword results with chunk results, define and test a separate merge strategy rather than assuming raw scores are directly comparable.

## 12. Integrate Categories and Filters

Preserve the existing category architecture.

Requirements:

- Parent documents retain category assignments.
- Chunk documents carry the category metadata needed for filtering.
- Keyword search respects category filters.
- Semantic search respects category filters.
- Hybrid search respects category filters.
- Category changes reconcile chunk metadata.
- Category filtering does not cause unnecessary re-embedding.

Categories are search organization metadata, not access-control permissions.

Do not use categories as a substitute for authorization.

## 13. Implement Search UI

Inspect the existing Blazor search interface.

Add or complete the search mode selection.

Suggested modes:

```text
Keyword | Hybrid | Semantic
```

The exact UI should match existing conventions.

### Requirements

- Preserve normal keyword search.
- Show meaningful document results.
- Display relevant matched snippets.
- Link results to the correct source.
- Display page, slide, worksheet, or code location when available.
- Preserve category filters.
- Provide understandable error states.

### Hybrid availability

If embedding is not configured or unavailable:

- Keep Keyword Search functional.
- Clearly indicate that semantic and hybrid modes are unavailable.
- Do not silently return keyword results while claiming hybrid mode succeeded.

## 14. Integrate Role-Based API Keys

This implementation must follow the new credential architecture.

### Search API Key

Used for:

- Parent document search.
- Chunk search.
- Hybrid search.
- Semantic search.

### Indexing API Key

Used for:

- Parent document updates.
- Chunk insertion.
- Chunk updates.
- Chunk deletion.
- Required indexing task inspection.

### Admin / Internal Maintenance API Key

Used for:

- Index creation.
- Primary key and settings configuration.
- Embedder configuration.
- Schema reconciliation.

### Master Key

Used only for authorized API key management.

### Important

Do not solve missing permissions by replacing every scoped API key with the Master Key.

Use the minimum required Meilisearch actions.

Verify both permitted and forbidden operations.

## 15. Handle Errors and Partial Availability

Differentiate:

- Document extraction failure.
- Chunk generation failure.
- Embedding model failure.
- Embedding REST request failure.
- Meilisearch authentication failure.
- Meilisearch indexing failure.
- Missing embedder configuration.
- Search mode unavailable.
- Parent/chunk synchronization failure.

### Requirements

- Do not retry permanent errors indefinitely.
- Use bounded retries for transient failures.
- Preserve useful persistent state.
- Avoid marking partial indexing as complete.
- Allow safe recovery and reindexing.
- Keep keyword search usable when vector infrastructure fails, where supported.

## 16. Existing Documents and Backfill

Existing Lantern installations may contain parent documents but no chunks.

Provide a safe migration or backfill mechanism.

Requirements:

- Detect documents missing chunks.
- Reuse stored extracted content where available.
- Re-extract source documents only when necessary.
- Avoid duplicate parent documents.
- Avoid deleting existing indexes.
- Run backfill as a bounded, cancellable job.
- Persist progress.
- Support restart and retry.
- Report documents that cannot be reprocessed.

Do not automatically launch an unbounded full-library reindex during ordinary application startup.

Provide an explicit, manageable backfill workflow.

## 17. Automated Tests

Implement unit tests covering:

- Chunk boundaries.
- Token limits.
- Japanese text.
- Paragraph and heading preservation.
- Stable chunk identifiers.
- Incremental updates.
- Obsolete chunk removal.
- Parent-child relationships.
- Category propagation.
- Result aggregation.
- Ranking.
- Pagination.
- Search mode selection.
- Cancellation.

## 18. Real Integration Tests

Use real Meilisearch with authentication enabled.

Verify:

1. Parent index creation.
2. Chunk index creation.
3. Embedder configuration.
4. Parent document insertion.
5. Chunk insertion.
6. Embedding generation.
7. Vector storage.
8. Keyword search.
9. Semantic search.
10. Hybrid search.
11. Category filters.
12. Result aggregation.
13. Document updates.
14. Document deletion.
15. Repeated crawling.
16. Missing credentials.
17. Insufficient API permissions.
18. Embedding service failure.

### Critical requirement

Use the real Granite embedding model for release qualification.

Mock embeddings are acceptable for isolated unit tests, but they do not demonstrate functional Hybrid Search.

## 19. Windows and Linux Verification

Execute end-to-end tests on both platforms.

### Windows

Verify:

- LlamaPure native DLL loading.
- Granite inference.
- Authenticated Meilisearch indexing.
- Chunk synchronization.
- Hybrid retrieval.
- Visual Studio development execution.
- Windows Service operation where applicable.

### Linux

Verify:

- LlamaPure native `.so` loading.
- Granite inference.
- Authenticated Meilisearch indexing.
- Chunk synchronization.
- Hybrid retrieval.
- systemd operation where applicable.

Do not claim cross-platform support based only on successful compilation.

## 20. Documentation Updates

Update:

- Root README.
- Architecture documentation.
- Search documentation.
- Crawling documentation.
- Installation documentation.
- Developer documentation.
- Windows and Linux configuration documentation.
- English and Japanese documentation.

Explain:

- Parent and chunk indexes.
- Chunking behavior.
- Embedding configuration.
- Search modes.
- Semantic ratio.
- Search result aggregation.
- Category filtering.
- Backfill.
- Credential requirements.
- Failure recovery.

Do not describe Chunk Indexing or Hybrid Search as implemented until integration tests demonstrate functionality.

## 21. Acceptance Criteria

The implementation is complete when:

- Parent documents are indexed.
- Content is split into meaningful chunks.
- Chunk documents are indexed separately.
- Parent-child relationships are stable.
- Embedding vectors are generated and stored.
- Keyword search works.
- Semantic search works.
- Hybrid search works.
- Search results are aggregated by parent document.
- Relevant snippets and source locations are returned.
- Category filters work.
- Incremental crawling updates chunks.
- Deleted documents remove obsolete chunks.
- Existing documents can be backfilled.
- Role-based API key permissions are respected.
- Search works independently of administrator sessions.
- Windows and Linux integration tests pass.
- Documentation reflects the implementation.

## 22. Required Completion Report

Provide:

### A. Initial implementation status

Identify which parts were missing or incomplete.

### B. Architecture

Describe extraction, chunking, embedding, indexing, and retrieval.

### C. Meilisearch configuration

Report actual index names, embedder settings, dimensions, and relevant configuration.

### D. API key permissions

List actual required actions for Search, Indexing, and Maintenance roles.

### E. Files changed

List important additions and modifications.

### F. Tests

Report actual unit and integration test results.

### G. Search verification

Provide examples demonstrating that keyword, semantic, and hybrid modes are genuinely distinct and functional.

### H. Windows/Linux verification

Report which real platform tests were executed.

### I. Limitations

Document incomplete features, performance limitations, and unresolved issues.

---

## Final Instructions

Correct the previous assumption that Chunk Indexing and Hybrid Search already exist.

Implement them as new core Lantern features.

Use structure-aware chunking, stable parent-child identities, and a dedicated Meilisearch chunk index.

Integrate the existing local LlamaPure embedding server and Granite model.

Implement genuine keyword, semantic, and hybrid retrieval.

Aggregate chunk matches into document-level search results.

Preserve category filtering, incremental synchronization, document deletion, and existing search behavior.

Respect the role-based Meilisearch API key architecture.

Do not introduce cloud embedding dependencies, another vector database, or a separate authentication system.

Validate using real Meilisearch and the real embedding model on Windows and Linux.

Update all affected documentation and tests.

**The primary success condition is that Lantern can ingest documents, produce searchable chunks, generate local embeddings, execute genuine hybrid retrieval, and return correctly ranked parent documents with useful snippets and source locations.**
