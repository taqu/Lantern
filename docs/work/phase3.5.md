# Lantern — Phase 3.5 Implementation Instructions
## Local Embedding Server, Document Chunking, and Hybrid Search Foundation

You are implementing **Phase 3.5: Local Embedding and Hybrid Search Foundation** of the Lantern project.

Lantern is a lightweight, self-hosted full-text search platform built with .NET 10, ASP.NET Core, Blazor Interactive Server, Meilisearch Community Edition, and SQLite.

Lantern targets Windows x64 and intranet environments without requiring Docker, Java, Node.js, external database servers, or cloud-based inference services.

Phases 0 through 3 have already been implemented.

Your task is to add a reusable embedding server, text chunking foundation, and optional hybrid search support while preserving all existing functionality.

**Do not implement Phase 4 document extraction or subsequent crawler features.**

---

## 1. Existing Architecture and Dependencies

The current application already provides:

- A working search UI.
- Meilisearch indexing and search services.
- A normalized document model.
- Deterministic document IDs.
- SQLite persistence.
- Administration pages.
- Administrator authentication and authorization.
- Application configuration and health monitoring.

Before changing anything:

1. Read `roadmap.md` and existing documentation.
2. Inspect the implemented services and dependency injection setup.
3. Examine existing Meilisearch index management.
4. Review the current document identity strategy.
5. Review SQLite schema and migrations.
6. Inspect existing administrator settings and search UI.
7. Execute existing builds and tests.
8. Preserve established patterns and conventions.

Do not assume the repository exactly matches the original roadmap.

**The actual implementation is the source of truth.**

Reuse existing services and models wherever possible.

## 2. Technology Requirements

Use the following components.

| Component | Technology |
|---|---|
| Runtime | .NET 10 |
| Embedding engine | LlamaPure |
| Native backend | llama.cpp |
| Embedding model | IBM Granite Embedding 97M Multilingual R2 |
| Model format | GGUF |
| Model location | `resources/granite-embedding-97M-multilingual-r2.gguf` |
| Embedding dimensions | 384 |
| REST server | ASP.NET Core |
| Search backend | Meilisearch Community Edition |
| Hybrid search | Meilisearch native hybrid search |
| Metadata database | Existing SQLite database |
| Background execution | .NET hosted services |
| Request coordination | System.Threading.Channels |

References:

- https://github.com/taqu/LlamaPure
- https://github.com/ibm-granite/granite-embedding-models
- https://www.meilisearch.com/docs/capabilities/hybrid_search/how_to/configure_rest_embedder

### LlamaPure implementation status

The LlamaPure library has already been modified by the project author.

Its embedding output has been verified to match the output produced by the official `llama-embedding` utility.

Treat this as an established validation result.

Do not independently rewrite embedding pooling, token extraction, or llama.cpp inference logic without identifying a concrete compatibility problem.

Inspect the actual LlamaPure API before integration.

The public GitHub repository may not contain the latest local modifications. Prefer the validated local library implementation where available.

Do not silently replace the validated implementation with an older NuGet package or repository version.

## 3. Implementation Scope

Implement the following features:

1. Local Embedding REST API.
2. Dedicated LlamaPure inference worker.
3. Bounded embedding request queue.
4. GGUF model initialization and lifecycle management.
5. Embedding health and readiness reporting.
6. Generic text chunking service.
7. Document and chunk identity models.
8. Chunk-level Meilisearch indexing.
9. Meilisearch REST Embedder configuration.
10. Keyword and hybrid search modes.
11. Parent-document search result grouping.
12. Basic hybrid search administration settings.
13. Unit and integration tests.
14. Documentation.

Do not implement PDF, DOCX, XLSX, or PPTX parsing.

The chunking service must accept text already extracted from a document.

## 4. Embedding Server Architecture

Implement an embedding REST server running inside the Lantern application process.

The server must use a separate loopback HTTP listener from the main application interface.

