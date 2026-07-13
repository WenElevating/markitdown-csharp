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
            Assert.Equal(1, result.Document!.Blocks.First(block => block.Kind == "heading").Source!.Slide);
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

    [Fact]
    public async Task ConvertAsync_MultimodalAddsDoclingSupplementWithoutReplacingNativeContent()
    {
        var pptxPath = CreateTestPptx();
        try
        {
            var result = await _converter.ConvertAsync(new DocumentConversionRequest
            {
                FilePath = pptxPath,
                Context = new ConversionContext
                {
                    Pipeline = PipelineMode.Multimodal,
                    Vision = VisionMode.Auto,
                    Options = new ConversionOptions { PipelineMode = PipelineMode.Multimodal, VisionMode = VisionMode.Auto, DoclingTransport = new FakeDoclingTransport() }
                }
            });

            Assert.Equal(FidelityStatus.Complete, result.FidelityStatus);
            Assert.Contains("## Welcome", result.Markdown);
            Assert.Contains("Visual chart insight", result.Markdown);
            Assert.DoesNotContain(result.Diagnostics ?? [], d => d.Code == "MULTIMODAL_FORMAT_UNSUPPORTED");
            Assert.NotEmpty(result.Document?.Supplements ?? []);
        }
        finally
        {
            File.Delete(pptxPath);
        }
    }

    [Fact]
    public async Task ConvertAsync_MultimodalDoclingFailureFallsBackToNativeOutput()
    {
        var pptxPath = CreateTestPptx();
        try
        {
            var result = await _converter.ConvertAsync(new DocumentConversionRequest
            {
                FilePath = pptxPath,
                Context = new ConversionContext
                {
                    Pipeline = PipelineMode.Multimodal,
                    Vision = VisionMode.Auto,
                    Options = new ConversionOptions { PipelineMode = PipelineMode.Multimodal, VisionMode = VisionMode.Auto, DoclingTransport = new FailingDoclingTransport() }
                }
            });

            Assert.Equal(FidelityStatus.Partial, result.FidelityStatus);
            Assert.Contains("## Welcome", result.Markdown);
            Assert.Contains(result.Diagnostics!, d => d.Code == "DOCLING_FAILED_FALLBACK_NATIVE");
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

    private sealed class FakeDoclingTransport : IDoclingTransport
    {
        public Task<DoclingResponse> SendAsync(DoclingRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DoclingResponse(request.RequestId, "1", true,
                "{\"texts\":[{\"text\":\"Hello World\"},{\"text\":\"Visual chart insight\"}]}", null, null));
    }

    private sealed class FailingDoclingTransport : IDoclingTransport
    {
        public Task<DoclingResponse> SendAsync(DoclingRequest request, CancellationToken cancellationToken = default) =>
            throw new IOException("provider unavailable");
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
