using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace MarkItDown.Converters.Office.Tests;

internal static class GoldenOfficeFixtureFactory
{
    public static string Create(string format)
    {
        var path = Path.Combine(Path.GetTempPath(), $"golden-{format}-{Guid.NewGuid():N}.{format}");
        switch (format)
        {
            case "docx": CreateDocx(path); break;
            case "pptx": CreatePptx(path); break;
            case "xlsx": CreateXlsx(path); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
        return path;
    }

    private static void CreateDocx(string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        main.Document = new Document(new Body(
            Paragraph("Golden DOCX", "Heading2"),
            new Paragraph(new W.Run(new W.Text("Golden DOCX body")))));
        var image = main.AddImagePart(ImagePartType.Png);
        image.FeedData(new MemoryStream(PngBytes()));
        main.Document.Save();
    }

    private static void CreatePptx(string path)
    {
        using var document = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentationPart = document.AddPresentationPart();
        presentationPart.Presentation = new Presentation();
        var slidePart = presentationPart.AddNewPart<SlidePart>();
        var image = slidePart.AddImagePart(ImagePartType.Png);
        image.FeedData(new MemoryStream(PngBytes()));
        slidePart.Slide = new Slide(
            new CommonSlideData(new ShapeTree(
                new NonVisualGroupShapeProperties(new NonVisualDrawingProperties { Id = 1, Name = "" }),
                new GroupShapeProperties(new A.TransformGroup()),
                Shape(2, "Golden PPTX", isTitle: true),
                Shape(3, "Golden PPTX body", isTitle: false))),
            new ColorMapOverride(new A.ColorMap()));
        presentationPart.Presentation.SlideIdList = new SlideIdList(
            new SlideId { Id = 256, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
        presentationPart.Presentation.Save();
    }

    private static void CreateXlsx(string path)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var image = worksheetPart.AddImagePart(ImagePartType.Png);
        image.FeedData(new MemoryStream(PngBytes()));
        var sheetData = new SheetData(
            new Row(new Cell { CellReference = "A1", CellValue = new CellValue("Name"), DataType = CellValues.String }, new Cell { CellReference = "B1", CellValue = new CellValue("Department"), DataType = CellValues.String }),
            new Row(new Cell { CellReference = "A2", CellValue = new CellValue("Alice"), DataType = CellValues.String }, new Cell { CellReference = "B2", CellValue = new CellValue("Engineering"), DataType = CellValues.String }));
        worksheetPart.Worksheet = new Worksheet(sheetData);
        workbookPart.Workbook.AppendChild(new Sheets(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Golden XLSX"
        }));
        workbookPart.Workbook.Save();
    }

    private static Paragraph Paragraph(string text, string style)
    {
        return new Paragraph(new W.Run(new W.Text(text)))
        {
            ParagraphProperties = new ParagraphProperties { ParagraphStyleId = new ParagraphStyleId { Val = style } }
        };
    }

    private static Shape Shape(uint id, string text, bool isTitle)
    {
        var textBody = new TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.Run(new A.Text(text))));
        return new Shape(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = id, Name = text },
                new NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties
                {
                    PlaceholderShape = new PlaceholderShape { Type = isTitle ? PlaceholderValues.Title : PlaceholderValues.Body }
                }),
            new ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 9144000, Cy = 4572000 })),
            textBody);
    }

    private static byte[] PngBytes() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
}
