# Lantern — Fix Meilisearch Authentication and Add Legacy Microsoft Office Support

## Objective

Investigate and fix two problems in Lantern's file crawling and document indexing pipeline.

**Problem 1: Meilisearch authentication failures**

PDF and PPTX documents are extracted but fail during indexing:

```text
MeilisearchApiError
Code: missing_authorization_header
Type: auth
Message: The Authorization header is missing.
It must use the bearer authorization method.
```

**Problem 2: Unsupported legacy Microsoft Office formats**

Older PowerPoint documents fail during extraction:

```text
Unsupported format: .ppt
```

Legacy Word and Excel documents may also be unsupported.

Your task is to:

1. Fix Meilisearch authentication first.
2. Verify successful indexing of existing supported formats.
3. Add extraction support for legacy `.doc`, `.xls`, and `.ppt` documents.
4. Integrate those formats into the existing crawling, extraction, chunking, indexing, and search pipeline.
5. Add regression tests and update documentation.

Do not introduce Java, Microsoft Office automation, Docker, or mandatory external document conversion services.

---

## Part A — Fix Meilisearch Authentication

### 1. Investigate the authentication configuration

Inspect:

- `MeilisearchProcessManager`
- Meilisearch SDK client registration
- Dependency injection configuration
- Indexing services
- Search services
- Chunk indexing services
- Index configuration services
- Document deletion and synchronization
- `scripts/setup-dev.ps1`
- `scripts/start-meilisearch.ps1`
- Production initialization and startup
- Development configuration
- Secret files and API key loading

Determine which credentials are used by each component.

Verify whether the application creates multiple Meilisearch clients with inconsistent authentication settings.

### 2. Identify the actual failure

Trace the failing request from the crawler through the indexing pipeline.

Determine:

- Which Meilisearch endpoint is called.
- Which SDK client or HTTP client sends the request.
- Whether the Authorization header is present.
- Whether an API key was loaded.
- Whether the API key is empty.
- Whether the key was generated locally but never registered.
- Whether the client was initialized before credentials became available.
- Whether configuration changes require client recreation.

Do not log actual credential values.

Do not assume the crawler itself is responsible for the missing header.

### 3. Correct Meilisearch API key handling

Meilisearch requires bearer authentication when a master key is configured.

Ensure application requests use:

```http
Authorization: Bearer <valid-api-key>
```

The API key must actually be recognized by the running Meilisearch instance.

Do not treat an arbitrary randomly generated string as a registered Meilisearch API key.

Use the supported Meilisearch key-management API when a scoped key must be created.

Prefer a server-side API key with the necessary permissions for:

- Document indexing.
- Document updates.
- Document deletion.
- Search.
- Index creation and configuration where required.
- Task inspection.

Use least privilege where practical.

Keep the master key separate from routine application credentials when the existing architecture permits.

### 4. Make initialization reliable

Verify the startup sequence:

```text
Load Configuration
        |
        v
Load Meilisearch Master Key
        |
        v
Start Meilisearch
        |
        v
Verify Readiness
        |
        v
Provision or Validate API Keys
        |
        v
Initialize Meilisearch Clients
        |
        v
Enable Search and Indexing
```

Ensure a client cannot silently start with an empty API key.

Do not regenerate credentials on every startup.

Do not overwrite existing Meilisearch secrets.

Do not reset search indexes.

### 5. Unify client configuration

Prefer a single, well-defined configuration mechanism for Meilisearch SDK clients.

Ensure all indexing and search components obtain correctly authenticated clients.

Review both parent-document and chunk indexes.

Do not create separate anonymous clients unintentionally.

If components have different permission requirements, document and configure their respective authenticated clients explicitly.

### 6. Improve authentication error handling

When Meilisearch returns:

```text
missing_authorization_header
invalid_api_key
```

treat the response as a configuration or authentication failure, not an ordinary per-document extraction failure.

Requirements:

- Report the failing subsystem.
- Record an actionable diagnostic without revealing secrets.
- Avoid retrying identical authentication failures indefinitely.
- Mark the crawler job appropriately.
- Preserve pending indexing state.
- Avoid repeating the same error for every document when the failure is systemic.
- Do not mark the affected documents as successfully indexed.

Authentication recovery must not require deleting the SQLite database or Meilisearch indexes.

### 7. Verify the fix

Use a real authenticated Meilisearch instance.

Verify:

1. Meilisearch starts with authentication enabled.
2. The configured application key is valid.
3. Index creation works.
4. Index settings updates work.
5. PDF indexing works.
6. PPTX indexing works.
7. Chunk indexing works.
8. Keyword search works.
9. Hybrid search works when embedding is available.
10. Document updates and deletions work.
11. Application restart preserves authentication.

Only proceed to legacy Office support after the existing indexing pipeline works.

---

