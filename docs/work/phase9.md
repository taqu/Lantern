# Lantern — Phase 9 Implementation Instructions
## Search Experience Refinement and Search Category Management

You are implementing **Phase 9: Search Experience Refinement and Search Category Management** of the Lantern project.

Lantern is a lightweight, self-hosted full-text search platform designed primarily for Windows and intranet environments.

The application uses:

- .NET 10 and ASP.NET Core.
- Blazor Web App with Interactive Server.
- Meilisearch Community Edition.
- SQLite with Entity Framework Core.
- LlamaPure and IBM Granite Embedding 97M Multilingual R2.
- Local REST-based embedding and hybrid search.
- Document extraction and structure-aware chunking.
- Incremental file and web crawling.
- Persistent crawler execution and scheduling.
- Browser-based crawler administration.

Phases 0–8 have been implemented. Phase 9 has not yet been implemented.

**Your task is to implement a production-facing, document-oriented search experience with administrator-managed search categories that can combine content from multiple file and web crawlers.**

This assignment replaces the original Phase 9 instructions.

Do not implement Phase 10 or later roadmap features.

---

## 1. Review the Existing Implementation

Before modifying the repository:

1. Read `roadmap.md` and existing architecture documentation.
2. Inspect the Phase 1 search UI and search services.
3. Review the Phase 2 document indexing and document ID strategy.
4. Review the Phase 3 SQLite persistence and administration infrastructure.
5. Review the Phase 3.5 hybrid search and chunk indexing implementation.
6. Review Phase 4 extraction metadata and source locations.
7. Review Phase 5 file crawler synchronization.
8. Review Phase 6 crawler configuration UI.
9. Review Phase 7 job execution and scheduling.
10. Review Phase 8 web crawler configuration and indexing.
11. Review Meilisearch index settings and filterable attributes.
12. Run existing builds and tests to establish a baseline.

The actual repository is the source of truth.

Reuse existing services, models, and infrastructure.

Avoid architectural refactoring unrelated to Phase 9.

Preserve all functionality from Phases 0–8.

## 2. Phase Objectives

Implement:

1. Persistent search category management.
2. Many-to-many crawler/category relationships.
3. Search category administration UI.
4. Category assignment in file and web crawler configuration.
5. Category metadata in parent documents and chunks.
6. Safe synchronization of category changes.
7. Category-based search filtering.
8. Google-style category navigation tabs.
9. Search result highlighting and snippets.
10. Parent-document result grouping.
11. Document-type, source-type, and crawler filtering.
12. Sorting and pagination.
13. Safe source navigation.
14. Search state stored in URLs.
15. Keyword and hybrid search integration.
16. Accessibility and performance improvements.
17. Automated tests.

Keep categories independent of file formats and crawler types.

A category must be able to combine content from multiple file and web crawlers.

## 3. Search Category Concepts

A search category represents a logical collection of searchable documents.

Examples include:

```text
All
Documents
News
Research Papers
Patents
Source Code
Technical Documentation
```

These are examples, not mandatory predefined categories.

### Important distinction

**Search Category**

A logical grouping selected by the user.

**Document Type**

A physical or logical file format, such as PDF, DOCX, HTML, or source code.

**Crawler**

A configured source that discovers and indexes documents.

These concepts must remain separate.

For example, the Research Papers category may contain:

```text
Research Papers
    |
    +-- arXiv Web Crawler
    |
    +-- Local Research Papers File Crawler
    |
    +-- Internal Research Reports File Crawler
```

A category must not be restricted to one document format or crawler type.

## 4. Search Category Data Model

Introduce a persistent category entity using SQLite.

Suggested model:

```text
SearchCategory
    |
    +-- Id
    +-- Key
    +-- Name
    +-- Description
    +-- SortOrder
    +-- Enabled
    +-- CreatedAt
    +-- UpdatedAt
```

### Field requirements

**Id**

Stable internal identifier.

**Key**

Stable, unique, URL-friendly category identifier.

Examples:

```text
papers
news
patents
source-code
```

**Name**

Human-readable display name.

**Description**

Optional category description.

**SortOrder**

Controls the order of search category tabs.

**Enabled**

Controls category visibility and availability.

**CreatedAt / UpdatedAt**

UTC timestamps.

### Requirements

- Persist categories in SQLite.
- Use EF Core migrations.
- Enforce unique category keys.
- Validate names and keys.
- Preserve stable category identities.
- Support category ordering.
- Support enabling and disabling categories.
- Preserve categories across application restarts.

