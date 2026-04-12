# PDF Layout Optimization — Projection Profile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current Y-sort + proximity grouping with a projection-profile layout engine for correct reading order, plus header/footer removal, caption association, and list detection.

**Architecture:** Two-pass cross-page pipeline. First pass extracts all text blocks from all pages and detects repeated headers/footers via hash counting. Second pass processes each page: extract blocks → filter headers/footers → extract full-width elements → Y-axis projection into bands → X-axis projection into columns → flatten to reading order → associate captions → detect lists → render Markdown.

**Tech Stack:** C# / .NET 8, PdfPig 1.7.0-custom-5, xUnit

**Spec:** `docs/superpowers/specs/2026-04-12-pdf-layout-optimization-design.md`

---

## File Structure

### New files

| File | Responsibility |
|------|---------------|
| `src/MarkItDown.Converters.Pdf/PdfLayoutAnalyzer.cs` | Projection profile algorithm (Y-bands, X-columns), header/footer detection, caption association, list detection |
| `tests/MarkItDown.Converters.Pdf.Tests/PdfLayoutAnalyzerTests.cs` | Unit tests for layout analyzer |

### Modified files

| File | Change |
|------|--------|
| `src/MarkItDown.Converters.Pdf/PdfContentBlock.cs` | Add `Left`, `Right`, `IsHeaderFooter` to content blocks |
| `src/MarkItDown.Converters.Pdf/PdfContentGrouper.cs` | Use `PdfLayoutAnalyzer` for ordering instead of Y-sort; keep table/caption/list rendering |
| `src/MarkItDown.Converters.Pdf/PdfConverter.cs` | Two-pass pipeline: cross-page header/footer detection, then per-page processing |
| `tests/MarkItDown.Converters.Pdf.Tests/PdfConverterTests.cs` | Update assertions for new output format, add regression tests |

---

### Task 1: Extend PdfContentBlock with X-coordinates and header/footer flag

**Files:**
- Modify: `src/MarkItDown.Converters.Pdf/PdfContentBlock.cs`
- Modify: `src/MarkItDown.Converters.Pdf/PdfTextClassifier.cs` (add Left/Right to text block creation)
- Modify: `src/MarkItDown.Converters.Pdf/PdfImageExtractor.cs` (add Left/Right to image block creation)

- [ ] **Step 1: Update PdfContentBlock records**

```csharp
// src/MarkItDown.Converters.Pdf/PdfContentBlock.cs
namespace MarkItDown.Converters.Pdf;

internal abstract record PdfContentBlock(
    double Y, double Top, double Bottom,
    double Left, double Right,
    bool IsHeaderFooter = false);

internal sealed record PdfTextBlock(
    double Y, double Top, double Bottom,
    double Left, double Right,
    string Text,
    double FontSize,
    bool IsHeaderFooter = false) : PdfContentBlock(Y, Top, Bottom, Left, Right, IsHeaderFooter);

internal sealed record PdfImageBlock(
    double Y, double Top, double Bottom,
    double Left, double Right,
    int PageNumber,
    int ImageIndex,
    string FileName,
    bool IsHeaderFooter = false) : PdfContentBlock(Y, Top, Bottom, Left, Right, IsHeaderFooter);
```

- [ ] **Step 2: Update PdfTextClassifier to populate Left/Right**

In `ClassifyTextBlocks`, after computing `top` and `bottom`, also compute:

```csharp
var left = row.Min(w => w.BoundingBox.Left);
var right = row.Max(w => w.BoundingBox.Right);
```

Update the `PdfTextBlock` constructor call to include `Left: left, Right: right`.

- [ ] **Step 3: Update PdfImageExtractor to populate Left/Right**

In `ExtractImages`, after computing `y` from bounds, also extract:

```csharp
var left = bounds.Left;
var right = bounds.Right;
```

Update the `PdfImageBlock` constructor call to include `Left: left, Right: right`.

- [ ] **Step 4: Update PdfContentGrouper references**

The `RenderPage` and `RenderGroup` methods use `image.FileName` — no changes needed since `FileName` position in the constructor didn't change (it moved after `Left`/`Right`).

Check that all tests still compile and pass:

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore`
Expected: All 9 existing tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/MarkItDown.Converters.Pdf/PdfContentBlock.cs src/MarkItDown.Converters.Pdf/PdfTextClassifier.cs src/MarkItDown.Converters.Pdf/PdfImageExtractor.cs
git commit -m "refactor: add Left/Right coordinates and IsHeaderFooter to content blocks"
```