## Part B — Legacy Microsoft Office Support

### 8. Investigate existing extraction architecture

Inspect the Phase 4 document extraction implementation.

Identify:

- File format detection.
- Extension registration.
- Content extraction interfaces.
- PDF extraction.
- DOCX extraction.
- XLSX extraction.
- PPTX extraction.
- Text normalization.
- Structure-aware chunking.
- Extraction metadata.
- Crawler format filters.

Determine which libraries are currently used.

Reuse existing extraction abstractions.

Do not create a separate indexing pipeline for legacy Office documents.

### 9. Support legacy document formats

Add support for:

| Extension | Format |
|---|---|
| `.doc` | Microsoft Word 97–2003 |
| `.xls` | Microsoft Excel 97–2003 |
| `.ppt` | Microsoft PowerPoint 97–2003 |

These are generally compound binary document formats, not Office Open XML packages.

Do not attempt to parse them using DOCX, XLSX, or PPTX extraction logic.

### 10. Evaluate suitable .NET libraries

Before implementation, investigate libraries capable of extracting text from legacy Office binary documents.

Evaluate:

- Compatibility with .NET 10.
- Actual `.doc` support.
- Actual `.xls` support.
- Actual `.ppt` support.
- Windows x64 compatibility.
- Text extraction accuracy.
- Japanese text handling.
- Structured metadata support.
- License compatibility.
- Security maintenance.
- Native dependencies.
- Offline redistribution requirements.

NPOI may be considered for supported formats, but verify the actual APIs and format coverage in the selected version.

Do not assume one library supports all three formats.

Do not introduce Java-based Apache POI as a runtime dependency.

Do not require Microsoft Office to be installed.

Do not require LibreOffice for normal document extraction.

If no acceptable maintained .NET library supports a format adequately, document the gap and propose a focused alternative rather than implementing a fragile binary parser.

### 11. Implement DOC extraction

Support extraction from legacy `.doc` documents.

Requirements:

- Extract readable text.
- Preserve paragraph boundaries where possible.
- Preserve Japanese and English characters.
- Extract tables where supported.
- Preserve useful document metadata.
- Reject encrypted or unsupported files clearly.
- Handle malformed files safely.

Do not execute macros or embedded content.

### 12. Implement XLS extraction

Support extraction from legacy `.xls` workbooks.

Requirements:

- Extract worksheet names.
- Extract cell text and values.
- Preserve row and worksheet boundaries.
- Handle multiple worksheets.
- Preserve useful cell location metadata.
- Handle Japanese and English text.
- Bound the number of cells processed.
- Handle malformed and encrypted workbooks safely.

Avoid evaluating arbitrary formulas.

Use safely available cached or stored values where appropriate.

Do not execute workbook macros.

### 13. Implement PPT extraction

Support extraction from legacy `.ppt` presentations.

Requirements:

- Extract slide text.
- Preserve slide boundaries.
- Preserve slide numbers.
- Extract speaker notes where supported.
- Preserve Japanese and English characters.
- Handle text boxes and grouped objects where supported.
- Handle malformed presentations safely.

Do not execute macros, embedded scripts, or external content.

The original reported `.ppt` files should no longer fail solely because of their extension.

### 14. Integrate legacy formats into crawling

Update the existing format registry and crawler support.

Ensure `.doc`, `.xls`, and `.ppt` can be processed by:

- Local file crawlers.
- UNC file crawlers.
- Web crawlers when these formats are downloaded.

Preserve the existing crawler inclusion/exclusion rules.

Do not silently override administrator-configured extension filters.

Ensure old Office documents use the existing:

```text
File Discovery
     |
     v
Format Detection
     |
     v
Content Extraction
     |
     v
Structure-Aware Chunking
     |
     v
Parent and Chunk Indexing
     |
     v
Keyword / Hybrid Search
```

### 15. Preserve structure metadata

Map legacy Office metadata into the existing search result model.

Examples:

- DOC paragraph or section.
- XLS worksheet and cell location.
- PPT slide number and speaker notes.

Preserve available metadata without inventing unsupported location information.

Avoid adding a second incompatible metadata schema.

### 16. Handle legacy encoding correctly

Legacy Office binary files may use document-specific character encodings.

Requirements:

- Use the extraction library's supported encoding handling.
- Preserve Japanese text correctly.
- Avoid applying generic UTF-8 decoding directly to OLE binary data.
- Normalize extracted content into ordinary .NET strings.
- Preserve text accurately when indexing.
- Do not replace unrecognized characters silently.

Add regression fixtures containing Japanese text.

### 17. Extraction resource limits

Legacy Office files must use the same security limits as other supported formats.

Requirements:

- Maximum input file size.
- Maximum extracted text size.
- Bounded processing concurrency.
- Bounded worksheet and cell enumeration.
- Malformed document handling.
- Safe cancellation.
- No macro execution.
- No uncontrolled external resource loading.

