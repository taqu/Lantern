# Lantern — Phase 8 Implementation Instructions
## Web Crawler and Incremental Web Content Indexing

You are implementing **Phase 8: Web Crawler** of the Lantern project.

Lantern is a lightweight, self-hosted full-text search platform for Windows and intranet environments.

The application uses:

- .NET 10 and ASP.NET Core.
- Blazor Web App with Interactive Server.
- Meilisearch Community Edition.
- SQLite with Entity Framework Core.
- LlamaPure and IBM Granite Embedding 97M Multilingual R2.
- Local REST-based embedding and hybrid search.
- Document extraction and structure-aware chunking.
- Incremental filesystem crawling.
- Persistent crawler jobs and scheduling.

Phases 0–7 have established the search interface, indexing infrastructure, administration foundation, local embeddings, document extraction, filesystem crawling, crawler configuration UI, and persistent job execution.

**Your task is to implement a secure, reliable web crawler capable of discovering, extracting, indexing, and incrementally synchronizing website and intranet content.**

The crawler must reuse existing document processing, Meilisearch indexing, hybrid search, and Phase 7 job execution infrastructure.

Do not implement features belonging to Phase 9 or later.

---

## 1. Review the Existing Implementation

Before modifying the repository:

1. Read `roadmap.md` and existing architecture documentation.
2. Inspect the current repository and solution structure.
3. Review the Phase 3 crawler configuration entities and SQLite persistence.
4. Review the Phase 4 HTML, document, and text extraction pipeline.
5. Review the Phase 3.5 chunking and hybrid search infrastructure.
6. Review the Phase 5 incremental filesystem synchronization design.
7. Review Phase 6 crawler administration and validation.
8. Review Phase 7 job scheduling, dispatching, cancellation, progress, and history.
9. Inspect the existing `SearchDocument`, `SearchChunk`, and deterministic ID models.
10. Review existing Meilisearch task completion and reconciliation services.
11. Run existing builds and tests to establish a baseline.

The actual repository is the source of truth.

Reuse existing infrastructure wherever practical.

Do not create a second job management system.

Do not introduce duplicate document extraction or indexing services.

Preserve completed behavior from previous phases.

## 2. Phase Objectives

Implement:

1. Web crawler configuration.
2. HTTP/HTTPS content fetching.
3. HTML link discovery.
4. URL normalization.
5. URL deduplication.
6. Host and path boundary enforcement.
7. Crawl depth and page-count limits.
8. URL include/exclude rules.
9. robots.txt handling.
10. Per-host request throttling.
11. Safe redirect handling.
12. HTML text and metadata extraction.
13. Linked PDF, Office, and text document extraction.
14. Incremental web content synchronization.
15. Safe deletion and missing-page reconciliation.
16. Integration with Phase 7 persistent job execution.
17. Web crawler administration UI.
18. Automated tests with a deterministic local HTTP fixture server.

The completed system must allow an administrator to configure, schedule, run, and monitor a web crawler without modifying application configuration files manually.

## 3. Architecture

Use the following logical pipeline:

```text
Web Crawler Configuration
          |
          v
Phase 7 Job Dispatcher
          |
          v
Web Crawler Executor
          |
          v
URL Frontier
          |
          v
URL Boundary Validator
          |
          v
robots.txt Policy
          |
          v
Per-Host Throttling
          |
          v
HTTP Fetcher
          |
          +-- HTML
          |     |
          |     +-- Content Extraction
          |     |
          |     +-- Link Discovery
          |
          +-- PDF / Office / Text
                |
                v
       Phase 4 Extraction
                |
                v
       Structure-Aware Chunking
                |
                v
       Meilisearch Indexing
                |
                v
       Embedding REST Server
                |
                v
       Hybrid Search Index
                |
                v
       SQLite Crawl State
```

### Responsibilities

**Web crawler**

Discovers URLs, controls navigation boundaries, and coordinates processing.

**HTTP fetcher**

Performs bounded, validated HTTP requests.

**HTML link extractor**

Discovers links without executing JavaScript.

**Document extraction pipeline**

Converts downloaded content into normalized structured text.

**Chunking service**

Creates searchable chunks with useful source metadata.

**Indexing services**

Synchronize parent documents and chunks with Meilisearch.

**SQLite**

Stores URL crawl state, fingerprints, and reconciliation information.

**Phase 7 job infrastructure**

Handles execution, scheduling, cancellation, history, and progress.

Do not call LlamaPure directly from the web crawler.

## 4. Web Crawler Configuration

Extend the existing crawler configuration model to support web crawler definitions.

Suggested configuration:

```json
{
  "id": "crawler-id",
  "name": "Company Intranet",
  "type": "web",
  "enabled": true,
  "startUrls": [
    "https://intranet.example.com/"
  ],
  "allowedHosts": [
    "intranet.example.com"
  ],
  "allowedPathPrefixes": [
    "/"
  ],
  "maximumDepth": 5,
  "maximumPages": 10000,
  "requestDelayMilliseconds": 500,
  "maximumConcurrentRequests": 2,
  "maximumResponseSizeMB": 100,
  "followRedirects": true,
  "respectRobotsTxt": true,
  "indexLinkedDocuments": true
}
```

Values are illustrative.

### Requirements

- Support multiple starting URLs.
- Support HTTP and HTTPS.
- Support explicit allowed hosts.
- Support allowed path prefixes.
- Support include/exclude URL patterns.
- Support maximum crawl depth.
- Support maximum page count.
- Support response size limits.
- Support bounded request concurrency.
- Support configurable crawl delays.
- Support robots.txt.
- Support optional linked document indexing.
- Validate configuration before saving and execution.

Do not introduce a large generic crawler configuration framework.

Reuse the existing Phase 6 administration conventions.

## 5. URL Normalization

Implement a shared URL normalization service.

### Requirements

- Normalize scheme casing.
- Normalize host casing.
- Remove fragments.
- Normalize default HTTP/HTTPS ports.
- Resolve relative URLs.
- Resolve protocol-relative URLs.
- Preserve meaningful query parameters.
- Preserve path case unless equivalence is established.
- Handle percent encoding carefully.
- Reject malformed URLs.

### Example

```text
https://example.com/docs/page.html#section1
```

Canonical location:

```text
https://example.com/docs/page.html
```

### Important restrictions

Do not aggressively remove query strings.

Do not assume URLs differing by query parameters represent the same content.

Do not blindly lowercase paths.

Avoid normalizations that merge distinct resources.

Use the normalized canonical URL for deterministic document identity.

## 6. URL Frontier and Deduplication

Implement a bounded URL frontier.

### Requirements

1. Start from configured seed URLs.
2. Track discovered URLs.
3. Avoid duplicate fetches during one scan.
4. Preserve crawl depth.
5. Enforce page-count limits.
6. Enforce configured boundaries before enqueueing.
7. Prevent traversal loops.
8. Respect cancellation.
9. Avoid unbounded memory growth.

### Suggested representation

```text
DiscoveredUrl
    |
    +-- CanonicalUrl
    +-- DiscoveryDepth
    +-- ParentUrl
    +-- DiscoverySource
```

Use a simple breadth-first traversal or another documented deterministic strategy.

Prefer bounded work queues.

For large crawl frontiers, use bounded in-memory queues together with SQLite-backed pending-discovery state if required to preserve limits without exhausting memory.

Do not introduce a distributed crawler framework.

### Duplicate detection

Deduplicate normalized URLs within each crawl.

Avoid relying exclusively on raw URL string comparison.

Do not repeatedly fetch the same URL because of fragment variations.

### Scope counting

Define whether `maximumPages` limits fetched URLs, successfully processed pages, or attempted requests.

Prefer a conservative bound on total fetch attempts.

Enforce the limit before launching additional requests.

## 7. Crawl Boundaries

Implement strict crawl boundaries.

### Allowed hosts

Only crawl explicitly permitted hosts.

Do not automatically include unrelated subdomains.

For example:

```text
Allowed:
intranet.example.com

Not automatically allowed:
admin.intranet.example.com
external.example.com
```

### Allowed paths

Support configured path prefixes.

Avoid naive string-prefix matching that unintentionally includes unrelated paths.

For example, `/docs` should not automatically match `/docs-private`.

Define a predictable path-boundary policy.

### URL patterns

Support simple include/exclude rules.

Exclusion rules must take precedence where appropriate.

Avoid introducing complex regular expressions unless already supported by existing infrastructure.

### Important requirement

Every URL must pass boundary validation before fetching.

Do not validate only the initial seed URLs.

Discovered links, redirects, canonical candidates, and document URLs must be validated independently.

## 8. SSRF Protection and Network Security

Web crawler URLs are administrator-configured but must still be treated as untrusted network destinations.

Implement defense-in-depth against Server-Side Request Forgery.

### Requirements

- Accept only HTTP/HTTPS URLs.
- Reject embedded credentials in URLs.
- Validate destination hostnames.
- Validate destination ports.
- Resolve DNS and evaluate resolved addresses.
- Reject prohibited IP address ranges.
- Validate each redirect destination.
- Prevent DNS rebinding from bypassing destination checks.
- Avoid requests to local process management endpoints.
- Avoid exposing internal credentials through errors.

### Intranet support

Lantern is explicitly intended to crawl intranet sites.

Therefore, private IP ranges must not be universally prohibited.

Instead:

**Private and internal network destinations must require explicit administrator-approved network boundaries.**

Public websites and approved intranet sites must both be supported.

### Connection validation

Avoid a design that validates DNS once and then connects to an unrelated address returned by a second DNS lookup.

