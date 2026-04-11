using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDown.Core;
using MarkItDown.Converters.Office;

namespace MarkItDown.Converters.Office.Tests;

public sealed class DocxConverterTests
{
    private readonly DocxConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsDocxExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "doc.docx" }));
    }

    [Fact]
    public async Task ConvertAsync_ExtractsHeadingsAndParagraphs()
    {
        var docxPath = CreateTestDocx();
        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = docxPath });

            Assert.Contains("## Introduction", result.Markdown);
            Assert.Contains("**bold**", result.Markdown);
            Assert.Contains("*italic*", result.Markdown);
            Assert.Contains("- Item 1", result.Markdown);
            Assert.Contains("| Header A | Header B |", result.Markdown);
            Assert.Equal("Docx", result.Kind);
        }
        finally
        {
            File.Delete(docxPath);
        }
    }

    [Fact]
    public async Task ConvertAsync_HandlesEmptyDocument()
    {
        var docxPath = CreateEmptyDocx();
        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = docxPath });
            Assert.NotNull(result.Markdown);
        }
        finally
        {
            File.Delete(docxPath);
        }
    }

    private static string CreateTestDocx()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);

        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        // Heading
        body.AppendChild(CreateParagraph("Introduction", "Heading2"));

        // Paragraph with bold and italic
        var para = new Paragraph();
        var run1 = new Run(new Text("This is "));
        var run2 = new Run(new Text("bold"))
        {
            RunProperties = new RunProperties { Bold = new Bold() }
        };
        var run3 = new Run(new Text(" and "));
        var run4 = new Run(new Text("italic"))
        {
            RunProperties = new RunProperties { Italic = new Italic() }
        };
        var run5 = new Run(new Text(" text."));
        run1.RunProperties = new RunProperties();
        run3.RunProperties = new RunProperties();
        run5.RunProperties = new RunProperties();
        para.Append(run1, run2, run3, run4, run5);
        body.AppendChild(para);

        // Bullet list
        body.AppendChild(CreateListParagraph("Item 1"));
        body.AppendChild(CreateListParagraph("Item 2"));

        // Table
        var table = new Table(
            new TableRow(
                new TableCell(new Paragraph(new Run(new Text("Header A")))),
                new TableCell(new Paragraph(new Run(new Text("Header B"))))),
            new TableRow(
                new TableCell(new Paragraph(new Run(new Text("Cell 1")))),
                new TableCell(new Paragraph(new Run(new Text("Cell 2"))))));
        body.AppendChild(table);

        mainPart.Document.Save();
        return path;
    }

    private static Paragraph CreateParagraph(string text, string styleId)
    {
        var para = new Paragraph(new Run(new Text(text)));
        para.ParagraphProperties = new ParagraphProperties
        {
            ParagraphStyleId = new ParagraphStyleId { Val = styleId }
        };
        return para;
    }

    private static Paragraph CreateListParagraph(string text)
    {
        var para = new Paragraph(new Run(new Text(text)));
        para.ParagraphProperties = new ParagraphProperties
        {
            NumberingProperties = new NumberingProperties
            {
                NumberingId = new NumberingId { Val = 1 },
                NumberingLevelReference = new NumberingLevelReference { Val = 0 }
            }
        };
        return para;
    }

    private static string CreateEmptyDocx()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        mainPart.Document.Save();
        return path;
    }
}