Do not use category display names as stable identifiers.

### All category

The `All` search category should be a virtual search scope.

It must not require a physical database category record.

Selecting `All` means that no category restriction is applied.

Do not automatically assign every crawler to an `All` category.

## 5. Crawler and Category Relationships

Implement a many-to-many relationship.

Suggested structure:

```text
CrawlerDefinition
    |
    +-- CrawlerCategory[]
              |
              +-- SearchCategory
```

Suggested join entity:

```text
CrawlerCategory
    |
    +-- CrawlerId
    +-- CategoryId
```

### Requirements

- One crawler may belong to multiple categories.
- One category may contain multiple crawlers.
- Categories may contain both file and web crawlers.
- Relationships must persist in SQLite.
- Duplicate relationships must be prevented.
- Referential integrity must be enforced.
- Category assignment changes must be auditable through normal application logging.

### Example

| Crawler | Categories |
|---|---|
| arXiv | Research Papers |
| Local Papers | Research Papers |
| Internal Reports | Research Papers, Technical Documentation |
| Company Wiki | Documents |
| Git Repository | Source Code |
| Patent Website | Patents |

### Default assignment

Allow crawlers to have no explicit category.

Uncategorized documents must remain searchable through `All`.

Do not silently exclude uncategorized documents from global search.

### Category changes

Changing a crawler's category assignment must not alter:

- Crawler identity.
- Document identity.
- Chunk identity.
- File fingerprints.
- Web content fingerprints.
- Crawl schedules.
- Existing execution history.

Category assignment is metadata, not source identity.

## 6. Category Administration UI

Add an administrator-only category management section.

Suggested route:

```text
/admin/categories
```

Additional routes may include:

```text
/admin/categories/create
/admin/categories/{id}/edit
```

Follow existing routing conventions.

### Category list

Display:

- Category name.
- Category key.
- Description.
- Enabled status.
- Sort order.
- Number of assigned crawlers.
- Edit action.
- Delete or archive action.

Do not fabricate document counts.

### Category creation

Provide fields for:

```text
Name
Key
Description
Sort Order
Enabled
```

### Category editing

Allow administrators to:

- Rename a category.
- Change its description.
- Change its display order.
- Enable or disable it.
- Modify its stable key only through an explicitly validated operation, if supported.

Prefer immutable category keys after creation.

### Category deletion

Require confirmation.

Explain how assigned crawlers and indexed category metadata will be affected.

Do not automatically delete source documents or chunks when a category is deleted.

### Disabled categories

Disabled categories must not appear as selectable search tabs.

Define how direct URLs referencing disabled categories are handled.

Preserve underlying crawler assignments and indexed content unless an explicit synchronization operation changes them.

Do not confuse hiding a category with deleting its documents.

### Security

All category administration actions require administrator authorization.

Enforce authorization server-side.

Do not rely only on hidden UI controls.

## 7. Category Assignment in Crawler Configuration

Extend the existing Phase 6 and Phase 8 crawler configuration screens.

Add a multi-select category field.

Example:

```text
Edit Crawler

Name:
[ arXiv Papers                    ]

Start URL:
[ https://arxiv.org/             ]

Search Categories:

[x] Research Papers
[ ] News
[ ] Patents
[x] Technical Documentation

[ Save ]
```

The example is illustrative.

### Requirements

- Display available enabled categories.
- Support selecting multiple categories.
- Preserve selections during editing.
- Validate category identifiers.
- Persist relationships transactionally.
- Preserve unrelated crawler configuration.
- Do not restart Lantern after saving.
- Do not automatically execute a full crawl when categories change.

### Existing crawlers

Existing file and web crawler definitions must remain valid after migration.

Do not force all existing crawlers into a default category.

Allow administrators to assign categories to previously created crawlers.

### Configuration snapshots

Phase 7 jobs use configuration snapshots.

Ensure crawler category metadata can be resolved consistently during indexing.

Avoid allowing an old job configuration snapshot to overwrite more recent category assignments.

Prefer treating category membership as current indexing metadata rather than a permanent property of the source-content fingerprint.

Document the chosen consistency model.

## 8. Category Metadata in Meilisearch

Add category metadata to searchable parent documents and chunks.

Use a field such as:

```json
{
  "categoryIds": ["papers", "technical"]
}
```

Use the established stable category key or ID consistently.

Do not mix display names and identifiers.

### Parent document example

