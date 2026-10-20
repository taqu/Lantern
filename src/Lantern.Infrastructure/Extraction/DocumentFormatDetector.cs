using Lantern.Core.Extraction;

namespace Lantern.Infrastructure.Extraction;

public sealed class DocumentFormatDetector
{
    private static readonly Dictionary<string, DocFormat> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = DocFormat.PlainText,
        [".text"] = DocFormat.PlainText,
        [".md"] = DocFormat.Markdown,
        [".markdown"] = DocFormat.Markdown,
        [".html"] = DocFormat.Html,
        [".htm"] = DocFormat.Html,
        [".pdf"] = DocFormat.Pdf,
        [".docx"] = DocFormat.Docx,
        [".xlsx"] = DocFormat.Xlsx,
        [".pptx"] = DocFormat.Pptx,
        [".csv"] = DocFormat.Csv,
    };

    // Magic bytes for format validation
    private static readonly byte[] PdfMagic = "%PDF"u8.ToArray();
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04]; // PK header

    public DocFormat Detect(DocumentInput input)
    {
        string ext = input.Extension;
        if (string.IsNullOrEmpty(ext) && input.FilePath != null)
            ext = Path.GetExtension(input.FilePath).ToLowerInvariant();

        if (!string.IsNullOrEmpty(ext) && ExtensionMap.TryGetValue(ext, out var format))
            return format;

        // Try magic bytes from file if available
        if (input.FilePath != null && File.Exists(input.FilePath))
            return DetectFromFile(input.FilePath);

        return DocFormat.Unknown;
    }

    private static DocFormat DetectFromFile(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[8];
            int read = stream.Read(header, 0, header.Length);
            if (read < 4) return DocFormat.Unknown;

            if (header[0] == PdfMagic[0] && header[1] == PdfMagic[1] &&
                header[2] == PdfMagic[2] && header[3] == PdfMagic[3])
                return DocFormat.Pdf;

            if (header[0] == ZipMagic[0] && header[1] == ZipMagic[1] &&
                header[2] == ZipMagic[2] && header[3] == ZipMagic[3])
                return DocFormat.Unknown; // Could be DOCX/XLSX/PPTX - rely on extension
        }
        catch { }
        return DocFormat.Unknown;
    }

    public static bool IsSupported(DocFormat format) => format switch
    {
        DocFormat.PlainText or DocFormat.Markdown or DocFormat.Html
            or DocFormat.Pdf or DocFormat.Docx or DocFormat.Xlsx
            or DocFormat.Pptx or DocFormat.Csv => true,
        _ => false
    };
}