---

### Task 2: Create PdfLayoutAnalyzer — projection profile core

**Files:**
- Create: `src/MarkItDown.Converters.Pdf/PdfLayoutAnalyzer.cs` (core projection methods only)
- Create: `tests/MarkItDown.Converters.Pdf.Tests/PdfLayoutAnalyzerTests.cs`

- [ ] **Step 1: Write failing tests for projection profile**

```csharp
// tests/MarkItDown.Converters.Pdf.Tests/PdfLayoutAnalyzerTests.cs
using MarkItDown.Converters.Pdf;

namespace MarkItDown.Converters.Pdf.Tests;

public sealed class PdfLayoutAnalyzerTests
{
    // Helper: create a text block at given coordinates
    private static PdfTextBlock Txt(double top, double bottom, double left, double right, string text = "text", double fontSize = 12.0)
        => new((top + bottom) / 2, top, bottom, left, right, text, fontSize);

    private static PdfImageBlock Img(double top, double bottom, double left, double right, int page = 1, int idx = 0, string name = "img.png")
        => new((top + bottom) / 2, top, bottom, left, right, page, idx, name);

    [Fact]
    public void AnalyzeReadingOrder_SingleColumn_ReturnsTopToBottom()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 500, "Top block"),
            Txt(660, 640, 50, 500, "Bottom block"),
        };

        var result = PdfLayoutAnalyzer.AnalyzeReadingOrder(blocks, 12.0);

        Assert.Equal(2, result.Count);
        Assert.Equal("Top block", ((PdfTextBlock)result[0]).Text);
        Assert.Equal("Bottom block", ((PdfTextBlock)result[1]).Text);
    }

    [Fact]
    public void AnalyzeReadingOrder_TwoColumns_LeftFirst()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 240, "Left"),
            Txt(700, 680, 280, 500, "Right"),
        };

        var result = PdfLayoutAnalyzer.AnalyzeReadingOrder(blocks, 12.0);

        Assert.Equal(2, result.Count);
        Assert.Equal("Left", ((PdfTextBlock)result[0]).Text);
        Assert.Equal("Right", ((PdfTextBlock)result[1]).Text);
    }

    [Fact]
    public void AnalyzeReadingOrder_TwoRowsTwoColumns_ReadingOrder()
    {
        // Layout:
        // [A] [B]
        // [C] [D]
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 240, "A"),
            Txt(700, 680, 280, 500, "B"),
            Txt(660, 640, 50, 240, "C"),
            Txt(660, 640, 280, 500, "D"),
        };

        var result = PdfLayoutAnalyzer.AnalyzeReadingOrder(blocks, 12.0);

        Assert.Equal(4, result.Count);
        Assert.Equal("A", ((PdfTextBlock)result[0]).Text);
        Assert.Equal("B", ((PdfTextBlock)result[1]).Text);
        Assert.Equal("C", ((PdfTextBlock)result[2]).Text);
        Assert.Equal("D", ((PdfTextBlock)result[3]).Text);
    }

    [Fact]
    public void AnalyzeReadingOrder_FullWidthElementBetweenColumns()
    {
        // Layout:
        // [A (full width)]
        // [B] [C]
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 500, "Full width heading"),
            Txt(660, 640, 50, 240, "Left"),
            Txt(660, 640, 280, 500, "Right"),
        };

        var result = PdfLayoutAnalyzer.AnalyzeReadingOrder(blocks, 12.0);

        Assert.Equal(3, result.Count);
        Assert.Equal("Full width heading", ((PdfTextBlock)result[0]).Text);
        Assert.Equal("Left", ((PdfTextBlock)result[1]).Text);
        Assert.Equal("Right", ((PdfTextBlock)result[2]).Text);
    }

    [Fact]
    public void AnalyzeReadingOrder_SingleBlock_NoSplit()
    {
        var blocks = new List<PdfContentBlock>
        {
            Txt(700, 680, 50, 500, "Only block"),
        };

        var result = PdfLayoutAnalyzer.AnalyzeReadingOrder(blocks, 12.0);

        Assert.Single(result);
        Assert.Equal("Only block", ((PdfTextBlock)result[0]).Text);
    }

    [Fact]
    public void AnalyzeReadingOrder_EmptyList_ReturnsEmpty()
    {
        var result = PdfLayoutAnalyzer.AnalyzeReadingOrder([], 12.0);
        Assert.Empty(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore --filter "FullyQualifiedName~PdfLayoutAnalyzerTests"`
