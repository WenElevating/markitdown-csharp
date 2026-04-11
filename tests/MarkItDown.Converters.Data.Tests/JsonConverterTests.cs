using MarkItDown.Core;

namespace MarkItDown.Converters.Data.Tests;

public sealed class JsonConverterTests
{
    private readonly JsonConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsJsonExtension()
    {
        var request = new DocumentConversionRequest { FilePath = "test.json" };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public async Task ConvertAsync_WrapsInCodeFence()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        try
        {
            const string content = """{"name":"Alice","age":30}""";
            await File.WriteAllTextAsync(tempFile, content);

            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = tempFile });

            Assert.Equal("Json", result.Kind);
            Assert.Contains("```json", result.Markdown);
            Assert.Contains("name", result.Markdown);
            Assert.Contains("Alice", result.Markdown);
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
