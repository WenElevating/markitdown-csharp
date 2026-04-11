using MarkItDown.McpServer;

namespace MarkItDown.McpServer.Tests;

public sealed class MarkItDownToolsTests
{
    [Fact]
    public void ConvertToMarkdown_ReturnsErrorForMissingFile()
    {
        var result = MarkItDownTools.ConvertToMarkdown(
            "/nonexistent/path/to/file.docx");

        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public void ConvertToMarkdown_ConvertsMarkdownFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.md");
        var content = "# Hello World\n\nThis is a test.";
        File.WriteAllText(tempFile, content);

        try
        {
            var result = MarkItDownTools.ConvertToMarkdown(tempFile);
            Assert.Contains("Hello World", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConvertToMarkdown_ConvertsJsonFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.json");
        File.WriteAllText(tempFile, "{\"name\": \"test\", \"value\": 42}");

        try
        {
            var result = MarkItDownTools.ConvertToMarkdown(tempFile);
            Assert.Contains("```json", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
