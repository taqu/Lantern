using Lantern.Core.Extraction;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Lantern.Infrastructure.Extraction;

public sealed class PdfExtractor : IDocumentExtractor
{
    public bool CanExtract(DocFormat format) => format == DocFormat.Pdf;

    public Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Extract(input, cancellationToken), cancellationToken);
    }

    private static ExtractedDocument Extract(DocumentInput input, CancellationToken ct)
    {
        byte[] pdfBytes;
        if (input.FilePath != null)
            pdfBytes = File.ReadAllBytes(input.FilePath);
        else if (input.Stream != null)
        {
            using var ms = new MemoryStream();
            input.Stream.CopyTo(ms);
            pdfBytes = ms.ToArray();
        }
        else
            throw new ArgumentException("No source in DocumentInput");

        PdfDocument pdf;
        try
        {
            pdf = PdfDocument.Open(pdfBytes);
        }
        catch (Exception ex)
        {
            if (ex.Message.IndexOf("encrypt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidOperationException($"PDF_ENCRYPTED: {ex.Message}", ex);
            throw;
        }

        using (pdf)
        {
            string title = pdf.Information?.Title?.Trim() is { Length: > 0 } t
                ? t
                : (input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Untitled");

            var blocks = new List<ExtractedBlock>();
            int pageCount = pdf.NumberOfPages;

            for (int pageNum = 1; pageNum <= pageCount; pageNum++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var page = pdf.GetPage(pageNum);
                    string pageText;
                    try
                    {
                        pageText = ContentOrderTextExtractor.GetText(page);
                    }
                    catch
                    {
                        // Fallback: join words
                        pageText = string.Join(" ", page.GetWords().Select(w => w.Text));
                    }

                    pageText = TextNormalizer.NormalizeAll(pageText ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(pageText)) continue;

                    var paragraphs = pageText.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var para in paragraphs)
                    {
                        if (!string.IsNullOrWhiteSpace(para))
                            blocks.Add(new ExtractedBlock(BlockType.Paragraph, para.Trim(), PageNumber: pageNum));
                    }
                }
                catch (Exception) { /* Skip pages that fail */ }
            }

            return new ExtractedDocument
            {
                Title = title,
                Blocks = blocks,
                Metadata = new ExtractedDocumentMetadata(
                    Title: title,
                    Author: pdf.Information?.Author?.Trim(),
                    Format: DocFormat.Pdf,
                    PageCount: pageCount)
            };
        }
    }
}