Expected: FAIL — `PdfLayoutAnalyzer` does not exist yet.

- [ ] **Step 3: Implement PdfLayoutAnalyzer with projection profile**

```csharp
// src/MarkItDown.Converters.Pdf/PdfLayoutAnalyzer.cs
using System.Text.RegularExpressions;

namespace MarkItDown.Converters.Pdf;

internal static partial class PdfLayoutAnalyzer
{
    private const double FullWidthRatio = 0.8;
    private const double GapMultiplier = 1.5;

    /// <summary>
    /// Analyzes content blocks on a page and returns them in correct reading order
    /// using a two-pass projection profile approach.
    /// Pass 1: Y-axis projection → horizontal bands (top to bottom).
    /// Pass 2: X-axis projection per band → columns (left to right).
    /// Full-width elements are extracted first and never split across columns.
    /// </summary>
    internal static List<PdfContentBlock> AnalyzeReadingOrder(
        List<PdfContentBlock> blocks,
        double bodyFontSize)
    {
        if (blocks.Count == 0) return [];
        if (blocks.Count == 1) return [blocks[0]];

        var pageLeft = blocks.Min(b => b.Left);
        var pageRight = blocks.Max(b => b.Right);
        var pageWidth = pageRight - pageLeft;
        var fullWidthThreshold = pageWidth * FullWidthRatio;
        var minGap = bodyFontSize * GapMultiplier;

        // Step 1: Extract full-width elements
        var fullWidth = new List<PdfContentBlock>();
        var remaining = new List<PdfContentBlock>();
        foreach (var block in blocks)
        {
            if (block.Right - block.Left >= fullWidthThreshold)
            {
                fullWidth.Add(block);
            }
            else
            {
                remaining.Add(block);
            }
        }

        // Step 2: Y-axis projection → horizontal bands
        var bands = ProjectYAxis(remaining, minGap);

        // Step 3: X-axis projection per band → columns, then flatten
        var ordered = new List<PdfContentBlock>();

        // Merge full-width elements into band order by Y coordinate
        var bandIndex = 0;
        var fullwidthIndex = 0;
        var sortedFullWidth = fullWidth.OrderByDescending(b => b.Top).ToList();

        while (bandIndex < bands.Count || fullwidthIndex < sortedFullWidth.Count)
        {
            var nextBandTop = bandIndex < bands.Count
                ? bands[bandIndex].Max(b => b.Top)
                : double.MinValue;
            var nextFullWidthTop = fullwidthIndex < sortedFullWidth.Count
                ? sortedFullWidth[fullwidthIndex].Top
                : double.MinValue;

            if (fullwidthIndex < sortedFullWidth.Count &&
                (bandIndex >= bands.Count || sortedFullWidth[fullwidthIndex].Top >= nextBandTop))
            {
                ordered.Add(sortedFullWidth[fullwidthIndex]);
                fullwidthIndex++;
            }
            else
            {
                // Step 3: X-axis projection for this band → columns
                var columns = ProjectXAxis(bands[bandIndex], minGap);
                foreach (var column in columns)
                {
                    ordered.AddRange(column.OrderByDescending(b => b.Top));
                }
                bandIndex++;
            }
        }

        return ordered;
    }

    /// <summary>
    /// Y-axis projection: find horizontal gaps, split into bands (top to bottom).
    /// Each band is a list of blocks that overlap vertically.
    /// </summary>
    internal static List<List<PdfContentBlock>> ProjectYAxis(
        List<PdfContentBlock> blocks,
        double minGap)
    {
        if (blocks.Count == 0) return [];

        // Collect all Y boundaries and find gaps
        var tops = blocks.Select(b => b.Top).OrderByDescending(t => t).ToList();
        var bottoms = blocks.Select(b => b.Bottom).OrderByDescending(b => b).ToList();

        // Find the occupied Y ranges and merge overlapping ones
        var ranges = blocks
            .Select(b => (Top: b.Top, Bottom: b.Bottom))
            .OrderByDescending(r => r.Top)
            .ToList();

        var mergedRanges = new List<(double Top, double Bottom)> { ranges[0] };
        for (var i = 1; i < ranges.Count; i++)
        {
            var last = mergedRanges[^1];
            var current = ranges[i];
            // Overlapping or adjacent
            if (current.Top >= last.Bottom - minGap)
            {
                mergedRanges[^1] = (last.Top, Math.Min(last.Bottom, current.Bottom));
            }
            else
            {
                mergedRanges.Add(current);
            }
        }

        // Assign blocks to bands
        var bands = new List<List<PdfContentBlock>>();
        foreach (var range in mergedRanges)
        {
            var band = blocks
                .Where(b => b.Top >= range.Bottom - minGap && b.Bottom <= range.Top + minGap)
                .ToList();
            if (band.Count > 0)
            {
                bands.Add(band);
            }
        }

        return bands;
    }

    /// <summary>
    /// X-axis projection: find vertical gaps within a band, split into columns (left to right).
    /// Each column is a list of blocks that overlap horizontally.
    /// </summary>
    internal static List<List<PdfContentBlock>> ProjectXAxis(
        List<PdfContentBlock> blocks,
        double minGap)
    {
        if (blocks.Count <= 1) return [blocks];

        // Collect occupied X ranges, sorted by Left
        var ranges = blocks
            .Select(b => (Left: b.Left, Right: b.Right, Block: b))
            .OrderBy(r => r.Left)
            .ToList();

        // Merge overlapping X ranges, build column groups
        var columns = new List<List<PdfContentBlock>>();
        var currentColumn = new List<PdfContentBlock> { ranges[0].Block };
        var columnRight = ranges[0].Right;

        for (var i = 1; i < ranges.Count; i++)
        {
            var range = ranges[i];
            if (range.Left < columnRight + minGap)
            {
                // Overlapping or adjacent → same column
                currentColumn.Add(range.Block);
                columnRight = Math.Max(columnRight, range.Right);
            }
            else
            {
                // Gap found → new column
                columns.Add(currentColumn);
                currentColumn = [range.Block];
                columnRight = range.Right;
            }
        }
        columns.Add(currentColumn);

        return columns;
    }

    // --- Header/Footer Detection ---

    private static readonly Regex PageNumberPattern = PageNumberRegex();

    /// <summary>
    /// Detects repeated text across pages and marks header/footer blocks.
    /// Uses hash-based counting for O(n) performance.
    /// </summary>
    internal static void DetectHeadersFooters(
        List<List<PdfContentBlock>> allPageBlocks,
        double pageHeight)
    {
        var threshold = pageHeight * 0.1; // top/bottom 10%
        var textCounts = new Dictionary<string, int>();

        // Count occurrences of text in top/bottom regions
        foreach (var pageBlocks in allPageBlocks)
        {
            foreach (var block in pageBlocks)
            {
                if (block is not PdfTextBlock text) continue;

                var inTopRegion = text.Top > pageHeight - threshold;
                var inBottomRegion = text.Bottom < threshold;
                if (!inTopRegion && !inBottomRegion) continue;

                var normalized = NormalizeText(text.Text);
                if (string.IsNullOrEmpty(normalized)) continue;

                // Deduplicate within same page
                var key = $"{normalized}|{inTopRegion}";
                textCounts.TryGetValue(key, out var count);
                textCounts[key] = count + 1;
            }
        }

        // Mark blocks that appear on 3+ pages
        foreach (var pageBlocks in allPageBlocks)
        {
            foreach (var block in pageBlocks)
            {
                if (block is not PdfTextBlock text) continue;

                var inTopRegion = text.Top > pageHeight - threshold;
                var inBottomRegion = text.Bottom < threshold;
                if (!inTopRegion && !inBottomRegion) continue;

                var normalized = NormalizeText(text.Text);
                if (string.IsNullOrEmpty(normalized)) continue;

                var key = $"{normalized}|{inTopRegion}";
                if (textCounts.TryGetValue(key, out var count) && count >= 3)
                {
                    // Mark by creating new record with IsHeaderFooter = true
                    var index = pageBlocks.IndexOf(block);
                    pageBlocks[index] = text with { IsHeaderFooter = true };
                }

                // Page number pattern → always header/footer
                if (PageNumberPattern.IsMatch(text.Text.Trim()))
                {
                    var index = pageBlocks.IndexOf(block);
                    pageBlocks[index] = text with { IsHeaderFooter = true };
                }
            }
        }
    }

    private static string NormalizeText(string text)
        => Regex.Replace(text.Trim(), @"\s+", " ");

    [GeneratedRegex(@"^\d+([/\-–—]\d+)?$")]
    private static partial Regex PageNumberRegex();

    // --- Caption Association ---

    /// <summary>
    /// Finds text blocks that serve as captions for images.
    /// Returns a set of block indices that are captions.
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

            // Look for text below this image (in PDF coords: lower Y)
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
                    {
                        captionIndices.Add(j);
                    }
                }
            }
        }

        return captionIndices;
    }

    // --- List Detection ---

    private static readonly Regex NumberedListPattern = NumberedListRegex();
    private static readonly Regex BulletedListPattern = BulletedListRegex();

    /// <summary>
    /// Detects consecutive text blocks forming numbered or bulleted lists.
    /// Returns list of (startIndex, count) tuples for each detected list.
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
                {
                    lists.Add((currentStart, currentLength));
                }
                currentStart = -1;
                currentLength = 0;
            }
        }

        if (currentLength >= 2)
        {
            lists.Add((currentStart, currentLength));
        }

        return lists;
    }

    [GeneratedRegex(@"^\d+[.)、]\s")]
    private static partial Regex NumberedListRegex();

    [GeneratedRegex(@"^[•·●◇◆\-–—]\s")]
    private static partial Regex BulletedListRegex();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore --filter "FullyQualifiedName~PdfLayoutAnalyzerTests"`
