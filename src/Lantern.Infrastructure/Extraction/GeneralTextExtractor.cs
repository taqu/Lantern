using System.Text.RegularExpressions;
using Lantern.Core.Extraction;

namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// General-purpose text extractor for source code, configuration files, logs,
/// and any text-based file not handled by a format-specific extractor.
/// Preserves indentation and line structure. Records line numbers per block.
/// </summary>
public sealed class GeneralTextExtractor : IDocumentExtractor
{
    private readonly EncodingDetector _encodingDetector = new();

    // Patterns that hint at a logical section start (function/class/top-level declaration)
    private static readonly Regex SectionPattern = new(
        @"^\s*(public|private|protected|internal|static|async|def |fn |func |function |class |struct |impl |interface |namespace |module |type |sub |end sub|#region|//\s*MARK:|###|##)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool CanExtract(DocFormat format) => format == DocFormat.GeneralText;

    public async Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await ReadBytesAsync(input, cancellationToken);

        // Binary check
        if (TextBinaryClassifier.ClassifyBytes(bytes) == TextBinaryClassifier.Classification.Binary)
            throw new InvalidOperationException("BINARY_CONTENT: Input appears to be binary, not text.");

        EncodingDetector.DetectionResult detection = _encodingDetector.Detect(bytes, hintEncoding: null);
        string text = _encodingDetector.Decode(bytes, detection);

        // Normalize line endings only (preserve indentation and whitespace)
        text = NormalizeLineEndings(text);

        string title = DeriveTitle(input, text);
        IReadOnlyList<ExtractedBlock> blocks = BuildBlocks(text);

        return new ExtractedDocument
        {
            Title = title,
            Blocks = blocks,
            Metadata = new ExtractedDocumentMetadata(
                Title: title,
                Format: DocFormat.GeneralText,
                DetectedEncoding: detection.EncodingName)
        };
    }

    private static async Task<byte[]> ReadBytesAsync(DocumentInput input, CancellationToken ct)
    {
        if (input.FilePath != null)
            return await File.ReadAllBytesAsync(input.FilePath, ct);
        if (input.Stream != null)
        {
            using var ms = new MemoryStream();
            await input.Stream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        return [];
    }

    // Normalize CR/CRLF to LF; strip only truly invalid control characters (keep tab)
    private static string NormalizeLineEndings(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                sb.Append('\n');
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (c < 0x09 || (c > 0x0D && c < 0x20) || c == 0x7F)
            {
                // Drop invalid control chars; keep \t (0x09), \n (0x0A), \v(0x0B), \f(0x0C), \r(0x0D already handled)
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string DeriveTitle(DocumentInput input, string text)
    {
        if (input.FileName != null)
        {
            string name = Path.GetFileNameWithoutExtension(input.FileName);
            if (!string.IsNullOrEmpty(name)) return name;
            // Extensionless file: use full filename
            return input.FileName;
        }
        if (input.FilePath != null)
        {
            string name = Path.GetFileNameWithoutExtension(input.FilePath);
            if (!string.IsNullOrEmpty(name)) return name;
        }
        // Fallback: first non-empty line up to 80 chars
        string first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Untitled";
        return first.Length > 80 ? first[..80] : first;
    }

    /// <summary>
    /// Split text into blocks at blank-line boundaries, tracking line numbers.
    /// Applies lightweight heuristics to detect section boundaries within blocks.
    /// Preserves indentation and source structure.
    /// </summary>
    private static IReadOnlyList<ExtractedBlock> BuildBlocks(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];

        string[] lines = text.Split('\n');
        var blocks = new List<ExtractedBlock>();
        string? currentSection = null;
        int blockStartLine = 0;
        var buffer = new System.Text.StringBuilder();

        void FlushBlock(int endLine)
        {
            string content = buffer.ToString();
            // Trim trailing blank lines but preserve internal structure
            content = content.TrimEnd('\n', '\r');
            if (string.IsNullOrWhiteSpace(content)) return;

            blocks.Add(new ExtractedBlock(
                BlockType.PlainText,
                content,
                SectionTitle: currentSection,
                StartLine: blockStartLine + 1,  // 1-based
                EndLine: endLine));
            buffer.Clear();
        }

        int consecutiveBlanks = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            bool isBlank = string.IsNullOrWhiteSpace(line);

            if (isBlank)
            {
                consecutiveBlanks++;
                if (consecutiveBlanks >= 2 && buffer.Length > 0)
                {
                    // Flush current block on 2+ consecutive blank lines
                    FlushBlock(endLine: i); // 1-based end is i (blank line before)
                    blockStartLine = i + 1;
                    consecutiveBlanks = 0;
                    continue;
                }
                if (buffer.Length > 0)
                    buffer.AppendLine();
                continue;
            }

            consecutiveBlanks = 0;

            // Detect section boundary for common patterns
            if (buffer.Length == 0) blockStartLine = i;
            if (SectionPattern.IsMatch(line))
            {
                // Flush if we already have content and this looks like a new declaration
                if (buffer.Length > 0)
                {
                    string trimmed = line.TrimStart();
                    bool isTopLevel = line.Length == trimmed.Length || line[0] != ' ' && line[0] != '\t';
                    if (isTopLevel)
                    {
                        FlushBlock(endLine: i);
                        blockStartLine = i;
                    }
                }
                // Track section name from simple patterns
                Match match = Regex.Match(line, @"(?:class|struct|interface|fn|func|function|def|namespace|module)\s+(\w+)");
                if (match.Success) currentSection = match.Groups[1].Value;
            }

            buffer.AppendLine(line);
        }

        if (buffer.Length > 0)
            FlushBlock(endLine: lines.Length);

        return blocks;
    }
}
