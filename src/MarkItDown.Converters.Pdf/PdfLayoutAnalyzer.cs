using System.Text.RegularExpressions;

namespace MarkItDown.Converters.Pdf;

/// <summary>
/// Projection-profile algorithm for determining reading order of PDF content blocks.
/// Uses XY-cut (recursive projection) to detect rows (Y-axis bands) and columns
/// (X-axis bands), producing a natural reading order: top-to-bottom within each row,
/// left-to-right across columns.
/// </summary>
internal static class PdfLayoutAnalyzer
{
    /// <summary>
    /// Fraction of page width above which an element is considered "full-width"
    /// and should be extracted into its own row rather than merged into a column.
    /// </summary>
    private const double FullWidthThreshold = 0.8;

    /// <summary>
    /// Determines the reading order of content blocks using projection profiles.
    /// Full-width elements (headings, images spanning &gt;80% of page) are placed
    /// in their own row; remaining blocks are split into Y-axis bands (rows),
    /// then each row is split into X-axis columns (left to right).
    /// </summary>
    internal static List<PdfContentBlock> AnalyzeReadingOrder(
        List<PdfContentBlock> blocks,
        double bodyFontSize)
    {
        if (blocks.Count == 0)
        {
            return [];
        }

        if (blocks.Count == 1)
        {
            return [blocks[0]];
        }

        // Determine page width from the widest block.
        var pageWidth = blocks.Max(b => b.Right - b.Left);
        var fullWidthLimit = pageWidth * FullWidthThreshold;

        // Separate full-width elements from columnar content.
        var fullWidthElements = new List<PdfContentBlock>();
        var columnarBlocks = new List<PdfContentBlock>();

        foreach (var block in blocks)
        {
            var blockWidth = block.Right - block.Left;
            if (blockWidth > fullWidthLimit)
            {
                fullWidthElements.Add(block);
            }
            else
            {
                columnarBlocks.Add(block);
            }
        }

        // Project Y-axis on columnar blocks to get horizontal bands (rows).
        var minGap = bodyFontSize * 0.5;
        var bands = ProjectYAxis(columnarBlocks, minGap);

        // Merge full-width elements into the band sequence by Top coordinate.
        // Each full-width element becomes its own single-element band.
        foreach (var element in fullWidthElements)
        {
            bands.Add([element]);
        }

        // Sort all bands by Top descending (PDF coordinates: higher Y = higher on page).
        bands.Sort((a, b) => b[0].Top.CompareTo(a[0].Top));

        // Within each band, project X-axis to get columns, then sort columns
        // left to right, and within each column sort blocks by Top descending.
        var result = new List<PdfContentBlock>(blocks.Count);

        foreach (var band in bands)
        {
            var columns = ProjectXAxis(band, minGap);

            foreach (var column in columns)
            {
                var sorted = column.OrderByDescending(b => b.Top).ToList();
                result.AddRange(sorted);
            }
        }

        return result;
    }

    /// <summary>
    /// Projects blocks onto the Y-axis to detect horizontal bands (rows).
    /// Blocks with overlapping Y ranges are merged into the same band.
    /// Bands are sorted by Top descending (top of page first).
    /// </summary>
    internal static List<List<PdfContentBlock>> ProjectYAxis(
        List<PdfContentBlock> blocks,
        double minGap)
    {
        if (blocks.Count == 0)
        {
            return [];
        }

        // Build initial Y-intervals from each block.
        var intervals = new List<(double Top, double Bottom, int Index)>();
        for (var i = 0; i < blocks.Count; i++)
        {
            intervals.Add((blocks[i].Top, blocks[i].Bottom, i));
        }

        // Sort by Top descending.
        intervals.Sort((a, b) => b.Top.CompareTo(a.Top));

        // Merge overlapping or close-enough Y ranges into bands.
        var mergedBands = new List<(double Top, double Bottom)>();
        var bandTop = intervals[0].Top;
        var bandBottom = intervals[0].Bottom;

        for (var i = 1; i < intervals.Count; i++)
        {
            var interval = intervals[i];
            // If the gap between current band and this interval is small enough, merge.
            var gap = bandBottom - interval.Top;
            if (gap > -minGap)
            {
                bandTop = Math.Max(bandTop, interval.Top);
                bandBottom = Math.Min(bandBottom, interval.Bottom);
            }
            else
            {
                mergedBands.Add((bandTop, bandBottom));
                bandTop = interval.Top;
                bandBottom = interval.Bottom;
            }
        }

        mergedBands.Add((bandTop, bandBottom));

        // Sort bands by Top descending.
        mergedBands.Sort((a, b) => b.Top.CompareTo(a.Top));

        // Assign each block to the band it overlaps with.
        var result = new List<List<PdfContentBlock>>(mergedBands.Count);
        foreach (var _ in mergedBands)
        {
            result.Add([]);
        }

        foreach (var block in blocks)
        {
            for (var i = 0; i < mergedBands.Count; i++)
            {
                var range = mergedBands[i];
                // Block overlaps band if its range intersects the band range.
                if (block.Top >= range.Bottom && block.Bottom <= range.Top)
                {
                    result[i].Add(block);
                    break;
                }
            }
        }

        // Remove empty bands (can happen if no block matched).
        result.RemoveAll(b => b.Count == 0);

        return result;
    }

