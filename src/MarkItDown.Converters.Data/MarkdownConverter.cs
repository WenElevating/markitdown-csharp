using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class MarkdownConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown" };

    public override IReadOnlySet<string> SupportedMimeTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/markdown", "text/x-markdown" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("FilePath is required for Markdown conversion.");

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            return new DocumentConversionResult("Markdown", content);
        }
        catch (ConversionException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to convert Markdown: {ex.Message}", ex);
        }
    }
}
