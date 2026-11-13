using Lantern.Core.Extraction;

namespace Lantern.Infrastructure.Extraction;

public sealed class DocumentFormatDetector
{
    private static readonly Dictionary<string, DocFormat> ExtensionMap = new(StringComparer.OrdinalIgnoreCase) {
        // Legacy Office binary formats
        [".doc"] = DocFormat.Doc,
        [".xls"] = DocFormat.Xls,
        [".ppt"] = DocFormat.Ppt,
        // Office and document formats
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
        // C / C++
        [".c"] = DocFormat.GeneralText,
        [".h"] = DocFormat.GeneralText,
        [".cc"] = DocFormat.GeneralText,
        [".cpp"] = DocFormat.GeneralText,
        [".cxx"] = DocFormat.GeneralText,
        [".hpp"] = DocFormat.GeneralText,
        [".hxx"] = DocFormat.GeneralText,
        // C# / .NET
        [".cs"] = DocFormat.GeneralText,
        [".csproj"] = DocFormat.GeneralText,
        [".fs"] = DocFormat.GeneralText,
        [".fsproj"] = DocFormat.GeneralText,
        [".sln"] = DocFormat.GeneralText,
        [".slnx"] = DocFormat.GeneralText,
        [".props"] = DocFormat.GeneralText,
        [".targets"] = DocFormat.GeneralText,
        // Rust
        [".rs"] = DocFormat.GeneralText,
        [".toml"] = DocFormat.GeneralText,
        // Python
        [".py"] = DocFormat.GeneralText,
        [".pyi"] = DocFormat.GeneralText,
        // Java / Kotlin
        [".java"] = DocFormat.GeneralText,
        [".kt"] = DocFormat.GeneralText,
        [".kts"] = DocFormat.GeneralText,
        // JavaScript / TypeScript
        [".js"] = DocFormat.GeneralText,
        [".jsx"] = DocFormat.GeneralText,
        [".ts"] = DocFormat.GeneralText,
        [".tsx"] = DocFormat.GeneralText,
        [".mjs"] = DocFormat.GeneralText,
        [".cjs"] = DocFormat.GeneralText,
        // Web / Style
        [".css"] = DocFormat.GeneralText,
        [".scss"] = DocFormat.GeneralText,
        [".sass"] = DocFormat.GeneralText,
        [".less"] = DocFormat.GeneralText,
        // Data / Config
        [".json"] = DocFormat.GeneralText,
        [".jsonl"] = DocFormat.GeneralText,
        [".ndjson"] = DocFormat.GeneralText,
        [".xml"] = DocFormat.GeneralText,
        [".yaml"] = DocFormat.GeneralText,
        [".yml"] = DocFormat.GeneralText,
        [".ini"] = DocFormat.GeneralText,
        [".cfg"] = DocFormat.GeneralText,
        [".conf"] = DocFormat.GeneralText,
        [".config"] = DocFormat.GeneralText,
        [".properties"] = DocFormat.GeneralText,
        [".env"] = DocFormat.GeneralText,
        // Shell / Scripts
        [".sh"] = DocFormat.GeneralText,
        [".bash"] = DocFormat.GeneralText,
        [".zsh"] = DocFormat.GeneralText,
        [".ps1"] = DocFormat.GeneralText,
        [".bat"] = DocFormat.GeneralText,
        [".cmd"] = DocFormat.GeneralText,
        // SQL
        [".sql"] = DocFormat.GeneralText,
        // Documentation
        [".rst"] = DocFormat.GeneralText,
        [".adoc"] = DocFormat.GeneralText,
        [".tex"] = DocFormat.GeneralText,
        [".tsv"] = DocFormat.GeneralText,
        [".log"] = DocFormat.GeneralText,
        // Go, Ruby, Swift, Scala, Dart
        [".go"] = DocFormat.GeneralText,
        [".rb"] = DocFormat.GeneralText,
        [".swift"] = DocFormat.GeneralText,
        [".scala"] = DocFormat.GeneralText,
        [".dart"] = DocFormat.GeneralText,
        [".lua"] = DocFormat.GeneralText,
        [".r"] = DocFormat.GeneralText,
        [".vim"] = DocFormat.GeneralText,
        [".tf"] = DocFormat.GeneralText,
        [".hcl"] = DocFormat.GeneralText,
        [".proto"] = DocFormat.GeneralText,
        [".graphql"] = DocFormat.GeneralText,
        [".gql"] = DocFormat.GeneralText,
        [".gitignore"] = DocFormat.GeneralText,
        [".gitattributes"] = DocFormat.GeneralText,
        [".editorconfig"] = DocFormat.GeneralText,
        [".dockerfile"] = DocFormat.GeneralText,
        [".makefile"] = DocFormat.GeneralText,
    };

