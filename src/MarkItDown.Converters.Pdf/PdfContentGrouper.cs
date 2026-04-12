using System.Text;
using System.Text.RegularExpressions;

namespace MarkItDown.Converters.Pdf;

internal static class PdfContentGrouper
{
    private static readonly Regex ColumnSplitRegex = new(@"\s{2,}", RegexOptions.Compiled);

    internal static string RenderPage(
        List<PdfContentBlock> blocks,
        double bodyFontSize,
        string? assetDirName = null)
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
            var markdown = RenderGroup(group, bodyFontSize, assetDirName);
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

    internal static string RenderGroup(List<PdfContentBlock> group, double bodyFontSize, string? assetDirName = null)
    {
        var images = group.OfType<PdfImageBlock>().ToList();
        var texts = group.OfType<PdfTextBlock>().ToList();
        var parts = new List<string>();

        // Images first — prefix with asset directory name if provided
        foreach (var image in images)
        {
            var imagePath = string.IsNullOrEmpty(assetDirName)
                ? $"./{image.FileName}"
                : $"./{assetDirName}/{image.FileName}";
            parts.Add($"![image]({imagePath})");
        }

        // Check for table pattern among text blocks
        var (tableStart, tableLength, tableMarkdown) = DetectTableRows(texts);

        // Render text blocks before the table
        for (var i = 0; i < texts.Count; i++)
        {
            if (tableMarkdown is not null && i >= tableStart && i < tableStart + tableLength)
            {
                if (i == tableStart)
                {
                    parts.Add(tableMarkdown);
                }
                continue;
            }

            var text = texts[i];
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

    private static (int start, int length, string? markdown) DetectTableRows(List<PdfTextBlock> texts)
    {
        if (texts.Count < 2) return (0, 0, null);

        // Find the longest contiguous run of rows with 3+ columns.
        var bestStart = -1;
        var bestLength = 0;
        var currentStart = -1;
        var currentLength = 0;

        for (var i = 0; i < texts.Count; i++)
        {
            var cells = ColumnSplitRegex
                .Split(texts[i].Text.Trim())
                .Where(cell => !string.IsNullOrWhiteSpace(cell))
                .ToList();

            if (cells.Count >= 3)
            {
                if (currentStart < 0) currentStart = i;
                currentLength++;
            }
            else
            {
                if (currentLength > bestLength)
                {
                    bestStart = currentStart;
                    bestLength = currentLength;
                }
                currentStart = -1;
                currentLength = 0;
            }
        }

        // Check final run
        if (currentLength > bestLength)
        {
            bestStart = currentStart;
            bestLength = currentLength;
        }

        if (bestLength < 2) return (0, 0, null);

        var tableRows = new List<List<string>>();
        for (var i = bestStart; i < bestStart + bestLength; i++)
        {
            var cells = ColumnSplitRegex
                .Split(texts[i].Text.Trim())
                .Where(cell => !string.IsNullOrWhiteSpace(cell))
                .ToList();
            tableRows.Add(cells);
        }

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

        return (bestStart, bestLength, builder.ToString().TrimEnd());
    }

    private static string EscapeCell(string value) => value.Replace("|", "\\|").Trim();
}