Do not assume legacy binary files are safe merely because they are not ZIP-based.

---

## Part C — Tests and Verification

### 18. Authentication regression tests

Use real Meilisearch.

Test:

1. Valid API key.
2. Missing API key.
3. Invalid API key.
4. Incorrectly configured client.
5. Correct client initialization.
6. Application restart.
7. Index creation.
8. Parent document indexing.
9. Chunk indexing.
10. Search.
11. Update.
12. Delete.
13. Crawler job failure handling.
14. No infinite retry on authentication failure.

Ensure a missing Authorization header cannot silently reach normal indexing operations.

### 19. Legacy Office extraction tests

Create deterministic test fixtures.

Include:

- English DOC.
- Japanese DOC.
- English XLS.
- Japanese XLS.
- Multiple worksheets.
- English PPT.
- Japanese PPT.
- Multiple slides.
- PPT speaker notes where supported.
- Malformed files.
- Encrypted files where practical.
- Incorrect extensions.
- Oversized files.

Verify extracted text and metadata.

Do not claim support for document features that the chosen library cannot extract.

### 20. End-to-end crawler tests

For each supported legacy format:

1. Create a test fixture.
2. Configure a file crawler.
3. Run the crawler.
4. Verify extraction succeeds.
5. Verify parent document indexing.
6. Verify chunk indexing.
7. Verify keyword retrieval.
8. Verify source metadata.
9. Verify repeated crawl idempotency.
10. Verify update and deletion behavior.

Run a similar controlled test through the web crawler where supported.

### 21. Real-world regression scenario

Reproduce the originally reported workflow using authorized test documents.

Verify that:

- Existing PDF files index successfully.
- Existing PPTX files index successfully.
- Legacy PPT files are accepted.
- Extraction errors are accurately reported.
- Authentication failures no longer occur.
- Documents are searchable after successful indexing.

Do not commit private user documents to the repository.

Use synthetic fixtures for automated tests.

---

## Part D — Documentation and Completion

### 22. Update supported format documentation

Update:

- README.
- Installation guide.
- Developer guide.
- Supported file format documentation.
- Crawler documentation.

Document the implemented legacy Office support accurately.

Distinguish:

- Fully supported formats.
- Partially supported features.
- Encrypted files.
- Unsupported embedded content.

### 23. Acceptance criteria

The task is complete when:

- Meilisearch authentication is correct.
- No anonymous indexing clients remain unintentionally.
- PDF and PPTX indexing work.
- Authentication configuration survives restart.
- Missing or invalid API keys fail clearly.
- Systemic authentication failures do not produce repeated per-document errors.
- Legacy DOC text extraction works.
- Legacy XLS text extraction works.
- Legacy PPT text extraction works.
- Japanese text is preserved.
- Legacy documents use the existing chunking pipeline.
- Keyword search works.
- Hybrid search works when available.
- Crawler synchronization remains idempotent.
- Existing formats are not broken.
- Tests pass.
- Documentation matches the implementation.

If a legacy format cannot be supported safely with acceptable dependencies, report it as an unmet acceptance criterion rather than falsely claiming full support.

### 24. Required completion report

Report:

**A. Authentication root cause**

Identify why the Authorization header was missing and which components were affected.

**B. Authentication changes**

Explain API key provisioning, configuration, SDK client initialization, and error handling.

**C. Legacy Office implementation**

List the libraries selected for `.doc`, `.xls`, and `.ppt`, including their versions and licensing considerations.

**D. Extraction capabilities**

Describe text and metadata support for each format.

**E. Files changed**

List important modifications.

**F. Tests**

Report actual commands executed and distinguish passed, failed, skipped, and unexecuted tests.

**G. Regression verification**

Describe PDF, PPTX, DOC, XLS, and PPT indexing results.

**H. Limitations**

Document unsupported legacy Office features and security restrictions.

---

## Final Instructions

Fix Meilisearch authentication before extending document format support.

Investigate the complete authentication configuration from startup through indexing.

Ensure all internal Meilisearch clients use appropriate, valid API credentials.

Do not generate arbitrary unregistered API keys.

Preserve existing indexes and persistent data.

After authentication works, implement legacy `.doc`, `.xls`, and `.ppt` extraction using suitable .NET-compatible libraries.

Do not introduce mandatory Java, Microsoft Office, LibreOffice, Docker, or cloud services.

Preserve Japanese text and useful document structure metadata.

Reuse the existing crawler, extraction, chunking, and indexing pipelines.

Add real Meilisearch integration tests and deterministic legacy Office extraction tests.

**The primary success condition is that Lantern successfully indexes and searches PDF, PPTX, DOC, XLS, and PPT documents, with correct Meilisearch authentication, preserved document metadata, and reliable incremental crawling.**
