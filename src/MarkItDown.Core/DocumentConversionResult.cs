namespace MarkItDown.Core;

public sealed record DocumentConversionResult(
    string Kind,
    string Markdown,
    string? Title = null,
    string? AssetDirectory = null,
    DocumentModel? Document = null,
    FidelityStatus Fidelity = FidelityStatus.NotEvaluated,
    IReadOnlyList<ConversionDiagnostic>? Diagnostics = null)
{
    public IReadOnlyList<AssetReference> Assets { get; init; } = Array.Empty<AssetReference>();
    public FidelityStatus FidelityStatus => Fidelity;
    public ConversionUsage Usage { get; init; } = new();
}
