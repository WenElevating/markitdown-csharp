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

        Assert.Contains("Scanned or image-only PDFs are not supported", exception.Message);
    }
}