```json
{
  "id": "parent-document-id",
  "sourceId": "arxiv-crawler",
  "sourceType": "web",
  "categoryIds": ["papers"],
  "title": "Transformer Architecture",
  "uri": "https://example.org/paper",
  "content": "Extracted document content"
}
```

### Chunk example

```json
{
  "id": "chunk-id",
  "documentId": "parent-document-id",
  "sourceId": "arxiv-crawler",
  "categoryIds": ["papers"],
  "title": "Transformer Architecture",
  "content": "Attention mechanisms enable..."
}
```

### Requirements

- Parent documents and chunks must have consistent category metadata.
- Category metadata must be filterable.
- Category metadata must not alter deterministic document IDs.
- Category assignment must survive ordinary reindexing.
- Newly indexed documents must receive current crawler categories.
- Existing indexed documents must be updated when assignments change.

### Meilisearch configuration

Add:

```text
categoryIds
```

to the relevant index's filterable attributes.

Use existing index configuration services.

Wait for Meilisearch settings tasks to complete.

Do not recreate indexes unnecessarily.

Do not silently delete existing documents.

## 9. Category Synchronization

Implement synchronization between SQLite crawler/category assignments and Meilisearch document metadata.

**This is a critical requirement.**

### New documents

When a crawler indexes a new document:

1. Resolve the crawler's current categories.
2. Attach category identifiers to the parent document.
3. Attach the same categories to its searchable chunks.
4. Index the content normally.
5. Confirm Meilisearch task completion.

### Existing documents

When category assignments change:

1. Persist the new relationships in SQLite.
2. Identify indexed documents belonging to the crawler.
3. Update category metadata.
4. Update associated chunk metadata.
5. Track the asynchronous Meilisearch tasks.
6. Report synchronization success or failure.

### Avoid unnecessary reprocessing

Category-only changes must not require:

- File enumeration.
- HTTP crawling.
- PDF extraction.
- Office extraction.
- Text decoding.
- Chunk generation.
- Embedding inference.

Use metadata-only update operations where supported.

### Embedding regeneration

The Granite embedding input should not contain category metadata by default.

Verify that category updates do not unnecessarily regenerate embeddings.

Inspect Meilisearch's actual REST Embedder and document-update behavior.

Use an appropriate update strategy that preserves existing embeddings where possible.

Do not assume partial document updates always preserve vectors without verification.

### Synchronization failure

SQLite and Meilisearch do not share a transaction.

If metadata synchronization fails:

- Preserve the authoritative SQLite assignment.
- Record the failure.
- Keep the operation retryable.
- Do not falsely report complete synchronization.
- Do not delete unrelated document content.

### Crash recovery

If Lantern stops during category synchronization, the remaining updates must be recoverable.

A small persistent synchronization marker, version, or equivalent mechanism is acceptable.

Avoid introducing an entirely separate distributed job framework.

### Concurrent crawler execution

A crawler may be actively indexing while its category assignments change.

Prevent stale assignments from permanently overwriting newer metadata.

Use a documented ordering or versioning strategy.

Ensure eventual consistency with the current SQLite category assignments.

## 10. Category Filtering

Extend the search request model with an optional category identifier.

Conceptually:

```csharp
public sealed record SearchRequest(
    string Query,
    string? CategoryKey,
    SearchMode Mode,
    double SemanticRatio,
    SearchFilters Filters,
    SearchSortOrder Sort,
    int Page,
    int PageSize);
```

Adapt this to existing models.

### Search behavior

When no category is selected:

- Search all accessible documents.

When a category is selected:

- Restrict results to documents assigned to that category.

Use Meilisearch filters.

Example:

```json
{
  "q": "Transformer architecture",
  "filter": "categoryIds = 'papers'",
  "hybrid": {
    "embedder": "lantern-granite",
    "semanticRatio": 0.5
  }
}
```

The example illustrates the intended behavior.

Construct filter expressions safely using validated category identifiers.

Do not directly concatenate arbitrary user input into Meilisearch filter expressions.

### Multiple category membership

A document assigned to multiple categories must appear in each corresponding search scope.

It must appear only once within a single document-oriented search result page.

### Uncategorized documents

Uncategorized documents remain visible in `All`.

Do not automatically include them in unrelated named categories.

## 11. Google-Style Search Category Tabs

Add a horizontal category navigation bar to the existing search interface.

Suggested layout:

```text
Lantern

[ Search documents...                 ] [ Search ]

All | Documents | News | Research Papers | Patents | Source Code

Mode: [ Keyword | Hybrid ]

Filters:
[ Document Type ] [ Source Type ] [ Crawler ]

--------------------------------------------------

Search Results
```

