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

        // Step 1: Determine reading order via projection-profile algorithm.
        var ordered = PdfLayoutAnalyzer.AnalyzeReadingOrder(blocks, bodyFontSize);

        // Merge consecutive body paragraphs.
        ordered = PdfLayoutAnalyzer.MergeParagraphs(ordered, bodyFontSize);

        // Step 2: Detect captions (indices into ordered list).
        var captionIndices = PdfLayoutAnalyzer.DetectCaptions(ordered, bodyFontSize);

        // Step 3: Detect list ranges (indices into ordered list).
        var listRanges = PdfLayoutAnalyzer.DetectLists(ordered);

        // Step 3b: Detect table among text blocks.
        var textBlocks = ordered.OfType<PdfTextBlock>().ToList();
        var textBlockIndices = new List<int>();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i] is PdfTextBlock)
            {
                textBlockIndices.Add(i);
            }
        }

        var (tableTextStart, tableTextLength, tableMarkdown) = DetectTable(textBlocks);
        var tableStartIndex = tableMarkdown is not null ? textBlockIndices[tableTextStart] : -1;
        var tableEndIndex = tableMarkdown is not null ? textBlockIndices[tableTextStart + tableTextLength - 1] : -1;

        // Step 4: Render each block in reading order.
        var builder = new StringBuilder();
        var tableEmitted = false;

        for (var i = 0; i < ordered.Count; i++)
        {
            var block = ordered[i];

            // Skip headers/footers entirely.
            if (block.IsHeaderFooter)
            {
                continue;
            }

            // Skip individual text blocks that are part of a detected table.
            if (tableMarkdown is not null && i >= tableStartIndex && i <= tableEndIndex)
            {
                // Emit the full table markdown once at the first table row.
                if (!tableEmitted)
                {
                    tableEmitted = true;
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                        builder.AppendLine();
                    }
                    builder.Append(tableMarkdown);
                }
                continue;
            }

            var rendered = block switch
            {
                PdfImageBlock image => RenderImage(image, assetDirName),
                PdfTextBlock text => RenderText(
                    text, i, bodyFontSize, captionIndices, listRanges),
                _ => null
            };

            if (rendered is null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(rendered);
        }

        return builder.ToString();
    }

    private static string RenderImage(PdfImageBlock image, string? assetDirName)
    {
        var imagePath = string.IsNullOrEmpty(assetDirName)
            ? $"./{image.FileName}"
            : $"./{assetDirName}/{image.FileName}";
        return $"![image]({imagePath})";
    }

    private static string RenderText(
        PdfTextBlock text,
        int index,
        double bodyFontSize,
        HashSet<int> captionIndices,
        List<(int Start, int Length)> listRanges)
    {
        // Caption: italic text.
        if (captionIndices.Contains(index))
        {
            return $"*{text.Text}*";
        }

        // List item: strip marker and render as unordered list.
        if (IsInListRange(index, listRanges))
        {
            return $"- {StripListMarker(text.Text)}";
        }

        // Heading: font-size-based classification.
        var role = PdfTextClassifier.ClassifyRole(text.FontSize, bodyFontSize, text.Text, text.IsBold);
        if (role == "heading")
        {
            return $"## {text.Text}";
        }

        return text.Text;
    }

    private static bool IsInListRange(int index, List<(int Start, int Length)> listRanges)
    {
        foreach (var range in listRanges)
        {
            if (index >= range.Start && index < range.Start + range.Length)
            {
                return true;
            }
        }

        return false;
    }

    private static string StripListMarker(string text)
    {
        var match = Regex.Match(text, @"^(\d+[.)、]|[•·●◇◆\-–—])\s*(.*)$");
        return match.Success ? match.Groups[2].Value : text;
    }

    internal static (int start, int length, string? markdown) DetectTable(
        List<PdfTextBlock> texts)
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