    /// <summary>
    /// Projects blocks onto the X-axis to detect vertical columns.
    /// Blocks are sorted by Left ascending, then overlapping X ranges
    /// are merged. Columns are returned left to right.
    /// </summary>
    internal static List<List<PdfContentBlock>> ProjectXAxis(
        List<PdfContentBlock> blocks,
        double minGap)
    {
        if (blocks.Count == 0)
        {
            return [];
        }

        // Sort by Left ascending.
        var sorted = blocks.OrderBy(b => b.Left).ToList();

        // Merge overlapping or close-enough X ranges into columns.
        var columns = new List<List<PdfContentBlock>>();
        var current = new List<PdfContentBlock> { sorted[0] };
        var columnLeft = sorted[0].Left;
        var columnRight = sorted[0].Right;

        for (var i = 1; i < sorted.Count; i++)
        {
            var block = sorted[i];
            // If this block's left edge is within or close to current column, merge.
            var gap = block.Left - columnRight;
            if (gap <= minGap)
            {
                current.Add(block);
                columnLeft = Math.Min(columnLeft, block.Left);
                columnRight = Math.Max(columnRight, block.Right);
            }
            else
            {
                columns.Add(current);
                current = [block];
                columnLeft = block.Left;
                columnRight = block.Right;
            }
        }

        columns.Add(current);

        // Columns are already in left-to-right order due to initial sort.
        return columns;
    }

    /// <summary>
    /// Detects headers and footers across pages by finding text that repeats
    /// in the top or bottom margin region on 3+ pages, or that matches a page
    /// number pattern. Detected blocks are marked with <see cref="PdfContentBlock.IsHeaderFooter"/>.
    /// </summary>
    internal static void DetectHeadersFooters(
        List<List<PdfContentBlock>> allPageBlocks,
        double pageHeight)
    {
        var threshold = pageHeight * 0.1;
        var textCounts = new Dictionary<string, int>();
        var seenPerPage = new HashSet<string>();

        // Count occurrences of text in top/bottom regions across pages.
        foreach (var pageBlocks in allPageBlocks)
        {
            seenPerPage.Clear();
            foreach (var block in pageBlocks)
            {
                if (block is not PdfTextBlock text) continue;
                var inTopRegion = text.Top > pageHeight - threshold;
                var inBottomRegion = text.Bottom < threshold;
                if (!inTopRegion && !inBottomRegion) continue;

                var normalized = Regex.Replace(text.Text.Trim(), @"\s+", " ");
                if (string.IsNullOrEmpty(normalized)) continue;

                var key = $"{normalized}|{inTopRegion}";
                if (seenPerPage.Add(key))
                {
                    textCounts.TryGetValue(key, out var count);
                    textCounts[key] = count + 1;
                }
            }
        }

        // Mark blocks on pages.
        var pageNumberPattern = new Regex(@"^\d+([/\-–—]\d+)?$");
        for (var p = 0; p < allPageBlocks.Count; p++)
        {
            var pageBlocks = allPageBlocks[p];
            for (var i = 0; i < pageBlocks.Count; i++)
            {
                if (pageBlocks[i] is not PdfTextBlock text) continue;
                var inTopRegion = text.Top > pageHeight - threshold;
                var inBottomRegion = text.Bottom < threshold;
                if (!inTopRegion && !inBottomRegion) continue;

                var shouldMark = false;

                // Repeated on 3+ pages.
                var normalized = Regex.Replace(text.Text.Trim(), @"\s+", " ");
                if (!string.IsNullOrEmpty(normalized))
                {
                    var key = $"{normalized}|{inTopRegion}";
                    if (textCounts.TryGetValue(key, out var count) && count >= 3)
                        shouldMark = true;
                }

                // Page number pattern.
                if (pageNumberPattern.IsMatch(text.Text.Trim()))
                    shouldMark = true;

                if (shouldMark)
                    pageBlocks[i] = text with { IsHeaderFooter = true };
            }
        }
    }