Suggested endpoint:

```text
http://127.0.0.1:7701
```

The embedding API must not be accessible from remote network clients.

### Architecture

```text
Meilisearch
    |
    | HTTP POST /embed
    v
Embedding REST Endpoint
    |
    v
Bounded Channel<EmbeddingRequest>
    |
    v
Dedicated Inference Worker
    |
    v
LlamaPureClient
    |
    v
Granite Embedding 97M GGUF
```

### Execution requirements

- Run the embedding listener in the Lantern process.
- Execute synchronous native inference outside HTTP request handlers.
- Use a dedicated long-running worker where necessary.
- Do not assume `BackgroundService.ExecuteAsync` creates a dedicated OS thread.
- Serialize access to `LlamaPureClient` initially.
- Use bounded request queues.
- Avoid busy waiting.
- Support graceful application shutdown.
- Dispose model resources safely.

Do not create a separate Windows executable or microservice.

Reuse ASP.NET Core infrastructure where practical, but keep the embedding endpoint isolated from the normal search and administration routes.

## 5. Embedding REST API

Implement:

```http
POST /embed
Content-Type: application/json
```

Request:

```json
{
  "text": "This is an example document."
}
```

Response structure:

```json
{
  "embedding": [0.012, -0.034, 0.056]
}
```

The example vector is shortened. Actual vectors must contain exactly 384 finite floating-point values.

### Validation

Reject:

- Missing text.
- Invalid JSON.
- Empty or whitespace-only input.
- Oversized requests.
- Inputs exceeding the configured model context limit.
- Requests that cannot be processed within resource limits.

Use appropriate HTTP status codes.

Do not return success with an empty or invalid vector.

Do not serialize `NaN` or infinity values.

### Health endpoints

Implement lightweight health and readiness checks.

Suggested routes:

```text
GET /health
GET /ready
```

Distinguish:

- HTTP listener is running.
- Model is successfully loaded.
- Inference worker is ready.
- Model initialization failed.
- Service is stopping.

Do not load the model merely to answer a health request.

Do not expose sensitive filesystem details.

## 6. LlamaPure Model Lifecycle

Use:

```text
resources/granite-embedding-97M-multilingual-r2.gguf
```

as the initial development model path.

Resolve relative paths against a documented, deterministic application root rather than the current working directory.

Validate that the model exists before initialization.

### Requirements

1. Initialize the model once.
2. Reuse the loaded model across requests.
3. Avoid concurrent native inference until thread safety is verified.
4. Validate output dimensions.
5. Validate finite vector values.
6. Detect model initialization errors.
7. Dispose resources during shutdown.
8. Avoid repeated model loading for individual requests.

### Embedding correctness

Confirm that the integrated library uses the validated embedding implementation.

Verify:

- Expected 384-dimensional vectors.
- Correct pooling behavior.
- Correct normalization.
- Correct tokenization behavior.
- Consistent outputs for identical inputs.
- Correct Japanese and English text handling.

Use the model's intended embedding behavior.

Do not add arbitrary instruction prefixes to queries or documents without model-specific justification.

Preserve the model's expected input formatting.

### Model availability

If the model is missing or cannot be loaded:

- Log an actionable diagnostic.
- Mark embedding readiness as unavailable.
- Disable hybrid search operations.
- Preserve keyword search.
- Keep ordinary Lantern administration functionality available.

Do not crash the complete application unnecessarily.

## 7. Inference Queue and Concurrency

Implement a bounded request queue using `System.Threading.Channels`.

Suggested initial configuration:

```text
InferenceWorkers = 1
QueueCapacity = 32
```

Use an appropriate bounded-channel full mode.

Do not permit unbounded memory growth.

### Requirements

- Exactly one inference worker initially.
- HTTP handlers enqueue work without performing native inference.
- Concurrent requests are serialized safely.
- Full queues produce bounded waiting or explicit overload responses.
- Cancellation is propagated to queued requests.
- Canceled queued requests must not produce incorrect successful responses.
- Application shutdown stops accepting new work.
- Outstanding requests receive appropriate completion or failure signals.

