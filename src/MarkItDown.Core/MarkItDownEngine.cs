namespace MarkItDown.Core;

public sealed class MarkItDownEngine
{
    private readonly FileFormatClassifier _classifier;
    private readonly IReadOnlyDictionary<DocumentKind, IConverter> _converters;

    public MarkItDownEngine(IEnumerable<IConverter> converters, FileFormatClassifier? classifier = null)
    {
        _classifier = classifier ?? new FileFormatClassifier();
        _converters = converters.ToDictionary(x => x.Kind);
    }

    public static MarkItDownEngine CreateDefault()
    {
        return new MarkItDownEngine(
            new IConverter[]
            {
                new HtmlConverter(),
                new PdfConverter()
            });
    }

    public DocumentKind? Classify(string filePath) => _classifier.Classify(filePath);

    public async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            throw new ConversionException(ConversionErrorCode.InvalidInput, "A file path is required.");
        }

        if (!File.Exists(request.FilePath))
        {
            throw new ConversionException(
                ConversionErrorCode.InvalidInput,
                $"Input file was not found: {request.FilePath}");
        }

        var kind = _classifier.Classify(request.FilePath);
        if (kind is null || !_converters.TryGetValue(kind.Value, out var converter))
        {
            throw new ConversionException(
                ConversionErrorCode.UnsupportedFormat,
                $"Unsupported file format: {Path.GetExtension(request.FilePath)}");
        }

        if (!converter.CanConvert(request))
        {
            throw new ConversionException(
                ConversionErrorCode.UnsupportedFormat,
                $"No converter accepted the input file: {request.FilePath}");
        }

        return await converter.ConvertAsync(request, cancellationToken);
    }
}
