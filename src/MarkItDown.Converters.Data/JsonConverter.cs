using System.Text.Json;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class JsonConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json" };

    public override IReadOnlySet<string> SupportedMimeTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/json" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("FilePath is required for JSON conversion.");

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);

            using var document = JsonDocument.Parse(content);
            var formatted = JsonSerializer.Serialize(document, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var markdown = $"```json{Environment.NewLine}{formatted}{Environment.NewLine}```";
            return new DocumentConversionResult("Json", markdown);
        }
        catch (ConversionException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to convert JSON: {ex.Message}", ex);
        }
    }
}
