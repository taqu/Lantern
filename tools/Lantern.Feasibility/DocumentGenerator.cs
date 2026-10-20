using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using IOPath = System.IO.Path;

namespace Lantern.Feasibility;

/// <summary>
/// Generates sample Office Open XML documents (DOCX, XLSX, PPTX) in samples/documents/.
/// These are used as test fixtures for document extraction feasibility testing.
/// </summary>
internal static class DocumentGenerator
{
    public static void GenerateSamples(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        Console.WriteLine();
        Console.WriteLine("=== Generating Sample Documents ===");
        Console.WriteLine($"  Output: {outputDir}");
        Console.WriteLine();

        GenerateDocx(IOPath.Combine(outputDir, "sample.docx"));
        GenerateXlsx(IOPath.Combine(outputDir, "sample.xlsx"));
        GeneratePptx(IOPath.Combine(outputDir, "sample.pptx"));

        Console.WriteLine();
        Console.WriteLine("  Done. Note: PDF samples require manual creation (see samples/documents/README.md).");
    }

    private static void GenerateDocx(string path)
    {
        Console.Write($"  DOCX: {IOPath.GetFileName(path)} ... ");
        try
        {
            using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            MainDocumentPart mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(
                new DocumentFormat.OpenXml.Wordprocessing.Body(
                    // English paragraphs
                    MakeParagraph("Lantern Sample Document"),
                    MakeParagraph("This is a test document for Lantern feasibility evaluation."),
                    MakeParagraph("Lantern is a lightweight full-text search platform for Windows."),
                    // Japanese paragraphs
                    MakeParagraph("ランタン（Lantern）は、Windowsで動作する全文検索プラットフォームです。"),
                    MakeParagraph("このドキュメントは、文書抽出の互換性テスト用サンプルです。"),
                    // Table
                    MakeTable(new[]
                    {
                        new[] { "Format", "Library",    "Status" },
                        new[] { "DOCX",   "Toxy",       "Testing" },
                        new[] { "XLSX",   "Toxy",       "Testing" },
                        new[] { "PPTX",   "Toxy",       "Testing" },
                        new[] { "PDF",    "Toxy/PdfPig","Testing" },
                    })
                )
            );
            Console.WriteLine("PASS");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL — {ex.Message}");
        }
    }

    private static void GenerateXlsx(string path)
    {
        Console.Write($"  XLSX: {IOPath.GetFileName(path)} ... ");
        try
        {
            using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
            WorkbookPart workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            // Header row
            sheetData.Append(MakeRow(1, "ID", "Title", "Description", "Language"));
            // Data rows
            sheetData.Append(MakeRow(2, "1", "Lantern Introduction", "Lightweight full-text search platform", "English"));
            sheetData.Append(MakeRow(3, "2", "Lanternの紹介", "軽量な全文検索プラットフォーム", "Japanese"));
            sheetData.Append(MakeRow(4, "3", "Document Extraction", "PDF and Office document text extraction", "English"));
            sheetData.Append(MakeRow(5, "4", "文書抽出", "PDFおよびOffice文書のテキスト抽出", "Japanese"));

            worksheetPart.Worksheet = new Worksheet(sheetData);

            Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "TestData"
            });

