using System.ComponentModel;
using System.Reflection;
using MarkItDown.Core;
using ModelContextProtocol.Server;

namespace MarkItDown.McpServer;

[McpServerToolType]
public static class MarkItDownTools
{
    private const string AllowedRootsEnvironmentVariable = "MARKITDOWN_MCP_ALLOWED_ROOTS";
    private static readonly Lazy<MarkItDownEngine> Engine = new(CreateEngine);

    private static MarkItDownEngine CreateEngine()
    {
        // Force-load all MarkItDown converter assemblies from the application
        // base directory so that CreateWithAllConverters() can discover them.
        LoadConverterAssemblies();

        return MarkItDownEngine.CreateWithAllConverters();
    }

    private static void LoadConverterAssemblies()
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var dll in Directory.GetFiles(baseDir, "MarkItDown.Converters.*.dll"))
        {
            try
            {
                Assembly.LoadFrom(dll);
            }
            catch
            {
                // Skip assemblies that cannot be loaded
            }
        }
    }

    [McpServerTool, Description("Converts a file to Markdown. Supports DOCX, PPTX, XLSX, CSV, MSG, JSON, JSONL, XML, RSS, IPYNB, EPUB, ZIP, Markdown, HTML, PDF, images, audio, and web URLs.")]
    public static string ConvertToMarkdown(
        [Description("Path to a file to convert")] string path)
    {
        try
        {
            EnsurePathIsAllowed(path);
            var result = Engine.Value.ConvertAsync(path).GetAwaiter().GetResult();
            return result.Markdown;
        }
        catch (FileNotFoundException ex)
        {
            return $"Error: File not found: {ex.Message}";
        }
        catch (UnsupportedFormatException ex)
        {
            return $"Error: Unsupported format: {ex.Message}";
        }
        catch (ConversionException ex)
        {
            return $"Error: Conversion failed: {ex.Message}";
        }
    }

    private static void EnsurePathIsAllowed(string path)
    {
        var allowedRoots = GetAllowedRoots();
        var fullPath = Path.GetFullPath(path);
        foreach (var root in allowedRoots)
        {
            if (FileSystemBoundary.IsPathWithinRoot(fullPath, root))
            {
                return;
            }
        }

        throw new ConversionException($"Path is outside allowed MCP roots. Set {AllowedRootsEnvironmentVariable} to include this location.");
    }

    private static List<string> GetAllowedRoots()
    {
        var value = Environment.GetEnvironmentVariable(AllowedRootsEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return [FileSystemBoundary.NormalizeRoot(Environment.CurrentDirectory)];
        }

        return value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.GetFullPath)
            .Select(FileSystemBoundary.NormalizeRoot)
            .ToList();
    }
}
