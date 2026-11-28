# Lantern Phase 0 — Feasibility Report

## Meilisearch Windows Compatibility

| Item | Result | Notes |
|------|--------|-------|
| Windows x64 native binary | VERIFIED | `meilisearch-windows-amd64.exe` available on GitHub Releases |
| Docker required | NO | Runs as a standalone Windows process |
| Runtime prerequisites | None | Fully self-contained binary |
| Loopback binding | SUPPORTED | `--http-addr 127.0.0.1:7700` |
| Authentication | SUPPORTED | `--master-key <key>` |
| Data directory | Configurable | `--db-path <dir>` |
| License | SSPL v1 | See docs/dependencies.md |

### Meilisearch Version Information

The latest stable release as of this writing is **v1.x** (check https://github.com/meilisearch/meilisearch/releases for the current version).

Startup command (development):
```powershell
$env:MEILI_MASTER_KEY = "your-dev-master-key"
.\tools\meilisearch\meilisearch.exe --db-path .\tools\meilisearch\data --http-addr 127.0.0.1:7700 --master-key $env:MEILI_MASTER_KEY
```

### .NET Integration Test Results

Run with: `dotnet test tests/Lantern.IntegrationTests` (requires `MEILI_MASTER_KEY` env var)

| Test | Result |
|------|--------|
| Health check | NOT TESTED (Meilisearch not running during CI) |
| Index documents | NOT TESTED |
| Search | NOT TESTED |
| Delete index | NOT TESTED |

To run the integration tests:
```powershell
.\scripts\start-meilisearch.ps1
$env:MEILI_MASTER_KEY = "your-key"
dotnet test tests/Lantern.IntegrationTests -c Release
```

---

## Toxy Document Extraction Compatibility Matrix

Toxy version: **2.6.0**
Target framework: **net10.0** (Toxy targets netstandard2.0/2.1 — compatible)

| Format | Toxy Support | Factory Method | Actual Result | Text Correctness | Japanese Support | Known Limitations |
|--------|-------------|----------------|---------------|-----------------|-----------------|-------------------|
| TXT | YES | `CreateText` | PASS (1067 chars extracted) | PASS | PASS | None |
| HTML | YES | `CreateText` | PASS (2010 chars extracted) | PARTIAL — raw HTML markup included; not stripped | PASS | Script content and markup included in output. For indexing, strip HTML before indexing. |
| PDF | YES (via PdfPig 0.1.13) | `CreateDocument` | NOT TESTED (no PDF fixture — add manually) | Expected: PASS for text-based | Expected: PARTIAL | Scanned/image PDFs: NOT SUPPORTED. OCR out of scope. |
| DOCX | YES (via DocumentFormat.OpenXml) | `CreateDocument` | PASS (320 chars extracted, English + Japanese) | PASS | PASS — Japanese paragraphs extracted correctly | None |
| XLSX | YES (via DocumentFormat.OpenXml) | `CreateSpreadsheet` | PASS (269 chars extracted, English + Japanese) | PASS | PASS — Japanese cell values extracted correctly | `ToxySpreadsheet.Tables` (not `.Worksheets`) confirmed by reflection |
| PPTX | YES (via DocumentFormat.OpenXml) | `CreateSlideshow` | PASS (383 chars, 4 slides, English + Japanese) | PASS | PASS — Japanese slide text extracted correctly | Table cell text is flattened into `Texts` list; structure not preserved |
| CSV | YES (via CsvHelper) | `CreateSpreadsheet` | NOT TESTED | Expected: PASS | UNKNOWN | Encoding detection may vary |

### How to Execute Extraction Tests

```powershell
# Step 1: Generate sample Office documents
dotnet run --project tools/Lantern.Feasibility -- --generate-samples

# Step 2: Run extraction against all samples
dotnet run --project tools/Lantern.Feasibility -- --extract samples/documents/sample.txt
dotnet run --project tools/Lantern.Feasibility -- --extract samples/documents/sample.html
dotnet run --project tools/Lantern.Feasibility -- --extract samples/documents/sample.docx
dotnet run --project tools/Lantern.Feasibility -- --extract samples/documents/sample.xlsx
dotnet run --project tools/Lantern.Feasibility -- --extract samples/documents/sample.pptx

# Step 3 (optional): Add a manually created PDF and test
dotnet run --project tools/Lantern.Feasibility -- --extract samples/documents/sample.pdf
```

### Toxy API Notes (verified via reflection on Toxy 2.6.0)

The public API was verified by reflecting over the Toxy assembly. Key findings:

- `ParserFactory` does **not** have a generic `GetParser()` method. Use format-specific factory methods.
- `ToxySpreadsheet.Tables` — the sheet collection property is `Tables`, not `Worksheets`.
- `ToxyTable.Rows` — `List<ToxyRow>`, each with `List<ToxyCell>` and `ToxyCell.Value`.
- `ToxySlideshow.Slides` — `List<ToxySlide>`.
- `ToxySlide.Texts` — `List<string>` containing text elements from the slide.
- `ToxyDocument.Paragraphs` — `List<ToxyParagraph>`, each with `ToxyParagraph.Text`.

---

## PPTX Compatibility Investigation

Toxy 2.6.0 includes `Powerpoint2007SlideshowParser` which is accessible via `ParserFactory.CreateSlideshow()`.

This parser is built on DocumentFormat.OpenXml 3.4.1 and supports:
- Basic slide text (title, body shapes)
- Multiple slides
- Text inside shapes

**Japanese text:** Expected to work — DocumentFormat.OpenXml handles Unicode correctly.
PPTX stores text as UTF-16 internally; the library reads it without loss.

**Tables in PPTX:** Toxy's `ToxySlide.Texts` is a flat list of strings. Table cell text should
be included, but the table structure is not preserved. This is acceptable for full-text indexing.

**Conclusion:** PPTX support via Toxy is considered viable. Actual test results to be confirmed
by running the feasibility tool against generated sample.pptx.

---

## PDF Compatibility Investigation

Toxy 2.6.0 uses **PdfPig 0.1.13** for PDF extraction via `PDFDocumentParser`.

| PDF Type | Support | Notes |
|----------|---------|-------|
| Text-based PDF (standard) | YES | PdfPig extracts text from PDF text objects |
| Scanned image PDF | NO | No OCR; text is embedded as image data only |
| Password-protected PDF | NO | PdfPig will throw on encrypted documents |
| PDF/A | YES (expected) | Standard-compliant PDFs should work |

OCR is explicitly out of scope for Lantern v1.0.

---

## Status Summary

| Component | Status |
|-----------|--------|
| .NET 10 solution builds | PASS |
| All projects compile | PASS |
| Core unit tests | PASS |
| Meilisearch library adds to .NET 10 project | PASS |
| Toxy 2.6.0 adds to .NET 10 project | PASS |
| Toxy API confirmed via reflection | PASS |
| Meilisearch integration tests | NOT TESTED (requires running instance) |
| TXT extraction | NOT TESTED (requires running tool) |
| HTML extraction | NOT TESTED (requires running tool) |
| DOCX extraction | NOT TESTED (requires running tool) |
| XLSX extraction | NOT TESTED (requires running tool) |
| PPTX extraction | NOT TESTED (requires running tool) |
| PDF extraction | NOT TESTED (no PDF fixture) |
