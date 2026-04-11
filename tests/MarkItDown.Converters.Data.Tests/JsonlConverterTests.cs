using MarkItDown.Core;

namespace MarkItDown.Converters.Data.Tests;

public sealed class JsonlConverterTests
{
    private readonly JsonlConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsJsonlExtension()
    {
        var request = new DocumentConversionRequest { FilePath = "test.jsonl" };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public async Task ConvertAsync_PrettyPrintsEachLine()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jsonl");
        try
        {
            var content = """{"name":"Alice","age":30}""" + "\n" + """{"name":"Bob","age":25}""";
            await File.WriteAllTextAsync(tempFile, content);

            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = tempFile });

            Assert.Equal("Jsonl", result.Kind);
            Assert.Contains("```jsonl", result.Markdown);
            Assert.Contains("Alice", result.Markdown);
            Assert.Contains("Bob", result.Markdown);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task ConvertAsync_HandlesEmptyFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jsonl");
        try
        {
            await File.WriteAllTextAsync(tempFile, string.Empty);

            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = tempFile });

            Assert.Equal("Jsonl", result.Kind);
            Assert.Equal(string.Empty, result.Markdown);
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
