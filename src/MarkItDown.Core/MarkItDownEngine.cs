using System.Reflection;

namespace MarkItDown.Core;

public sealed class MarkItDownEngine
{
    private readonly ConverterRegistry _registry;

    public MarkItDownEngine(Action<ConverterRegistryBuilder> configure)
    {
        var builder = new ConverterRegistryBuilder();
        configure(builder);
        _registry = builder.Build();
    }

    public static MarkItDownEngine CreateWithAllConverters()
    {
        return new MarkItDownEngine(builder =>
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    builder.AddFromAssembly(assembly);
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that can't be loaded
                }
                catch (MissingMethodException)
                {
                    // Skip types without parameterless constructors
                }
            }
        });
    }

    public async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return await ConvertCoreAsync(request, cancellationToken);
    }

    public async Task<DocumentConversionResult> ConvertAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A file path is required.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"Input file was not found: {filePath}", filePath);
        }

        var request = new DocumentConversionRequest { FilePath = filePath };
        return await ConvertCoreAsync(request, cancellationToken);
    }

    public async Task<DocumentConversionResult> ConvertAsync(
        Stream stream,
        string? filename = null,
        string? mimeType = null,
        CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (string.IsNullOrWhiteSpace(filename) && string.IsNullOrWhiteSpace(mimeType))
        {
            throw new ArgumentException(
                "Stream input requires at least a filename or MIME type hint.");
        }

        var request = new DocumentConversionRequest
        {
            Stream = stream,
            Filename = filename,
            MimeType = mimeType
        };

        return await ConvertCoreAsync(request, cancellationToken);
    }

    private async Task<DocumentConversionResult> ConvertCoreAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken)
    {
        var options = request.Options ?? request.Context?.Options ?? new ConversionOptions();
        ValidateOptions(request, options);
        var resolvedVision = options.VisionMode ??
            (request.VisionAnalyzer is not null || request.LlmClient is not null ? VisionMode.Auto : VisionMode.Off);
        var context = request.Context ?? new ConversionContext
        {
            Input = new DocumentInput(
                request.FilePath,
                request.Filename ?? (request.FilePath is null ? null : Path.GetFileName(request.FilePath)),
                request.MimeType,
                request.Stream?.CanSeek == true ? request.Stream.Length : null)
        };
        context = context with
        {
            Input = context.Input ?? new DocumentInput(
                request.FilePath,
                request.Filename ?? (request.FilePath is null ? null : Path.GetFileName(request.FilePath)),
                request.MimeType,
                request.Stream?.CanSeek == true ? request.Stream.Length : null),
            Options = options,
            Pipeline = options.PipelineMode,
            Vision = resolvedVision,
            VisionAnalyzer = request.VisionAnalyzer ?? context.VisionAnalyzer ??
                (options.PipelineMode == PipelineMode.Multimodal && request.LlmClient is not null
                    ? new LlmVisionAnalyzerAdapter(request.LlmClient)
                    : null),
            Privacy = options.Privacy.AllowDocumentUpload ? PrivacyMode.AllowConfiguredServices : context.Privacy,
            Assets = request.AssetStore ?? context.Assets
        };
        var rawAssetTransaction = request.AssetStore?.BeginTransaction();
        await using var assetTransaction = rawAssetTransaction is null
            ? null
            : new LimitedAssetTransaction(rawAssetTransaction, options.Limits);
        context = context with { AssetTransaction = assetTransaction };
        if (context.MaxBytes is { } maxBytes && request.Stream?.CanSeek == true && request.Stream.Length > maxBytes)
            throw new ConversionException($"Input exceeds the configured byte limit of {maxBytes}.");
        var inputPath = request.FilePath;
        var configuredMaxBytes = context.MaxBytes ?? context.Options.Limits.MaxInputBytes;
        if (!string.IsNullOrWhiteSpace(inputPath) && File.Exists(inputPath) && new FileInfo(inputPath).Length > configuredMaxBytes)
            throw new ConversionException($"Input exceeds the configured byte limit of {configuredMaxBytes}.");

        using var timeout = context.Timeout > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        timeout?.CancelAfter(context.Timeout);
        var effectiveCancellationToken = timeout?.Token ?? cancellationToken;
        request = request with { Context = context };
        var converter = _registry.FindConverter(request);

        if (converter is null)
        {
            var extension = Path.GetExtension(request.Filename ?? request.FilePath);
            throw new UnsupportedFormatException(
                $"No converter registered for format '{extension}'.");
        }

        try
        {
            var result = await converter.ConvertAsync(request, effectiveCancellationToken);
            if (assetTransaction is not null)
            {
                await assetTransaction.CommitAsync(effectiveCancellationToken);
            }
            if (request.AssetStore is not null && result.Assets.Count == 0)
                result = result with { Assets = request.AssetStore.Assets.ToArray() };
            return ApplyPipelineStatus(result, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ConversionException ex)
        {
            if (context.Pipeline != PipelineMode.Multimodal || ex.FailureReport is not null)
                throw;
            throw new ConversionException(ex.Message, ex)
            {
                FailureReport = ConversionFailureReport.Create("CONVERSION_FAILED", ex.Message, context.OperationId)
            };
        }
        catch (Exception ex)
        {
            var filename = request.Filename ?? request.FilePath ?? "stream";
            var failure = new ConversionException(
                $"Failed to convert '{filename}': {ex.Message}", ex)
            {
                FailureReport = context.Pipeline == PipelineMode.Multimodal
                    ? ConversionFailureReport.Create("CONVERSION_FAILED", ex.Message, context.OperationId)
                    : null
            };
            throw failure;
        }
    }

    private static void ValidateOptions(DocumentConversionRequest request, ConversionOptions options)
    {
        var hasMultimodalOnlyOption = options.VisionMode is not null
            || request.AssetStore is not null
            || request.VisionAnalyzer is not null
            || options.Privacy.AllowDocumentUpload;
        if (options.PipelineMode == PipelineMode.Legacy && hasMultimodalOnlyOption)
            throw new ArgumentException("Multimodal options require PipelineMode.Multimodal.", nameof(request));
        if (options.PipelineMode == PipelineMode.Multimodal && request.AssetStore is not null && !string.IsNullOrWhiteSpace(request.AssetBasePath))
            throw new ArgumentException("AssetStore and AssetBasePath cannot both be configured.", nameof(request));
        if (options.PipelineMode == PipelineMode.Multimodal && options.VisionMode == VisionMode.Off && options.Privacy.AllowDocumentUpload)
            throw new ArgumentException("AllowDocumentUpload cannot be enabled when VisionMode is Off.", nameof(request));
        if (options.PipelineMode == PipelineMode.Multimodal && request.VisionAnalyzer is not null && request.LlmClient is not null)
            throw new ArgumentException("VisionAnalyzer and LlmClient cannot both be configured.", nameof(request));
    }

    private static DocumentConversionResult ApplyPipelineStatus(DocumentConversionResult result, ConversionContext context)
    {
        if (context.Pipeline != PipelineMode.Multimodal || result.Kind is "Pdf" or "Docx") return result;
        var diagnostics = (result.Diagnostics ?? Array.Empty<ConversionDiagnostic>()).ToList();
        if (diagnostics.All(d => d.Code != "MULTIMODAL_FORMAT_UNSUPPORTED"))
        {
            diagnostics.Add(new ConversionDiagnostic(
                "MULTIMODAL_FORMAT_UNSUPPORTED", DiagnosticSeverity.Warning,
                $"The {result.Kind} converter is not yet part of the shared multimodal pipeline.",
                AffectsSubstantiveContent: true, Backend: result.Kind, RequiresReview: true));
        }
        if (context.Vision == VisionMode.Required)
        {
            throw new ConversionException($"Multimodal conversion is not supported for {result.Kind}.")
            {
                FailureReport = ConversionFailureReport.Create(
                    "MULTIMODAL_FORMAT_UNSUPPORTED",
                    $"The {result.Kind} converter is not yet part of the shared multimodal pipeline.",
                    context.OperationId, result.Kind)
            };
        }
        return result with { Fidelity = FidelityStatus.NotEvaluated, Diagnostics = diagnostics };
    }
}
