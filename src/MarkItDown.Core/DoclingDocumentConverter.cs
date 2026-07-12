namespace MarkItDown.Core;

public static class DoclingDocumentConverter
{
    public static async Task<DocumentConversionResult> ConvertFileAsync(
        string kind,
        DocumentConversionRequest request,
        IDoclingTransport transport,
        CancellationToken cancellationToken = default)
    {
        var path = request.FilePath ?? throw new ConversionException("Docling conversion requires a file path.");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var response = await transport.SendAsync(new DoclingRequest(
            Guid.NewGuid().ToString("N"), "1", Path.GetFileName(path), request.MimeType,
            Convert.ToBase64String(bytes)), cancellationToken);
        if (!response.Success || string.IsNullOrWhiteSpace(response.DocumentJson))
            throw new ConversionException($"Docling conversion failed: {response.ErrorCode} {response.ErrorMessage}");
        var document = DoclingDocumentAdapter.Adapt(response.DocumentJson, kind);
        var markdown = MarkdownRenderer.Render(document);
        return new DocumentConversionResult(kind, markdown, Document: document,
            Fidelity: document.Fidelity,
            Diagnostics: document.Diagnostics)
        {
            Usage = new ConversionUsage { InputBytes = bytes.LongLength }
        };
    }
}
