# Lantern — Phase 4 Implementation Instructions
## Document Extraction and Structure-Aware Chunking Pipeline

You are implementing **Phase 4: Document Extraction Pipeline** of the Lantern project.

Lantern is a lightweight, self-hosted full-text search platform designed for Windows and intranet environments.

The application uses .NET 10, ASP.NET Core, Blazor Interactive Server, Meilisearch Community Edition, and SQLite.

The project also introduces local hybrid search using LlamaPure and IBM Granite Embedding 97M Multilingual R2.

Phases 0–3 have been implemented. Phase 3.5 defines the local embedding server, generic text chunking, chunk indexing, and hybrid search foundation.

**Your task is to implement a reliable document extraction pipeline that converts supported files into normalized, structure-aware text blocks and searchable chunks.**

Do not implement file crawling, crawler scheduling, or other subsequent roadmap phases.

---

## 1. Review the Existing Implementation

Before modifying the repository:

1. Read `roadmap.md` and existing architecture documentation.
2. Inspect the current solution and project structure.
3. Review the existing `SearchDocument` and `SearchChunk` models.
4. Examine the Phase 3.5 text chunking service.
5. Review the Meilisearch chunk indexing implementation.
6. Review LlamaPure and embedding integration boundaries.
7. Review existing configuration and dependency injection registrations.
8. Review the Phase 0 document extraction compatibility results.
9. Run the existing build and tests to establish a baseline.

The actual repository is the source of truth.

If Phase 3.5 is incomplete, identify which extraction and chunking integration points are available. Do not silently implement unrelated missing hybrid search infrastructure.

Preserve existing functionality.

Reuse established models and services.

Avoid unrelated refactoring.

## 2. Phase Objectives

Implement:

1. A reusable document extraction abstraction.
2. Format-specific extraction adapters.
3. Text and metadata extraction.
4. Structural document representation.
5. Structure-aware chunk generation.
6. Page, slide, and sheet location metadata.
7. Unicode and whitespace normalization.
8. File-size and resource limits.
9. Malformed and encrypted document handling.
10. Extraction diagnostics.
11. A deterministic regression-test corpus.
12. Integration tests with the existing chunking pipeline.

The output must be independent of the Blazor UI and Meilisearch APIs.

### Supported formats

| Format | Priority |
|---|---|
| TXT | Required |
| Markdown | Required |
| HTML / HTM | Required |
| PDF | Required |
| DOCX | Required |
| XLSX | Required |
| PPTX | Required |
| CSV | Secondary |
| Legacy DOC / XLS / PPT | Deferred |

All required formats must be validated using representative real files.

Do not claim that a format is supported merely because an extraction library advertises compatibility.

## 3. Overall Architecture

Use the following logical pipeline:

```text
Document File / Stream
        |
        v
Format Detection
        |
        v
Document Extractor
        |
        +-- Plain Text
        +-- Markdown
        +-- HTML
        +-- PDF
        +-- DOCX
        +-- XLSX
        +-- PPTX
        |
        v
Structured Document
        |
        +-- Metadata
        +-- Sections
        +-- Paragraphs
        +-- Tables
        +-- Page / Slide / Sheet Locations
        |
        v
Text Normalization
        |
        v
Structure-Aware Chunker
        |
        v
SearchChunk[]
        |
        v
Existing Phase 3.5 Indexing Pipeline
```

The extraction layer must not depend directly on Meilisearch or LlamaPure.

Extraction and embedding are separate responsibilities.

## 4. Document Extraction Abstraction

Introduce or extend an `IDocumentExtractor` abstraction.

Illustrative interface:

```csharp
public interface IDocumentExtractor
{
    bool CanExtract(DocumentFormat format);

    Task<ExtractedDocument> ExtractAsync(
        DocumentInput input,
        CancellationToken cancellationToken);
}
```

Adapt this interface to existing repository conventions.

### Requirements

- Extractors must be independently testable.
- Extraction results must use a shared representation.
- Input may originate from a file path or stream.
- Do not require callers to load entire files into memory.
- Clearly define stream ownership and disposal.
- Propagate cancellation where supported.
- Distinguish unsupported formats from failed extraction.
- Do not embed Meilisearch operations inside extractors.