These category labels are illustrative.

Load categories from SQLite.

### Requirements

- Display `All` first.
- Display enabled categories in configured order.
- Highlight the active category.
- Preserve the current query when switching categories.
- Reset pagination when the category changes.
- Update the URL.
- Keep tabs keyboard accessible.
- Handle large category lists without breaking the layout.

Do not hardcode category names into the search page.

### Tab semantics

Use accessible navigation or tab patterns consistently.

The controls must work using keyboard navigation.

Do not introduce excessive animations or complicated transitions.

### Disabled categories

Do not display disabled categories.

When a previously saved URL references a disabled or deleted category, show an understandable state or return to an explicitly communicated default.

Do not silently search an unintended category while showing another category as selected.

## 12. Search Request and Validation

Extend the existing search request abstraction.

Support:

```text
Query
Category
Search Mode
Semantic Ratio
Document Type
Source Type
Crawler
Sort Order
Page
Page Size
```

### Requirements

- Validate query length.
- Validate category identifiers.
- Validate search mode.
- Bound semantic ratio.
- Validate document-type filters.
- Validate source-type filters.
- Validate crawler identifiers.
- Validate sorting.
- Bound pagination.
- Preserve existing keyword and hybrid behavior.

The category filter must combine predictably with other filters.

For example:

```text
Category = Research Papers
Source Type = Files
Document Type = PDF
```

This should return PDF documents from file crawlers assigned to Research Papers.

Do not expose raw Meilisearch filter syntax as a user-editable field.

## 13. Search Result Presentation

Refine the search UI into a conventional document-oriented results page.

### Requirements

- Display document titles.
- Display representative snippets.
- Display document type.
- Display source information.
- Display modification time when available.
- Display crawler name where useful.
- Display category context when useful.
- Preserve matching chunk locations.

Avoid displaying raw internal index records.

### Suggested result model

```text
DocumentSearchResult
    |
    +-- DocumentId
    +-- Title
    +-- SourceId
    +-- SourceType
    +-- SourceUri
    +-- DisplayPath
    +-- Extension
    +-- ModifiedAt
    +-- CategoryIds[]
    +-- RepresentativeSnippet
    +-- MatchingChunks[]
```

Reuse existing result models where possible.

Do not add fields solely for speculative future features.

## 14. Parent-Document Result Grouping

Lantern uses chunk-level retrieval.

Search results must remain document-oriented.

### Requirements

- Group chunks by parent document ID.
- Display each parent once per results page.
- Select a representative matching chunk.
- Preserve additional useful matching chunks.
- Maintain relevant ranking order.
- Preserve category and source metadata.

### Ranking

Prefer the highest-ranked matching chunk as the representative result.

Do not invent unsupported numerical relevance scores.

### Pagination correctness

Grouping already paginated chunk results can produce incorrect document-level pagination.

Do not simply retrieve 20 chunks, group them, and claim the result contains 20 documents.

Evaluate Meilisearch-supported distinct or grouping capabilities, or another bounded strategy that preserves correct semantics.

Document limitations explicitly.

### Counts

Do not confuse chunk hit counts with unique document counts.

Display exact document counts only when actually available.

Otherwise communicate approximate or limited counts accurately.

## 15. Search Result Highlighting

Use Meilisearch-supported highlighting features where practical.

### Requirements

- Highlight literal query matches.
- Preserve contextual snippets.
- Support Japanese and English text.
- Avoid excessively long excerpts.
- Avoid breaking Unicode text.
- Handle semantic matches without literal keyword overlap.

### Security

Do not render raw extracted HTML.

Treat formatted search results as untrusted.

Prefer safe text-and-highlight rendering.

If sanitized markup is required, allow only narrowly defined highlighting elements.

Do not display scripts or arbitrary HTML attributes.

### Hybrid search

Semantic results may not contain exact query terms.

Do not invent literal highlights.

Display the relevant chunk as a normal contextual snippet.

## 16. Document-Type Filtering

Support filtering by document type.

Examples:

```text
All Types
PDF
Word
Excel
PowerPoint
HTML
Text
Source Code
```

Reuse existing format classification from Phase 4.

Do not create a conflicting extension mapping.

### Requirements

- Support document type filtering.
- Support source-code and general-text categories.
- Handle extensionless text documents.
- Combine document-type filters with search categories.
- Use safe Meilisearch filter construction.

Document type filtering must not replace logical search categories.

## 17. Source and Crawler Filtering