If LlamaPure cannot interrupt an inference already executing inside native code, document that limitation.

Do not claim immediate cancellation of native inference unless it is actually supported.

### Resource safety

Ensure that canceled or timed-out requests do not leave unresolved completion tasks or retained request buffers.

Do not allow native inference exceptions to permanently terminate request processing without a clear unavailable state.

## 8. Text Chunking Foundation

Implement a reusable text chunking service.

The service must be independent of:

- PDF extraction.
- Office extraction.
- File crawling.
- Web crawling.
- Blazor UI components.
- Meilisearch-specific APIs.

### Default configuration

```text
TargetChunkTokens = 512
MaximumChunkTokens = 768
OverlapTokens = 64
```

These are initial values and should be configurable.

### Chunking strategy

Prefer natural structural boundaries:

1. Headings.
2. Paragraphs.
3. Sentences.
4. Token boundaries when necessary.

Avoid splitting words or Unicode characters incorrectly.

Preserve Japanese and English text.

Normalize insignificant whitespace without destroying meaningful structure.

Use the embedding model's tokenization, or a verified equivalent, to enforce token limits.

Do not assume a fixed correspondence between characters and tokens.

If the current LlamaPure API does not expose token counting, inspect whether a minimal tokenizer interface can be added safely. Do not implement a second incompatible tokenizer without validation.

### Overlap

Apply overlap where useful to preserve context across adjacent chunks.

Do not create overlapping chunks larger than the configured maximum.

Avoid unnecessary duplication when a natural section boundary already provides sufficient context.

### Special cases

Handle:

- Empty documents.
- Very short documents.
- Long paragraphs.
- Long Japanese text without whitespace.
- Repeated headings.
- Extremely large input documents.
- Content consisting primarily of whitespace.

Return no searchable chunks for completely empty input.

## 9. Document and Chunk Data Models

Preserve the existing Phase 2 document model.

Introduce a separate chunk representation.

Conceptually:

```text
SearchDocument
    |
    +-- DocumentId
    +-- SourceId
    +-- SourceType
    +-- Title
    +-- Canonical URI
    +-- Metadata
    |
    +-- SearchChunk[]
            |
            +-- ChunkId
            +-- DocumentId
            +-- ChunkIndex
            +-- Content
            +-- SectionTitle
            +-- PageNumber
            +-- StartOffset
            +-- EndOffset
```

These names are illustrative.

Adapt them to the existing codebase.

### Parent document identity

Continue using the existing deterministic document ID strategy.

Do not change parent IDs unnecessarily.

### Chunk identity

Implement deterministic chunk IDs derived from the parent document and chunk identity.

An initial implementation may use:

```text
ChunkId =
    SHA256(DocumentId + ChunkIndex)
```

Use an unambiguous encoding of the identity components.

Document that index-based IDs can change when earlier content is inserted.

Do not introduce complex content-defined chunking or deduplication algorithms at this stage.

### Chunk metadata

Preserve optional fields for future document extraction:

- Page number.
- Slide number.
- Sheet name.
- Section title.
- Text offsets.

Phase 3.5 does not need to populate all these fields.

Do not fabricate page or slide information for plain text.

## 10. Meilisearch Chunk Index

Introduce a searchable chunk representation in Meilisearch.

Prefer an isolated chunk index initially, such as:

```text
lantern-chunks
```

Keep the existing document index and search interface intact during development.

Do not automatically delete or destructively migrate the existing index.

### Example indexed chunk

```json
{
  "id": "stable-chunk-id",
  "documentId": "stable-parent-id",
  "sourceId": "company-documents",
  "sourceType": "file",
  "title": "Operations Manual",
  "content": "Backup recovery procedures...",
  "chunkIndex": 5,
  "sectionTitle": "Backup Recovery",
  "pageNumber": 12,
  "uri": "file:///C:/Documents/manual.pdf",
  "extension": ".pdf"
}
```