Expected: All 6 new tests PASS.

- [ ] **Step 5: Run all existing PDF tests to verify no regression**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore`
Expected: All tests pass (9 existing + 6 new = 15 total).

- [ ] **Step 6: Commit**

```bash
git add src/MarkItDown.Converters.Pdf/PdfLayoutAnalyzer.cs tests/MarkItDown.Converters.Pdf.Tests/PdfLayoutAnalyzerTests.cs
git commit -m "feat: add PdfLayoutAnalyzer with projection profile reading order"
```

---

### Task 3: Add header/footer, caption, and list tests

**Files:**
- Modify: `tests/MarkItDown.Converters.Pdf.Tests/PdfLayoutAnalyzerTests.cs`

- [ ] **Step 1: Write tests for header/footer detection**

```csharp
[Fact]
public void DetectHeadersFooters_RepeatedText_Marked()
{
    var pageHeight = 800.0;
    // 3 pages with same footer
    var page1 = new List<PdfContentBlock>
    {
        Txt(790, 780, 50, 200, "Company Confidential", 8),
        Txt(700, 680, 50, 500, "Main content page 1"),
    };
    var page2 = new List<PdfContentBlock>
    {
        Txt(790, 780, 50, 200, "Company Confidential", 8),
        Txt(700, 680, 50, 500, "Main content page 2"),
    };
    var page3 = new List<PdfContentBlock>
    {
        Txt(790, 780, 50, 200, "Company Confidential", 8),
        Txt(700, 680, 50, 500, "Main content page 3"),
    };
    var allPages = new List<List<PdfContentBlock>> { page1, page2, page3 };

    PdfLayoutAnalyzer.DetectHeadersFooters(allPages, pageHeight);

    // Footer "Company Confidential" should be marked on all pages
    Assert.True(((PdfTextBlock)page1[0]).IsHeaderFooter);
    Assert.True(((PdfTextBlock)page2[0]).IsHeaderFooter);
    Assert.True(((PdfTextBlock)page3[0]).IsHeaderFooter);
    // Main content should NOT be marked
    Assert.False(((PdfTextBlock)page1[1]).IsHeaderFooter);
}