Support:

### Source type

```text
All Sources
Files
Websites
```

### Crawler

Allow selecting one or more configured crawlers where the existing UI design permits.

Use stable crawler identifiers.

Display human-readable crawler names.

### Category interaction

When a category is selected, crawler filter options may be restricted to crawlers assigned to that category.

Do not silently clear unrelated filters without updating the visible search state.

Preserve consistent query semantics.

## 18. Search Modes

Support:

```text
Keyword
Hybrid
```

### Keyword

Use Meilisearch full-text search.

Keyword mode must remain available when the embedding server is offline.

### Hybrid

Use the existing Granite REST Embedder configuration.

Support the existing semantic ratio.

Do not implement custom ranking fusion.

### Failure behavior

If Hybrid Search is unavailable:

- Display an understandable error.
- Preserve query and filters.
- Keep Keyword mode available.
- Do not silently label keyword-only results as hybrid results.

### Category compatibility

Category filtering must work in both Keyword and Hybrid modes.

Do not create separate category semantics for semantic search.

## 19. Sorting

Support useful sorting options where available.

Suggested options:

```text
Relevance
Newest First
Oldest First
Title
```

### Requirements

- Relevance is the default.
- Sorting must be compatible with active filters.
- Sorting must behave consistently with document grouping.
- Missing metadata must be handled predictably.
- Unsupported sorting options must not be displayed.

Avoid introducing a custom reranking engine.

Document how explicit sorting affects relevance ranking.

## 20. Pagination

Implement document-oriented pagination.

### Requirements

- Support previous and next navigation.
- Display current page.
- Bound page size.
- Preserve active category and filters.
- Avoid duplicated documents across pages.
- Handle invalid page requests.
- Reset to page one when the category, query, or filters change.

Suggested defaults:

```text
PageSize = 20
MaximumPageSize = 100
```

Use existing defaults if different.

Do not load the entire index to produce one page.

Avoid unbounded overfetching.

Do not display inaccurate total page counts.

## 21. Search State in URLs

All significant search state must be represented in URL query parameters.

Example:

```text
/search?q=transformer&category=papers&mode=hybrid&page=1
```

Additional parameters may represent:

```text
type
source
crawler
sort
semanticRatio
```

### Requirements

- Shared URLs restore search state.
- Browser back/forward navigation works.
- Category changes update the URL.
- Filters update the URL.
- Search mode updates the URL.
- Pagination updates the URL.
- Invalid parameters are handled safely.
- Sensitive values are not stored in URLs.

Do not store search state exclusively in Blazor component memory.

Avoid navigation loops caused by URL synchronization.

## 22. Source Navigation

Provide safe navigation to original documents.

### Web sources

- Display original URLs.
- Open validated HTTP/HTTPS links.
- Reject unsafe URL schemes.
- Preserve source identity.

### File sources

- Display Windows or UNC paths.
- Provide Copy Path.
- Open the original document only where a safe supported mechanism exists.
- Do not create arbitrary server-side file access endpoints.

### Location metadata

Display available information:

- PDF page number.
- PPTX slide number.
- XLSX worksheet name.
- DOCX section title.
- Source-code line range.
- HTML section heading.

Do not fabricate unsupported deep links.

Distinguish informational locations from actual navigable destinations.

## 23. Category Changes and Running Crawlers

Category configuration can change while crawlers are running.

Define a safe consistency model.

### Requirements

- Existing crawler jobs continue safely.
- Category edits do not cancel unrelated crawling.
- A stale job snapshot must not permanently overwrite new category assignments.
- Newly indexed content must eventually receive current categories.
- Category synchronization must be retryable.
- Category changes must not alter file or web fingerprints.

### Recommended approach

Resolve current category assignments during indexing or immediately before committing searchable metadata.

Use a persistent category assignment version or equivalent mechanism where needed.

If concurrent changes occur, perform a final metadata reconciliation after the relevant indexing operation.

Avoid duplicating crawler execution merely to update category metadata.

## 24. Performance Considerations

Category filtering must use Meilisearch indexes rather than application-side full-result filtering.

### Requirements

- Configure `categoryIds` as filterable.
- Avoid loading all matching documents into application memory.
- Avoid N+1 SQLite queries for category names.
- Cache stable category metadata when appropriate.
- Bound search result payloads.
- Do not return embedding vectors to the browser.
- Avoid unnecessary re-embedding.
- Avoid unbounded synchronization work.

### Measurements

Where practical, record:

- Keyword search latency.
- Hybrid search latency.
- Category-filtered search latency.
- Result grouping overhead.
- Pagination behavior.
- Category synchronization throughput.
- Memory usage.

Do not invent benchmark results.

## 25. Security

All category administration operations are administrator-only.

### Requirements

- Enforce server-side authorization.
- Validate category keys.
- Validate crawler assignments.
- Prevent unauthorized configuration changes.
- Protect state-changing requests against CSRF.
- Avoid raw Meilisearch filter injection.
- Avoid rendering untrusted HTML.
- Protect Meilisearch credentials.
- Avoid exposing sensitive crawler configuration.

### Search authorization

Preserve existing search access boundaries.

Do not introduce per-document Windows ACL synchronization.

A category is a search organization mechanism, not an access-control boundary.

**Do not treat category filtering as authorization.**

Users must not gain access to restricted content merely by changing category URL parameters.

## 26. Database Migration and Existing Data

Phase 9 must work with existing SQLite and Meilisearch data.

### SQLite migration

Add:

```text
SearchCategories
CrawlerCategories
```

or equivalent entities.

Use tracked EF Core migrations.

Do not delete existing crawler definitions.

Do not reset execution history.

Do not recreate the entire database.

### Meilisearch migration

Add category metadata and filter settings safely.

Existing documents without category assignments must remain searchable in `All`.

### Existing crawler migration

Existing crawlers should initially have no explicit categories unless valid assignments already exist.

Administrators may assign them through the new UI.

### Reconciliation

When existing crawlers receive categories, synchronize their already indexed documents and chunks.

Do not require full recrawling.

Do not require full re-extraction.

Do not silently trigger complete embedding regeneration.

### Validation

Test migration using a database containing preexisting crawlers, jobs, and settings.

Ensure existing search results remain available after migration.

## 27. Unit Tests

Add tests covering:

1. Category key validation.
2. Category name validation.
3. Duplicate category rejection.
4. Category ordering.
5. Enabled/disabled behavior.
6. Many-to-many relationship validation.
7. Duplicate assignment prevention.
8. Category assignment removal.
9. Category filter generation.
10. Combined filters.
11. Uncategorized documents.
12. Invalid category URL handling.
13. Search request validation.
14. Semantic ratio validation.
15. Document grouping.
16. Representative snippet selection.
17. Sorting.
18. Pagination.
19. Highlight safety.
20. Source navigation validation.

## 28. SQLite Integration Tests

Use actual SQLite databases.

Test:

1. Category creation.
2. Category editing.
3. Category deletion or archival.
4. Relationship persistence.
5. Multiple categories per crawler.
6. Multiple crawlers per category.
7. Unique key constraints.
8. Existing crawler migration.
9. Configuration persistence across restart.
10. Concurrent category updates.
11. Synchronization state persistence.
12. Administrator authorization where applicable.

Do not rely solely on EF Core's in-memory provider.

## 29. Meilisearch Integration Tests

Use a real Meilisearch instance.

Test:

1. Category filterable attribute configuration.
2. Parent document category metadata.
3. Chunk category metadata.
4. Keyword category filtering.
5. Hybrid category filtering.
6. Multiple category membership.
7. Uncategorized global search.
8. Category assignment updates.
9. Category removal.
10. Repeated metadata synchronization.
11. No duplicate documents.
12. Existing vector preservation.
13. No unnecessary embedding regeneration.
14. Failed synchronization recovery.
15. Concurrent crawler indexing and category updates.

### Embedding regeneration test

Verify the actual behavior rather than assuming it.

Where possible, instrument embedding requests or inspect Meilisearch tasks to determine whether category-only changes caused new embedding generation.

Record the observed result.

Do not claim that metadata-only updates preserve vectors unless validated.

## 30. Search UI Tests

Test:

1. Category tabs load from SQLite.
2. `All` appears first.
3. Categories follow configured order.
4. Disabled categories are hidden.
5. Category switching changes results.
6. Query is preserved when switching tabs.
7. Pagination resets appropriately.
8. URL state is updated.
9. Shared URLs restore category selection.
10. Keyboard navigation works.
11. Document type filters work.
12. Source type filters work.
13. Crawler filters work.
14. Keyword/Hybrid switching works.
15. Results are grouped by parent document.
16. Highlight rendering is safe.
17. Empty and error states are understandable.

Use existing Blazor component testing conventions.

## 31. End-to-End Test Scenarios

Create a deterministic test dataset.

### Research Papers

Configure:

```text
Category: Research Papers

Crawler A:
    Type: Web
    Source: arXiv-like fixture website

Crawler B:
    Type: File
    Source: Local paper PDF directory
```

Verify:

- Both crawlers belong to Research Papers.
- Documents from both sources appear in the same category.
- Keyword search works.
- Hybrid search works.
- Source filters distinguish local and web results.
- Parent-document grouping remains correct.

### Multiple category membership

Assign one crawler to:

```text
Research Papers
Technical Documentation
```

Verify its documents appear in both categories.

### Category reassignment

Change a crawler's categories.

Verify:

- Existing indexed documents are updated.
- Chunk metadata is synchronized.
- Document IDs remain unchanged.
- File fingerprints remain unchanged.
- No unnecessary extraction occurs.
- Embeddings are preserved where supported and verified.

### Category deletion

Delete or archive a category.

Verify:

- Its tab disappears.
- Underlying documents remain indexed.
- Other category assignments remain intact.
- `All` search still works.

### Existing data migration

Start from a pre-Phase-9 database and search index.

Verify that categories can be introduced without destroying existing data.

### Search experience

Verify:

- Highlighted keyword matches.
- Semantic result snippets.
- Combined filters.
- Correct source metadata.
- Source location presentation.
- URL state restoration.
- Browser navigation.
- Safe error handling.

## 32. Regression Tests

Run existing Phase 0–8 tests.

Verify:

- Administrator authentication.
- SQLite persistence.
- File crawler configuration.
- Web crawler configuration.
- Job scheduling.
- Job cancellation.
- Job history.
- Incremental synchronization.
- PDF and Office extraction.
- Source-code and general-text extraction.
- UTF-8 handling.
- Structure-aware chunking.
- Meilisearch indexing.
- Embedding server operation.
- Keyword search.
- Hybrid search.

Do not modify unrelated functionality to simplify category implementation.

Do not claim that tests passed unless they were actually executed.

## 33. Architecture Guidelines

Suggested logical organization:

```text
Lantern.Core
    |
    +-- SearchCategory
    +-- CrawlerCategory
    +-- SearchRequest
    +-- SearchFilters
    +-- DocumentSearchResult

Lantern.Infrastructure
    |
    +-- SQLite category persistence
    +-- Category assignment services
    +-- Category metadata synchronization
    +-- Meilisearch filtering
    +-- Search result grouping

Lantern.Crawling
    |
    +-- Existing file crawler
    +-- Existing web crawler
    +-- Category metadata integration

Lantern.Web
    |
    +-- Admin/Categories
    +-- Crawler category selection
    +-- Search category tabs
    +-- Search filters
    +-- Result presentation
    +-- Pagination
```

This structure is illustrative.

Adapt to the actual repository.

Do not create unnecessary assemblies.

Avoid large architectural changes.

## 34. Out of Scope

Do not implement:

- Document-level automatic category classification.
- Machine learning-based categorization.
- Hierarchical category trees.
- Category inheritance.
- Automatic cross-crawler document deduplication.
- Tags or arbitrary user labels.
- Per-document category overrides.
- Per-document ACL synchronization.
- Category-based authorization.
- New crawler types.
- OCR.
- RAG.
- Chat interfaces.
- Generative answer summaries.
- Reranking models.
- Embedding model replacement.
- Advanced search analytics.
- Saved searches.
- Notifications.
- Distributed search.
- Windows packaging.

These are deferred enhancements.

## 35. Acceptance Criteria

Phase 9 is complete only when all required conditions are satisfied.

### Categories

- Categories are persisted in SQLite.
- Administrators can create, edit, reorder, enable, disable, and safely delete or archive categories.
- Crawlers can belong to multiple categories.
- Categories can contain multiple file and web crawlers.
- Existing crawlers remain valid after migration.
- Uncategorized documents remain searchable.

### Category synchronization

- New indexed documents receive current category metadata.
- Existing documents update when crawler assignments change.
- Parent documents and chunks remain consistent.
- Category-only changes do not require recrawling.
- Category-only changes do not require re-extraction.
- Unnecessary re-embedding is avoided and verified.
- Failed synchronization is recoverable.
- Concurrent crawler indexing does not permanently restore stale assignments.

### Search UI

- Google-style category tabs are available.
- Category tabs load dynamically.
- `All` searches across all categories.
- Category selection filters results.
- Keyword and Hybrid modes both support categories.
- Search state is preserved in URLs.
- Category navigation is keyboard accessible.

### Search experience

