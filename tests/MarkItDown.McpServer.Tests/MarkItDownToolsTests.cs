using MarkItDown.McpServer;

namespace MarkItDown.McpServer.Tests;

public sealed class MarkItDownToolsTests
{
    private const string AllowedRootsEnvironmentVariable = "MARKITDOWN_MCP_ALLOWED_ROOTS";

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
        var tempFile = Path.Combine(Environment.CurrentDirectory, $"test_{Guid.NewGuid()}.md");
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
        var tempFile = Path.Combine(Environment.CurrentDirectory, $"test_{Guid.NewGuid()}.json");
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

    [Fact]
    public void ConvertToMarkdown_RejectsPathOutsideConfiguredRoots()
    {
        var previous = Environment.GetEnvironmentVariable(AllowedRootsEnvironmentVariable);
        var allowedRoot = Path.Combine(Path.GetTempPath(), $"allowed_{Guid.NewGuid():N}");
        var deniedRoot = Path.Combine(Path.GetTempPath(), $"denied_{Guid.NewGuid():N}");
        Directory.CreateDirectory(allowedRoot);
        Directory.CreateDirectory(deniedRoot);
        var deniedFile = Path.Combine(deniedRoot, "file.md");
        File.WriteAllText(deniedFile, "# Denied");

        try
        {
            Environment.SetEnvironmentVariable(AllowedRootsEnvironmentVariable, allowedRoot);

            var result = MarkItDownTools.ConvertToMarkdown(deniedFile);

            Assert.Contains("outside allowed MCP roots", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AllowedRootsEnvironmentVariable, previous);
            Directory.Delete(allowedRoot, recursive: true);
            Directory.Delete(deniedRoot, recursive: true);
        }
    }

    [Fact]
    public void ConvertToMarkdown_AllowsPathInsideConfiguredRoot()
    {
        var previous = Environment.GetEnvironmentVariable(AllowedRootsEnvironmentVariable);
        var allowedRoot = Path.Combine(Path.GetTempPath(), $"allowed_{Guid.NewGuid():N}");
        Directory.CreateDirectory(allowedRoot);
        var allowedFile = Path.Combine(allowedRoot, "file.md");
        File.WriteAllText(allowedFile, "# Allowed");

        try
        {
            Environment.SetEnvironmentVariable(AllowedRootsEnvironmentVariable, allowedRoot);

            var result = MarkItDownTools.ConvertToMarkdown(allowedFile);

            Assert.Contains("Allowed", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AllowedRootsEnvironmentVariable, previous);
            Directory.Delete(allowedRoot, recursive: true);
        }
    }

    [Fact]
    public void ConvertToMarkdown_DefaultRootRejectsTempPathOutsideCurrentDirectory()
    {
        var previous = Environment.GetEnvironmentVariable(AllowedRootsEnvironmentVariable);
        var deniedRoot = Path.Combine(Path.GetTempPath(), $"denied_{Guid.NewGuid():N}");
        Directory.CreateDirectory(deniedRoot);
        var deniedFile = Path.Combine(deniedRoot, "file.md");
        File.WriteAllText(deniedFile, "# Denied");

        try
        {
            Environment.SetEnvironmentVariable(AllowedRootsEnvironmentVariable, null);

            var result = MarkItDownTools.ConvertToMarkdown(deniedFile);

            Assert.Contains("outside allowed MCP roots", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AllowedRootsEnvironmentVariable, previous);
            Directory.Delete(deniedRoot, recursive: true);
        }
    }
}
