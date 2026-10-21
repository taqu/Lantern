using System.Net;
using System.Text;
using HtmlAgilityPack;
using Lantern.Core.Extraction;

namespace Lantern.Infrastructure.Extraction;

public sealed class HtmlExtractor : IDocumentExtractor
{
    public bool CanExtract(DocFormat format) => format == DocFormat.Html;

    public async Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        string html = await ReadHtmlAsync(input, cancellationToken);

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(html);

        // Remove script, style, nav, footer, header elements
        RemoveNonContentNodes(htmlDoc);

        string title = ExtractTitle(htmlDoc, input);
        IReadOnlyList<ExtractedBlock> blocks = ExtractBlocks(htmlDoc);

        return new ExtractedDocument
        {
            Title = title,
            Blocks = blocks,
            Metadata = new ExtractedDocumentMetadata(Title: title, Format: DocFormat.Html)
        };
    }

    private static async Task<string> ReadHtmlAsync(DocumentInput input, CancellationToken ct)
    {
        if (input.FilePath != null)
        {
            byte[] bytes = await File.ReadAllBytesAsync(input.FilePath, ct);
            return DecodeHtml(bytes);
        }
        if (input.Stream != null)
        {
            using var ms = new MemoryStream();
            await input.Stream.CopyToAsync(ms, ct);
            return DecodeHtml(ms.ToArray());
        }
        return string.Empty;
    }

    private static string DecodeHtml(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return Encoding.Default.GetString(bytes); }
    }

    private static void RemoveNonContentNodes(HtmlDocument htmlDoc)
    {
        string[] removeXPaths = new[]
        {
            "//script", "//style", "//nav", "//footer", "//header",
            "//noscript", "//iframe", "//object", "//embed",
            "//comment()"
        };
        foreach (string? xpath in removeXPaths)
        {
            HtmlNodeCollection? nodes = htmlDoc.DocumentNode.SelectNodes(xpath);
            if (nodes != null)
                foreach (HtmlNode? node in nodes.ToList())
                    node.Remove();
        }
    }

    private static string ExtractTitle(HtmlDocument htmlDoc, DocumentInput input)
    {
        HtmlNode? titleNode = htmlDoc.DocumentNode.SelectSingleNode("//title");
        if (titleNode != null)
        {
            string t = WebUtility.HtmlDecode(titleNode.InnerText).Trim();
            if (!string.IsNullOrEmpty(t)) return t;
        }
        HtmlNode? h1 = htmlDoc.DocumentNode.SelectSingleNode("//h1");
        if (h1 != null)
        {
            string t = WebUtility.HtmlDecode(h1.InnerText).Trim();
            if (!string.IsNullOrEmpty(t)) return t;
        }
        return input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Untitled";
    }

    private static IReadOnlyList<ExtractedBlock> ExtractBlocks(HtmlDocument htmlDoc)
    {
        var blocks = new List<ExtractedBlock>();
        string? currentSection = null;

        HtmlNode body = htmlDoc.DocumentNode.SelectSingleNode("//body") ?? htmlDoc.DocumentNode;
        WalkNode(body, blocks, ref currentSection);
        return blocks;
    }

    private static void WalkNode(HtmlNode node, List<ExtractedBlock> blocks, ref string? currentSection)
    {
        foreach (HtmlNode? child in node.ChildNodes)
        {
            string name = child.Name.ToLowerInvariant();
            switch (name)
            {
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    {
                        int level = name[1] - '0';
                        string text = WebUtility.HtmlDecode(child.InnerText).Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            if (level <= 2) currentSection = text;
                            blocks.Add(new ExtractedBlock(BlockType.Heading, text, SectionTitle: currentSection, HeadingLevel: level));
                        }
                        break;
                    }
                case "p":
                    {
                        string text = WebUtility.HtmlDecode(child.InnerText).Trim();
                        text = TextNormalizer.Normalize(text);
                        if (!string.IsNullOrWhiteSpace(text))
                            blocks.Add(new ExtractedBlock(BlockType.Paragraph, text, SectionTitle: currentSection));
                        break;
                    }
                case "ul":
                case "ol":
                    {
                        var sb = new StringBuilder();
                        foreach (HtmlNode? li in child.ChildNodes.Where(n => n.Name == "li"))
                        {
                            string liText = WebUtility.HtmlDecode(li.InnerText).Trim();
                            if (!string.IsNullOrEmpty(liText))
                                sb.AppendLine($"- {liText}");
                        }
                        string listText = sb.ToString().Trim();
                        if (!string.IsNullOrEmpty(listText))
                            blocks.Add(new ExtractedBlock(BlockType.List, listText, SectionTitle: currentSection));
                        break;
                    }
                case "table":
                    {
                        string tableText = ExtractTableText(child);
                        if (!string.IsNullOrEmpty(tableText))
                            blocks.Add(new ExtractedBlock(BlockType.Table, tableText, SectionTitle: currentSection));
                        break;
                    }
                case "div":
                case "section":
                case "article":
                case "main":
                    {
                        WalkNode(child, blocks, ref currentSection);
                        break;
                    }
                case "br":
                    break;
                default:
                    if (child.NodeType == HtmlNodeType.Text)
                    {
                        string text = WebUtility.HtmlDecode(child.InnerText).Trim();
                        if (!string.IsNullOrWhiteSpace(text) && text.Length > 10)
                            blocks.Add(new ExtractedBlock(BlockType.PlainText, text, SectionTitle: currentSection));
                    }
                    else if (child.HasChildNodes)
                        WalkNode(child, blocks, ref currentSection);
                    break;
            }
        }
    }

    private static string ExtractTableText(HtmlNode tableNode)
    {
        var sb = new StringBuilder();
        foreach (HtmlNode? row in tableNode.Descendants("tr"))
        {
            IEnumerable<string> cells = row.ChildNodes
                .Where(n => n.Name == "td" || n.Name == "th")
                .Select(n => WebUtility.HtmlDecode(n.InnerText).Trim());
            sb.AppendLine(string.Join(" | ", cells));
        }
        return sb.ToString().Trim();
    }
}