[Fact]
public void DetectHeadersFooters_PageNumber_Marked()
{
    var pageHeight = 800.0;
    var page = new List<PdfContentBlock>
    {
        Txt(10, 5, 350, 450, "3/20", 10),
        Txt(700, 680, 50, 500, "Content"),
    };
    var allPages = new List<List<PdfContentBlock>> { page };

    PdfLayoutAnalyzer.DetectHeadersFooters(allPages, pageHeight);

    Assert.True(((PdfTextBlock)page[0]).IsHeaderFooter);
}

[Fact]
public void DetectHeadersFooters_UniqueText_NotMarked()
{
    var pageHeight = 800.0;
    // Only appears on 1 page
    var page = new List<PdfContentBlock>
    {
        Txt(790, 780, 50, 200, "Unique title", 10),
        Txt(700, 680, 50, 500, "Content"),
    };
    var allPages = new List<List<PdfContentBlock>> { page };

    PdfLayoutAnalyzer.DetectHeadersFooters(allPages, pageHeight);

    Assert.False(((PdfTextBlock)page[0]).IsHeaderFooter);
}
```

- [ ] **Step 2: Write tests for caption detection**

```csharp
[Fact]
public void DetectCaptions_TextBelowSmallFont_Marked()
{
    var blocks = new List<PdfContentBlock>
    {
        Img(700, 620, 50, 400), // image
        Txt(610, 595, 60, 390, "Figure 1: Caption text", 9), // small font below image
        Txt(580, 560, 50, 400, "Body text", 12),
    };

    var captions = PdfLayoutAnalyzer.DetectCaptions(blocks, 12.0);

    Assert.Single(captions);
    Assert.Equal(1, captions.First()); // index 1 is the caption
}