Avoid building an overly complex extractor plugin framework.

A simple format dispatcher is sufficient.

## 5. Structured Document Model

Introduce a structured intermediate representation.

Conceptually:

```text
ExtractedDocument
    |
    +-- Title
    +-- Metadata
    +-- Format
    +-- Blocks[]
            |
            +-- BlockType
            +-- Text
            +-- SectionTitle
            +-- PageNumber
            +-- SlideNumber
            +-- SheetName
            +-- StartOffset
            +-- EndOffset
```

Suggested block types:

```text
Heading
Paragraph
Table
List
PlainText
```

These are logical types, not mandatory enum names.

### Requirements

- Preserve document reading order where reasonably possible.
- Preserve meaningful section boundaries.
- Preserve page and slide numbers when available.
- Preserve worksheet names for spreadsheets.
- Preserve document metadata when available.
- Use nullable fields for unavailable information.
- Avoid generating fake structural information.
- Keep document representation independent of UI and search storage.

Do not introduce detailed layout coordinates unless required for a current extraction capability.

### Metadata

Extract available information such as:

- Title.
- Author.
- Creation date.
- Modification date.
- File format.
- Page or slide count.
- Worksheet names.

Do not fabricate missing metadata.

Do not overwrite reliable source filesystem metadata with unverified embedded document metadata.

## 6. Format Detection

Implement a centralized format-selection mechanism.

Consider:

- File extension.
- Known file signatures.
- MIME type when available.
- Format-specific validation.

Do not trust the extension alone when it conflicts with actual file content.

### Required behavior

- Supported inputs select the appropriate extractor.
- Unsupported inputs return a clear result.
- Malformed documents produce controlled errors.
- Misleading extensions do not cause uncontrolled failures.
- Format detection must be bounded and inexpensive.

Do not implement exhaustive file-type forensic analysis.

## 7. Toxy Integration

Use Toxy as the preferred general-purpose extraction library, consistent with the original roadmap.

Reference:

https://github.com/nissl-lab/toxy

Inspect the existing version and Phase 0 compatibility results before integrating it.

### Requirements

1. Reuse validated Toxy functionality.
2. Convert Toxy results into the shared structured document model.
3. Preserve available text and metadata.
4. Detect unsupported or unreliable extraction paths.
5. Use format-specific adapters where Toxy is insufficient.

Do not force all formats through Toxy when doing so loses important structural metadata.

Do not replace the complete extraction strategy without documenting the reason.

Prefer proven .NET libraries with compatible distribution licenses.

## 8. Plain Text Extraction

Implement extraction for:

```text
.txt
.md
.markdown
```

### Requirements

- Detect or handle common text encodings.
- Support UTF-8 and appropriate UTF-16 variants.
- Recognize BOMs.
- Preserve Japanese and English Unicode text.
- Normalize line endings.
- Preserve meaningful paragraph breaks.
- Avoid silently corrupting undecodable text.

Use explicit fallback behavior for unknown encodings.

### Markdown

For Markdown documents:

- Recognize headings.
- Preserve paragraph boundaries.
- Preserve list content.
- Preserve fenced code blocks as appropriate.
- Avoid treating Markdown syntax as ordinary prose when doing so damages search quality.

Do not implement a full Markdown renderer.

The goal is structured searchable text extraction.

## 9. HTML Extraction

Implement HTML text extraction without browser automation.

### Requirements

- Extract the document title.
- Extract readable visible text.
- Identify heading structure.
- Preserve paragraph boundaries.
- Preserve useful list and table content.
- Decode HTML entities.
- Handle declared character encodings when practical.
- Exclude scripts, styles, and noncontent markup.
- Preserve Japanese and English content.

Do not execute JavaScript.

Do not make additional network requests from the extractor.

Do not implement a web crawler.

HTML content may be supplied directly by future crawler services.

### Safety

