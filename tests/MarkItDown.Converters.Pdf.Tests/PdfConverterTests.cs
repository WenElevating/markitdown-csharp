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
    public async Task ConvertAsync_WithoutAssetBasePath_ProducesTextOnly()
    {
        var result = await _converter.ConvertAsync(
            new DocumentConversionRequest { FilePath = FixturePath.For("sample.pdf") });

        Assert.Null(result.AssetDirectory);
        Assert.False(string.IsNullOrWhiteSpace(result.Markdown));
    }
}