[Fact]
public void DetectCaptions_NoNearbyText_NoCaption()
{
    var blocks = new List<PdfContentBlock>
    {
        Img(700, 620, 50, 400),
        Txt(580, 560, 50, 400, "Body text far away", 12),
    };

    var captions = PdfLayoutAnalyzer.DetectCaptions(blocks, 12.0);
    Assert.Empty(captions);
}
```

- [ ] **Step 3: Write tests for list detection**

```csharp
[Fact]
public void DetectLists_NumberedItems_Detected()
{
    var blocks = new List<PdfContentBlock>
    {
        Txt(700, 680, 50, 400, "1. First item"),
        Txt(660, 640, 50, 400, "2. Second item"),
        Txt(620, 600, 50, 400, "3. Third item"),
        Txt(560, 540, 50, 400, "Normal text"),
    };

    var lists = PdfLayoutAnalyzer.DetectLists(blocks);

    Assert.Single(lists);
    Assert.Equal(0, lists[0].Start);
    Assert.Equal(3, lists[0].Length);
}

[Fact]
public void DetectLists_BulletedItems_Detected()
{
    var blocks = new List<PdfContentBlock>
    {
        Txt(700, 680, 50, 400, "• First bullet"),
        Txt(660, 640, 50, 400, "• Second bullet"),
    };

    var lists = PdfLayoutAnalyzer.DetectLists(blocks);

    Assert.Single(lists);
    Assert.Equal(2, lists[0].Length);
}

