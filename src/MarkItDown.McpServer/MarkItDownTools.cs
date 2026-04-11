using System.ComponentModel;
using System.Reflection;
using MarkItDown.Core;
using ModelContextProtocol.Server;

namespace MarkItDown.McpServer;

[McpServerToolType]
public static class MarkItDownTools
{
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
}