- Parse HTML as untrusted content.
- Avoid executing embedded resources.
- Do not render extracted HTML directly into Blazor.
- Return normalized text, not executable markup.

## 10. PDF Extraction

Implement PDF text and metadata extraction.

Use Toxy where validated.

If Toxy cannot preserve useful page information or extraction quality, introduce a focused PDF-specific adapter.

A library such as PdfPig may be considered if justified.

### Requirements

- Extract selectable text.
- Preserve page numbers.
- Preserve approximate reading order.
- Preserve meaningful paragraph boundaries where possible.
- Handle multipage documents.
- Preserve Japanese and English text.
- Extract metadata where available.
- Avoid merging unrelated columns when possible.

### Page numbering

Use 1-based page numbers for search-facing metadata.

Ensure that the mapping to original PDF pages is consistent.

### Complex layouts

PDF reading order is not always represented correctly by raw extraction order.

Handle common layouts reasonably, including:

- Single-column text.
- Multiple paragraphs.
- Headings.
- Basic tables.
- Simple multicolumn layouts.

Do not claim perfect layout reconstruction.

Do not implement a complete PDF layout engine.

### Scanned PDF

OCR is outside Phase 4.

For image-only or scanned PDFs:

- Return an understandable no-text or unsupported-content diagnostic.
- Do not report fabricated extracted content.
- Do not attempt OCR automatically.

### Encrypted PDF

Handle password-protected and encrypted PDFs safely.

Do not attempt password guessing or bypass encryption.

Return a specific extraction failure category where practical.

## 11. DOCX Extraction

Implement Word document extraction.

Preserve:

- Paragraphs.
- Headings.
- Lists.
- Tables.
- Document title and metadata.
- Logical document order.

### Requirements

- Extract all relevant body text.
- Identify headings where styles provide reliable information.
- Preserve paragraph boundaries.
- Preserve table row and cell separation.
- Handle documents containing mixed Japanese and English text.
- Handle empty documents gracefully.

Use an Open XML-based adapter if Toxy loses essential structure.

### Page numbers

DOCX files do not necessarily contain reliable fixed page boundaries because pagination depends on rendering.

Do not fabricate page numbers.

Use structural offsets and section information instead.

Do not introduce a full Office rendering engine solely to calculate pagination.

## 12. XLSX Extraction

Implement spreadsheet text extraction.

Spreadsheets require a different chunking strategy from ordinary prose documents.

### Requirements

- Enumerate worksheets.
- Preserve worksheet names.
- Extract cell values.
- Handle shared strings.
- Handle numeric and date values appropriately.
- Preserve table row and column relationships.
- Avoid combining unrelated worksheets into one text block.
- Avoid excessively large text representations of empty cell ranges.

### Structural representation

Prefer logical blocks based on:

- Worksheet.
- Table.
- Row group.
- Nonempty region.

When appropriate, include column headings in the extracted representation.

### Formulas

Prefer meaningful cached displayed values when available.

Do not evaluate arbitrary spreadsheet formulas.

If cached values are unavailable, use a clearly documented fallback.

### Chunking

Avoid splitting a table row across chunks unnecessarily.

Include relevant column headers when generating table-related chunks.

Do not create one enormous chunk for an entire workbook.

Do not create one tiny chunk per individual cell by default.

## 13. PPTX Extraction

Implement PowerPoint extraction with reliable slide-level structure.

**PPTX is a required format.**

Do not assume Toxy provides sufficient PPTX support.

If necessary, use the Microsoft Open XML SDK for a dedicated PPTX extractor.

### Requirements

- Extract text from slides.
- Preserve slide numbers.
- Preserve slide order.
- Extract text from shapes and text containers.
- Handle tables where supported.
- Preserve slide titles when identifiable.
- Preserve Japanese and English text.
- Handle slides with no text.

### Speaker notes

Speaker notes may be extracted when straightforward and reliable.

If implemented, distinguish them from visible slide content.

Do not silently combine notes and visible content without metadata.

### Slide numbering

Use 1-based slide numbers.

### Chunking

Use each slide as a natural structural boundary.

Short slides may produce one chunk.

