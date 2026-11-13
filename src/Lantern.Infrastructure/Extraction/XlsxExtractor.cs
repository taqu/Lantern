using System.Text;
using Lantern.Core.Extraction;
using Toxy;

namespace Lantern.Infrastructure.Extraction;

public sealed class XlsxExtractor : IDocumentExtractor
{
    public bool CanExtract(DocFormat format) =>
        format is DocFormat.Xlsx or DocFormat.Csv or DocFormat.Xls;

    public Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Extract(input), cancellationToken);
    }

    private static ExtractedDocument Extract(DocumentInput input)
    {
        string? filePath = input.FilePath;
        if(filePath == null)
            throw new ArgumentException("XlsxExtractor requires FilePath");

        ParserContext context = new ParserContext(filePath);
        ISpreadsheetParser parser = ParserFactory.CreateSpreadsheet(context);
        if(parser == null)
            throw new InvalidOperationException($"No Toxy spreadsheet parser for {input.Extension}");

        ToxySpreadsheet spreadsheet = parser.Parse();
        DocFormat earlyFormat = string.Equals(input.Extension, ".xls", StringComparison.OrdinalIgnoreCase)
            ? DocFormat.Xls : DocFormat.Xlsx;
        if(spreadsheet?.Tables == null)
            return EmptyDocument(input, earlyFormat);

        List<ExtractedBlock> blocks = new List<ExtractedBlock>();
        List<string> worksheetNames = new List<string>();

        foreach(ToxyTable? table in spreadsheet.Tables) {
            string sheetName = table.Name ?? "Sheet";
            worksheetNames.Add(sheetName);

            if(table.Rows == null || table.Rows.Count == 0) continue;

            // Build table block per sheet
            StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{sheetName}]");

            bool hasContent = false;
            foreach(ToxyRow? row in table.Rows) {
                if(row?.Cells == null) continue;
                List<string> cellValues = row.Cells
                    .Select(c => c?.Value?.Trim() ?? "")
                    .ToList();
                // Skip entirely empty rows
                if(cellValues.All(string.IsNullOrEmpty)) continue;
                sb.AppendLine(string.Join("\t", cellValues));
                hasContent = true;
            }

            if(hasContent) {
                string text = TextNormalizer.NormalizeAll(sb.ToString());
                blocks.Add(new ExtractedBlock(BlockType.Table, text, SheetName: sheetName));
            }
        }

        string title = input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Untitled";

        DocFormat format = string.Equals(input.Extension, ".xls", StringComparison.OrdinalIgnoreCase)
            ? DocFormat.Xls : DocFormat.Xlsx;

        return new ExtractedDocument {
            Title = title,
            Blocks = blocks,
            Metadata = new ExtractedDocumentMetadata(
                Title: title,
                Format: format,
                WorksheetNames: worksheetNames)
        };
    }

    private static ExtractedDocument EmptyDocument(DocumentInput input, DocFormat format) =>
        new() {
            Title = input.FileName != null ? Path.GetFileNameWithoutExtension(input.FileName) : "Untitled",
            Blocks = [],
            Metadata = new ExtractedDocumentMetadata(Format: format)
        };
}
