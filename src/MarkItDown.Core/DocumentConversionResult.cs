namespace MarkItDown.Core;

public sealed record DocumentConversionResult(
    DocumentKind Kind,
    string Markdown,
    string? Title = null);