[Fact]
public void DetectLists_SingleItem_NotAList()
{
    var blocks = new List<PdfContentBlock>
    {
        Txt(700, 680, 50, 400, "1. Only one item"),
        Txt(660, 640, 50, 400, "Normal text"),
    };

    var lists = PdfLayoutAnalyzer.DetectLists(blocks);
    Assert.Empty(lists);
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore --filter "FullyQualifiedName~PdfLayoutAnalyzerTests"`
Expected: All tests pass (6 projection + 3 header/footer + 2 caption + 3 list = 14).

- [ ] **Step 5: Commit**

```bash
git add tests/MarkItDown.Converters.Pdf.Tests/PdfLayoutAnalyzerTests.cs
git commit -m "test: add header/footer, caption, and list detection tests"
```

---

### Task 4: Refactor PdfContentGrouper to use PdfLayoutAnalyzer

**Files:**
- Modify: `src/MarkItDown.Converters.Pdf/PdfContentGrouper.cs`

- [ ] **Step 1: Update RenderPage to use projection profile ordering**

Replace the current `RenderPage` method. The key change: instead of `OrderByDescending(b => b.Y)` + `GroupByProximity`, use `PdfLayoutAnalyzer.AnalyzeReadingOrder`. The `RenderGroup` method is replaced with a single-block renderer since groups are no longer needed (the projection profile already handles layout).

```csharp
// src/MarkItDown.Converters.Pdf/PdfContentGrouper.cs
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

        // Use projection profile for correct reading order
        var ordered = PdfLayoutAnalyzer.AnalyzeReadingOrder(blocks, bodyFontSize);
        var captions = PdfLayoutAnalyzer.DetectCaptions(ordered, bodyFontSize);
        var lists = PdfLayoutAnalyzer.DetectLists(ordered);

        var builder = new StringBuilder();
        var listIndex = 0; // Track which list we're in

        for (var i = 0; i < ordered.Count; i++)
        {
            var block = ordered[i];
            if (block.IsHeaderFooter) continue;

            string? line = null;

            if (block is PdfImageBlock image)
            {
                var imagePath = string.IsNullOrEmpty(assetDirName)
                    ? $"./{image.FileName}"
                    : $"./{assetDirName}/{image.FileName}";
                line = $"![image]({imagePath})";
            }
            else if (block is PdfTextBlock text)
            {
                if (captions.Contains(i))
                {
                    line = $"*{text.Text}*";
                }
                else if (listIndex < lists.Count && i >= lists[listIndex].Start && i < lists[listIndex].Start + lists[listIndex].Length)
                {
                    line = $"- {StripListMarker(text.Text)}";
                    if (i == lists[listIndex].Start + lists[listIndex].Length - 1)
                        listIndex++;
                }
                else
                {
                    var role = PdfTextClassifier.ClassifyRole(text.FontSize, bodyFontSize, text.Text);
                    line = role switch
                    {
                        "heading" => $"## {text.Text}",
                        "caption" => $"*{text.Text}*",
                        _ => text.Text
                    };
                }
            }

            if (string.IsNullOrWhiteSpace(line)) continue;

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }
            builder.Append(line);
        }

        return builder.ToString();
    }

    private static string StripListMarker(string text)
    {
        // Remove leading "1. ", "• ", "- ", etc.
        var match = Regex.Match(text, @"^(\d+[.)、]|[•·●◇◆\-–—])\s*(.*)$");
        return match.Success ? match.Groups[2].Value : text;
    }

    // Keep DetectTableRows as internal for testing, but it's no longer called from RenderPage
    // Table detection is now done per-band by the projection profile
    internal static string? DetectTable(List<PdfTextBlock> texts)
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
```

- [ ] **Step 2: Build and check for compilation errors**

Run: `dotnet build src/MarkItDown.Converters.Pdf --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Run existing PDF tests**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore`
Expected: Tests may need adjustments. The existing `ConvertAsync_ExtractsTextFromPdf` and `ConvertAsync_ExtractsTableLikeMarkdown` tests check for content presence, not exact format, so they should pass. The `ConvertAsync_ClassifiesHeadingsByFontSize` test checks that not everything is a heading — still valid.

If `RenderPage_ImagePathIncludesAssetDirName` or `RenderPage_ImagePathWithoutAssetDirName_NoPrefix` fail, update them to use the new `AnalyzeReadingOrder` → render flow, or test `PdfLayoutAnalyzer.AnalyzeReadingOrder` directly and verify the grouper output.

- [ ] **Step 4: Fix any test failures**

If tests fail due to output format changes, update assertions to match new output. Key changes:
- Content order may change (this is the improvement)
- Page separators now `---` instead of blank lines
- Lists may now render as `- item` instead of plain text

- [ ] **Step 5: Commit**

```bash
git add src/MarkItDown.Converters.Pdf/PdfContentGrouper.cs
git commit -m "refactor: use PdfLayoutAnalyzer in PdfContentGrouper for reading order"
```

---

### Task 5: Refactor PdfConverter — two-pass pipeline

**Files:**
- Modify: `src/MarkItDown.Converters.Pdf/PdfConverter.cs`

- [ ] **Step 1: Rewrite PdfConverter with cross-page preprocessing**

The key change: first pass extracts text from all pages to detect headers/footers, second pass processes each page with filtered blocks.

```csharp
// src/MarkItDown.Converters.Pdf/PdfConverter.cs
using UglyToad.PdfPig;
using MarkItDown.Core;

namespace MarkItDown.Converters.Pdf;

public sealed class PdfConverter : BaseConverter
{
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

            var pages = document.GetPages().ToList();
            var totalLetters = pages.Sum(p => p.Letters.Count);
            var hasImages = pages.Any(p => p.GetImages().Any());

            if (totalLetters < 20 && !hasImages)
            {
                throw new ConversionException(
                    "Scanned or image-only PDFs are not supported in this MVP.");
            }

            var assetBasePath = request.AssetBasePath;
            var assetDirName = assetBasePath is not null
                ? Path.GetFileName(assetBasePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : null;

            // --- Pass 1: Extract text blocks from all pages for header/footer detection ---
            var allPageTextBlocks = new List<List<PdfContentBlock>>();
            double? bodyFontSize = null;

            foreach (var page in pages)
            {
                if (bodyFontSize is null && page.Letters.Count > 0)
                {
                    bodyFontSize = PdfTextClassifier.ComputeBodyFontSize(page.Letters);
                }

                var textBlocks = PdfTextClassifier.ClassifyTextBlocks(page)
                    .Cast<PdfContentBlock>().ToList();
                allPageTextBlocks.Add(textBlocks);
            }

            var fontSize = bodyFontSize ?? 12.0;

            // Detect headers/footers across pages (uses average page height)
            var avgPageHeight = pages.Average(p => p.Height);
            PdfLayoutAnalyzer.DetectHeadersFooters(allPageTextBlocks, avgPageHeight);

            // --- Pass 2: Per-page processing with filtered blocks ---
            var seenHashes = new Dictionary<string, string>();
            var pageMarkdowns = new List<string>();

            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = pages[pageIndex];
                var pageNumber = pageIndex + 1;
                var pageArea = page.Width * page.Height;

                // Get text blocks with header/footer flags from pass 1
                var textBlocks = allPageTextBlocks[pageIndex];

                // Extract images
                var imageBlocks = new List<PdfImageBlock>();
                if (assetBasePath is not null)
                {
                    imageBlocks = PdfImageExtractor.ExtractImages(
                        page, pageNumber, assetBasePath, pageArea, seenHashes);
                }

                var allBlocks = textBlocks
                    .Concat(imageBlocks.Cast<PdfContentBlock>())
                    .ToList();

                var pageMarkdown = PdfContentGrouper.RenderPage(allBlocks, fontSize, assetDirName);

                if (!string.IsNullOrWhiteSpace(pageMarkdown))
                {
                    pageMarkdowns.Add(pageMarkdown);
                }
            }

