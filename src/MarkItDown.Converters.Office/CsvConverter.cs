using System.Globalization;
using System.Text;
using CsvHelper;
using MarkItDown.Core;

namespace MarkItDown.Converters.Office;

public sealed class CsvConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".csv" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/csv", "text/comma-separated-values" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var filePath = request.FilePath
            ?? throw new ConversionException("CSV converter requires a file path.");

        try
        {
            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            if (!csv.Read())
                return new DocumentConversionResult("Csv", string.Empty);

            csv.ReadHeader();

            var headers = csv.HeaderRecord;
            if (headers is null || headers.Length == 0)
                return new DocumentConversionResult("Csv", string.Empty);

            var builder = new StringBuilder();
            builder.AppendLine($"| {string.Join(" | ", headers)} |");
            builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", headers.Length))} |");

            while (csv.Read())
            {
                var fields = new List<string>();
                for (var i = 0; i < headers.Length; i++)
                {
                    var field = csv.GetField(i) ?? string.Empty;
                    fields.Add(EscapePipe(field));
                }
                builder.AppendLine($"| {string.Join(" | ", fields)} |");
            }

            var markdown = builder.ToString().TrimEnd();
            return new DocumentConversionResult("Csv", markdown);
        }
        catch (ConversionException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to convert CSV: {ex.Message}", ex);
        }
    }

    private static string EscapePipe(string value) =>
        value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
