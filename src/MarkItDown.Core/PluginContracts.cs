namespace MarkItDown.Core;

public interface IMarkItDownPlugin
{
    string Id { get; }
    string Version { get; }
    IReadOnlyCollection<string> Capabilities { get; }

    void Register(MarkItDownPluginContext context);
}

public interface IOcrProvider
{
    string Id { get; }

    bool IsAvailable(ConversionContext context);

    Task<OcrDocumentResult> RecognizeAsync(
        OcrRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record OcrRequest(
    Stream Image,
    string? Filename = null,
    string? MimeType = null,
    ConversionContext? Context = null);

public sealed record OcrDocumentResult(
    DocumentModel Document,
    double? Confidence = null,
    string? Provider = null,
    IReadOnlyList<ConversionDiagnostic>? Diagnostics = null);

public sealed class MarkItDownPluginContext
{
    private readonly List<IOcrProvider> _ocrProviders = [];

    public void RegisterOcrProvider(IOcrProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(provider.Id))
            throw new ArgumentException("OCR provider id is required.", nameof(provider));
        if (_ocrProviders.Any(p => string.Equals(p.Id, provider.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"An OCR provider with id '{provider.Id}' is already registered.");

        _ocrProviders.Add(provider);
    }

    public IReadOnlyList<IOcrProvider> OcrProviders => _ocrProviders.AsReadOnly();
}
