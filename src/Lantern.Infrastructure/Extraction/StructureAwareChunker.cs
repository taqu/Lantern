using Lantern.Core.Extraction;
using Lantern.Core.Search;
using Microsoft.Extensions.Options;
using System.Text;

namespace Lantern.Infrastructure.Extraction;

public sealed class StructureAwareChunker
{
    private readonly ITextChunkingService _textChunker;
    private readonly ChunkingOptions _defaultOptions;

    public StructureAwareChunker(ITextChunkingService textChunker, IOptions<ChunkingOptions> options)
    {
        _textChunker = textChunker;
        _defaultOptions = options.Value;
    }

    public IReadOnlyList<SearchChunk> Chunk(
        string documentId,
        string title,
        string uri,
        string? extension,
        string? sourceId,
        string? sourceType,
        IReadOnlyList<ExtractedBlock> blocks,
        ChunkingOptions? options = null)
    {
        ChunkingOptions opts = options ?? _defaultOptions;
        if (blocks.Count == 0) return [];

        var chunks = new List<SearchChunk>();
        int chunkIndex = 0;

        // Group blocks so page/slide/sheet boundaries prevent merging
        List<List<ExtractedBlock>> groups = GroupBlocks(blocks);

        foreach (List<ExtractedBlock> group in groups)
        {
            List<SearchChunk> groupChunks = ChunkGroup(
                documentId, title, uri, extension, sourceId, sourceType,
                group, opts, ref chunkIndex);
            chunks.AddRange(groupChunks);
        }

        return chunks;
    }

    // Group blocks so that page/slide/sheet boundaries prevent merging
    private static List<List<ExtractedBlock>> GroupBlocks(IReadOnlyList<ExtractedBlock> blocks)
    {
        var groups = new List<List<ExtractedBlock>>();
        var current = new List<ExtractedBlock>();

        for (int i = 0; i < blocks.Count; i++)
        {
            ExtractedBlock block = blocks[i];
            if (current.Count > 0)
            {
                ExtractedBlock prev = current[^1];
                // Start new group on page/slide/sheet boundary change
                bool newPage = block.PageNumber.HasValue && prev.PageNumber.HasValue && block.PageNumber != prev.PageNumber;
                bool newSlide = block.SlideNumber.HasValue && prev.SlideNumber.HasValue && block.SlideNumber != prev.SlideNumber;
                bool newSheet = block.SheetName != null && prev.SheetName != null && block.SheetName != prev.SheetName;

                if (newPage || newSlide || newSheet)
                {
                    groups.Add(current);
                    current = [];
                }
            }
            current.Add(block);
        }
        if (current.Count > 0) groups.Add(current);
        return groups;
    }

    private List<SearchChunk> ChunkGroup(
        string documentId, string title, string uri, string? extension,
        string? sourceId, string? sourceType,
        List<ExtractedBlock> group, ChunkingOptions opts, ref int chunkIndex)
    {
        var chunks = new List<SearchChunk>();

        // Extract group metadata from first block
        int? pageNumber = group.FirstOrDefault()?.PageNumber;
        int? slideNumber = group.FirstOrDefault()?.SlideNumber;
        string? sheetName = group.FirstOrDefault()?.SheetName;
        string? currentSection = null;

        var buffer = new StringBuilder();
        string? bufferSection = null;
        int bufferStart = 0;
        int? bufferStartLine = null;
        int? bufferEndLine = null;

        // Use a local (non-ref) counter so local function can capture it
        int localChunkIndex = chunkIndex;

        void FlushBuffer()
        {
            string content = buffer.ToString().Trim();
            if (string.IsNullOrEmpty(content)) return;

            string chunkId = ChunkIdentity.Generate(documentId, localChunkIndex);
            chunks.Add(new SearchChunk
            {
                Id = chunkId,
                DocumentId = documentId,
                SourceId = sourceId ?? "",
                SourceType = sourceType ?? "file",
                Title = title,
                Content = content,
                ChunkIndex = localChunkIndex++,
                SectionTitle = bufferSection,
                PageNumber = pageNumber,
                SlideNumber = slideNumber,
                SheetName = sheetName,
                Uri = uri,
                Extension = extension,
                StartLine = bufferStartLine,
                EndLine = bufferEndLine
            });
            buffer.Clear();
            bufferSection = null;
            bufferStartLine = null;
            bufferEndLine = null;
        }

        foreach (ExtractedBlock block in group)
        {
            if (block.Type == BlockType.Heading)
            {
                currentSection = block.Text;
                // Headings are short - include inline rather than separate chunks
                if (buffer.Length == 0)
                {
                    buffer.Append(block.Text);
                    bufferSection = currentSection;
                    bufferStart = block.StartOffset ?? 0;
                }
                else
                {
                    buffer.AppendLine();
                    buffer.AppendLine(block.Text);
                }
                continue;
            }

            string blockText = block.Text;
            if (string.IsNullOrWhiteSpace(blockText)) continue;

            // Update section from block if it has one
            if (block.SectionTitle != null) currentSection = block.SectionTitle;
            // Update location metadata from block
            if (block.PageNumber.HasValue) pageNumber = block.PageNumber;
            if (block.SlideNumber.HasValue) slideNumber = block.SlideNumber;
            if (block.SheetName != null) sheetName = block.SheetName;
            // Track line range across buffer
            if (block.StartLine.HasValue && bufferStartLine == null) bufferStartLine = block.StartLine;
            if (block.EndLine.HasValue) bufferEndLine = block.EndLine;

            int blockTokens = _textChunker.CountTokens(blockText);

            if (blockTokens > opts.MaxTokens)
            {
                // Flush current buffer first
                FlushBuffer();
                // Split oversized block using text chunker
                IReadOnlyList<TextChunk> subChunks = _textChunker.Chunk(blockText, opts);
                foreach (TextChunk sc in subChunks)
                {
                    string subId = ChunkIdentity.Generate(documentId, localChunkIndex);
                    chunks.Add(new SearchChunk
                    {
                        Id = subId,
                        DocumentId = documentId,
                        SourceId = sourceId ?? "",
                        SourceType = sourceType ?? "file",
                        Title = title,
                        Content = sc.Content,
                        ChunkIndex = localChunkIndex++,
                        SectionTitle = currentSection ?? sc.SectionTitle,
                        PageNumber = pageNumber,
                        SlideNumber = slideNumber,
                        SheetName = sheetName,
                        Uri = uri,
                        Extension = extension,
                        StartOffset = sc.StartOffset,
                        EndOffset = sc.EndOffset
                    });
                }
                continue;
            }

            int currentTokens = buffer.Length > 0 ? _textChunker.CountTokens(buffer.ToString()) : 0;
            if (currentTokens + blockTokens > opts.MaxTokens && buffer.Length > 0)
            {
                FlushBuffer();
                bufferSection = currentSection;
                bufferStart = block.StartOffset ?? 0;
                bufferStartLine = block.StartLine;
                bufferEndLine = block.EndLine;
            }

            if (buffer.Length == 0)
            {
                bufferSection = currentSection;
                bufferStart = block.StartOffset ?? 0;
                bufferStartLine = block.StartLine;
                bufferEndLine = block.EndLine;
            }
            else
            {
                buffer.AppendLine();
                buffer.AppendLine();
            }
            buffer.Append(blockText);

            int totalTokens = _textChunker.CountTokens(buffer.ToString());
            if (totalTokens >= opts.TargetTokens)
                FlushBuffer();
        }

        FlushBuffer();
        // Write back the updated index to the ref parameter
        chunkIndex = localChunkIndex;
        return chunks;
    }
}
