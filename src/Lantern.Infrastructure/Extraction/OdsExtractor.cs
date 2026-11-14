using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Lantern.Core.Extraction;

namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// Extracts worksheet content from <c>.ods</c> and <c>.ots</c> OpenDocument
/// Spreadsheet packages. Produces one table block per non-empty worksheet,
/// enforcing bounded iteration over the ODF repeated-row and repeated-column
/// attributes (a 1 KB file can declare 2^31 logical cells).
/// </summary>
public sealed class OdsExtractor : IDocumentExtractor
{
    // Per-sheet caps that protect against repeated-cell abuse. These are logical
    // caps — a sheet that legitimately contains more than this many non-empty
    // cells will be truncated at the limit, with no exception thrown.
    private const int MaxRowsPerSheet = 50_000;
    private const int MaxColumnsPerRow = 1_024;
    private const int MaxNonEmptyCellsPerSheet = 200_000;
    private const int MaxTextBytesPerSheet = 4 * 1024 * 1024;

    public bool CanExtract(DocFormat format) => format == DocFormat.Ods;

    public Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Extract(input, cancellationToken), cancellationToken);
    }

    private static ExtractedDocument Extract(DocumentInput input, CancellationToken cancellationToken)
    {
        using OdfPackageReader package = OdfPackageReader.Open(input);
        if(package.Kind != OdfDocumentKind.Spreadsheet)
            throw new NotSupportedException(
                $"Package is not an OpenDocument Spreadsheet (kind={package.Kind})");

        XDocument? content = package.LoadXml("content.xml", cancellationToken);
        OdfPackageMetadata meta = OdfPackageMetadata.Read(package, cancellationToken);

        List<ExtractedBlock> blocks = new List<ExtractedBlock>();
        List<string> worksheetNames = new List<string>();

        if(content?.Root is not null) {
            XElement? body = content.Root.Element(OdfNamespaces.OfficeNs + "body");
            XElement? spreadsheet = body?.Element(OdfNamespaces.OfficeNs + "spreadsheet");
            if(spreadsheet is not null) {
                foreach(XElement table in spreadsheet.Elements(OdfNamespaces.TableNs + "table")) {
                    cancellationToken.ThrowIfCancellationRequested();
                    string sheetName = table.Attribute(OdfNamespaces.TableNs + "name")?.Value ?? "Sheet";
                    worksheetNames.Add(sheetName);
                    string sheetText = BuildSheetText(sheetName, table, cancellationToken);
                    if(!string.IsNullOrWhiteSpace(sheetText))
                        blocks.Add(new ExtractedBlock(BlockType.Table,
                            TextNormalizer.NormalizeAll(sheetText),
                            SheetName: sheetName));
                }
            }
        }

        string title = !string.IsNullOrEmpty(meta.Title)
            ? meta.Title!
            : (input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Untitled");

        return new ExtractedDocument {
            Title = title,
            Blocks = blocks,
            Metadata = new ExtractedDocumentMetadata(
                Title: title,
                Author: meta.Author,
                CreatedAt: meta.CreatedAt,
                ModifiedAt: meta.ModifiedAt,
                Format: DocFormat.Ods,
                WorksheetNames: worksheetNames)
        };
    }

    private static string BuildSheetText(string sheetName, XElement table, CancellationToken cancellationToken)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append('[').Append(sheetName).Append(']').Append('\n');

        int logicalRow = 0;
        int rowsEmitted = 0;
        int cellsEmitted = 0;
        bool truncated = false;

        foreach(XElement row in table.Elements(OdfNamespaces.TableNs + "table-row")) {
            if(truncated) break;
            cancellationToken.ThrowIfCancellationRequested();

            int rowRepeat = ParseRepeat(row, "number-rows-repeated");
            List<string> cellValues = ReadRowCells(row);
            // Trim trailing empty cells so we do not emit padding.
            int lastNonEmpty = -1;
            for(int i = 0; i < cellValues.Count; i++)
                if(cellValues[i].Length > 0) lastNonEmpty = i;

            // Advance logical row position. If the entire row is empty, skip emission
            // but preserve the row index so cell coordinates stay accurate.
            if(lastNonEmpty < 0) {
                logicalRow += rowRepeat;
                continue;
            }

            // Emit the first copy only; repeated non-empty rows are rare in
            // practice and collapsing them avoids duplicate indexing.
            int startRow = logicalRow + 1; // 1-based
            StringBuilder lineBuilder = new StringBuilder();
            lineBuilder.Append("R").Append(startRow).Append(":\t");
            for(int i = 0; i <= lastNonEmpty; i++) {
                if(i > 0) lineBuilder.Append('\t');
                lineBuilder.Append(cellValues[i]);
                cellsEmitted++;
                if(cellsEmitted >= MaxNonEmptyCellsPerSheet) { truncated = true; break; }
            }
            lineBuilder.Append('\n');
            sb.Append(lineBuilder);
            rowsEmitted++;
            logicalRow += rowRepeat;

            if(rowsEmitted >= MaxRowsPerSheet) truncated = true;
            if(sb.Length >= MaxTextBytesPerSheet) truncated = true;
        }

        if(truncated) sb.Append("… (truncated)\n");
        return sb.ToString();
    }

    private static List<string> ReadRowCells(XElement row)
    {
        List<string> cells = new List<string>();
        int columnCount = 0;
        foreach(XElement cell in row.Elements()) {
            if(cell.Name != OdfNamespaces.TableNs + "table-cell" &&
                cell.Name != OdfNamespaces.TableNs + "covered-table-cell")
                continue;
            int repeat = ParseRepeat(cell, "number-columns-repeated");
            string text = ReadCellText(cell);
            // Bound per-row width even in the face of 2^31 repeated-column metadata.
            int toAdd = Math.Min(repeat, MaxColumnsPerRow - columnCount);
            if(toAdd <= 0) break;
            for(int i = 0; i < toAdd; i++) cells.Add(text);
            columnCount += toAdd;
            if(columnCount >= MaxColumnsPerRow) break;
        }
        return cells;
    }

    private static string ReadCellText(XElement cell)
    {
        // Prefer the human-readable display text (concatenated paragraphs).
        StringBuilder sb = new StringBuilder();
        foreach(XElement p in cell.Elements(OdfNamespaces.TextNs + "p")) {
            string text = ReadParagraphText(p).Trim();
            if(text.Length == 0) continue;
            if(sb.Length > 0) sb.Append(' ');
            sb.Append(text);
        }
        if(sb.Length > 0) return sb.ToString();

        // Fall back to the typed value attribute for numeric / date / boolean cells
        // that may lack a <text:p> payload.
        string? valueType = cell.Attribute(OdfNamespaces.OfficeNs + "value-type")?.Value;
        switch(valueType) {
            case "float":
            case "percentage":
            case "currency":
                return cell.Attribute(OdfNamespaces.OfficeNs + "value")?.Value ?? "";
            case "boolean":
                return cell.Attribute(OdfNamespaces.OfficeNs + "boolean-value")?.Value ?? "";
            case "date":
                return cell.Attribute(OdfNamespaces.OfficeNs + "date-value")?.Value ?? "";
            case "time":
                return cell.Attribute(OdfNamespaces.OfficeNs + "time-value")?.Value ?? "";
            case "string":
                return cell.Attribute(OdfNamespaces.OfficeNs + "string-value")?.Value ?? "";
            default:
                return "";
        }
    }

    private static string ReadParagraphText(XElement p)
    {
        StringBuilder sb = new StringBuilder();
        foreach(XNode node in p.Nodes()) {
            if(node is XText t) sb.Append(t.Value);
            else if(node is XElement e) AppendCellInline(e, sb);
        }
        return sb.ToString();
    }

    private static void AppendCellInline(XElement element, StringBuilder sb)
    {
        if(element.Name.Namespace == OdfNamespaces.TextNs) {
            switch(element.Name.LocalName) {
                case "s": {
                    int count = 1;
                    XAttribute? c = element.Attribute(OdfNamespaces.TextNs + "c");
                    if(c != null && int.TryParse(c.Value, out int parsed) && parsed > 0)
                        count = Math.Min(parsed, 128);
                    sb.Append(' ', count);
                    return;
                }
                case "tab":
                    sb.Append(' ');
                    return;
                case "line-break":
                    sb.Append(' ');
                    return;
            }
        }
        foreach(XNode node in element.Nodes()) {
            if(node is XText t) sb.Append(t.Value);
            else if(node is XElement child) AppendCellInline(child, sb);
        }
    }

    private static int ParseRepeat(XElement element, string localName)
    {
        XAttribute? attr = element.Attribute(OdfNamespaces.TableNs + localName);
        if(attr == null) return 1;
        if(!int.TryParse(attr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            return 1;
        return value <= 0 ? 1 : value;
    }
}