    /// <summary>
    /// Detects caption blocks (e.g. figure captions) by finding text blocks
    /// that appear directly below images, with a smaller font size than body text
    /// and significant horizontal overlap with the image.
    /// </summary>
    internal static HashSet<int> DetectCaptions(
        List<PdfContentBlock> orderedBlocks,
        double bodyFontSize)
    {
        var captionIndices = new HashSet<int>();
        var maxDistance = bodyFontSize * 1.5;

        for (var i = 0; i < orderedBlocks.Count; i++)
        {
            if (orderedBlocks[i] is not PdfImageBlock image) continue;

            for (var j = i + 1; j < orderedBlocks.Count && j <= i + 3; j++)
            {
                if (orderedBlocks[j] is not PdfTextBlock text) continue;
                if (text.IsHeaderFooter) continue;

                var verticalDistance = image.Bottom - text.Top;
                if (verticalDistance < 0 || verticalDistance > maxDistance) break;

                var horizontalOverlap = Math.Min(image.Right, text.Right) - Math.Max(image.Left, text.Left);
                var imageWidth = image.Right - image.Left;
                if (imageWidth > 0 && horizontalOverlap / imageWidth >= 0.5)
                {
                    if (text.FontSize < bodyFontSize)
                        captionIndices.Add(j);
                }
            }
        }

        return captionIndices;
    }

    /// <summary>
    /// Merges consecutive body-text blocks into paragraphs when they share
    /// the same font size, similar left alignment, and small vertical gap.
    /// </summary>
    internal static List<PdfContentBlock> MergeParagraphs(
        List<PdfContentBlock> blocks,
        double bodyFontSize)
    {
        if (blocks.Count == 0) return [];

        var result = new List<PdfContentBlock>();
        var maxGap = bodyFontSize * 1.2;
        var fontSizeTolerance = 0.5;
        var alignmentTolerance = 5.0;

        PdfTextBlock? currentParagraph = null;

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];

            if (block is PdfImageBlock)
            {
                if (currentParagraph is not null)
                {
                    result.Add(currentParagraph);
                    currentParagraph = null;
                }
                result.Add(block);
                continue;
            }

            if (block is not PdfTextBlock text)
            {
                if (currentParagraph is not null)
                {
                    result.Add(currentParagraph);
                    currentParagraph = null;
                }
                result.Add(block);
                continue;
            }

            if (currentParagraph is null)
            {
                currentParagraph = text;
                continue;
            }

            var sameFontSize = Math.Abs(currentParagraph.FontSize - text.FontSize) < fontSizeTolerance;
            var smallGap = currentParagraph.Bottom - text.Top < maxGap && currentParagraph.Bottom > text.Top;
            var sameAlignment = Math.Abs(currentParagraph.Left - text.Left) < alignmentTolerance;

            if (sameFontSize && smallGap && sameAlignment)
            {
                currentParagraph = currentParagraph with
                {
                    Text = $"{currentParagraph.Text} {text.Text}",
                    Bottom = text.Bottom,
                    Y = (currentParagraph.Top + text.Bottom) / 2.0,
                    Right = Math.Max(currentParagraph.Right, text.Right)
                };
            }
            else
            {
                result.Add(currentParagraph);
                currentParagraph = text;
            }
        }

        if (currentParagraph is not null)
        {
            result.Add(currentParagraph);
        }

        return result;
    }

    private static readonly Regex NumberedListPattern = new(@"^\d+[.)、]\s", RegexOptions.Compiled);
    private static readonly Regex BulletedListPattern = new(@"^[•·●◇◆\-–—]\s", RegexOptions.Compiled);

    /// <summary>
    /// Detects list items in the ordered blocks by matching numbered (e.g. "1. ", "2) ")
    /// or bulleted (e.g. "• ", "- ") prefixes. Consecutive items are grouped into lists.
    /// Only groups of 2+ consecutive items are returned.
    /// </summary>
    internal static List<(int Start, int Length)> DetectLists(
        List<PdfContentBlock> orderedBlocks)
    {
        var lists = new List<(int Start, int Length)>();
        var currentStart = -1;
        var currentLength = 0;

        for (var i = 0; i < orderedBlocks.Count; i++)
        {
            if (orderedBlocks[i] is not PdfTextBlock text) continue;

            var isListItem = NumberedListPattern.IsMatch(text.Text) ||
                             BulletedListPattern.IsMatch(text.Text);

            if (isListItem)
            {
                if (currentStart < 0) currentStart = i;
                currentLength++;
            }
            else
            {
                if (currentLength >= 2)
                    lists.Add((currentStart, currentLength));
                currentStart = -1;
                currentLength = 0;
            }
        }

        if (currentLength >= 2)
            lists.Add((currentStart, currentLength));

        return lists;
    }
}
