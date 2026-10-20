namespace Lantern.Core.Extraction;

public sealed class DocumentInput
{
    public string? FilePath { get; init; }
    public Stream? Stream { get; init; }
    public string? MimeType { get; init; }
    public string Extension { get; init; } = "";
    public string? FileName { get; init; }

    public static DocumentInput FromFile(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return new DocumentInput
        {
            FilePath = filePath,
            Extension = Path.GetExtension(filePath).ToLowerInvariant(),
            FileName = Path.GetFileName(filePath)
        };
    }

    public static DocumentInput FromStream(Stream stream, string extension, string? fileName = null, string? mimeType = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new DocumentInput
        {
            Stream = stream,
            Extension = extension.ToLowerInvariant(),
            FileName = fileName,
            MimeType = mimeType
        };
    }
}