- Results are presented by parent document.
- Relevant snippets are visible.
- Keyword highlighting is safe.
- Source metadata is understandable.
- Document type filtering works.
- Source type filtering works.
- Crawler filtering works.
- Sorting works where supported.
- Pagination semantics are accurate or transparently qualified.
- Source navigation is safe.

### Compatibility

- Existing search indexes are preserved.
- Existing crawler IDs are preserved.
- Existing document and chunk IDs are preserved.
- Existing job history is preserved.
- Existing file and web crawlers work.
- Existing keyword and hybrid search remain functional.

### Testing

- The solution builds.
- Existing tests pass.
- New unit tests pass.
- SQLite integration tests pass.
- Meilisearch integration tests pass.
- Search UI tests pass.
- Category synchronization tests pass.
- End-to-end multi-source category tests pass.

Unverified acceptance criteria must be reported explicitly.

## 36. Deliverables

Provide:

1. Search category data model.
2. Crawler/category join model.
3. SQLite migrations.
4. Category administration UI.
5. Category CRUD operations.
6. Category ordering and visibility.
7. Category selection in crawler configuration.
8. Category metadata in parent documents.
9. Category metadata in search chunks.
10. Category synchronization service.
11. Safe category reassignment handling.
12. Meilisearch category filtering.
13. Search category navigation tabs.
14. Updated search request model.
15. Refined search result presentation.
16. Highlighting and snippets.
17. Parent-document grouping.
18. Document-type, source-type, and crawler filters.
19. Sorting and pagination.
20. URL search state.
21. Safe source navigation.
22. Automated tests.
23. Updated documentation.

Document:

- Category concepts.
- Category and crawler relationships.
- Category assignment.
- Category filtering.
- Metadata synchronization.
- Re-embedding behavior.
- Existing data migration.
- Search UI behavior.
- Search state URLs.
- Result grouping.
- Pagination limitations.
- Security boundaries.
- Known limitations.

## 37. Required Completion Report

At the end of implementation, provide:

### A. Implementation summary

Describe completed category management and search experience functionality.

### B. Files changed

List important files created and modified.

### C. Architectural decisions

Explain:

- Category identity.
- Many-to-many crawler relationships.
- SQLite schema.
- Category assignment lifecycle.
- Meilisearch metadata representation.
- Synchronization and failure recovery.
- Embedding preservation.
- Search category navigation.
- Filter construction.
- Document grouping.
- Pagination and counts.
- Source navigation.

### D. Build and test commands

List commands actually executed.

### E. Test results

Clearly distinguish:

- Passed tests.
- Failed tests.
- Skipped tests.
- Tests not executed.

### F. Performance observations

Report measured search latency and synchronization performance where available.

Do not invent benchmarks.

### G. Known limitations

Document limitations involving:

- Category synchronization consistency.
- Concurrent indexing.
- Metadata-only update behavior.
- Search grouping.
- Document-level pagination.
- Browser file navigation.
- Semantic highlighting.
- Automatic document classification.
- Cross-crawler duplicate documents.

### H. Remaining work

Identify incomplete acceptance criteria and their impact.

Do not begin Phase 10 automatically.

---

## Final Instructions

Implement the revised Phase 9 directly in the existing Lantern repository.

Preserve all completed functionality from Phases 0–8.

Introduce administrator-managed search categories as independent SQLite entities.

Allow each crawler to belong to multiple categories.

Allow one category to combine multiple file and web crawlers.

Persist category membership in SQLite and synchronize category metadata into Meilisearch parent documents and searchable chunks.

Do not change document identities when categories change.

Do not recrawl, re-extract, or unnecessarily regenerate embeddings for metadata-only category changes.

Use Meilisearch category filtering for both keyword and hybrid search.

Provide Google-style search category tabs populated dynamically from administrator configuration.

Keep `All` as a virtual global search scope.

Refine the search interface with safe highlighting, document-oriented results, filters, sorting, pagination, URL state, and source navigation.

Maintain a clear distinction between categories, document types, crawlers, and authorization.

Ensure existing SQLite data, search indexes, crawler configurations, and job history survive the migration.

Verify critical behavior with real SQLite and Meilisearch integration tests.

**The primary success condition is that an administrator can create a logical search category such as Research Papers, assign both an arXiv web crawler and a local PDF crawler to it, and allow users to search their combined content through one category tab using either keyword or hybrid search—without duplicating indexes, changing document identities, or unnecessarily regenerating embeddings.**

Do not implement Phase 10 or later features automatically.