Validate the actual selected connection destination.

Use suitable .NET HTTP connection handling where practical.

Do not disable TLS certificate validation globally.

Do not allow arbitrary redirects to expand the approved network scope.

### Metadata endpoints

Protect special-purpose local and link-local endpoints, including cloud metadata services.

Do not treat a general private-network allowance as authorization to crawl every internal address.

### Network identity

Document that requests originate from the Lantern server, not the administrator's browser.

## 9. HTTP Fetching

Implement HTTP fetching using `HttpClient` and existing dependency injection conventions.

### Requirements

- Reuse HttpClient instances through `IHttpClientFactory` or equivalent.
- Use asynchronous requests.
- Support cancellation.
- Apply connection and response timeouts.
- Bound response body sizes.
- Avoid buffering unlimited content.
- Validate content types.
- Handle HTTP status codes.
- Handle connection failures.
- Handle TLS failures.
- Handle server timeouts.
- Avoid excessive retries.

### Request headers

Use an identifiable crawler User-Agent.

For example:

```text
LanternCrawler/0.x
```

Do not impersonate ordinary browsers unnecessarily.

Support conditional requests where appropriate.

### Response handling

Distinguish:

- Successful content.
- Redirect.
- Not modified.
- Not found.
- Forbidden.
- Too many requests.
- Temporary server failure.
- Unsupported media type.
- Oversized response.

Do not interpret all non-200 responses as deleted pages.

## 10. Redirect Handling

Implement controlled redirect processing.

Disable automatic HTTP redirects for the crawler client.

Process redirect responses explicitly.

### Requirements

1. Limit redirect count.
2. Resolve relative redirect targets.
3. Normalize target URLs.
4. Validate host and path boundaries.
5. Validate DNS and destination IP policy.
6. Respect allowed ports and schemes.
7. Detect redirect loops.
8. Avoid forwarding credentials to unauthorized destinations.

### Redirect identity

Define how permanent redirects affect canonical document identity.

Do not silently create duplicate documents at both original and destination URLs.

Avoid destructive identity migrations during ordinary indexing.

Document the selected redirect policy.

## 11. robots.txt Support

Implement robots.txt handling compatible with RFC 9309.

Reference:

https://www.rfc-editor.org/rfc/rfc9309.html

### Requirements

- Fetch robots.txt for relevant origins.
- Parse applicable User-Agent groups.
- Support Allow and Disallow rules.
- Use correct rule precedence.
- Cache robots policies appropriately.
- Handle robots.txt redirects safely.
- Handle unavailable robots.txt.
- Handle unreachable robots.txt.
- Respect configured crawler identity.

### Crawl policy

Enable robots.txt compliance by default.

Do not silently bypass disallow rules.

For private intranet systems where administrators explicitly control crawling policy, provide only narrowly justified configuration behavior consistent with project security requirements.

robots.txt does not replace URL boundary or SSRF validation.

### Request delays

Honor configured crawl delays.

If additional delay rules are supported by the chosen parser, document their behavior.

Do not assume every robots.txt extension is standardized.

## 12. Per-Host Throttling

Implement polite request throttling.

### Requirements

- Limit concurrent requests per host.
- Enforce minimum delay between requests.
- Respect cancellation.
- Avoid busy waiting.
- Avoid overwhelming small intranet servers.
- Handle HTTP 429 appropriately.
- Honor valid Retry-After values within configured bounds.

### Suggested defaults

```text
MaximumConcurrentRequestsPerHost = 2
RequestDelayMilliseconds = 500
```

These are conservative initial values.

Make them configurable.

Do not create one unbounded task per discovered URL.

### Host identity

Use a consistent origin or host-based throttling key.

Document the policy.

Ensure multiple crawler jobs cannot accidentally bypass important shared host limits where practical.

## 13. HTML Processing and Link Discovery

Reuse the Phase 4 HTML extraction pipeline.

### Requirements

- Extract page title.
- Extract readable content.
- Preserve headings.
- Preserve meaningful paragraphs.
- Extract available metadata.
- Extract links.
- Resolve relative URLs.
- Respect HTML base URLs where safe.
- Remove URL fragments for canonical identity.
- Avoid duplicate discovered URLs.

### HTML metadata

Preserve available information such as:

- Title.
- Description.
- Language.
- Canonical URL hint.
- Modification metadata where reliable.

Do not fabricate missing metadata.

### Canonical links

Treat HTML canonical links as hints.

Do not automatically trust canonical destinations outside configured boundaries.

Validate canonical URLs before using them for identity decisions.

### JavaScript

Do not execute JavaScript.

Do not implement headless browser automation.

Pages requiring JavaScript rendering may have incomplete extracted content.

Document this limitation.

## 14. Downloaded Document Support

Support linked documents when enabled.

Target formats include:

- PDF.
- DOCX.
- XLSX.
- PPTX.
- TXT.
- Markdown.
- HTML.
- General text.
- Source code.
- Supported configuration formats.

Reuse the Phase 4 document extraction and structure-aware chunking pipeline.

### Requirements

- Detect format using response metadata and content inspection.
- Do not rely only on URL extensions.
- Apply response size limits before extraction.
- Preserve the original document URL.
- Preserve reliable HTTP metadata.
- Generate deterministic document IDs.
- Generate searchable chunks.
- Preserve page, slide, sheet, section, and line metadata when available.

### Encoding

Reuse Phase 4 character encoding detection.

Convert successfully decoded text into the existing Unicode representation.

Use UTF-8 for byte-oriented textual outputs.

Do not implement a separate, conflicting encoding detector.

### Binary data

Reject unsupported binary content safely.

Do not indiscriminately decode binary downloads as text.

### Linked document navigation

Preserve the original URL for later search result navigation.

Do not require downloading a local copy permanently unless necessary for bounded extraction.

Clean up temporary resources safely.

## 15. Document Identity and Chunk Indexing

Reuse the Phase 2 and Phase 3.5 identity and indexing infrastructure.

### Document identity

Use:

```text
DocumentId =
    Hash(
        CrawlerId +
        SourceType +
        CanonicalUrl
    )
```

This is conceptual.

Reuse the actual established document ID strategy.

Do not introduce incompatible IDs unnecessarily.

### Chunk identity

Reuse the existing chunk identity strategy.

Preserve parent-document relationships.

### Indexing

For new or changed content:

1. Extract normalized content.
2. Generate structured chunks.
3. Upsert the parent document.
4. Upsert searchable chunks.
5. Wait for relevant Meilisearch tasks.
6. Reconcile obsolete chunks.
7. Persist successful synchronization state.

Do not update successful crawl state merely because Meilisearch accepted a task.

### Embedding

Let Meilisearch invoke the existing REST embedding server.

Do not directly call LlamaPure from the crawler.

Unchanged content must not trigger unnecessary re-embedding.

## 16. Persistent Web Crawl State

Extend the existing SQLite crawl-state infrastructure.

Suggested entity:

```text
CrawledUrlState
    |
    +-- CrawlerId
    +-- CanonicalUrl
    +-- DocumentId
    +-- ETag
    +-- LastModified
    +-- ContentHash
    +-- LastSeenScanId
    +-- LastFetchedAt
    +-- LastIndexedAt
    +-- LastHttpStatus
    +-- SyncStatus
    +-- LastError
```

Adapt to the existing schema.

### Requirements

- Persist successfully indexed URLs.
- Persist HTTP validators.
- Persist content fingerprints.
- Track URL discovery across scans.
- Track indexing success and failure.
- Preserve crawl state across restarts.
- Avoid duplicate records.
- Use tracked EF Core migrations.

### HTTP validators

Support:

```text
ETag
Last-Modified
```

Use conditional requests:

```http
If-None-Match
If-Modified-Since
```

when reliable validators exist.

### HTTP 304

When a server returns `304 Not Modified`:

- Preserve existing indexed content.
- Update appropriate crawl observation metadata.
- Do not re-extract.
- Do not regenerate chunks.
- Do not regenerate embeddings.

However, do not treat an unprocessed `304` as proof that an independently required link-discovery traversal has been completed.

Reuse previously persisted outgoing links if available, or define a conservative traversal strategy.

## 17. Incremental Crawling

Implement incremental synchronization.

### New URL

Fetch, extract, chunk, index, and persist state.

### Unchanged URL

Avoid unnecessary extraction and indexing.

Use conditional responses or content hashes where appropriate.

### Changed URL

Reprocess content.

Replace outdated chunks using the existing safe synchronization mechanism.

### Deleted URL

Reconcile only when sufficient evidence exists that the resource is genuinely gone or no longer belongs to the intended crawl scope.

### Important distinction

A URL may disappear from a site's navigation while remaining directly accessible.

Do not automatically interpret link disappearance as a permanent document deletion.

Define and document the intended retention policy for unlinked but previously indexed URLs.

### Partial failures

If extraction or indexing fails:

- Preserve the last successfully indexed version where possible.
- Record the failure.
- Allow a subsequent crawl to retry.

Do not update the successful content fingerprint prematurely.

## 18. Safe Missing-Page Reconciliation

Web deletion detection is more complex than filesystem deletion detection.

Implement conservative reconciliation.

### HTTP 404 and 410

A confirmed `404 Not Found` or `410 Gone` response may indicate deletion.

However, deletion must still respect the configured reconciliation policy.

A transient response or misconfigured server must not cause mass deletion.

### HTTP 401 and 403

Authentication or authorization failures are not deletion evidence.

Do not delete indexed content because of access denial.

### HTTP 429 and 5xx

