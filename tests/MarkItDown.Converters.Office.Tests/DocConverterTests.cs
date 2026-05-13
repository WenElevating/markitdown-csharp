using MarkItDown.Core;
using MarkItDown.Converters.Office;

namespace MarkItDown.Converters.Office.Tests;

public sealed class DocConverterTests
{
    private readonly DocConverter _converter = new();

    // ── CanConvert ──────────────────────────────────────────────────────────

    [Fact]
    public void CanConvert_AcceptsDocExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "sample.doc" }));
    }

    [Fact]
    public void CanConvert_AcceptsMsWordMimeType()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest
            {
                FilePath = "x.bin",
                MimeType = "application/msword"
            }));
    }

    [Fact]
    public void CanConvert_RejectsDocxExtension()
    {
        Assert.False(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "doc.docx" }));
    }

    // ── ConvertAsync — normal content ───────────────────────────────────────

    [Fact]
    public async Task ConvertAsync_ExtractsParagraphText()
    {
        var path = FixturePath.For("office/sample.doc");
        var result = await _converter.ConvertAsync(
            new DocumentConversionRequest { FilePath = path });

        Assert.Equal("Doc", result.Kind);
        Assert.NotNull(result.Markdown);
        // The fixture contains "Second paragraph" (from Word COM creation)
        Assert.Contains("Second paragraph", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_ReturnsDocKind()
    {
        var path = FixturePath.For("office/sample.doc");
        var result = await _converter.ConvertAsync(
            new DocumentConversionRequest { FilePath = path });
        Assert.Equal("Doc", result.Kind);
    }

    [Fact]
    public async Task ConvertAsync_ReturnsNonNullMarkdown()
    {
        var path = FixturePath.For("office/sample.doc");
        var result = await _converter.ConvertAsync(
            new DocumentConversionRequest { FilePath = path });
        Assert.NotNull(result.Markdown);
    }

    // ── ConvertAsync — error cases ──────────────────────────────────────────

    [Fact]
    public async Task ConvertAsync_ThrowsConversionException_WhenFileIsMissing()
    {
        await Assert.ThrowsAsync<ConversionException>(() =>
            _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = "nonexistent_file.doc" }));
    }

    [Fact]
    public async Task ConvertAsync_ThrowsConversionException_WhenFilePathIsNull()
    {
        await Assert.ThrowsAsync<ConversionException>(() =>
            _converter.ConvertAsync(new DocumentConversionRequest()));
    }

    [Fact]
    public async Task ConvertAsync_ThrowsConversionException_WhenFileIsCorrupt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.doc");
        await File.WriteAllBytesAsync(path, [0x00, 0x01, 0x02, 0x03, 0x04]);
        try
        {
            await Assert.ThrowsAsync<ConversionException>(() =>
                _converter.ConvertAsync(
                    new DocumentConversionRequest { FilePath = path }));
        }
        finally { File.Delete(path); }
    }
}
