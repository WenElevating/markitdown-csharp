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
    /// Detects headers and footers across pages. Stub — full implementation
    /// in a subsequent task.
    /// </summary>
    internal static void DetectHeadersFooters(
        List<List<PdfContentBlock>> allPageBlocks,
        double pageHeight)
    {
        // Stub: no-op for now.
    }

    /// <summary>
    /// Detects caption blocks (e.g. figure captions). Stub — full implementation
    /// in a subsequent task.
    /// </summary>
    internal static HashSet<int> DetectCaptions(
        List<PdfContentBlock> orderedBlocks,
        double bodyFontSize)
    {
        // Stub: return empty set for now.
        return [];
    }

    /// <summary>
    /// Detects list items in the ordered blocks. Stub — full implementation
    /// in a subsequent task.
    /// </summary>
    internal static List<(int Start, int Length)> DetectLists(
        List<PdfContentBlock> orderedBlocks)
    {
        // Stub: return empty list for now.
        return [];
    }
}