### Index settings

Configure suitable searchable attributes:

```text
title
sectionTitle
content
```

Configure filterable attributes:

```text
documentId
sourceId
sourceType
extension
```

Configure sortable attributes only when required by current functionality.

Preserve metadata necessary for returning useful results.

### Indexing operations

Implement:

- Chunk batch upsert.
- Chunk deletion.
- Deletion of all chunks belonging to a parent document.
- Safe replacement of a document's chunk set.
- Idempotent repeated indexing.

Reuse Phase 2 task completion handling and batching infrastructure where possible.

Do not create duplicate Meilisearch integration frameworks.

### Reconciliation safety

When replacing a document's chunks, avoid leaving stale chunks or deleting valid chunks after a failed replacement.

Use staged or carefully ordered operations with explicit failure handling.

Document the limitations of nontransactional Meilisearch updates.

## 11. Meilisearch REST Embedder

Configure the chunk index to use the local embedding server.

Initial embedder name:

```text
lantern-granite
```

Use Meilisearch REST Embedder.

The following configuration illustrates the intended protocol:

```json
{
  "lantern-granite": {
    "source": "rest",
    "url": "http://127.0.0.1:7701/embed",
    "dimensions": 384,
    "request": {
      "text": "{{text}}"
    },
    "response": {
      "embedding": "{{embedding}}"
    },
    "documentTemplate": "{{doc.title}}\n{{doc.sectionTitle}}\n{{doc.content}}",
    "documentTemplateMaxBytes": 8192
  }
}
```

Verify the configuration against the actual installed Meilisearch version.

The model's input must not exceed its configured context limit.

Note that `documentTemplateMaxBytes` is measured in bytes, not tokens.

Ensure the chunking stage limits model input correctly.

### Requirements

- Create or update embedder settings explicitly.
- Wait for the Meilisearch settings task to complete.
- Report failed settings tasks.
- Verify embedding generation against an actual index.
- Avoid repeated embedder configuration on every application startup.
- Do not enable the embedder when the inference service is unavailable.
- Do not trigger expensive full-index regeneration silently.

### Configuration changes

Treat the following as changes that may require re-embedding:

- Model replacement.
- Embedding dimensions.
- Embedding normalization behavior.
- Document template.
- Chunking parameters.
- Chunk content.

Document how to rebuild embeddings safely after these changes.

## 12. Hybrid Search Integration

Extend the existing search service to support:

```text
Keyword
Hybrid
```

Optionally expose semantic-only behavior through a semantic ratio of 1.0.

Use Meilisearch's native hybrid search functionality.

### Configuration

```text
EmbedderName = lantern-granite
DefaultSemanticRatio = 0.5
```

Support semantic ratios between 0.0 and 1.0.

Do not implement custom vector ranking fusion.

### Search compatibility

Preserve:

- Search query validation.
- Pagination.
- Existing filters where applicable.
- Error handling.
- Existing search result presentation.
- Server-side credentials.
- Authorization boundaries.

### Keyword mode

Keyword search must work when:

- Hybrid search is disabled.
- The model is missing.
- Embedding initialization fails.
- The embedding worker is temporarily unavailable.

Do not make the existing keyword search experience dependent on successful model initialization.

### Hybrid mode

When hybrid search is requested:

1. Validate that the embedding service is ready.
2. Validate embedder configuration.
3. Execute the Meilisearch hybrid query.
4. Return searchable chunk results.
5. Group results by parent document.
6. Preserve useful matching snippets and locations.

If hybrid search fails, report the failure appropriately.

Do not silently return keyword-only results while labeling them as hybrid results.

## 13. Parent-Document Result Grouping

Chunk-level retrieval can produce multiple hits from the same source document.

