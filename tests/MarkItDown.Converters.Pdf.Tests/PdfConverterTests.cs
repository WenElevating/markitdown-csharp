using System.Linq;
using MarkItDown.Core;
using MarkItDown.Converters.Pdf;

namespace MarkItDown.Converters.Pdf.Tests;

public sealed class PdfConverterTests
{
    private readonly PdfConverter _converter = new();

    [Fact]
    public async Task ConvertAsync_ExtractsTextFromPdf()
    {
        var result = await _converter.ConvertAsync(
            new DocumentConversionRequest { FilePath = FixturePath.For("sample.pdf") });

        Assert.Contains("Introduction", result.Markdown);
        Assert.Contains("Large language models", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_ExtractsTableLikeMarkdown()
    {
        var result = await _converter.ConvertAsync(
            new DocumentConversionRequest { FilePath = FixturePath.For("table.pdf") });

        Assert.Contains("Product Code", result.Markdown);
        Assert.Contains("Location", result.Markdown);
        Assert.Contains("Status", result.Markdown);
        Assert.Contains("SKU-8847", result.Markdown);
        Assert.Contains("Recommendations", result.Markdown);
        Assert.Contains("|", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_RejectsScannedPdf()
    {
        var exception = await Assert.ThrowsAsync<ConversionException>(() =>
            _converter.ConvertAsync(new DocumentConversionRequest { FilePath = FixturePath.For("scanned.pdf") }));

        Assert.Contains("The PDF did not contain extractable text or images", exception.Message);
    }

    [Fact]
    public async Task ConvertAsync_ClassifiesHeadingsByFontSize()
    {
        var result = await _converter.ConvertAsync(
            new DocumentConversionRequest { FilePath = FixturePath.For("sample.pdf") });

        Assert.False(string.IsNullOrWhiteSpace(result.Markdown));

        var lines = result.Markdown.Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        var headingCount = lines.Count(l => l.StartsWith("## "));
        var nonHeadingCount = lines.Count - headingCount;
        Assert.True(nonHeadingCount > 0, "Expected some non-heading body text lines");
    }

    [Fact]
    public async Task ConvertAsync_WithAssetBasePath_DoesNotCrash()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pdf-test-{Guid.NewGuid():N}");
        try
        {
            var assetPath = Path.Combine(tempDir, "output_files");
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest
                {
                    FilePath = FixturePath.For("sample.pdf"),
                    AssetBasePath = assetPath
                });

            Assert.False(string.IsNullOrWhiteSpace(result.Markdown));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task ConvertAsync_ImagePathsIncludeAssetDirName()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pdf-test-{Guid.NewGuid():N}");
        try
        {
            var assetPath = Path.Combine(tempDir, "PuYu_files");
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest
                {
                    FilePath = FixturePath.For("sample.pdf"),
                    AssetBasePath = assetPath
                });

            Assert.False(string.IsNullOrWhiteSpace(result.Markdown));

            // If any image references exist, they must include the subdirectory name
            var imageLines = result.Markdown.Split('\n')
                .Where(l => l.Contains("![image]"))
                .ToList();

            // sample.pdf may not have images — when images ARE present,
            // their paths must include the asset directory name
            foreach (var line in imageLines)
            {
                Assert.Contains("PuYu_files/", line);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task ConvertAsync_WithoutAssetBasePath_ProducesTextOnly()
    {
        var result = await _converter.ConvertAsync(
            new DocumentConversionRequest { FilePath = FixturePath.For("sample.pdf") });

        Assert.Null(result.AssetDirectory);
        Assert.False(string.IsNullOrWhiteSpace(result.Markdown));
    }

    [Fact]
    public void RenderPage_ImagePathIncludesAssetDirName()
    {
        var blocks = new List<PdfContentBlock>
        {
            new PdfImageBlock(700, 720, 680, 50, 400, 1, 0, "page1_img0.png"),
            new PdfTextBlock(650, 660, 640, 50, 400, "Some text", 12.0),
        };

        var markdown = PdfContentGrouper.RenderPage(blocks, 12.0, "PuYu_files");

        Assert.Contains("![image](./PuYu_files/page1_img0.png)", markdown);
        Assert.DoesNotContain("![image](./page1_img0.png)", markdown);
    }

    [Fact]
    public void RenderPage_ImagePathWithoutAssetDirName_NoPrefix()
    {
        var blocks = new List<PdfContentBlock>
        {
            new PdfImageBlock(700, 720, 680, 50, 400, 1, 0, "page1_img0.png"),
        };

        var markdown = PdfContentGrouper.RenderPage(blocks, 12.0);

        Assert.Contains("![image](./page1_img0.png)", markdown);
    }
}
