namespace MarkItDown.Core;

public sealed class ConverterDocumentBackend(IConverter converter) : IDocumentBackend
{
    public bool CanHandle(DocumentInput input)
    {
        var request = new DocumentConversionRequest
        {
            FilePath = input.FilePath,
            Filename = input.Filename,
            MimeType = input.MimeType
        };
        return converter.CanConvert(request);
    }

    public async Task<BackendResult> ConvertAsync(DocumentInput input, ConversionContext context, CancellationToken cancellationToken = default)
    {
        var request = new DocumentConversionRequest
        {
            FilePath = input.FilePath,
            Filename = input.Filename,
            MimeType = input.MimeType,
            Options = context.Options,
            Context = context,
            AssetStore = context.Assets,
            VisionAnalyzer = context.VisionAnalyzer
        };
        var result = await converter.ConvertAsync(request, cancellationToken);
        var document = result.Document ?? DocumentModelBuilder.FromMarkdown(result.Kind, result.Markdown, result.FidelityStatus, result.Diagnostics);
        return new BackendResult(document, result.FidelityStatus, result.Diagnostics);
    }
}