The existing Lantern search UI should remain primarily document-oriented.

Implement an initial parent-document grouping strategy.

### Requirements

- Group chunks using `documentId`.
- Display each parent document once within the returned result group.
- Choose a representative matching chunk.
- Preserve matching snippet information.
- Preserve page or section metadata when available.
- Avoid duplicate visible results from the same document.

### Ranking and pagination

Do not assume that grouping a paginated chunk result set produces correct document-level pagination.

For the initial implementation:

- Use a bounded candidate retrieval window.
- Group candidates explicitly.
- Apply a documented representative-hit ranking strategy.
- Clearly distinguish chunk hit counts from unique document counts.
- Do not present approximate unique-document counts as exact totals.

Document pagination and ranking limitations.

Do not build an elaborate custom ranking engine.

### Search UI

Add a minimal search mode control if compatible with existing UI conventions.

For example:

```text
Search Mode:

( ) Keyword
(*) Hybrid
```

Keep the original search presentation.

Avoid redesigning the search interface or introducing advanced search features.

## 14. Administration Integration

Extend the existing Phase 3 administration interface.

Provide a minimal Hybrid Search settings section.

### Configuration fields

- Hybrid Search enabled/disabled.
- Model path.
- Embedding server status.
- Model readiness.
- Embedder name.
- Default semantic ratio.
- Chunk target size.
- Chunk maximum size.
- Chunk overlap size.

Display the active embedding model and reported dimensions.

### Operational controls

Provide a safe way to:

- Check embedding server health.
- Test embedding generation.
- Validate Meilisearch embedder connectivity.
- Apply or verify embedder configuration.

Do not implement a complex model management interface.

### Configuration safety

Distinguish runtime-editable settings from settings requiring a restart or reindex.

Do not pretend a model change took effect when the existing model is still loaded.

Avoid changing the embedding model while requests are actively executing.

Do not implement arbitrary model uploads or downloads.

Preserve administrator authorization for all configuration operations.

## 15. Example Configuration

Use configuration resembling:

```json
{
  "Embedding": {
    "Enabled": false,
    "ModelPath": "resources/granite-embedding-97M-multilingual-r2.gguf",
    "ListenAddress": "127.0.0.1",
    "Port": 7701,
    "ContextSize": 4096,
    "Threads": 4,
    "InferenceWorkers": 1,
    "QueueCapacity": 32,
    "RequestTimeoutSeconds": 60
  },
  "Chunking": {
    "TargetTokens": 512,
    "MaxTokens": 768,
    "OverlapTokens": 64
  },
  "HybridSearch": {
    "Enabled": false,
    "EmbedderName": "lantern-granite",
    "ChunkIndexName": "lantern-chunks",
    "SemanticRatio": 0.5
  }
}
```

These values are initial development defaults.

Reuse the existing application configuration and SQLite settings infrastructure.

Do not introduce a competing settings system.

Keep deployment-sensitive settings in appropriate server-side configuration.

Persist only administrator-editable values that can be applied safely.

## 16. Testing Requirements

Implement meaningful unit and integration tests.

### Embedding tests

Test:

1. Model initialization.
2. Correct output dimensions.
3. Finite vector values.
4. Identical input consistency.
5. Japanese text.
6. English text.
7. Empty input rejection.
8. Oversized input rejection.
9. Model initialization failures.
10. Worker shutdown and resource disposal.
11. Concurrent request serialization.
12. Bounded queue behavior.
13. Queue cancellation and timeouts.

### Chunking tests

Test:

1. Empty documents.
2. Short documents.
3. Long paragraphs.
4. Japanese text without spaces.
5. English text.
6. Unicode preservation.
7. Chunk token limits.
8. Overlap behavior.
9. Stable chunk IDs.
10. Repeated chunk generation.
11. Section boundary handling.

### Meilisearch integration tests

Use a real Meilisearch Community Edition instance.

Test:

