using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using MarkItDown.Core;

namespace MarkItDown.Converters.Pdf;

public sealed class PdfConverter : BaseConverter
{
    private static readonly Regex ColumnSplitRegex = new(@"\s{2,}", RegexOptions.Compiled);

    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    public override IReadOnlySet<string> SupportedMimeTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/pdf" };

    public override Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var document = PdfDocument.Open(request.FilePath);
            var totalLetters = document.GetPages().Sum(page => page.Letters.Count);
            if (totalLetters < 20)
            {
                throw new ConversionException(
                    "Scanned or image-only PDFs are not supported in this MVP.");
            }

            var pageMarkdown = new List<string>();
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = ExtractPageText(page);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var pageContent = ConvertPageTextToMarkdown(text);
                if (!string.IsNullOrWhiteSpace(pageContent))
                {
                    pageMarkdown.Add(pageContent);
                }
            }

            var markdown = string.Join($"{Environment.NewLine}{Environment.NewLine}", pageMarkdown).Trim();
            if (string.IsNullOrWhiteSpace(markdown))
            {
                throw new ConversionException(
                    "The PDF did not contain extractable text.");
            }

            return Task.FromResult(new DocumentConversionResult("Pdf", markdown));
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConversionException("Failed to convert PDF to Markdown.", ex);
        }
    }

    private static string ExtractPageText(Page page)
    {
        var rows = page.GetWords()
            .GroupBy(word => Math.Round(word.BoundingBox.Bottom, 1))
            .OrderByDescending(group => group.Key)
            .Select(group => group.OrderBy(word => word.BoundingBox.Left).ToList())
            .ToList();

        var builder = new StringBuilder();

        foreach (var row in rows)
        {
            if (row.Count == 0)
            {
                continue;
            }

            var rowBuilder = new StringBuilder();
            Word? previous = null;

            foreach (var word in row)
            {
                if (previous is not null)
                {
                    var gap = word.BoundingBox.Left - previous.BoundingBox.Right;
                    var previousWidth = previous.BoundingBox.Width;
                    var approximateChars = Math.Max(previous.Text.Length, 1);
                    var averageCharWidth = Math.Max(previousWidth / approximateChars, 1);
                    var spaces = gap <= averageCharWidth * 3
                        ? 1
                        : Math.Max(2, (int)Math.Round(gap / (averageCharWidth * 2)));

                    rowBuilder.Append(' ', spaces);
                }

                rowBuilder.Append(word.Text);
                previous = word;
            }

            builder.AppendLine(rowBuilder.ToString().TrimEnd());
        }

        return builder.ToString().Trim();
    }

    internal static string ConvertPageTextToMarkdown(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToList();

        var builder = new StringBuilder();
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
            {
                return;
            }

            AppendBlock(builder, string.Join(" ", paragraph.Select(x => x.Trim())));
            paragraph.Clear();
        }

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                continue;
            }

            if (IsNoiseLine(line))
            {
                FlushParagraph();
                continue;
            }

            var tableRows = CollectTableRows(lines, ref index);
            if (tableRows.Count >= 2)
            {
                FlushParagraph();
                AppendBlock(builder, RenderTable(tableRows));
                continue;
            }

            if (LooksLikeHeading(line))
            {
                FlushParagraph();
                AppendBlock(builder, $"## {line}");
                continue;
            }

            if (LooksLikeListItem(line))
            {
                FlushParagraph();
                AppendBlock(builder, NormalizeListItem(line));
                continue;
            }

            paragraph.Add(line);
        }

        FlushParagraph();

        return builder.ToString().Trim();
    }

    private static bool IsNoiseLine(string line)
    {
        return Regex.IsMatch(line, @"^\d+$");
    }

    private static bool LooksLikeHeading(string line)
    {
        if (line.Length is < 3 or > 80)
        {
            return false;
        }

        if (line.EndsWith('.') || line.Contains("  "))
        {
            return false;
        }

        return char.IsLetter(line[0]) && line.Count(char.IsLetterOrDigit) >= 3 && line.Split(' ').Length <= 8;
    }

    private static bool LooksLikeListItem(string line)
    {
        return Regex.IsMatch(line, @"^(\-|\*|\d+[\.\)])\s+");
    }

    private static string NormalizeListItem(string line)
    {
        if (line.StartsWith("- ") || line.StartsWith("* "))
        {
            return $"- {line[2..].Trim()}";
        }

        return Regex.Replace(line, @"^\d+[\.\)]\s+", "- ");
    }

    private static List<List<string>> CollectTableRows(IReadOnlyList<string> lines, ref int index)
    {
        var originalIndex = index;
        var rows = new List<List<string>>();

        while (index < lines.Count)
        {
            var current = lines[index].TrimEnd();
            if (string.IsNullOrWhiteSpace(current))
            {
                break;
            }

            var cells = ColumnSplitRegex
                .Split(current.Trim())
                .Where(cell => !string.IsNullOrWhiteSpace(cell))
                .ToList();

            if (cells.Count < 3)
            {
                break;
            }

            rows.Add(cells);
            index++;
        }

        if (rows.Count < 2)
        {
            index = originalIndex;
            return [];
        }

        index--;
        var columnCount = rows.Max(r => r.Count);
        if (columnCount < 3)
        {
            index = originalIndex;
            return [];
        }

        var baselineColumns = rows[0].Count;
        if (rows.Any(row => Math.Abs(row.Count - baselineColumns) > 1))
        {
            index = originalIndex;
            return [];
        }

        foreach (var row in rows)
        {
            while (row.Count < columnCount)
            {
                row.Add(string.Empty);
            }
        }

        return rows;
    }

    private static string RenderTable(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"| {string.Join(" | ", rows[0].Select(EscapeCell))} |");
        builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", rows[0].Count))} |");

        foreach (var row in rows.Skip(1))
        {
            builder.AppendLine($"| {string.Join(" | ", row.Select(EscapeCell))} |");
        }

        return builder.ToString().TrimEnd();
    }

    private static string EscapeCell(string value) => value.Replace("|", "\\|").Trim();

    private static void AppendBlock(StringBuilder builder, string block)
    {
        if (string.IsNullOrWhiteSpace(block))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.Append(block.Trim());
    }
}
