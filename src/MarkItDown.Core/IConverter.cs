namespace MarkItDown.Core;

public interface IConverter
{
    IReadOnlySet<string> SupportedExtensions { get; }
    IReadOnlySet<string> SupportedMimeTypes { get; }
    double Priority { get; }
    bool CanConvert(DocumentConversionRequest request);
    Task<DocumentConversionResult> ConvertAsync(DocumentConversionRequest request, CancellationToken cancellationToken = default);
}