Rate limiting and server errors are not deletion evidence.

Preserve previous indexed content.

### Network errors

DNS failures, timeouts, TLS failures, and connection resets must not cause deletion.

### Incomplete scans

Do not perform discovery-based deletion reconciliation when:

- Crawling was canceled.
- Maximum page/depth limits prevented full discovery.
- Required pages could not be fetched.
- robots.txt prevented portions of the crawl.
- Network failures interrupted discovery.
- The job failed unexpectedly.

### Safety

Do not delete all indexed documents because the start page temporarily returns an error.

Avoid destructive mass reconciliation based solely on absence from a partial discovery set.

Reuse the Phase 5 principles for safe state transitions.

## 19. Phase 7 Job Integration

Register a web crawler executor with the existing generic job execution infrastructure.

Conceptually:

```text
ICrawlerExecutor
    |
    +-- FileCrawlerExecutor
    |
    +-- WebCrawlerExecutor
```

Use existing contracts where available.

### Requirements

- Execute as a persistent job.
- Support manual start.
- Support recurring scheduling.
- Support cancellation.
- Support retry.
- Report progress.
- Persist execution history.
- Prevent overlapping executions of the same crawler.
- Recover safely after application interruption.

Do not create a separate web crawler scheduler.

Do not duplicate Phase 7 job tables unless an extension is justified.

### Configuration snapshots

Web crawler jobs must use immutable configuration snapshots.

Changes made during execution must apply to future jobs.

### Crash recovery

Interrupted web crawls must not perform deletion reconciliation.

Preserve last successful indexed state.

## 20. Progress Reporting

Use the existing Phase 7 progress reporting infrastructure.

Suggested counters:

```text
DiscoveredUrls
QueuedUrls
FetchedUrls
IndexedDocuments
IndexedChunks
UnchangedUrls
SkippedUrls
FailedUrls
DeletedUrls
```

Use names consistent with existing progress models.

### Requirements

- Report actual values.
- Persist progress periodically.
- Avoid writing one database record per HTTP request unless necessary.
- Do not fabricate total page counts.
- Do not fabricate progress percentages.
- Preserve final counts and failures.

### Useful diagnostics

Where practical, report:

- Current URL or host.
- Last successful fetch.
- Last error category.
- HTTP failure counts.
- Number of out-of-scope links.
- Number of redirects rejected by policy.

Avoid storing sensitive content or credentials in diagnostics.

## 21. Web Crawler Administration UI

Extend the existing Phase 6 crawler management interface.

### Crawler list

Support both:

```text
File
Web
```

crawler types.

Display type-specific configuration summaries.

### Web crawler creation form

Suggested fields:

```text
Create Web Crawler

Name:
[ Company Intranet              ]

Start URL:
[ https://intranet.example.com/ ]

Allowed Hosts:
[ intranet.example.com          ]

Allowed Paths:
[ /                              ]

Maximum Depth:
[ 5 ]

Maximum Pages:
[ 10000 ]

Request Delay:
[ 500 ] ms

Maximum Concurrent Requests:
[ 2 ]

[✓] Respect robots.txt
[✓] Follow redirects
[✓] Index linked documents

[ Test Connection ] [ Cancel ] [ Save ]
```

### Requirements

- Create web crawlers.
- Edit web crawlers.
- Enable/disable web crawlers.
- Safely delete or archive configurations.
- Test target accessibility.
- Configure recurring schedules.
- Run manually.
- View job status and history.

Reuse Phase 6 and Phase 7 UI patterns.

Do not redesign the administration area.

## 22. Test Connection

Implement a bounded web crawler connection test.

### Required behavior

1. Validate the start URL.
2. Validate configured crawl boundaries.
3. Resolve and validate the destination.
4. Perform a bounded HTTP request.
5. Report status and accessibility.
6. Avoid recursive link traversal.
7. Avoid indexing content.
8. Avoid updating crawl state.

### Security

The connection test must enforce the same SSRF and network boundary rules as actual crawling.

Do not allow the Test Connection operation to become an unrestricted internal network scanner.

### Response

Provide clear feedback such as:

```text
Connection successful.
HTTP 200
Content-Type: text/html
```

Or:

```text
Connection failed.
Destination is outside the configured allowed network.
```

Do not reveal unnecessary network topology or credentials.

## 23. Authentication and Credentials

Initial web crawling may focus on anonymously accessible public and intranet pages.

Do not build a complete credential management system in this phase.

### Requirements

- Do not embed credentials in URLs.
- Do not log authorization headers.
- Do not send credentials to unauthorized redirects.
- Do not expose authentication secrets through the administration UI.
- Do not automatically reuse administrator browser credentials for crawler requests.

If the existing application already has secure server-side secret storage suitable for web crawling, it may be reused for a narrowly scoped authentication mechanism.