Long slides may produce multiple chunks.

Avoid combining unrelated slides into one chunk by default.

## 14. Text Normalization

Implement or reuse a shared text normalization service.

### Requirements

- Normalize line endings.
- Remove invalid control characters.
- Preserve Unicode.
- Preserve meaningful punctuation.
- Preserve paragraph boundaries.
- Avoid excessive whitespace.
- Avoid corrupting Japanese text.
- Avoid destructive character normalization without justification.

Do not remove all formatting distinctions.

Structural boundaries are important for chunking.

### Repeated content

Handle repeated headers and footers conservatively.

Do not remove repeated text automatically unless it can be identified reliably.

An incorrect deduplication algorithm may destroy useful document content.

## 15. Structure-Aware Chunking

Extend the Phase 3.5 generic text chunker.

Do not replace the existing chunking implementation unnecessarily.

Use structured extraction blocks as input.

### Default settings

```text
TargetChunkTokens = 512
MaximumChunkTokens = 768
OverlapTokens = 64
```

Reuse existing configuration values where available.

### Chunking priorities

Prefer:

1. Document section boundaries.
2. Page or slide boundaries where appropriate.
3. Headings.
4. Paragraphs.
5. Table and row boundaries.
6. Sentence boundaries.
7. Token-based splitting as a fallback.

Do not allow chunks to exceed the configured model token limit.

Account for metadata and document-template text that will be included in embedding requests.

Use the Granite tokenizer or a verified equivalent.

### Format-specific rules

**PDF**

Prefer paragraph and page boundaries.

Do not merge unrelated page content indiscriminately.

**DOCX**

Prefer headings, paragraphs, and table boundaries.

Preserve the active section heading.

**PPTX**

Prefer slide boundaries.

Preserve slide numbers and titles.

**XLSX**

Prefer worksheet, table, and row-group boundaries.

Preserve relevant column headings.

**Markdown**

Prefer heading sections and paragraphs.

**HTML**

Prefer semantic sections and headings.

### Overlap

Apply token overlap where useful.

Do not introduce overlap that accidentally crosses unrelated worksheets or slides.

Avoid duplicating complete large tables merely to satisfy overlap settings.

### Output metadata

Each generated chunk should retain available source metadata:

```json
{
  "documentId": "parent-document-id",
  "chunkIndex": 5,
  "content": "Extracted section text",
  "sectionTitle": "Backup Recovery",
  "pageNumber": 12,
  "slideNumber": null,
  "sheetName": null
}
```

The example is conceptual.

Use the established Phase 3.5 `SearchChunk` model and deterministic ID strategy.

## 16. Extraction Pipeline Service

Implement a service that coordinates extraction and chunking.

Conceptually:

```csharp
Task<DocumentProcessingResult> ProcessAsync(
    DocumentInput input,
    CancellationToken cancellationToken);
```

The pipeline should:

1. Validate input.
2. Enforce file-size limits.
3. Detect document format.
4. Select the extractor.
5. Extract structured content.
6. Normalize metadata and text.
7. Generate structure-aware chunks.
8. Validate chunk sizes.
9. Return chunks and diagnostics.

### Responsibilities

Do not perform Meilisearch indexing inside the extraction pipeline.

Do not require the embedding server to be running for extraction tests.

The output must be usable by both keyword indexing and hybrid search.

Keep extraction, chunking, and indexing independently testable.

## 17. Resource Limits and Failure Isolation

Document extraction processes untrusted files and must use bounded resources.

Introduce configurable limits such as:

```json
{
  "DocumentExtraction": {
    "MaxFileSizeMB": 100,
    "MaxExtractedCharacters": 5000000,
    "TimeoutSeconds": 60,
    "MaxConcurrentExtractions": 2
  }
}
```

These are illustrative initial defaults.

### Requirements

- Validate file size before extraction when possible.
- Enforce extracted-text limits.
- Bound concurrent extraction operations.
- Prevent uncontrolled temporary-file growth.
- Support cancellation where possible.
- Report extraction timeouts.
- Handle malformed files.
- Handle encrypted files.
- Avoid unhandled exceptions from corrupted documents.

