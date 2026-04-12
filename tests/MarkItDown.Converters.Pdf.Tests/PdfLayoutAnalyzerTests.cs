using MarkItDown.Converters.Pdf;

namespace MarkItDown.Converters.Pdf.Tests;

public sealed class PdfLayoutAnalyzerTests
{
    private static PdfTextBlock Txt(double top, double bottom, double left, double right, string text = "text", double fontSize = 12.0)
        => new((top + bottom) / 2, top, bottom, left, right, text, fontSize);

    private static PdfImageBlock Img(double top, double bottom, double left, double right, int page = 1, int idx = 0, string name = "img.png")
        => new((top + bottom) / 2, top, bottom, left, right, page, idx, name);

    [Fact]
    public void AnalyzeReadingOrder_SingleColumn_ReturnsTopToBottom()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 500, "Top block"),
            Txt(660, 640, 50, 500, "Bottom block"),
        };
        var result = PdfLayoutAnalyzer.AnalyzeReadingOrder(blocks, 12.0);
        Assert.Equal(2, result.Count);
        Assert.Equal("Top block", ((PdfTextBlock)result[0]).Text);
        Assert.Equal("Bottom block", ((PdfTextBlock)result[1]).Text);
    }

    [Fact]
    public void AnalyzeReadingOrder_TwoColumns_LeftFirst()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 240, "Left"),
            Txt(700, 680, 280, 500, "Right"),
        };
        var result = PdfLayoutAnalyzer.AnalyzeReadingOrder(blocks, 12.0);
        Assert.Equal(2, result.Count);
        Assert.Equal("Left", ((PdfTextBlock)result[0]).Text);
        Assert.Equal("Right", ((PdfTextBlock)result[1]).Text);
    }

    [Fact]
    public void AnalyzeReadingOrder_TwoRowsTwoColumns_ReadingOrder()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 240, "A"),
            Txt(700, 680, 280, 500, "B"),
            Txt(660, 640, 50, 240, "C"),
            Txt(660, 640, 280, 500, "D"),
        };
        var result = PdfLayoutAnalyzer.AnalyzeReadingOrder(blocks, 12.0);
        Assert.Equal(4, result.Count);
        Assert.Equal("A", ((PdfTextBlock)result[0]).Text);
        Assert.Equal("B", ((PdfTextBlock)result[1]).Text);
        Assert.Equal("C", ((PdfTextBlock)result[2]).Text);
        Assert.Equal("D", ((PdfTextBlock)result[3]).Text);
    }

    [Fact]
    public void AnalyzeReadingOrder_FullWidthElementBetweenColumns()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 500, "Full width heading"),
            Txt(660, 640, 50, 240, "Left"),
            Txt(660, 640, 280, 500, "Right"),
        };
        var result = PdfLayoutAnalyzer.AnalyzeReadingOrder(blocks, 12.0);
        Assert.Equal(3, result.Count);
        Assert.Equal("Full width heading", ((PdfTextBlock)result[0]).Text);
        Assert.Equal("Left", ((PdfTextBlock)result[1]).Text);
        Assert.Equal("Right", ((PdfTextBlock)result[2]).Text);
    }

    [Fact]
    public void AnalyzeReadingOrder_SingleBlock_NoSplit()
    {
        var blocks = new List<PdfContentBlock> { Txt(700, 680, 50, 500, "Only block") };
        var result = PdfLayoutAnalyzer.AnalyzeReadingOrder(blocks, 12.0);
        Assert.Single(result);
        Assert.Equal("Only block", ((PdfTextBlock)result[0]).Text);
    }

    [Fact]
    public void AnalyzeReadingOrder_EmptyList_ReturnsEmpty()
    {
        var result = PdfLayoutAnalyzer.AnalyzeReadingOrder([], 12.0);
        Assert.Empty(result);
    }
}
