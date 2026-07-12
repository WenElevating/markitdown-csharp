using MarkItDown.Core;

namespace MarkItDown.McpServer;

public sealed record DetailedConversionResponse(
    string Status,
    string? Markdown,
    IReadOnlyList<string> AssetUris,
    IReadOnlyList<ConversionDiagnostic> Diagnostics,
    ConversionUsage Usage);