1. Chunk index creation.
2. Embedder configuration.
3. Embedding task completion.
4. Chunk indexing.
5. Keyword search.
6. Hybrid search.
7. Japanese semantic retrieval.
8. English semantic retrieval.
9. Cross-language retrieval.
10. Parent-document grouping.
11. Repeated indexing without duplicates.
12. Chunk deletion.
13. Safe document chunk replacement.
14. Missing embedding server behavior.
15. Keyword search availability when hybrid search is disabled.

Use isolated test indexes.

Do not modify or delete production indexes.

### Retrieval quality smoke tests

Create a small, deterministic multilingual retrieval corpus.

Include examples where:

- Query wording differs from document wording.
- Japanese queries retrieve relevant Japanese content.
- English queries retrieve relevant English content.
- Cross-language queries retrieve relevant documents.
- Irrelevant documents are present.

Compare keyword and hybrid search behavior.

Do not claim that hybrid search improves every query.

Record representative examples and observed results.

### Regression tests

Run existing Phase 0–3 tests.

Verify that:

- Existing search functions still work.
- Administration authentication still works.
- SQLite settings persist.
- Existing Meilisearch indexing remains functional.
- Hybrid Search disabled mode does not introduce regressions.

## 17. Performance and Reliability

Record basic performance observations:

- Model initialization time.
- Single-text embedding latency.
- Approximate throughput.
- Model memory usage.
- Indexing time for a small chunk dataset.
- Hybrid query latency.
- Queue behavior under concurrent requests.

Avoid premature optimization.

Initially prioritize correctness and predictable resource usage over maximum throughput.

Do not create an inference thread for every HTTP request.

Avoid repeatedly loading GGUF files.

Do not introduce GPU inference in this phase.

### Failure isolation

The following failures must not prevent ordinary keyword search:

- Missing model file.
- Invalid model.
- Native inference initialization failure.
- Embedding worker failure.
- REST embedding endpoint unavailable.
- Meilisearch embedder configuration failure.

Report failures in the administration interface where appropriate.

Do not conceal failed indexing tasks or present incomplete vector indexing as fully ready.

## 18. Security Requirements

The embedding server is an internal Lantern component.

### Network restrictions

- Bind only to loopback.
- Validate the configured listener address.
- Do not expose embedding endpoints through the public or intranet-facing listener.
- Avoid permissive CORS configuration.
- Do not expose the endpoint through proxy forwarding accidentally.

### Request safety

- Enforce request body limits.
- Enforce text/token limits.
- Use bounded queues.
- Apply timeouts.
- Avoid exposing native exception details.
- Do not log entire sensitive document contents.

### Administration

- Require authorization for embedding configuration changes.
- Keep Meilisearch credentials server-side.
- Do not expose arbitrary filesystem operations through model path settings.
- Validate model paths against approved server-side locations where practical.

## 19. Architecture Guidelines

Preserve the current repository organization.

Suggested logical structure:

```text
Lantern.Core
    |
    +-- SearchDocument
    +-- SearchChunk
    +-- Chunking options
    +-- Chunking contracts
    +-- Hybrid search contracts

Lantern.Infrastructure
    |
    +-- Meilisearch hybrid search
    +-- Chunk indexing
    +-- REST embedder configuration
    +-- Parent-document result grouping

Lantern.Embedding
    |
    +-- LlamaPure integration
    +-- Model lifecycle
    +-- Inference worker
    +-- Bounded request queue
    +-- Embedding REST endpoints

Lantern.Web
    |
    +-- Existing search UI
    +-- Search mode selection
    +-- Administration settings
    +-- Dependency injection
    +-- Application startup
```

This is a logical grouping, not a mandatory project structure.

Do not create an additional project unless it provides a clear benefit.

Avoid introducing unnecessary interfaces, repositories, factories, or abstraction layers.

## 20. Out of Scope

Do not implement:

