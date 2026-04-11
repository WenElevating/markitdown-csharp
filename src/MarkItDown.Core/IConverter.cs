namespace MarkItDown.Core;

public interface IConverter
{
    DocumentKind Kind { get; }

    bool CanConvert(DocumentConversionRequest request);

    Task<DocumentConversionResult> ConvertAsync(DocumentConversionRequest request, CancellationToken cancellationToken = default);
}
