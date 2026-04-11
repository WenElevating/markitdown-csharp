using MarkItDown.Core;

namespace MarkItDown.Core.Tests;

public sealed class HtmlConverterTests
{
    private readonly HtmlConverter _converter = new();

    [Fact]
    public async Task ConvertAsync_PreservesHeadingsListsAndLinks()
    {
        var result = await _converter.ConvertAsync(new DocumentConversionRequest(FixturePath.For("sample.html")));

        Assert.Contains("## Experiment Setup", result.Markdown);
        Assert.Contains("- gpt-3.5-turbo", result.Markdown);
        Assert.Contains("[MATH](", result.Markdown);
        Assert.DoesNotContain("Skip to main content", result.Markdown);
        Assert.DoesNotContain("What's new in AutoGen?", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_RendersMarkdownTableFromHtmlTable()
    {
        var result = await _converter.ConvertAsync(new DocumentConversionRequest(FixturePath.For("table.html")));

        Assert.Contains("# Quarterly Inventory Review", result.Markdown);
        Assert.Contains("| Product | Expected | Actual |", result.Markdown);
        Assert.Contains("| SKU-100 | 24 | 26 |", result.Markdown);
        Assert.Contains("[inventory trends](https://example.com/inventory)", result.Markdown);
        Assert.DoesNotContain("Home", result.Markdown);
    }
}