- PDF parsing.
- DOCX parsing.
- XLSX parsing.
- PPTX parsing.
- File crawler integration.
- Web crawler integration.
- Automatic source discovery.
- Crawl scheduling.
- Persistent crawl jobs.
- OCR.
- RAG.
- Chat interfaces.
- Generative AI functionality.
- Reranking models.
- GPU inference.
- Distributed embedding workers.
- Automatic model downloads.
- Model marketplaces.
- Advanced vector database abstractions.
- Production Windows packaging.

These features belong to later phases or future enhancements.

## 21. Acceptance Criteria

Phase 3.5 is complete only when all required conditions are satisfied.

### Embedding server

- Lantern starts with the embedding server enabled.
- The Granite GGUF model loads successfully.
- The REST endpoint returns valid 384-dimensional embeddings.
- Japanese and English text are processed correctly.
- Inference is safely serialized.
- Queues and resource usage are bounded.
- Graceful shutdown is implemented.

### Chunking

- Generic text chunking works.
- Chunk sizes respect the configured token limits.
- Chunk IDs are deterministic.
- Parent-document relationships are preserved.
- Unicode text is handled correctly.

### Meilisearch

- The REST Embedder is configured successfully.
- Real embeddings are generated during indexing.
- Chunk-level keyword search works.
- Hybrid search works.
- Existing document indexing remains functional.
- Failed embedding tasks are reported accurately.

### Search UI

- Keyword mode remains available.
- Hybrid mode is optional.
- Parent-document grouping works.
- Relevant snippets are displayed.
- Existing search behavior is preserved.

### Administration

- Embedding status is visible.
- Hybrid settings can be configured safely.
- Administrative actions require authorization.
- Model and index configuration failures are reported.

### Testing

- The solution builds.
- Existing tests pass.
- New unit tests pass.
- Real model inference tests pass.
- Meilisearch hybrid integration tests pass.
- Regression tests confirm compatibility with Phases 0–3.

Clearly identify any unverified criteria.

## 22. Required Completion Report

At the end of implementation, provide:

### A. Implementation summary

Describe implemented functionality.

### B. Files changed

List important created and modified files.

### C. Architectural decisions

Explain:

- Embedding server hosting.
- Threading and inference queue.
- LlamaPure integration.
- Model lifecycle.
- Chunking strategy.
- Chunk identity.
- Meilisearch index design.
- REST Embedder integration.
- Search result grouping.
- Failure handling.

### D. Build and test commands

List commands actually executed.

### E. Test results

Distinguish passed, failed, skipped, and unexecuted tests.

### F. Performance observations

Report available measurements without inventing benchmark results.

### G. Known limitations

Document current limitations, including:

- CPU-only inference.
- Single inference worker.
- Generic text chunking.
- Search result grouping and pagination limitations.
- Model changes requiring re-embedding.
- Native inference cancellation limitations.

### H. Remaining work

Identify unfinished acceptance criteria.

Do not begin Phase 4 automatically.

---

## Final Instructions

Implement Phase 3.5 directly in the existing Lantern repository.

Preserve all existing Phase 0–3 functionality.

Use the locally validated LlamaPure embedding implementation.

Use the existing Granite GGUF model located at:

`resources/granite-embedding-97M-multilingual-r2.gguf`

Do not replace the model or introduce external inference providers.

Keep the embedding server inside the Lantern application process, but isolate its inference workload from normal HTTP request processing.

Use Meilisearch's native REST Embedder and hybrid search capabilities.

Keep hybrid search optional and preserve conventional keyword search.

Implement generic document chunking now, but defer PDF and Office-specific extraction and structural chunking to Phase 4.

Prefer simple, maintainable .NET implementations with predictable resource usage.

**The primary success condition is a fully functional local hybrid search pipeline: plain text → deterministic chunks → Meilisearch indexing → LlamaPure embeddings → hybrid retrieval → document-oriented search results.**

Do not implement subsequent roadmap phases automatically.
