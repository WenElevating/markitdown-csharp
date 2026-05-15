using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDown.Core;
using NPOI.HWPF; // for HWPFDocument (NPOI.HWPFCore package)
using A = DocumentFormat.OpenXml.Drawing;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

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
                var mainPart = doc.MainDocumentPart;
                var body = mainPart?.Document?.Body;
                if (body is null)
                    return new DocumentConversionResult("Docx", string.Empty);

                // AssetBasePath is set by the CLI to {output_stem}_files next to the output file.
                // Fall back to a sibling folder of the source if not provided.
                var imageDir = request.AssetBasePath
                    ?? Path.Combine(
                        Path.GetDirectoryName(filePath) ?? ".",
                        Path.GetFileNameWithoutExtension(filePath) + "_files");
                var imageCounter = new ImageCounter();

                var blocks = new List<string>();

                foreach (var element in body.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (element is Paragraph para)
                        blocks.Add(RenderParagraph(para, mainPart!, imageDir, imageCounter));
                    else if (element is Table table)
                        blocks.Add(RenderTable(table, mainPart!, imageDir, imageCounter));
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

    private static string RenderParagraph(
        Paragraph para, MainDocumentPart mainPart, string imageDir, ImageCounter counter)
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
                    var text = RenderRuns(para, mainPart, imageDir, counter);
                    return string.IsNullOrWhiteSpace(text) ? string.Empty
                        : $"{new string('#', level)} {text}";
                }
            }

            // Title style -> H1
            if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase))
            {
                var text = RenderRuns(para, mainPart, imageDir, counter);
                return string.IsNullOrWhiteSpace(text) ? string.Empty : $"# {text}";
            }
        }

        // List detection
        var numbering = para.ParagraphProperties?.NumberingProperties;
        if (numbering is not null)
        {
            var text = RenderRuns(para, mainPart, imageDir, counter);
            return string.IsNullOrWhiteSpace(text) ? string.Empty : $"- {text}";
        }

        // Regular paragraph
        var content = RenderRuns(para, mainPart, imageDir, counter);
        return string.IsNullOrWhiteSpace(content) ? string.Empty : content;
    }

    private static string RenderRuns(
        Paragraph para, MainDocumentPart mainPart, string imageDir, ImageCounter counter)
    {
        var builder = new StringBuilder();

        foreach (var child in para.ChildElements)
        {
            if (child is Run run)
            {
                // Check for inline image drawing inside the run
                var drawing = run.GetFirstChild<Drawing>();
                if (drawing is not null)
                {
                    var imgMd = ExtractImage(drawing, mainPart, imageDir, counter);
                    if (imgMd is not null)
                        builder.Append(imgMd);
                    continue;
                }

                // Collect text, honoring soft line breaks within the run
                foreach (var runChild in run.ChildElements)
                {
                    if (runChild is Text t)
                    {
                        var text = t.Text;
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
                    else if (runChild is Break br)
                    {
                        // Soft line break (Shift+Enter) or page/column break
                        var breakType = br.Type?.Value;
                        if (breakType is null || breakType == BreakValues.TextWrapping)
                            builder.Append('\n');
                    }
                }
            }
            else if (child is Break)
            {
                // Paragraph-level break (rare but possible)
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string RenderTable(
        Table table, MainDocumentPart mainPart, string imageDir, ImageCounter counter)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count == 0) return string.Empty;

        var columnCount = rows.Max(r => r.Elements<TableCell>().Count());
        if (columnCount == 0) return string.Empty;

        // Single-column tables are typically used for preformatted content (code, JSON, formulas).
        // Render them as fenced code blocks with paragraph line-breaks preserved.
        if (columnCount == 1)
        {
            var cellLines = new List<string>();
            foreach (var row in rows)
            {
                var cell = row.Elements<TableCell>().FirstOrDefault();
                if (cell is null) continue;
                var text = RenderCellParagraphs(cell, mainPart, imageDir, counter);
                if (!string.IsNullOrWhiteSpace(text))
                    cellLines.Add(text);
            }
            if (cellLines.Count == 0) return string.Empty;
            var content = string.Join("\n", cellLines);
            return $"```\n{content}\n```";
        }

        var data = rows.Select(row =>
            row.Elements<TableCell>()
                .Select(cell => EscapePipe(cell.InnerText.Trim()))
                .ToList()
        ).ToList();

        foreach (var row in data)
            while (row.Count < columnCount)
                row.Add(string.Empty);

        var sb = new StringBuilder();
        sb.AppendLine($"| {string.Join(" | ", data[0])} |");
        sb.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", columnCount))} |");

        foreach (var row in data.Skip(1))
            sb.AppendLine($"| {string.Join(" | ", row)} |");

        return sb.ToString().TrimEnd();
    }

    private static string RenderCellParagraphs(
        TableCell cell, MainDocumentPart mainPart, string imageDir, ImageCounter counter)
    {
        var lines = cell.Elements<Paragraph>()
            .Select(p => RenderRuns(p, mainPart, imageDir, counter))
            .Where(t => !string.IsNullOrWhiteSpace(t));
        return string.Join("\n", lines);
    }

    private static string? ExtractImage(
        Drawing drawing, MainDocumentPart mainPart, string imageDir, ImageCounter counter)
    {
        try
        {
            var blip = drawing.Descendants<A.Blip>().FirstOrDefault();
            if (blip?.Embed?.Value is not { Length: > 0 } relId)
                return null;

            if (!mainPart.TryGetPartById(relId, out var part) || part is not ImagePart imagePart)
                return null;

            var ext = imagePart.ContentType switch
            {
                "image/png"  => ".png",
                "image/jpeg" => ".jpg",
                "image/gif"  => ".gif",
                "image/bmp"  => ".bmp",
                "image/tiff" => ".tif",
                "image/webp" => ".webp",
                _            => ".bin"
            };

            Directory.CreateDirectory(imageDir);
            var fileName = $"image{counter.Next()}{ext}";
            var destPath = Path.Combine(imageDir, fileName);
            using var src = imagePart.GetStream();
            using var dst = File.Create(destPath);
            src.CopyTo(dst);

            // Alt text lives in the DocProperties element
            var docPr = drawing.Descendants<Wp.DocProperties>().FirstOrDefault();
            var alt = docPr?.Description?.Value ?? docPr?.Name?.Value ?? fileName;

            // Relative path: just {folder_name}/{file} — resolves correctly next to the output markdown.
            var relPath = Path.GetFileName(imageDir) + "/" + fileName;
            return $"![{alt}]({relPath})";
        }
        catch
        {
            return null;
        }
    }

    private static string EscapePipe(string value) =>
        value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");

    private sealed class ImageCounter
    {
        private int _n;
        public int Next() => Interlocked.Increment(ref _n);
    }
}
