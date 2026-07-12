namespace MarkItDown.Core;

public sealed class DocumentBackendRouter(IEnumerable<IDocumentBackend> backends)
{
    private readonly IReadOnlyList<IDocumentBackend> _backends = backends.ToArray();

    public IDocumentBackend? Select(DocumentInput input, ConversionBackendMode mode)
    {
        if (mode == ConversionBackendMode.Native) return _backends.FirstOrDefault(backend => backend.CanHandle(input));
        return _backends.FirstOrDefault(backend => backend.CanHandle(input));
    }

    public async Task<BackendResult> ConvertAsync(DocumentInput input, ConversionContext context, CancellationToken cancellationToken = default)
    {
        var backend = Select(input, context.Backend)
            ?? throw new UnsupportedFormatException($"No document backend can handle '{input.Filename ?? input.FilePath}'.");
        return await backend.ConvertAsync(input, context, cancellationToken);
    }
}
