using System.Text.Json;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class JsonlConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jsonl" };

    public override IReadOnlySet<string> SupportedMimeTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/jsonl", "application/x-jsonlines" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("FilePath is required for JSONL conversion.");

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);

            if (string.IsNullOrWhiteSpace(content))
            {
                return new DocumentConversionResult("Jsonl", string.Empty);
            }

            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var formattedLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(trimmed);
                var formatted = JsonSerializer.Serialize(document, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                formattedLines.Add(formatted);
            }

            var inner = string.Join(Environment.NewLine + Environment.NewLine, formattedLines);
            var markdown = $"```jsonl{Environment.NewLine}{inner}{Environment.NewLine}```";
            return new DocumentConversionResult("Jsonl", markdown);
        }
        catch (ConversionException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to convert JSONL: {ex.Message}", ex);
        }
    }
}
