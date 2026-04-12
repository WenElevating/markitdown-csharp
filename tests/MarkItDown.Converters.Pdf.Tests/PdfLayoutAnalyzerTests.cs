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

    [Fact]
    public void DetectHeadersFooters_RepeatedText_Marked()
    {
        var pageHeight = 800.0;
        var page1 = new List<PdfContentBlock>
        {
            Txt(790, 780, 50, 200, "Company Confidential", 8),
            Txt(700, 680, 50, 500, "Main content page 1"),
        };
        var page2 = new List<PdfContentBlock>
        {
            Txt(790, 780, 50, 200, "Company Confidential", 8),
            Txt(700, 680, 50, 500, "Main content page 2"),
        };
        var page3 = new List<PdfContentBlock>
        {
            Txt(790, 780, 50, 200, "Company Confidential", 8),
            Txt(700, 680, 50, 500, "Main content page 3"),
        };
        var allPages = new List<List<PdfContentBlock>> { page1, page2, page3 };

        PdfLayoutAnalyzer.DetectHeadersFooters(allPages, pageHeight);

        Assert.True(((PdfTextBlock)page1[0]).IsHeaderFooter);
        Assert.True(((PdfTextBlock)page2[0]).IsHeaderFooter);
        Assert.True(((PdfTextBlock)page3[0]).IsHeaderFooter);
        Assert.False(((PdfTextBlock)page1[1]).IsHeaderFooter);
    }

    [Fact]
    public void DetectHeadersFooters_PageNumber_Marked()
    {
        var pageHeight = 800.0;
        var page = new List<PdfContentBlock>
        {
            Txt(10, 5, 350, 450, "3/20", 10),
            Txt(700, 680, 50, 500, "Content"),
        };
        var allPages = new List<List<PdfContentBlock>> { page };

        PdfLayoutAnalyzer.DetectHeadersFooters(allPages, pageHeight);

        Assert.True(((PdfTextBlock)page[0]).IsHeaderFooter);
    }

    [Fact]
    public void DetectHeadersFooters_UniqueText_NotMarked()
    {
        var pageHeight = 800.0;
        var page = new List<PdfContentBlock>
        {
            Txt(790, 780, 50, 200, "Unique title", 10),
            Txt(700, 680, 50, 500, "Content"),
        };
        var allPages = new List<List<PdfContentBlock>> { page };

        PdfLayoutAnalyzer.DetectHeadersFooters(allPages, pageHeight);

        Assert.False(((PdfTextBlock)page[0]).IsHeaderFooter);
    }

    [Fact]
    public void DetectCaptions_TextBelowSmallFont_Marked()
    {
        var blocks = new List<PdfContentBlock>
        {
            Img(700, 620, 50, 400),
            Txt(610, 595, 60, 390, "Figure 1: Caption text", 9),
            Txt(580, 560, 50, 400, "Body text", 12),
        };

        var captions = PdfLayoutAnalyzer.DetectCaptions(blocks, 12.0);

        Assert.Single(captions);
        Assert.Contains(1, captions);
    }

    [Fact]
    public void DetectCaptions_NoNearbyText_NoCaption()
    {
        var blocks = new List<PdfContentBlock>
        {
            Img(700, 620, 50, 400),
            Txt(580, 560, 50, 400, "Body text far away", 12),
        };

        var captions = PdfLayoutAnalyzer.DetectCaptions(blocks, 12.0);
        Assert.Empty(captions);
    }

    [Fact]
    public void DetectLists_NumberedItems_Detected()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 400, "1. First item"),
            Txt(660, 640, 50, 400, "2. Second item"),
            Txt(620, 600, 50, 400, "3. Third item"),
            Txt(560, 540, 50, 400, "Normal text"),
        };

        var lists = PdfLayoutAnalyzer.DetectLists(blocks);

        Assert.Single(lists);
        Assert.Equal(0, lists[0].Start);
        Assert.Equal(3, lists[0].Length);
    }

    [Fact]
    public void DetectLists_BulletedItems_Detected()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 400, "• First bullet"),
            Txt(660, 640, 50, 400, "• Second bullet"),
        };

        var lists = PdfLayoutAnalyzer.DetectLists(blocks);

        Assert.Single(lists);
        Assert.Equal(2, lists[0].Length);
    }

    [Fact]
    public void DetectLists_SingleItem_NotAList()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 400, "1. Only one item"),
            Txt(660, 640, 50, 400, "Normal text"),
        };

        var lists = PdfLayoutAnalyzer.DetectLists(blocks);
        Assert.Empty(lists);
    }

    [Fact]
    public void MergeParagraphs_ConsecutiveSameFont_Merges()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 400, "First line of paragraph.", 12.0),
            Txt(675, 655, 50, 400, "Second line of same paragraph.", 12.0),
            Txt(650, 630, 50, 400, "Third line.", 12.0),
        };

        var result = PdfLayoutAnalyzer.MergeParagraphs(blocks, 12.0);

        Assert.Single(result);
        var merged = (PdfTextBlock)result[0];
        Assert.Equal("First line of paragraph. Second line of same paragraph. Third line.", merged.Text);
        Assert.Equal(700, merged.Top);
        Assert.Equal(630, merged.Bottom);
    }

    [Fact]
    public void MergeParagraphs_DifferentFontSize_NoMerge()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 400, "Heading text", 18.0),
            Txt(660, 640, 50, 400, "Body text", 12.0),
        };

        var result = PdfLayoutAnalyzer.MergeParagraphs(blocks, 12.0);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void MergeParagraphs_LargeGap_NoMerge()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 400, "First paragraph.", 12.0),
            Txt(600, 580, 50, 400, "Second paragraph after gap.", 12.0),
        };

        var result = PdfLayoutAnalyzer.MergeParagraphs(blocks, 12.0);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void MergeParagraphs_ImageBlock_BreaksMerge()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 400, "Text before image.", 12.0),
            Img(660, 600, 50, 400),
            Txt(580, 560, 50, 400, "Text after image.", 12.0),
        };

        var result = PdfLayoutAnalyzer.MergeParagraphs(blocks, 12.0);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void MergeParagraphs_DifferentAlignment_NoMerge()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 400, "Left aligned text.", 12.0),
            Txt(675, 655, 200, 500, "Indented text.", 12.0),
        };

        var result = PdfLayoutAnalyzer.MergeParagraphs(blocks, 12.0);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void MergeParagraphs_EmptyInput_ReturnsEmpty()
    {
        var result = PdfLayoutAnalyzer.MergeParagraphs([], 12.0);
        Assert.Empty(result);
    }
}
