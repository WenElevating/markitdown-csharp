using System.Text;
using System.Text.Json;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class IpynbConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ipynb" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/x-ipynb+json" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("IPYNB converter requires a file path.");

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var builder = new StringBuilder();

            if (root.TryGetProperty("cells", out var cells))
            {
                foreach (var cell in cells.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var cellType = cell.TryGetProperty("cell_type", out var typeEl)
                        ? typeEl.GetString() ?? "" : "";

                    var source = cell.TryGetProperty("source", out var sourceEl)
                        ? ExtractSource(sourceEl) : "";

                    if (string.IsNullOrWhiteSpace(source))
                        continue;

                    switch (cellType)
                    {
                        case "markdown":
                            builder.AppendLine(source);
                            builder.AppendLine();
                            break;
                        case "code":
                            var language = "python";
                            if (root.TryGetProperty("metadata", out var meta)
                                && meta.TryGetProperty("language_info", out var langInfo)
                                && langInfo.TryGetProperty("name", out var langName))
                            {
                                language = langName.GetString() ?? "python";
                            }
                            builder.AppendLine($"```{language}");
                            builder.AppendLine(source);
                            builder.AppendLine("```");
                            builder.AppendLine();
                            break;
                        default:
                            builder.AppendLine("```");
                            builder.AppendLine(source);
                            builder.AppendLine("```");
                            builder.AppendLine();
                            break;
                    }
                }
            }

            var markdown = builder.ToString().TrimEnd();
            return new DocumentConversionResult("Ipynb", markdown);
        }
        catch (ConversionException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to convert IPYNB: {ex.Message}", ex);
        }
    }

    private static string ExtractSource(JsonElement sourceEl)
    {
        if (sourceEl.ValueKind == JsonValueKind.Array)
            return string.Join("", sourceEl.EnumerateArray()
                .Select(e => e.GetString() ?? ""));

        return sourceEl.GetString() ?? "";
    }
}
