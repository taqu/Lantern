using Toxy;

namespace Lantern.Feasibility;

/// <summary>
/// Tests document text extraction using Toxy 2.6.0 against sample files.
///
/// Toxy factory methods and return types (verified via reflection against 2.6.0):
///   ParserFactory.CreateText(ctx)        -> ITextParser        -> string Parse()
///   ParserFactory.CreateDocument(ctx)    -> IDocumentParser    -> ToxyDocument Parse()
///                                           ToxyDocument.Paragraphs: List&lt;ToxyParagraph&gt;
///                                           ToxyParagraph.Text: string
///   ParserFactory.CreateSpreadsheet(ctx) -> ISpreadsheetParser -> ToxySpreadsheet Parse()
///                                           ToxySpreadsheet.Tables: List&lt;ToxyTable&gt;
///                                           ToxyTable.Rows: List&lt;ToxyRow&gt;
///                                           ToxyRow.Cells: List&lt;ToxyCell&gt;
///                                           ToxyCell.Value: string
///   ParserFactory.CreateSlideshow(ctx)   -> ISlideshowParser   -> ToxySlideshow Parse()
///                                           ToxySlideshow.Slides: List&lt;ToxySlide&gt;
///                                           ToxySlide.Texts: List&lt;string&gt;
/// </summary>
internal sealed class DocumentExtractionFeasibility
{
    public async Task RunAsync(string? filePath)
    {
        Console.WriteLine();
        Console.WriteLine("=== Document Extraction Feasibility ===");

        if (filePath != null)
        {
            await ExtractSingleFileAsync(filePath);
            return;
        }

        // Run against all sample files in samples/documents/
        string? samplesDir = FindSamplesDirectory();
        if (samplesDir == null)
        {
            Console.WriteLine("  samples/documents/ directory not found. Run from repo root or provide --extract <path>.");
            return;
        }

        var files = Directory.GetFiles(samplesDir)
            .Where(f => !f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine($"  No sample files found in: {samplesDir}");
            Console.WriteLine("  Add test files or use --extract <path>.");
            return;
        }

        Console.WriteLine($"  Sample directory: {samplesDir}");
        Console.WriteLine($"  Files found: {files.Count}");
        Console.WriteLine();

        foreach (string file in files)
        {
            await ExtractSingleFileAsync(file);
        }
    }

    private static async Task ExtractSingleFileAsync(string filePath)
    {
        string name = System.IO.Path.GetFileName(filePath);
        string ext = System.IO.Path.GetExtension(filePath).ToUpperInvariant();

        Console.WriteLine($"  File   : {name}");
        Console.WriteLine($"  Format : {ext}");

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"  Result : FAIL — file not found: {filePath}");
            Console.WriteLine();
            return;
        }

        try
        {
            var context = new ParserContext(filePath);
            string? text = null;
            string parserKind = "unknown";

            switch (ext)
            {
                case ".TXT":
                case ".HTML":
                case ".HTM":
                {
                        ITextParser parser = ParserFactory.CreateText(context);
                    if (parser != null)
                    {
                        text = parser.Parse();
                        parserKind = "text";
                    }
                    break;
                }
                case ".DOCX":
                case ".PDF":
                case ".RTF":
                case ".EPUB":
                {
                        IDocumentParser parser = ParserFactory.CreateDocument(context);
                    if (parser != null)
                    {
                            ToxyDocument doc = parser.Parse();
                        if (doc?.Paragraphs != null)
                        {
                            text = string.Join("\n", doc.Paragraphs.Select(p => p.Text ?? ""));
                        }
                        parserKind = "document";
                    }
                    break;
                }
                case ".XLSX":
                case ".XLS":
                case ".CSV":
                {
                        ISpreadsheetParser parser = ParserFactory.CreateSpreadsheet(context);
                    if (parser != null)
                    {
                            ToxySpreadsheet spreadsheet = parser.Parse();
                        if (spreadsheet?.Tables != null)
                        {
                            var sb = new System.Text.StringBuilder();
                            foreach (ToxyTable? table in spreadsheet.Tables)
                            {
                                sb.AppendLine($"[Sheet: {table.Name}]");
                                if (table.Rows != null)
                                {
                                    foreach (ToxyRow? row in table.Rows)
                                    {
                                        if (row?.Cells != null)
                                            sb.AppendLine(string.Join("\t", row.Cells.Select(c => c?.Value ?? "")));
                                    }
                                }
                            }
                            text = sb.ToString();
                        }
                        parserKind = "spreadsheet";
                    }
                    break;
                }
                case ".PPTX":
                case ".PPT":
                {
                        ISlideshowParser parser = ParserFactory.CreateSlideshow(context);
                    if (parser != null)
                    {
                            ToxySlideshow slideshow = parser.Parse();
                        if (slideshow?.Slides != null)
                        {
                            var sb = new System.Text.StringBuilder();
                            int slideNum = 1;
                            foreach (ToxySlide? slide in slideshow.Slides)
                            {
                                sb.AppendLine($"[Slide {slideNum++}]");
                                if (slide?.Texts != null)
                                {
                                    foreach (string t in slide.Texts)
                                        sb.AppendLine(t);
                                }
                            }
                            text = sb.ToString();
                        }
                        parserKind = "slideshow";
                    }
                    break;
                }
                default:
                {
                        // Try text parser as fallback
                        ITextParser parser = ParserFactory.CreateText(context);
                    if (parser != null)
                    {
                        text = parser.Parse();
                        parserKind = "text (fallback)";
                    }
                    break;
                }
            }

            if (text == null)
            {
                Console.WriteLine($"  Result : FAIL — no Toxy parser available for {ext}");
            }
            else
            {
                int chars = text.Length;
                bool hasContent = chars > 0;
                Console.WriteLine($"  Parser : {parserKind}");
                Console.WriteLine($"  Result : {(hasContent ? "PASS" : "FAIL — empty output")}");
                Console.WriteLine($"  Chars  : {chars}");

                if (hasContent)
                {
                    string preview = text.Length > 300 ? text[..300] + " [...]" : text;
                    Console.WriteLine($"  Preview: {EscapeControlChars(preview)}");
                }
            }
        }
        catch (NotSupportedException ex)
        {
            Console.WriteLine($"  Result : FAIL — format not supported by Toxy: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Result : FAIL — {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine();
        await Task.CompletedTask;
    }

    private static string? FindSamplesDirectory()
    {
        string? dir = Directory.GetCurrentDirectory();
        for (int depth = 0; depth < 6; depth++)
        {
            if (dir == null) break;
            string candidate = System.IO.Path.Combine(dir, "samples", "documents");
            if (Directory.Exists(candidate)) return candidate;
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string EscapeControlChars(string s) =>
        s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
}