            workbookPart.Workbook.Save();
            Console.WriteLine("PASS");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL — {ex.Message}");
        }
    }

    private static void GeneratePptx(string path)
    {
        Console.Write($"  PPTX: {IOPath.GetFileName(path)} ... ");
        try
        {
            using var prs = PresentationDocument.Create(path, PresentationDocumentType.Presentation);

            PresentationPart presentationPart = prs.AddPresentationPart();
            presentationPart.Presentation = new P.Presentation();

            SlideLayoutPart slideLayoutPart = CreateSlideLayoutPart(presentationPart);

            (string, string)[] slideContents = new[]
            {
                ("Lantern Feasibility Test", "This presentation tests PPTX extraction with Toxy."),
                ("English Content", "Lantern is a lightweight full-text search platform for Windows."),
                ("Japanese Content\n日本語コンテンツ", "Lanternは、Windowsで動作する全文検索プラットフォームです。\nこのスライドは日本語テキストの抽出テスト用です。"),
                ("Summary", "DOCX, XLSX, and PPTX support is provided by Toxy via DocumentFormat.OpenXml."),
            };

            var slideIds = new SlideIdList();
            uint slideIdBase = 256;

            foreach ((string title, string body) in slideContents)
            {
                SlidePart slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = BuildSlide(title, body);
                slidePart.AddPart(slideLayoutPart);

                string slideRelId = presentationPart.GetIdOfPart(slidePart);
                slideIds.Append(new SlideId { Id = slideIdBase++, RelationshipId = slideRelId });
            }

            presentationPart.Presentation.AppendChild(
                new P.SlideMasterIdList(new P.SlideMasterId { Id = 2147483648U, RelationshipId = "rId1" }));
            presentationPart.Presentation.AppendChild(slideIds);
            presentationPart.Presentation.AppendChild(new P.SlideSize { Cx = 9144000, Cy = 5143500 });
            presentationPart.Presentation.AppendChild(new P.NotesSize { Cx = 6858000, Cy = 9144000 });

            presentationPart.Presentation.Save();
            Console.WriteLine("PASS");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL — {ex.Message}");
        }
    }

    // --- Helpers for DOCX ---

    private static DocumentFormat.OpenXml.Wordprocessing.Paragraph MakeParagraph(string text) =>
        new(new DocumentFormat.OpenXml.Wordprocessing.Run(
            new DocumentFormat.OpenXml.Wordprocessing.Text(text)));

    private static DocumentFormat.OpenXml.Wordprocessing.Table MakeTable(string[][] rows)
    {
        var table = new DocumentFormat.OpenXml.Wordprocessing.Table();
        foreach (string[] rowData in rows)
        {
            var row = new DocumentFormat.OpenXml.Wordprocessing.TableRow();
            foreach (string cellText in rowData)
            {
                row.Append(new DocumentFormat.OpenXml.Wordprocessing.TableCell(
                    new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                        new DocumentFormat.OpenXml.Wordprocessing.Run(
                            new DocumentFormat.OpenXml.Wordprocessing.Text(cellText)))));
            }
            table.Append(row);
        }
        return table;
    }

    // --- Helpers for XLSX ---

    private static Row MakeRow(uint rowIndex, params string[] values)
    {
        var row = new Row { RowIndex = rowIndex };
        char col = 'A';
        foreach (string value in values)
        {
            row.Append(new Cell
            {
                CellReference = $"{col}{rowIndex}",
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(value))
            });
            col++;
        }
        return row;
    }

    // --- Helpers for PPTX ---

    private static SlideLayoutPart CreateSlideLayoutPart(PresentationPart presentationPart)
    {
        SlideMasterPart slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>("rId1");
        slideMasterPart.SlideMaster = new P.SlideMaster();
        slideMasterPart.SlideMaster.AppendChild(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()))));
        slideMasterPart.SlideMaster.AppendChild(
            new P.ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
            });
        slideMasterPart.SlideMaster.AppendChild(new P.SlideLayoutIdList());

        SlideLayoutPart slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
        slideLayoutPart.SlideLayout = new P.SlideLayout();
        slideLayoutPart.SlideLayout.AppendChild(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()))));
        slideLayoutPart.SlideLayout.AppendChild(
            new P.ColorMapOverride(new A.MasterColorMapping()));

        slideLayoutPart.SlideLayout.Save();
        slideMasterPart.SlideMaster.Save();

        return slideLayoutPart;
    }

    private static P.Slide BuildSlide(string title, string body)
    {
        Shape titleShape = MakeTextShape(2U, "Title", title,
            x: 457200, y: 274638, cx: 8229600, cy: 1143000);

        Shape bodyShape = MakeTextShape(3U, "Content Placeholder", body,
            x: 457200, y: 1600200, cx: 8229600, cy: 3400200);

        var slide = new P.Slide();
        slide.AppendChild(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()),
                    titleShape,
                    bodyShape)));
        slide.AppendChild(new P.ColorMapOverride(new A.MasterColorMapping()));
        return slide;
    }

    private static P.Shape MakeTextShape(uint id, string name, string text,
        long x, long y, long cx, long cy)
    {
        string[] lines = text.Split('\n');

        var textBody = new P.TextBody(
            new A.BodyProperties(),
            new A.ListStyle());

        foreach (string line in lines)
        {
            textBody.AppendChild(
                new A.Paragraph(
                    new A.Run(
                        new A.RunProperties { Language = "en-US" },
                        new A.Text(line))));
        }

        var shape = new P.Shape();
        shape.AppendChild(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties(new PlaceholderShape())));
        shape.AppendChild(
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = cx, Cy = cy })));
        shape.AppendChild(textBody);
        return shape;
    }
}