### Important limitation

A cancellation token or `Task.WhenAny` timeout does not forcibly stop synchronous native or blocking library code.

Do not claim hard timeout enforcement unless the execution can actually be terminated safely.

If a library cannot reliably enforce a resource boundary in-process, document the limitation and consider a narrowly scoped isolation mechanism when necessary.

Do not introduce a general distributed worker architecture.

### Failure categories

Provide structured failure information such as:

```text
UnsupportedFormat
InvalidDocument
EncryptedDocument
FileTooLarge
ExtractionTimeout
ExtractionFailed
NoExtractableText
ResourceLimitExceeded
```

Avoid exposing raw sensitive document content in error messages.

## 18. Configuration and Dependency Injection

Register extractors and processing services through existing dependency injection conventions.

Example logical services:

```text
DocumentFormatDetector
DocumentExtractionDispatcher
PlainTextExtractor
MarkdownExtractor
HtmlExtractor
PdfExtractor
DocxExtractor
XlsxExtractor
PptxExtractor
DocumentTextNormalizer
StructureAwareChunker
DocumentProcessingService
```

These names are illustrative.

Avoid creating interfaces for every class unless they provide a useful extension or testing boundary.

Reuse the existing chunking configuration where possible.

Do not introduce a second unrelated chunking settings system.

## 19. Testing Requirements

Create a regression corpus under an appropriate test or sample directory.

Use small, deterministic fixtures.

Include Japanese and English content.

### Required test matrix

| Format | Basic extraction | Structure | Unicode | Malformed input |
|---|---|---|---|---|
| TXT | Required | Paragraphs | Required | Required |
| Markdown | Required | Headings | Required | Required |
| HTML | Required | Sections | Required | Required |
| PDF | Required | Pages | Required | Required |
| DOCX | Required | Paragraphs/tables | Required | Required |
| XLSX | Required | Sheets/tables | Required | Required |
| PPTX | Required | Slides | Required | Required |

### Extraction tests

Verify:

1. Correct format detection.
2. Correct extractor selection.
3. Expected text content.
4. Unicode preservation.
5. Meaningful structural boundaries.
6. Available metadata preservation.
7. Controlled error handling.
8. Resource limit enforcement.
9. Empty document behavior.
10. Repeatable extraction output.

### Structure-aware chunking tests

Verify:

1. Token limits.
2. Heading preservation.
3. Paragraph boundaries.
4. PDF page associations.
5. DOCX section associations.
6. PPTX slide associations.
7. XLSX worksheet associations.
8. Long text splitting.
9. Appropriate overlap behavior.
10. Deterministic repeated processing.
11. Correct parent document identity.
12. Correct chunk location metadata.

### Integration tests

Verify the complete local processing path:

```text
Real document fixture
    |
    v
Extractor
    |
    v
Structured document
    |
    v
Structure-aware chunker
    |
    v
SearchChunk[]
```

Also test compatibility with the existing Phase 3.5 indexing interfaces.

Where a real Meilisearch and embedding server are available, add a small end-to-end test demonstrating that content extracted from PDF and Office files can be searched.

Keep these integration tests isolated from production indexes.

### Regression tests

Run all existing Phase 0–3.5 tests.

Verify that:

- Keyword search remains functional.
- Hybrid search remains functional.
- Existing document and chunk IDs remain compatible.
- Administration functionality remains intact.
- No crawler implementation is required for extraction.

Do not claim tests passed unless they actually executed successfully.

## 20. Documentation

Document:

1. Supported formats.
2. Extractor selection.
3. Extraction dependencies.
4. Known format limitations.
5. Structured document representation.
6. Structure-aware chunking behavior.
7. Default resource limits.
8. Extraction error categories.
9. Sample processing commands.
10. Testing instructions.

Maintain a format compatibility matrix based on actual test results.

Distinguish fully supported behavior from partial support.

Document known limitations such as:

- Scanned PDFs requiring OCR.
- PDF reading-order ambiguity.
- Unavailable DOCX pagination.
- Unsupported encrypted documents.
- Spreadsheet formula limitations.
- Embedded objects without extractable text.

## 21. Out of Scope

Do not implement:

- Filesystem crawling.
- Recursive directory enumeration.
- UNC share crawling.
- Web crawling.
- Incremental file synchronization.
- Persistent crawl-state tracking.
- Crawler scheduling.
- Background crawl jobs.
- Crawler configuration UI.
- OCR.
- Document rendering.
- Browser automation.
- Spreadsheet formula execution.
- Legacy binary Office format support.
- RAG.
- Chat interfaces.
- Model training or fine-tuning.
- Additional embedding models.
- Reranking models.
- Windows release packaging.

These belong to future phases or deferred enhancements.

## 22. Acceptance Criteria

Phase 4 is complete only when all requirements are satisfied.

### Extraction

- TXT and Markdown extraction work.
- HTML extraction works.
- PDF extraction works.
- DOCX extraction works.
- XLSX extraction works.
- PPTX extraction works.
- Japanese and English text are preserved.
- Real fixture documents are used for validation.

### Structure

- PDF page numbers are preserved.
- DOCX headings and paragraphs are represented.
- XLSX worksheet information is preserved.
- PPTX slide numbers are preserved.
- Table structure is retained where reasonably supported.
- Unavailable metadata is not fabricated.

### Chunking

- Extracted documents produce valid searchable chunks.
- Chunking respects structural boundaries.
- Chunk token limits are enforced.
- Source location metadata survives chunk generation.
- Repeated processing produces deterministic chunk identities.

### Reliability

- Invalid documents do not crash the application.
- Unsupported formats are handled explicitly.
- Encrypted documents are handled gracefully.
- File-size and text-output limits are enforced.
- Extraction failures generate actionable diagnostics.
- Cancellation and timeout limitations are documented accurately.

### Compatibility

- Existing Phase 0–3.5 functionality remains intact.
- Extraction is independent of Meilisearch.
- Extraction is independent of the embedding server.
- Extracted chunks are compatible with Phase 3.5 indexing.
- No crawler implementation is required.

### Tests

- The solution builds.
- Existing tests pass.
- New unit tests pass.
- Format-specific extraction tests pass.
- Structure-aware chunking tests pass.
- Integration tests pass where their prerequisites are available.

Clearly report any unmet acceptance criteria.

## 23. Required Completion Report

At the end of implementation, provide:

### A. Implementation summary

Describe implemented extraction and chunking functionality.

### B. Files changed

List created and modified files.

### C. Format compatibility matrix

For each supported format, report:

- Extraction adapter.
- Validated capabilities.
- Known limitations.
- Test results.

### D. Architectural decisions

Explain:

- Extractor selection.
- Toxy usage.
- Format-specific fallback libraries.
- Structured document model.
- Chunking strategy.
- Metadata preservation.
- Resource limits.
- Failure handling.

### E. Build and test commands

List commands actually executed.

### F. Test results

Distinguish passed, failed, skipped, and unexecuted tests.

### G. Known limitations

Report extraction quality and format limitations accurately.

### H. Remaining work

Identify incomplete requirements and deferred improvements.

Do not begin Phase 5 automatically.

---

## Final Instructions

Implement Phase 4 directly in the existing Lantern repository.

Inspect and preserve all completed functionality from Phases 0–3.5.

Use Toxy as the preferred extraction library, with format-specific adapters when required.

Prioritize reliable extraction of PDF, DOCX, XLSX, PPTX, HTML, Markdown, and plain-text documents.

Preserve document structure and source location information whenever available.

Extend the existing chunking foundation rather than replacing it.

Ensure that the output can be consumed by the existing keyword and hybrid indexing infrastructure.

Keep resource usage bounded and handle malformed documents safely.

Do not introduce file crawling or scheduling.

**The primary success condition is a reliable pipeline that transforms supported real-world documents into normalized, structurally meaningful, location-aware search chunks suitable for both keyword search and local Granite-based hybrid search.**

Do not implement Phase 5 or later features.