Otherwise, document authenticated crawling as a limitation rather than introducing an insecure solution.

Complex NTLM, Kerberos, OAuth, browser session, and form-login automation are outside the initial scope.

## 24. Resource Limits

A web crawler must operate with bounded resources.

Suggested initial settings:

```json
{
  "WebCrawling": {
    "MaximumDepth": 5,
    "MaximumPages": 10000,
    "MaximumConcurrentRequests": 2,
    "MaximumConcurrentRequestsPerHost": 2,
    "RequestDelayMilliseconds": 500,
    "RequestTimeoutSeconds": 30,
    "MaximumRedirects": 5,
    "MaximumResponseSizeMB": 100,
    "MaximumHtmlSizeMB": 10,
    "MaximumPendingUrls": 10000
  }
}
```

Values are illustrative.

### Requirements

- Enforce depth limits.
- Enforce fetch-count limits.
- Enforce response size limits.
- Enforce redirect limits.
- Enforce queue bounds.
- Enforce request timeouts.
- Bound document extraction concurrency.
- Bound indexing concurrency.
- Avoid unlimited retry loops.

Do not launch unlimited requests when a page contains thousands of links.

### Download handling

Use streaming HTTP responses where possible.

Avoid keeping large downloaded documents in memory unnecessarily.

Clean up temporary files.

Reuse Phase 4 extraction resource limits.

## 25. Testing Strategy

Use a deterministic local HTTP fixture website.

Do not depend on arbitrary external public websites for automated tests.

### Fixture site

Include test routes representing:

```text
/
├── index.html
├── about.html
├── docs/
│   ├── guide.html
│   └── manual.pdf
├── downloads/
│   ├── report.docx
│   ├── presentation.pptx
│   ├── workbook.xlsx
│   └── source.cpp
├── redirects/
│   └── old-page
├── errors/
│   ├── not-found
│   ├── forbidden
│   └── server-error
└── robots.txt
```

Include relative and absolute links.

Include Japanese and English text.

Include deliberate link cycles.

Include out-of-scope links.

### Deterministic execution

Use a local ASP.NET Core test server or loopback HTTP fixture server where practical.

Control response content, headers, status codes, and redirects.

Use injectable time abstractions where scheduling or throttling tests require deterministic timing.

## 26. Unit Tests

Test:

1. URL normalization.
2. Fragment removal.
3. Default port normalization.
4. Relative URL resolution.
5. Query parameter preservation.
6. Duplicate URL detection.
7. Crawl depth calculation.
8. Page-count limits.
9. Host boundary validation.
10. Path boundary validation.
11. Include/exclude rules.
12. URL frontier behavior.
13. Redirect loop detection.
14. Redirect count limits.
15. robots.txt parsing.
16. Per-host throttling.
17. HTTP status classification.
18. Conditional request headers.
19. Response size limits.
20. Cancellation behavior.
21. Progress counting.
22. Safe deletion eligibility.

## 27. Security Tests

Implement tests for:

1. Unauthorized destination hosts.
2. Private IP targets without explicit approval.
3. Approved intranet destinations.
4. Loopback and link-local addresses.
5. Cloud metadata addresses.
6. Redirects outside allowed hosts.
7. Redirects to prohibited IP ranges.
8. DNS address changes.
9. Disallowed destination ports.
10. URL-embedded credentials.
11. Unsupported URL schemes.
12. Malformed URLs.
13. Connection testing authorization.
14. Administrator-only crawler modification.

Security validation must occur in the actual fetching path, not just in UI form validation.

Where appropriate, test that destination validation is tied to the address used by the HTTP connection.

Do not claim SSRF protection is complete based only on hostname string checks.

## 28. HTML and Document Extraction Tests

Verify:

1. HTML title extraction.
2. Heading and paragraph extraction.
3. Link discovery.
4. Relative link resolution.
5. HTML base URL handling.
6. Japanese and English preservation.
7. PDF processing.
8. DOCX processing.
9. XLSX processing.
10. PPTX processing.
11. Plain-text processing.
12. Source-code processing.
13. UTF-8 normalization.
14. Binary response rejection.
15. Chunk metadata preservation.

Reuse Phase 4 test fixtures where practical.

## 29. Incremental Crawling Tests

Test the following scenarios.

### Initial crawl

- Pages are discovered.
- Content is extracted.
- Documents and chunks are indexed.
- Crawl state is persisted.

### Unchanged crawl

- Conditional requests are sent.
- HTTP 304 responses are handled correctly.
- Unchanged content is not re-embedded.
- Existing documents remain searchable.

### Modified content

- Updated page content is detected.
- Chunks are replaced.
- Old chunks are reconciled.
- Parent document identity remains stable.

### Confirmed missing page

- A genuine missing page is handled according to the reconciliation policy.
- Associated chunks are safely removed when deletion is confirmed.

