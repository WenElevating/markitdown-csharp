using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using MarkItDown.Core;
using ModelContextProtocol.Server;

namespace MarkItDown.McpServer;

[McpServerToolType]
public static class MarkItDownTools
{
    private const string AllowedRootsEnvironmentVariable = "MARKITDOWN_MCP_ALLOWED_ROOTS";
    private static readonly Lazy<MarkItDownEngine> Engine = new(CreateEngine);
    private static readonly ConversionAssetRegistry AssetRegistry = new();

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
            catch (Exception ex) when (ex is IOException or BadImageFormatException or FileLoadException)
            {
                // Skip assemblies that cannot be loaded (corrupt, wrong arch, missing deps)
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

    [McpServerTool, Description("Converts a file using the multimodal pipeline and returns Markdown, diagnostics, fidelity status, and asset URIs.")]
    public static DetailedConversionResponse ConvertToMarkdownDetailed(
        [Description("Path to a file within an allowed MCP root")] string path,
        [Description("Allow a configured vision provider to upload document pages when required")] bool allowDocumentUpload = false)
    {
        var operationId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        try
        {
            EnsurePathIsAllowed(path);
            if (!AssetRegistry.TryAcquireConversion(operationId, out var conversionLease))
                return Failed("RESOURCE_LIMIT_EXCEEDED", "MCP active conversion quota exceeded.", operationId);
            using (conversionLease)
            {
            IDisposable? inputLease = null;
            if (File.Exists(path))
            {
                var inputBytes = new FileInfo(path).Length;
                if (!AssetRegistry.TryReserveBytes(inputBytes, out inputLease))
                    return Failed("RESOURCE_LIMIT_EXCEEDED", "MCP temporary input quota exceeded.", operationId);
            }
            using (inputLease)
            {
            var vision = allowDocumentUpload ? VisionMode.Auto : VisionMode.Off;
            var options = new ConversionOptions
            {
                PipelineMode = PipelineMode.Multimodal,
                VisionMode = vision,
                Privacy = new PrivacyOptions { AllowDocumentUpload = allowDocumentUpload }
            };
            var assetStore = new InMemoryAssetStore();
            var result = Engine.Value.ConvertAsync(new DocumentConversionRequest
            {
                FilePath = path,
                Options = options,
                AssetStore = assetStore,
                Context = new ConversionContext { OperationId = operationId }
            }).GetAwaiter().GetResult();
            var diagnostics = result.Diagnostics ?? Array.Empty<ConversionDiagnostic>();
            if (!AssetRegistry.TryRegister(operationId, assetStore))
                return Failed("RESOURCE_LIMIT_EXCEEDED", "MCP conversion resource quota exceeded.", operationId);
            var assetUris = result.Assets.Select(a => $"markitdown://conversion/{operationId}/assets/{a.Id}").ToArray();
            return new DetailedConversionResponse(result.FidelityStatus.ToString(), result.Markdown, assetUris, diagnostics, result.Usage);
            }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (FileNotFoundException ex)
        {
            return Failed("FILE_NOT_FOUND", ex.Message, operationId);
        }
        catch (UnsupportedFormatException ex)
        {
            return Failed("UNSUPPORTED_FORMAT", ex.Message, operationId);
        }
        catch (ConversionException ex)
        {
            return Failed(ex.FailureReport?.Diagnostics.FirstOrDefault()?.Code ?? "CONVERSION_FAILED", ex.Message, operationId);
        }
    }

    [McpServerTool, Description("Reads a temporary asset from a successful detailed conversion and returns its bytes as base64. The request never accepts a filesystem path.")]
    public static string ReadConversionAsset(
        [Description("256-bit conversion ID from the markitdown:// URI")] string conversionId,
        [Description("Asset ID from the markitdown:// URI")] string assetId)
    {
        if (!IsOpaqueId(conversionId) || string.IsNullOrWhiteSpace(assetId))
            return "Error: CONVERSION_NOT_FOUND";
        return AssetRegistry.TryRead(conversionId, assetId, out var bytes)
            ? Convert.ToBase64String(bytes)
            : "Error: CONVERSION_NOT_FOUND";
    }

    internal static bool TryReadAsset(string conversionId, string assetId, out byte[] bytes)
    {
        bytes = [];
        return IsOpaqueId(conversionId) && !string.IsNullOrWhiteSpace(assetId)
            && AssetRegistry.TryRead(conversionId, assetId, out bytes);
    }

    private static bool IsOpaqueId(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static DetailedConversionResponse Failed(string code, string message, string operationId) =>
        new("Failed", null, Array.Empty<string>(),
            [new ConversionDiagnostic(code, DiagnosticSeverity.Error, message, AffectsSubstantiveContent: true, RequiresReview: true)],
            new());

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
