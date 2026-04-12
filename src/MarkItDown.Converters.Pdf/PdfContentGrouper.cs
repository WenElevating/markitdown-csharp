using System.Text;
using System.Text.RegularExpressions;

namespace MarkItDown.Converters.Pdf;

internal static class PdfContentGrouper
{
    private static readonly Regex ColumnSplitRegex = new(@"\s{2,}", RegexOptions.Compiled);

    internal static string RenderPage(
        List<PdfContentBlock> blocks,
        double bodyFontSize)
    {
        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        var sorted = blocks.OrderByDescending(b => b.Y).ToList();
        var groups = GroupByProximity(sorted, bodyFontSize);

        var builder = new StringBuilder();
        foreach (var group in groups)
        {
            var markdown = RenderGroup(group, bodyFontSize);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(markdown);
        }

        return builder.ToString();
    }

    internal static List<List<PdfContentBlock>> GroupByProximity(
        List<PdfContentBlock> sorted,
        double bodyFontSize)
    {
        if (sorted.Count == 0) return [];

        var threshold = bodyFontSize * 1.5;
        var groups = new List<List<PdfContentBlock>>();
        var currentGroup = new List<PdfContentBlock> { sorted[0] };
        var groupTop = sorted[0].Top;
        var groupBottom = sorted[0].Bottom;

        for (var i = 1; i < sorted.Count; i++)
        {
            var block = sorted[i];
            // Gap: current group's lowest Y vs next block's highest Y
            var gap = groupBottom - block.Top;

            if (gap > -threshold)
            {
                currentGroup.Add(block);
                groupTop = Math.Max(groupTop, block.Top);
                groupBottom = Math.Min(groupBottom, block.Bottom);
            }
            else
            {
                groups.Add(currentGroup);
                currentGroup = [block];
                groupTop = block.Top;
                groupBottom = block.Bottom;
            }
        }

        groups.Add(currentGroup);
        return groups;
    }

    internal static string RenderGroup(List<PdfContentBlock> group, double bodyFontSize)
    {
        var images = group.OfType<PdfImageBlock>().ToList();
        var texts = group.OfType<PdfTextBlock>().ToList();
        var parts = new List<string>();

        // Images first
        foreach (var image in images)
        {
            parts.Add($"![image](./{image.FileName})");
        }

        // Check for table pattern among text blocks
        var tableTexts = DetectTableRows(texts);
        if (tableTexts is not null)
        {
            parts.Add(tableTexts);
            return string.Join(Environment.NewLine, parts);
        }

        // Render text blocks individually
        foreach (var text in texts)
        {
            var role = PdfTextClassifier.ClassifyRole(text.FontSize, bodyFontSize, text.Text);
            var rendered = role switch
            {
                "heading" => $"## {text.Text}",
                "caption" => $"*{text.Text}*",
                _ => text.Text
            };
            parts.Add(rendered);
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string? DetectTableRows(List<PdfTextBlock> texts)
    {
        if (texts.Count < 2) return null;

        var tableRows = new List<List<string>>();
        foreach (var text in texts)
        {
            var cells = ColumnSplitRegex
                .Split(text.Text.Trim())
                .Where(cell => !string.IsNullOrWhiteSpace(cell))
                .ToList();

            if (cells.Count >= 3)
            {
                tableRows.Add(cells);
            }
            else
            {
                break;
            }
        }

        if (tableRows.Count < 2) return null;

        var columnCount = tableRows.Max(r => r.Count);
        foreach (var row in tableRows)
        {
            while (row.Count < columnCount)
            {
                row.Add(string.Empty);
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine($"| {string.Join(" | ", tableRows[0].Select(EscapeCell))} |");
        builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", columnCount))} |");

        foreach (var row in tableRows.Skip(1))
        {
            builder.AppendLine($"| {string.Join(" | ", row.Select(EscapeCell))} |");
        }

        return builder.ToString().TrimEnd();
    }

    private static string EscapeCell(string value) => value.Replace("|", "\\|").Trim();
}