### Temporary server error

- Previously indexed content is preserved.
- The failure is recorded.
- No accidental deletion occurs.

### Interrupted crawl

- Cancellation stops additional work.
- Crawl state remains recoverable.
- No discovery-based mass deletion occurs.

### Redirect change

- Redirects are followed only within approved boundaries.
- Document identity remains consistent with the selected policy.

### Repeated crawl

- No duplicate documents or chunks accumulate.

## 30. Phase 7 Job Integration Tests

Verify:

1. Web crawler job creation.
2. Manual job submission.
3. Scheduled execution.
4. Job cancellation.
5. Job retry.
6. Job progress persistence.
7. Execution history.
8. Same-crawler overlap prevention.
9. Crash recovery.
10. File crawler compatibility.

The generic job dispatcher must support both file and web crawler executors.

Do not duplicate the scheduler or dispatcher.

## 31. Meilisearch and Hybrid Search Tests

Use a real Meilisearch Community Edition instance where available.

Verify:

- HTML content indexing.
- Downloaded document indexing.
- Chunk generation.
- Asynchronous indexing task completion.
- Keyword search.
- Hybrid search.
- Japanese content retrieval.
- English content retrieval.
- Cross-language retrieval where practical.
- Incremental updates.
- Deletion reconciliation.
- Retry after indexing failure.

Do not claim that embedding generation completed merely because a document update request was accepted.

Do not claim that integration tests passed unless actually executed.

Use isolated test indexes.

Never run destructive tests against production data.

## 32. Regression Testing

Run existing tests from Phases 0–7.

Verify:

- Existing search UI remains functional.
- Administration authentication remains functional.
- File crawler configuration remains functional.
- File crawler jobs remain functional.
- SQLite job persistence remains functional.
- Generic document extraction remains functional.
- Source-code extraction remains functional.
- Structure-aware chunking remains functional.
- Embedding server remains functional.
- Hybrid search remains functional.
- Existing synchronization safeguards remain intact.

Do not modify unrelated functionality merely to simplify web crawling.

## 33. Performance and Reliability

Record useful measurements where practical:

- URLs discovered per second.
- HTTP requests per second.
- Successful fetch count.
- Average fetch latency.
- Initial crawl duration.
- Incremental crawl duration.
- Extraction throughput.
- Indexing throughput.
- Number of unchanged pages skipped.
- Memory consumption.
- Error and retry counts.

Do not fabricate benchmark results.

### Reliability priorities

Prioritize:

1. Correct crawl boundaries.
2. SSRF protection.
3. Safe reconciliation.
4. Idempotent indexing.
5. Resource limits.
6. Cancellation.
7. Clear diagnostics.

Maximum throughput is not the primary goal.

## 34. Suggested Project Organization

Preserve the repository structure.

Suggested logical responsibilities:

```text
Lantern.Core
    |
    +-- WebCrawlerConfiguration
    +-- WebCrawlResult
    +-- URL metadata
    +-- Web crawl state models

Lantern.Crawling
    |
    +-- Files/
    |      +-- Existing file crawler
    |
    +-- Web/
    |      +-- WebCrawler
    |      +-- HttpFetcher
    |      +-- UrlNormalizer
    |      +-- UrlFrontier
    |      +-- CrawlBoundaryValidator
    |      +-- RobotsPolicy
    |      +-- HostRateLimiter
    |      +-- LinkExtractor
    |      +-- WebContentSynchronizer
    |
    +-- Scheduling/
           +-- Existing Phase 7 infrastructure

Lantern.Infrastructure
    |
    +-- SQLite web crawl state
    +-- EF Core migrations
    +-- Existing Meilisearch services

Lantern.Web
    |
    +-- Web crawler configuration
    +-- Connection testing
    +-- Existing jobs UI
```

This is illustrative.

Do not create additional assemblies without a concrete reason.

Avoid introducing speculative abstractions.

## 35. Out of Scope

Do not implement:

- JavaScript-rendered browser crawling.
- Playwright or Selenium.
- Headless Chromium.
- Complex authenticated crawling.
- Form-login automation.
- OAuth authorization flows.
- Distributed crawling.
- Multi-instance crawler coordination.
- Cloud-based crawling services.
- General-purpose proxy management.
- OCR.
- Automatic website screenshots.
- Semantic reranking models.
- RAG.
- Chat interfaces.
- Advanced search UI filtering.
- Full Windows release packaging.

These belong to later phases or deferred enhancements.

## 36. Acceptance Criteria

Phase 8 is complete only when all required conditions are satisfied.

### Web crawling

- Administrators can configure web crawlers.
- HTTP and HTTPS pages can be fetched.
- HTML links can be discovered.
- URL normalization works.
- Crawl boundaries are enforced.
- Crawl depth and page limits are enforced.
- robots.txt is respected.
- Per-host throttling works.
- Redirects are validated safely.

