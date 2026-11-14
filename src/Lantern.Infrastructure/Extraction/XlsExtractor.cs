using Lantern.Core.Extraction;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;

namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// Extracts worksheets, rows and cells from legacy Excel 97–2003 (.xls)
/// workbooks (BIFF8 CFBF). Uses NPOI's HSSF implementation — a pure managed
/// .NET port of Apache POI — so no Microsoft Office or external process is
/// required. Bounded by resource limits so a hostile or oversized workbook
/// cannot exhaust memory.
/// </summary>
public sealed class XlsExtractor : IDocumentExtractor
{
    private const int MaxWorksheets = 256;
    private const int MaxRowsPerSheet = 50_000;
    private const int MaxCellsPerRow = 1_024;
    private const int MaxCellTextLength = 4_096;

    public bool CanExtract(DocFormat format) => format == DocFormat.Xls;

    public Task<ExtractedDocument> ExtractAsync(DocumentInput input, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Extract(input, cancellationToken), cancellationToken);
    }

    private static ExtractedDocument Extract(DocumentInput input, CancellationToken cancellationToken)
    {
        if(input.FilePath == null && input.Stream == null)
            throw new ArgumentException("XlsExtractor requires FilePath or Stream");

        HSSFWorkbook workbook;
        try {
            if(input.FilePath != null) {
                using FileStream fs = new FileStream(input.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                workbook = new HSSFWorkbook(fs);
            } else {
                workbook = new HSSFWorkbook(input.Stream!);
            }
        } catch(NPOI.HSSF.OldExcelFormatException ex) {
            throw new InvalidOperationException("Unsupported pre-BIFF8 Excel format: " + ex.Message, ex);
        } catch(Exception ex) when(ex is not OperationCanceledException) {
            // Detect password-protected workbook from RecordInputStream / Biff8EncryptionKey path.
            if(ex.Message != null && ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("DOC_ENCRYPTED: " + ex.Message, ex);
            throw new InvalidOperationException("Failed to open .xls workbook: " + ex.Message, ex);
        }

        try {
            return BuildDocument(workbook, input, cancellationToken);
        } finally {
            workbook.Close();
        }
    }

    private static ExtractedDocument BuildDocument(HSSFWorkbook workbook, DocumentInput input, CancellationToken cancellationToken)
    {
        List<ExtractedBlock> blocks = new List<ExtractedBlock>();
        List<string> worksheetNames = new List<string>();

        int sheetCount = Math.Min(workbook.NumberOfSheets, MaxWorksheets);
        IFormulaEvaluator evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
        DataFormatter formatter = new DataFormatter();

        for(int s = 0; s < sheetCount; s++) {
            cancellationToken.ThrowIfCancellationRequested();

            ISheet sheet = workbook.GetSheetAt(s);
            string sheetName = sheet.SheetName ?? $"Sheet{s + 1}";
            worksheetNames.Add(sheetName);

            int lastRowNum = Math.Min(sheet.LastRowNum, MaxRowsPerSheet);
            for(int r = sheet.FirstRowNum; r <= lastRowNum; r++) {
                IRow? row = sheet.GetRow(r);
                if(row == null) continue;

                List<string> cellTexts = new List<string>();
                int lastCol = Math.Min((int)row.LastCellNum, MaxCellsPerRow);
                for(int c = row.FirstCellNum; c < lastCol; c++) {
                    if(c < 0) continue;
                    ICell? cell = row.GetCell(c);
                    if(cell == null) continue;

                    string cellText = FormatCell(cell, evaluator, formatter);
                    if(string.IsNullOrEmpty(cellText)) continue;

                    if(cellText.Length > MaxCellTextLength)
                        cellText = cellText.Substring(0, MaxCellTextLength);

                    cellTexts.Add(cellText);
                }

                if(cellTexts.Count == 0) continue;

                string rowText = TextNormalizer.NormalizeAll(string.Join("\t", cellTexts));
                if(string.IsNullOrWhiteSpace(rowText)) continue;

                blocks.Add(new ExtractedBlock(
                    Type: BlockType.Table,
                    Text: rowText,
                    SheetName: sheetName,
                    SectionTitle: sheetName,
                    StartLine: r + 1,
                    EndLine: r + 1));
            }
        }

        string title = input.FileName != null
            ? Path.GetFileNameWithoutExtension(input.FileName)
            : (input.FilePath != null ? Path.GetFileNameWithoutExtension(input.FilePath) : "Untitled");

        return new ExtractedDocument {
            Title = title,
            Blocks = blocks,
            Metadata = new ExtractedDocumentMetadata(
                Title: title,
                Format: DocFormat.Xls,
                WorksheetNames: worksheetNames)
        };
    }

    private static string FormatCell(ICell cell, IFormulaEvaluator evaluator, DataFormatter formatter)
    {
        try {
            return cell.CellType switch {
                CellType.Blank => string.Empty,
                CellType.Boolean => cell.BooleanCellValue ? "TRUE" : "FALSE",
                CellType.Error => string.Empty,
                CellType.Formula => FormatFormulaCell(cell, evaluator, formatter),
                CellType.Numeric => DateUtil.IsCellDateFormatted(cell)
                    ? cell.DateCellValue?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty
                    : formatter.FormatCellValue(cell),
                CellType.String => cell.StringCellValue ?? string.Empty,
                _ => formatter.FormatCellValue(cell)
            };
        } catch {
            // Corrupt or unsupported cell value — skip rather than fail the sheet.
            return string.Empty;
        }
    }

    private static string FormatFormulaCell(ICell cell, IFormulaEvaluator evaluator, DataFormatter formatter)
    {
        // Prefer cached results so we do not execute arbitrary formulas, but
        // fall back to the evaluator if there is no cached value. The cached
        // CellType lives in CachedFormulaResultType.
        try {
            return cell.CachedFormulaResultType switch {
                CellType.String => cell.StringCellValue ?? string.Empty,
                CellType.Boolean => cell.BooleanCellValue ? "TRUE" : "FALSE",
                CellType.Numeric => DateUtil.IsCellDateFormatted(cell)
                    ? cell.DateCellValue?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty
                    : formatter.FormatCellValue(cell),
                CellType.Error => string.Empty,
                _ => formatter.FormatCellValue(cell, evaluator)
            };
        } catch {
            return string.Empty;
        }
    }
}
