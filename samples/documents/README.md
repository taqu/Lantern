# Sample Documents

This directory contains test fixtures for the Lantern document extraction feasibility evaluation.

## Automatically Generated (by `dotnet run --project tools/Lantern.Feasibility -- --generate-samples`)

| File | Format | Generator | Contains |
|------|--------|-----------|---------|
| sample.docx | DOCX | DocumentFormat.OpenXml | English/Japanese paragraphs, table |
| sample.xlsx | XLSX | DocumentFormat.OpenXml | English/Japanese cells, header row |
| sample.pptx | PPTX | DocumentFormat.OpenXml | 4 slides with English/Japanese text |

## Manually Created (committed to repo)

| File | Format | Contains |
|------|--------|---------|
| sample.txt | Plain text | English + Japanese paragraphs |
| sample.html | HTML | Markup, table, Japanese text, script/comment noise |

## Required Manual Addition

### PDF (sample.pdf)

PDF binary files cannot be generated reliably in a cross-platform manner without external tools.
To create a test PDF:

1. Open `sample.txt` or `sample.html` in any browser.
2. Use "Print to PDF" (Ctrl+P -> Save as PDF).
3. Save as `samples/documents/sample.pdf`.
4. Verify the file contains **selectable text** (not a scanned image).

The PDF should contain these known verification strings:
- "Lantern is a lightweight"
- "full-text search platform"
- "全文検索" (Japanese, may not extract if rendered as image)

### Known PDF Limitations with Toxy/PdfPig

- Text-based PDFs: supported by PdfPig (bundled in Toxy 2.6.0)
- Scanned image PDFs: NOT supported (OCR is out of scope for v1.0)
- Password-protected PDFs: NOT supported

## Gitignore Note

Binary samples generated at runtime (DOCX, XLSX, PPTX, PDF) are excluded from git
via `.gitignore`. Regenerate them by running the feasibility tool.