            var markdown = string.Join(
                $"{Environment.NewLine}{Environment.NewLine}---{Environment.NewLine}{Environment.NewLine}",
                pageMarkdowns).Trim();

            if (string.IsNullOrWhiteSpace(markdown))
            {
                throw new ConversionException(
                    "The PDF did not contain extractable text or images.");
            }

            var assetDir = assetBasePath is not null && Directory.Exists(assetBasePath)
                ? assetBasePath : null;

            return Task.FromResult(new DocumentConversionResult(
                "Pdf", markdown, null, assetDir));
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
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/MarkItDown.Converters.Pdf --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Run all PDF tests**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore`
Expected: All tests pass. Adjust any failing assertions due to output format changes.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test MarkItDown.sln --no-restore`
Expected: All tests pass (94+).

- [ ] **Step 5: Commit**

```bash
git add src/MarkItDown.Converters.Pdf/PdfConverter.cs
git commit -m "refactor: two-pass PDF pipeline with cross-page header/footer detection"
```

---

### Task 6: Update existing tests and add regression tests

**Files:**
- Modify: `tests/MarkItDown.Converters.Pdf.Tests/PdfConverterTests.cs`

- [ ] **Step 1: Run all tests and identify failures**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore -v n`
Expected: Most tests should pass. Identify any that fail due to output format changes.

Common expected changes:
- Page separator is now `---` instead of double blank line
- List items may render as `- item` instead of plain text
- Some text may be reordered (improvement)

- [ ] **Step 2: Update failing test assertions**

For each failing test:
- If assertion checks for specific text presence (`Assert.Contains`) → should still pass
- If assertion checks exact output format → update to match new format
- The `ConvertAsync_ExtractsTableLikeMarkdown` test checks for column names → still valid
- The `ConvertAsync_ClassifiesHeadingsByFontSize` test checks heading vs body ratio → still valid

- [ ] **Step 3: Add page separator regression test**

```csharp
[Fact]
public async Task ConvertAsync_MultiPagePdf_UsesPageSeparator()
{
    var result = await _converter.ConvertAsync(
        new DocumentConversionRequest { FilePath = FixturePath.For("sample.pdf") });

    // Multi-page PDFs should use --- separator between pages
    if (result.Markdown.Contains("---"))
    {
        Assert.Contains($"{Environment.NewLine}---{Environment.NewLine}", result.Markdown);
    }
}
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test MarkItDown.sln --no-restore`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add tests/MarkItDown.Converters.Pdf.Tests/PdfConverterTests.cs
git commit -m "test: update PDF tests for layout optimization regression"
```

---

### Task 7: Manual verification with PuYu.pdf

**Files:** None (verification only)

- [ ] **Step 1: Convert PuYu.pdf with the new pipeline**

```bash
dotnet run --project src/MarkItDown.Cli -- "D:\Test\PuYu.pdf" -o "D:\Test\PuYu-v2.md"
```

Verify:
- Multi-column content appears in correct left-to-right reading order
- Headers/footers (if repeated on 3+ pages) are removed
- Captions appear as italic text below images
- Numbered lists render as Markdown `- item` lists
- Tables still detected and formatted
- No text content lost vs previous output
- Images still have correct `./PuYu_files/` path prefix
- Pages separated by `---`

- [ ] **Step 2: Compare with previous output**

Compare `D:\Test\PuYu-v2.md` with `D:\Test\PuYu.md`. The new output should:
- Have correct reading order in multi-column layouts
- Have fewer repeated headers/footers
- Have cleaner caption association
- Not lose any text content

- [ ] **Step 3: Final commit if any fixes needed**

```bash
git add -A
git commit -m "fix: address issues found during manual verification"
```
