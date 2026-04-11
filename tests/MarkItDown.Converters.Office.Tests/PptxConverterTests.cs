using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using MarkItDown.Core;
using MarkItDown.Converters.Office;

namespace MarkItDown.Converters.Office.Tests;

public sealed class PptxConverterTests
{
    private readonly PptxConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsPptxExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "slides.pptx" }));
    }

    [Fact]
    public async Task ConvertAsync_ExtractsSlideTitlesAndContent()
    {
        var pptxPath = CreateTestPptx();
        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = pptxPath });

            Assert.Contains("## Welcome", result.Markdown);
            Assert.Contains("Hello World", result.Markdown);
            Assert.Contains("First point", result.Markdown);
            Assert.Equal("Pptx", result.Kind);
        }
        finally
        {
            File.Delete(pptxPath);
        }
    }

    [Fact]
    public async Task ConvertAsync_HandlesEmptyPresentation()
    {
        var pptxPath = CreateEmptyPptx();
        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = pptxPath });
            Assert.NotNull(result.Markdown);
        }
        finally
        {
            File.Delete(pptxPath);
        }
    }

    private static string CreateTestPptx()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pptx");
        using var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation);

        var presentationPart = doc.AddPresentationPart();
        presentationPart.Presentation = new Presentation();

        var slidePart = presentationPart.AddNewPart<SlidePart>();
        var slide = new Slide(
            new CommonSlideData(
                new ShapeTree(
                    new NonVisualGroupShapeProperties(new NonVisualDrawingProperties() { Id = 1, Name = "" }),
                    new GroupShapeProperties(new A.TransformGroup()),
                    // Title shape
                    CreateShape(2, "Title 1", "Welcome", true),
                    // Content shape
                    CreateShape(3, "Content 1", "Hello World\nFirst point\nSecond point", false))),
            new ColorMapOverride(new A.ColorMap()));
        slidePart.Slide = slide;

        var slideIdList = new SlideIdList();
        slideIdList.Append(new SlideId { Id = 256, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
        presentationPart.Presentation.SlideIdList = slideIdList;
        presentationPart.Presentation.Save();

        return path;
    }

    private static Shape CreateShape(uint id, string name, string text, bool isTitle)
    {
        var textBody = new TextBody(
            new A.BodyProperties(),
            new A.ListStyle());

        foreach (var line in text.Split('\n'))
        {
            textBody.Append(new A.Paragraph(
                new A.Run(new A.RunProperties(), new A.Text(line))));
        }

        var shape = new Shape(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = id, Name = name },
                new NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties
                {
                    PlaceholderShape = isTitle
                        ? new PlaceholderShape { Type = PlaceholderValues.Title }
                        : new PlaceholderShape { Type = PlaceholderValues.Body }
                }),
            new ShapeProperties(new A.Transform2D(
                new A.Offset { X = 0, Y = 0 },
                new A.Extents { Cx = 9144000, Cy = isTitle ? 1143000 : 4572000 })),
            textBody);

        return shape;
    }

    private static string CreateEmptyPptx()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pptx");
        using var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentationPart = doc.AddPresentationPart();
        presentationPart.Presentation = new Presentation(new SlideIdList());
        presentationPart.Presentation.Save();
        return path;
    }
}
