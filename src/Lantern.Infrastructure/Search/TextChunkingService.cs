using Lantern.Core.Search;
using Microsoft.Extensions.Options;

namespace Lantern.Infrastructure.Search;

public sealed class TextChunkingService : ITextChunkingService
{
    private readonly ChunkingOptions _defaultOptions;
    private Func<string, int>? _tokenCounter;

    public TextChunkingService(IOptions<ChunkingOptions> options)
    {
        _defaultOptions = options.Value;
    }

    public void SetTokenCounter(Func<string, int> counter) => _tokenCounter = counter;

    public int CountTokens(string text)
    {
        if (_tokenCounter != null) return _tokenCounter(text);
        // Approximation: conservative estimate of 3 chars per token
        return Math.Max(1, text.Length / 3);
    }

    public IReadOnlyList<TextChunk> Chunk(string text, ChunkingOptions? options = null)
    {
        ChunkingOptions opts = options ?? _defaultOptions;
        if (string.IsNullOrWhiteSpace(text)) return [];

        var result = new List<TextChunk>();
        List<(string Text, int Offset, string? Section)> segments = SplitIntoSegments(text);
        BuildChunks(segments, opts, result);
        return result;
    }

    // Split text into segments: paragraphs with section tracking
    private static List<(string Text, int Offset, string? Section)> SplitIntoSegments(string text)
    {
        var segments = new List<(string Text, int Offset, string? Section)>();
        string? currentSection = null;

        List<(string Text, int Offset)> paragraphs = SplitByParagraph(text);
        foreach ((string para, int paraOffset) in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(para)) continue;

            // Detect headings (lines starting with # or all-caps short lines)
            if (IsHeading(para))
            {
                currentSection = para.TrimStart('#', ' ').Trim();
            }
            segments.Add((para, paraOffset, currentSection));
        }
        return segments;
    }

    private static bool IsHeading(string text)
    {
        string trimmed = text.Trim();
        return trimmed.StartsWith('#');
    }

    private static List<(string Text, int Offset)> SplitByParagraph(string text)
    {
        var result = new List<(string, int)>();
        int start = 0;
        int i = 0;
        while (i < text.Length)
        {
            if (i + 1 < text.Length && text[i] == '\n' && text[i + 1] == '\n')
            {
                result.Add((text[start..i], start));
                i += 2;
                start = i;
            }
            else
            {
                i++;
            }
        }
        if (start < text.Length)
            result.Add((text[start..], start));
        return result;
    }

    private void BuildChunks(List<(string Text, int Offset, string? Section)> segments, ChunkingOptions opts, List<TextChunk> result)
    {
        var currentContent = new System.Text.StringBuilder();
        int currentStart = 0;
        string? currentSection = null;
        int chunkIndex = 0;

        void FlushChunk()
        {
            string content = currentContent.ToString().Trim();
            if (string.IsNullOrEmpty(content)) return;
            result.Add(new TextChunk(content, chunkIndex++, currentStart, currentStart + content.Length, currentSection));
            currentContent.Clear();
        }

        foreach ((string text, int offset, string section) in segments)
        {
            string trimmed = text.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            int segTokens = CountTokens(trimmed);
            int currentTokens = currentContent.Length > 0 ? CountTokens(currentContent.ToString()) : 0;

            if (segTokens > opts.MaxTokens)
            {
                // Flush current and split this large segment into sentences
                FlushChunk();
                List<(string Text, int Offset)> sentences = SplitIntoSentences(trimmed, offset);
                foreach ((string sentence, int sentOffset) in sentences)
                {
                    string sentTrimmed = sentence.Trim();
                    if (string.IsNullOrEmpty(sentTrimmed)) continue;
                    int sentTokens = CountTokens(sentTrimmed);
                    int bufferedTokens = currentContent.Length > 0 ? CountTokens(currentContent.ToString()) : 0;

                    if (bufferedTokens + sentTokens > opts.MaxTokens && currentContent.Length > 0)
                    {
                        FlushChunk();
                        currentStart = sentOffset;
                        currentSection = section;
                    }

                    if (currentContent.Length > 0) currentContent.Append(' ');
                    else { currentStart = sentOffset; currentSection = section; }
                    currentContent.Append(sentTrimmed);
                }
            }
            else if (currentTokens + segTokens > opts.MaxTokens)
            {
                FlushChunk();
                currentStart = offset;
                currentSection = section;
                currentContent.Append(trimmed);
            }
            else
            {
                if (currentContent.Length == 0) { currentStart = offset; currentSection = section; }
                else currentContent.Append("\n\n");
                currentContent.Append(trimmed);

                if (currentTokens + segTokens >= opts.TargetTokens)
                    FlushChunk();
            }
        }
        FlushChunk();
    }

    private static List<(string Text, int Offset)> SplitIntoSentences(string text, int baseOffset)
    {
        var result = new List<(string, int)>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool isSentenceEnd = c == '.' || c == '!' || c == '?' || c == '\u3002' || c == '\uff01' || c == '\uff1f';
            if (isSentenceEnd && i + 1 < text.Length && (text[i + 1] == ' ' || text[i + 1] == '\n' || text[i + 1] == '\u3000'))
            {
                result.Add((text[start..(i + 1)], baseOffset + start));
                start = i + 1;
                while (start < text.Length && (text[start] == ' ' || text[start] == '\n')) start++;
                i = start - 1;
            }
        }
        if (start < text.Length)
            result.Add((text[start..], baseOffset + start));
        return result;
    }
}