### Document processing

- HTML pages produce searchable text.
- PDF and Office documents can be indexed when enabled.
- Supported text and source-code resources can be indexed.
- UTF-8 and Unicode behavior remains correct.
- Structural chunk metadata is preserved.

### Incremental synchronization

- New URLs are indexed.
- Changed pages are updated.
- Unchanged content avoids unnecessary embedding.
- Repeated crawls do not create duplicates.
- Deleted pages are reconciled safely.
- Temporary failures do not cause mass deletion.
- Interrupted crawls preserve valid indexed content.

### Job integration

- Web crawlers use the existing Phase 7 dispatcher.
- Manual execution works.
- Recurring schedules work.
- Cancellation works.
- Retry works.
- Execution history persists.
- Progress is visible.
- File crawler execution remains functional.

### Security

- Out-of-scope URLs are rejected.
- Unsafe redirects are rejected.
- Unapproved internal destinations are rejected.
- Approved intranet destinations work.
- DNS and destination-address validation is enforced.
- Administrative actions require authorization.
- Resource limits prevent uncontrolled crawling.

### Testing

- The solution builds.
- Existing tests pass.
- Unit tests pass.
- Local HTTP fixture tests pass.
- Security tests pass.
- Incremental synchronization tests pass.
- SQLite integration tests pass.
- Job integration tests pass.
- Meilisearch integration tests pass where prerequisites are available.

Clearly identify any unmet or unverified acceptance criteria.

## 37. Deliverables

Provide:

1. Web crawler configuration model.
2. Web crawler executor.
3. HTTP fetcher.
4. URL normalization.
5. URL frontier and deduplication.
6. Crawl boundary validation.
7. SSRF protection.
8. robots.txt processing.
9. Per-host throttling.
10. Safe redirect handling.
11. HTML link discovery.
12. Content extraction integration.
13. Downloaded document support.
14. SQLite web crawl state.
15. Incremental synchronization.
16. Safe deletion reconciliation.
17. Phase 7 job integration.
18. Web crawler administration UI.
19. Connection testing.
20. Automated tests.
21. Updated documentation.

Document:

- Web crawler configuration.
- Supported URL schemes.
- Crawl boundary behavior.
- Intranet network approval.
- robots.txt handling.
- Throttling.
- Redirect policy.
- URL identity.
- Incremental synchronization.
- Deletion policy.
- Job execution.
- Security restrictions.
- Known limitations.

## 38. Required Completion Report

At the end of implementation, provide:

### A. Implementation summary

Describe completed web crawler functionality.

### B. Files changed

List important created and modified files.

### C. Architectural decisions

Explain:

- HTTP fetching strategy.
- URL normalization.
- URL frontier.
- Deduplication.
- Boundary enforcement.
- SSRF protection.
- Redirect policy.
- robots.txt behavior.
- Host throttling.
- Incremental fingerprinting.
- Deletion reconciliation.
- Job integration.

### D. Build and test commands

List actual commands executed.

### E. Test results

Clearly distinguish:

- Passed tests.
- Failed tests.
- Skipped tests.
- Tests not executed.

### F. Performance observations

Report measured crawl performance where available.

Do not invent measurements.

### G. Known limitations

Document limitations involving:

- JavaScript-rendered pages.
- Authenticated intranet websites.
- HTTP caching.
- URL canonicalization.
- Dynamic website navigation.
- robots.txt interpretation.
- Network security boundaries.
- Incomplete discovery.
- Nontransactional Meilisearch updates.

### H. Remaining work

Identify incomplete acceptance criteria and their impact.

Do not begin Phase 9 automatically.

---

## Final Instructions

Implement Phase 8 directly in the existing Lantern repository.

Inspect and preserve all functionality from Phases 0–7.

Build a secure HTTP-based web crawler using .NET and HttpClient.

Reuse Phase 4 document extraction, Phase 3.5 structure-aware chunking and hybrid indexing, and Phase 7 persistent job execution.

Implement URL discovery, normalization, deduplication, robots.txt compliance, rate limiting, redirects, and strict crawl boundaries.

Support both public websites and explicitly approved intranet destinations.

Protect against SSRF, DNS rebinding, unbounded downloads, and unsafe redirects.

Use incremental synchronization to avoid unnecessary extraction and embedding.

Never interpret transient HTTP errors, inaccessible websites, or incomplete discovery as evidence of mass deletion.

Integrate web crawler configuration, execution, scheduling, progress, and history into the existing administration interface.

Do not introduce browser automation, distributed crawling, or unrelated features.

**The primary success condition is that a Lantern administrator can configure a website or intranet crawler, run or schedule it, safely discover and index HTML pages and supported linked documents, monitor its progress, and search the resulting content using both keyword and local Granite-based hybrid search.**

Do not implement Phase 9 or later features automatically.
