using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using M = DocumentFormat.OpenXml.Math;
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
        return await Task.Run(async () =>
        {
            try
            {
                using var input = DocumentInputMaterializer.Materialize(request, cancellationToken);
                OfficePackageGuard.Validate(input.FilePath,
                    request.Context?.Options.Limits ?? new ConversionLimits(),
                    request.Context?.Options.Privacy.AllowExternalRelationships == true);
                using var doc = WordprocessingDocument.Open(input.FilePath, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body is null)
                {
                    var emptyFidelity = request.Context?.Pipeline == PipelineMode.Multimodal
                        ? FidelityStatus.Complete : FidelityStatus.NotEvaluated;
                    var model = new DocumentModel("Docx", [], Fidelity: emptyFidelity);
                    return await OfficeDoclingEnhancer.EnhanceAsync(
                        "Docx", request, input.FilePath, model, cancellationToken);
                }

                var blocks = new List<string>();

                foreach (var element in body.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (element is Paragraph para)
                        blocks.Add(RenderParagraph(para));
                    else if (element is Table table)
                        blocks.Add(RenderTable(table));
                }

                var images = OfficeAssetExtractor.Extract(doc.MainDocumentPart?.ImageParts ?? [], request);
                if (!string.IsNullOrWhiteSpace(images)) blocks.Add(images);

                AppendSupplement(blocks, "Headers", doc.MainDocumentPart?.HeaderParts.SelectMany(p => p.Header?.Descendants<Paragraph>() ?? []));
                AppendSupplement(blocks, "Footers", doc.MainDocumentPart?.FooterParts.SelectMany(p => p.Footer?.Descendants<Paragraph>() ?? []));
                AppendSupplement(blocks, "Footnotes", doc.MainDocumentPart?.FootnotesPart?.Footnotes?.Descendants<Paragraph>());
                AppendSupplement(blocks, "Endnotes", doc.MainDocumentPart?.EndnotesPart?.Endnotes?.Descendants<Paragraph>());
                AppendSupplement(blocks, "Review comments", doc.MainDocumentPart?.WordprocessingCommentsPart?.Comments?.Descendants<Comment>());

                var markdown = string.Join(Environment.NewLine + Environment.NewLine,
                    blocks.Where(b => !string.IsNullOrWhiteSpace(b))).Trim();
                var fidelity = request.Context?.Pipeline == PipelineMode.Multimodal
                    ? FidelityStatus.Complete
                    : FidelityStatus.NotEvaluated;
                var document = DocumentModelBuilder.FromMarkdown("Docx", markdown, fidelity);
                return await OfficeDoclingEnhancer.EnhanceAsync(
                    "Docx", request, input.FilePath, document, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
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
        var equations = para.Descendants<M.OfficeMath>()
            .Select(math => $"\\({math.InnerText.Trim()}\\)")
            .Where(text => text.Length > 4);
        content = string.Join(string.Empty, new[] { content }.Concat(equations));
        return string.IsNullOrWhiteSpace(content) ? string.Empty : content;
    }

    private static string RenderRuns(Paragraph para)
    {
        var builder = new StringBuilder();

        foreach (var run in para.Descendants<Run>())
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

        var complex = table.Descendants<Table>().Any()
            || table.Descendants<TableCellProperties>().Any(properties => properties.GridSpan is not null || properties.VerticalMerge is not null);

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

        if (complex)
        {
            var html = new StringBuilder("<table>\n");
            html.AppendLine("  <tbody>");
            foreach (var row in data)
            {
                html.Append("    <tr>");
                foreach (var cell in row)
                    html.Append("<td>").Append(System.Net.WebUtility.HtmlEncode(cell.Replace("\\|", "|"))).Append("</td>");
                html.AppendLine("</tr>");
            }
            html.AppendLine("  </tbody>").Append("</table>");
            return html.ToString().TrimEnd();
        }

        var builder = new StringBuilder();
        builder.AppendLine($"| {string.Join(" | ", data[0])} |");
        builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", columnCount))} |");

        foreach (var row in data.Skip(1))
            builder.AppendLine($"| {string.Join(" | ", row)} |");

        return builder.ToString().TrimEnd();
    }

    private static string EscapePipe(string value) =>
        value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");

    private static void AppendSupplement<T>(ICollection<string> blocks, string title, IEnumerable<T>? elements)
        where T : OpenXmlElement
    {
        if (elements is null) return;
        var text = elements.Select(e => e.InnerText.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (text.Count == 0) return;
        blocks.Add($"## {title}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, text)}");
    }
}
