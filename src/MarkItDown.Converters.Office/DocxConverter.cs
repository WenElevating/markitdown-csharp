using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDown.Core;
using NPOI.HWPF; // for HWPFDocument (NPOI.HWPFCore package)

// Note: Encoding.RegisterProvider is called by DocConverter's static ctor.
// If DocxConverter OLE2 fallback is used independently, we register here too.

namespace MarkItDown.Converters.Office;

public sealed class DocxConverter : BaseConverter
{
    static DocxConverter()
    {
        // Required for .NET Core/5+: NPOI HWPF OLE2 fallback needs Windows code pages
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

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
            // Read OLE2 magic once; reused in the exception filter to avoid a second file open.
            var isOle2 = DocRendering.IsOle2File(filePath);

            if (isOle2)
                return ConvertAsHwpf(filePath, cancellationToken);

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
            catch (OperationCanceledException) { throw; }
            catch (Exception) when (isOle2)
            {
                // Secondary fallback: OpenXml threw but it really is an OLE2 file
                return ConvertAsHwpf(filePath, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert DOCX: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Fallback path: parse an OLE2 binary .doc file using NPOI HWPF.
    /// Transparent to the caller — still returns a "Docx" kind result.
    /// </summary>
    private static DocumentConversionResult ConvertAsHwpf(
        string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var doc = new HWPFDocument(fs);
            var markdown = DocRendering.RenderDocument(doc, cancellationToken);
            return new DocumentConversionResult("Docx", markdown);
        }
        catch (ConversionException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException(
                $"Failed to convert OLE2/WPS DOCX file '{filePath}': {ex.Message}", ex);
        }
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
