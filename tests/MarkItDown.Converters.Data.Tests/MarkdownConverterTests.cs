using MarkItDown.Core;

namespace MarkItDown.Converters.Data.Tests;

public sealed class MarkdownConverterTests
{
    private readonly MarkdownConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsMdExtension()
    {
        var request = new DocumentConversionRequest { FilePath = "test.md" };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_AcceptsMarkdownExtension()
    {
        var request = new DocumentConversionRequest { FilePath = "test.markdown" };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public async Task ConvertAsync_ReturnsContentVerbatim()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.md");
        try
        {
            const string content = "# Hello World\n\nThis is a test.";
            await File.WriteAllTextAsync(tempFile, content);

            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = tempFile });

            Assert.Equal("Markdown", result.Kind);
            Assert.Equal(content, result.Markdown);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
