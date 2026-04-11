using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDown.Core;

namespace MarkItDown.Converters.Office;

public sealed class DocxConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".docx" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("DOCX converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                using var doc = WordprocessingDocument.Open(filePath, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body is null)
                    return new DocumentConversionResult("Docx", string.Empty);

                var blocks = new List<string>();

                foreach (var element in body.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (element is Paragraph para)
                        blocks.Add(RenderParagraph(para));
                    else if (element is Table table)
                        blocks.Add(RenderTable(table));
                }

                var markdown = string.Join(Environment.NewLine + Environment.NewLine,
                    blocks.Where(b => !string.IsNullOrWhiteSpace(b))).Trim();
                return new DocumentConversionResult("Docx", markdown);
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert DOCX: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    private static string RenderParagraph(Paragraph para)
    {
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

        // Heading detection
        if (styleId is not null)
        {
            var prefix = "Heading";
            if (styleId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var levelStr = styleId[prefix.Length..];
                if (int.TryParse(levelStr, out var level) && level is >= 1 and <= 9)
                {
                    var text = RenderRuns(para);
                    return string.IsNullOrWhiteSpace(text) ? string.Empty
                        : $"{new string('#', level)} {text}";
                }
            }

            // Title style -> H1
            if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase))
            {
                var text = RenderRuns(para);
                return string.IsNullOrWhiteSpace(text) ? string.Empty : $"# {text}";
            }
        }

        // List detection
        var numbering = para.ParagraphProperties?.NumberingProperties;
        if (numbering is not null)
        {
            var text = RenderRuns(para);
            return string.IsNullOrWhiteSpace(text) ? string.Empty : $"- {text}";
        }

        // Regular paragraph
        var content = RenderRuns(para);
        return string.IsNullOrWhiteSpace(content) ? string.Empty : content;
    }

    private static string RenderRuns(Paragraph para)
    {
        var builder = new StringBuilder();

        foreach (var run in para.Elements<Run>())
        {
            var text = run.InnerText;
            if (string.IsNullOrEmpty(text)) continue;

            var isBold = run.RunProperties?.Bold is not null;
            var isItalic = run.RunProperties?.Italic is not null;

            if (isBold && isItalic)
                builder.Append($"***{text}***");
            else if (isBold)
                builder.Append($"**{text}**");
            else if (isItalic)
                builder.Append($"*{text}*");
            else
                builder.Append(text);
        }

        return builder.ToString();
    }

    private static string RenderTable(Table table)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count == 0) return string.Empty;

        var data = rows.Select(row =>
            row.Elements<TableCell>()
                .Select(cell => EscapePipe(cell.InnerText.Trim()))
                .ToList()
        ).ToList();

        var columnCount = data.Max(r => r.Count);
        if (columnCount == 0) return string.Empty;

        foreach (var row in data)
            while (row.Count < columnCount)
                row.Add(string.Empty);

        var builder = new StringBuilder();
        builder.AppendLine($"| {string.Join(" | ", data[0])} |");
        builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", columnCount))} |");

        foreach (var row in data.Skip(1))
            builder.AppendLine($"| {string.Join(" | ", row)} |");

        return builder.ToString().TrimEnd();
    }

    private static string EscapePipe(string value) =>
        value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