    // Known extensionless filenames that are always text
    private static readonly HashSet<string> KnownTextFilenames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Makefile", "Dockerfile", "dockerfile", "makefile", "GNUmakefile",
        "Rakefile", "Gemfile", "Procfile", "Brewfile",
        "LICENCE", "LICENSE", "NOTICE", "README", "CHANGELOG", "AUTHORS",
        ".gitignore", ".gitattributes", ".editorconfig", ".env",
    };

    // Magic bytes for format validation
    private static readonly byte[] PdfMagic = "%PDF"u8.ToArray();
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04]; // PK header (Office Open XML / ZIP)
    private static readonly byte[] Ole2Magic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]; // OLE2 Compound Document

    public DocFormat Detect(DocumentInput input)
    {
        string ext = input.Extension;
        if(string.IsNullOrEmpty(ext) && input.FilePath != null)
            ext = Path.GetExtension(input.FilePath).ToLowerInvariant();

        if(!string.IsNullOrEmpty(ext) && ExtensionMap.TryGetValue(ext, out DocFormat format))
            return format;

        // Check known extensionless filenames
        string? filename = input.FileName ?? (input.FilePath != null ? Path.GetFileName(input.FilePath) : null);
        if(filename != null && string.IsNullOrEmpty(ext)) {
            if(KnownTextFilenames.Contains(filename))
                return DocFormat.GeneralText;
        }

        // Try magic bytes from file if available
        if(input.FilePath != null && File.Exists(input.FilePath))
            return DetectFromFile(input.FilePath);

        return DocFormat.Unknown;
    }

    private static DocFormat DetectFromFile(string filePath)
    {
        try {
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] header = new byte[8192];
            int read = stream.Read(header, 0, header.Length);
            if(read < 4) return DocFormat.Unknown;

            if(header[0] == PdfMagic[0] && header[1] == PdfMagic[1] &&
                header[2] == PdfMagic[2] && header[3] == PdfMagic[3])
                return DocFormat.Pdf;

            if(header[0] == ZipMagic[0] && header[1] == ZipMagic[1] &&
                header[2] == ZipMagic[2] && header[3] == ZipMagic[3])
                return DocFormat.Unknown; // Could be DOCX/XLSX/PPTX — rely on extension

            if(read >= 8 &&
                header[0] == Ole2Magic[0] && header[1] == Ole2Magic[1] &&
                header[2] == Ole2Magic[2] && header[3] == Ole2Magic[3] &&
                header[4] == Ole2Magic[4] && header[5] == Ole2Magic[5] &&
                header[6] == Ole2Magic[6] && header[7] == Ole2Magic[7])
                return DocFormat.Unknown; // Could be DOC/XLS/PPT — rely on extension to distinguish

            // Try text classification for unknown-extension files
            TextBinaryClassifier.Classification classification = TextBinaryClassifier.ClassifyBytes(header.AsSpan(0, read));
            if(classification == TextBinaryClassifier.Classification.Text)
                return DocFormat.GeneralText;
        } catch { }
        return DocFormat.Unknown;
    }

    public static bool IsSupported(DocFormat format) => format switch {
        DocFormat.PlainText or DocFormat.Markdown or DocFormat.Html
            or DocFormat.Pdf or DocFormat.Docx or DocFormat.Xlsx
            or DocFormat.Pptx or DocFormat.Csv or DocFormat.GeneralText
            or DocFormat.Doc or DocFormat.Xls or DocFormat.Ppt => true,
        _ => false
    };
}
