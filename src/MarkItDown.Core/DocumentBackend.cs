namespace MarkItDown.Core;

public sealed record BackendResult(
    DocumentModel Document,
    FidelityStatus Fidelity,
    IReadOnlyList<ConversionDiagnostic>? Diagnostics = null);

public interface IDocumentBackend
{
    bool CanHandle(DocumentInput input);
    Task<BackendResult> ConvertAsync(DocumentInput input, ConversionContext context, CancellationToken cancellationToken = default);
}
