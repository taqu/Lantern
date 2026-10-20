using Lantern.Core.Extraction;
using Toxy;

namespace Lantern.Infrastructure.Extraction;

public sealed class PptxExtractor : IDocumentExtractor
{
    public bool CanExtract(DocFormat format) => format == DocFormat.Pptx;

    public Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Extract(input), cancellationToken);
    }

    private static ExtractedDocument Extract(DocumentInput input)
    {
        string? filePath = input.FilePath;
        if (filePath == null)
            throw new ArgumentException("PptxExtractor requires FilePath");

        var context = new ParserContext(filePath);
        var parser = ParserFactory.CreateSlideshow(context);
        if (parser == null)
            throw new InvalidOperationException($"No Toxy slideshow parser for {input.Extension}");

        var slideshow = parser.Parse();
        if (slideshow?.Slides == null)
            return EmptyDocument(input);

        var blocks = new List<ExtractedBlock>();
        int slideCount = slideshow.Slides.Count;

        for (int i = 0; i < slideshow.Slides.Count; i++)
        {
            var slide = slideshow.Slides[i];
            int slideNumber = i + 1;
            if (slide?.Texts == null || slide.Texts.Count == 0) continue;

            // First text item is likely the slide title
            string? slideTitle = slide.Texts.Count > 0 ? slide.Texts[0]?.Trim() : null;
            if (string.IsNullOrWhiteSpace(slideTitle)) slideTitle = null;

            // Create one block per slide combining all text
            var sb = new System.Text.StringBuilder();
            for (int j = 0; j < slide.Texts.Count; j++)
            {
                string? text = slide.Texts[j]?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    if (j > 0) sb.AppendLine();
                    sb.Append(text);
                }
            }

            string slideText = TextNormalizer.NormalizeAll(sb.ToString());
            if (!string.IsNullOrWhiteSpace(slideText))
            {
                // Heading block for slide title if available
                if (!string.IsNullOrEmpty(slideTitle))
                    blocks.Add(new ExtractedBlock(BlockType.Heading, slideTitle, SlideNumber: slideNumber, HeadingLevel: 2, SectionTitle: slideTitle));

                blocks.Add(new ExtractedBlock(BlockType.Paragraph, slideText, SlideNumber: slideNumber, SectionTitle: slideTitle));
            }
        }

        string title = input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Presentation";

        return new ExtractedDocument
        {
            Title = title,
            Blocks = blocks,
            Metadata = new ExtractedDocumentMetadata(
                Title: title,
                Format: DocFormat.Pptx,
                SlideCount: slideCount)
        };
    }

    private static ExtractedDocument EmptyDocument(DocumentInput input) =>
        new()
        {
            Title = input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Presentation",
            Blocks = [],
            Metadata = new ExtractedDocumentMetadata(Format: DocFormat.Pptx)
        };
}
