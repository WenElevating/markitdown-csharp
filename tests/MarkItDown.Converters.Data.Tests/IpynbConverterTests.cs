using System.Text.Json;
using MarkItDown.Core;
using MarkItDown.Converters.Data;

namespace MarkItDown.Converters.Data.Tests;

public sealed class IpynbConverterTests
{
    private readonly IpynbConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsIpynbExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "notebook.ipynb" }));
    }

    [Fact]
    public async Task ConvertAsync_ConvertsMarkdownAndCodeCells()
    {
        var path = CreateTestNotebook();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("# Test Notebook", result.Markdown);
            Assert.Contains("```python", result.Markdown);
            Assert.Contains("print(", result.Markdown);
            Assert.Equal("Ipynb", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_PreservesMarkdownCells()
    {
        var path = CreateTestNotebook();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("This is a **markdown** cell.", result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTestNotebook()
    {
        var notebook = new
        {
            nbformat = 4,
            nbformat_minor = 5,
            metadata = new { },
            cells = new object[]
            {
                new
                {
                    cell_type = "markdown",
                    metadata = new { },
                    source = new[] { "# Test Notebook" }
                },
                new
                {
                    cell_type = "markdown",
                    metadata = new { },
                    source = new[] { "This is a **markdown** cell." }
                },
                new
                {
                    cell_type = "code",
                    metadata = new { },
                    source = new[] { "print(\"hello world\")" },
                    execution_count = 1,
                    outputs = Array.Empty<object>()
                }
            }
        };

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.ipynb");
        var json = JsonSerializer.Serialize(notebook);
        File.WriteAllText(path, json);
        return path;
    }
}
