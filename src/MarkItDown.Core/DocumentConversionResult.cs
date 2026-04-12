namespace MarkItDown.Core;

public sealed record DocumentConversionResult(
    string Kind,
    string Markdown,
    string? Title = null,
    string? AssetDirectory = null);
