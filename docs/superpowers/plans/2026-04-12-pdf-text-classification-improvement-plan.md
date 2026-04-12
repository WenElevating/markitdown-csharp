# PDF Text Classification Improvement — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix italic over-detection, add paragraph merging, TOC detection, and page number filtering for better PDF-to-Markdown quality on technical documents.

**Architecture:** Incremental improvements to existing components. Remove font-ratio caption logic from `ClassifyRole`, add paragraph merging and TOC detection to `PdfLayoutAnalyzer`, add bold detection to `PdfTextClassifier`.

**Tech Stack:** C# / .NET 8, PdfPig 1.7.0-custom-5, xUnit

**Spec:** `docs/superpowers/specs/2026-04-12-pdf-text-classification-improvement-design.md`

---

## File Structure

### Modified files

| File | Change |
|------|--------|
| `src/MarkItDown.Converters.Pdf/PdfContentBlock.cs` | Add `IsBold` to `PdfTextBlock` |
| `src/MarkItDown.Converters.Pdf/PdfTextClassifier.cs` | Remove caption from `ClassifyRole`, add bold detection, adjust heading thresholds |
| `src/MarkItDown.Converters.Pdf/PdfLayoutAnalyzer.cs` | Add `MergeParagraphs`, `IsTocPage`, enhance page number regex |
| `src/MarkItDown.Converters.Pdf/PdfContentGrouper.cs` | Update rendering for merged paragraphs (no code change expected — merged blocks are already text blocks) |
| `src/MarkItDown.Converters.Pdf/PdfConverter.cs` | Add TOC page skip, paragraph merge step in pipeline |
| `tests/MarkItDown.Converters.Pdf.Tests/PdfLayoutAnalyzerTests.cs` | Add paragraph merge, TOC detection tests |
| `tests/MarkItDown.Converters.Pdf.Tests/PdfConverterTests.cs` | Update assertions if output format changed |

---

### Task 1: Remove caption from ClassifyRole + add IsBold to PdfTextBlock

**Files:**
- Modify: `src/MarkItDown.Converters.Pdf/PdfContentBlock.cs`
- Modify: `src/MarkItDown.Converters.Pdf/PdfTextClassifier.cs`

- [ ] **Step 1: Add IsBold to PdfTextBlock**

In `PdfContentBlock.cs`, add `bool IsBold = false` to `PdfTextBlock`:

```csharp
internal sealed record PdfTextBlock(
    double Y, double Top, double Bottom,
    double Left, double Right,
    string Text,
    double FontSize,
    bool IsHeaderFooter = false,
    bool IsBold = false) : PdfContentBlock(Y, Top, Bottom, Left, Right, IsHeaderFooter);
```

- [ ] **Step 2: Add bold detection to PdfTextClassifier.ClassifyTextBlocks**

In `ClassifyTextBlocks`, after computing `rowFontSize`, check if letters in the row use a bold font. PdfPig's `Letter` has a `Font` property — check the font name for bold indicators:

```csharp
var isBold = row.SelectMany(w => w.Letters)
    .Any(l => l.Font?.Name?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true
           || l.Font?.Name?.Contains("黑体", StringComparison.OrdinalIgnoreCase) == true
           || l.Font?.Name?.Contains("粗体", StringComparison.OrdinalIgnoreCase) == true);
```

Add `IsBold: isBold` to the `PdfTextBlock` constructor call.

- [ ] **Step 3: Remove caption from ClassifyRole, improve heading detection**

Replace `ClassifyRole` in `PdfTextClassifier.cs`:

```csharp
internal static string ClassifyRole(double fontSize, double bodyFontSize, string text, bool isBold = false)
{
    if (bodyFontSize <= 0) return "body";

    var ratio = fontSize / bodyFontSize;

    // Bold + short line → likely heading
    if (isBold && text.Length < 80)
    {
        return "heading";
    }

    // Significantly larger font → heading (if short enough)
    if (ratio >= 1.5 && text.Length < 60)
    {
        return "heading";
    }

    return "body";
}
```

Key changes:
- Removed "caption" case entirely — captions are now only detected by `PdfLayoutAnalyzer.DetectCaptions` (spatial proximity to images)
- Bold text < 80 chars → heading
- Large font (≥1.5x) < 60 chars → heading (increased from 40)
- Everything else → body

- [ ] **Step 4: Update ClassifyRole callers**

In `PdfContentGrouper.cs`, the call to `ClassifyRole` needs to pass `isBold`. Update:

```csharp
var role = PdfTextClassifier.ClassifyRole(text.FontSize, bodyFontSize, text.Text, text.IsBold);
```

- [ ] **Step 5: Build and run all tests**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore`
Expected: All 24 tests pass. Some output may change (less italic text) — this is expected improvement.

- [ ] **Step 6: Commit**

```bash
git add src/MarkItDown.Converters.Pdf/PdfContentBlock.cs src/MarkItDown.Converters.Pdf/PdfTextClassifier.cs src/MarkItDown.Converters.Pdf/PdfContentGrouper.cs
git commit -m "fix: remove font-ratio caption detection, add bold-based heading classification"
```

---

### Task 2: Add paragraph merging to PdfLayoutAnalyzer

**Files:**
- Modify: `src/MarkItDown.Converters.Pdf/PdfLayoutAnalyzer.cs`
- Modify: `tests/MarkItDown.Converters.Pdf.Tests/PdfLayoutAnalyzerTests.cs`

- [ ] **Step 1: Write tests for paragraph merging**

Add to `PdfLayoutAnalyzerTests.cs`:

```csharp
[Fact]
public void MergeParagraphs_ConsecutiveSameFont_Merges()
{
    var blocks = new List<PdfContentBlock>
    {
        Txt(700, 680, 50, 400, "First line of paragraph.", 12.0),
        Txt(675, 655, 50, 400, "Second line of same paragraph.", 12.0),
        Txt(650, 630, 50, 400, "Third line.", 12.0),
    };

    var result = PdfLayoutAnalyzer.MergeParagraphs(blocks, 12.0);

    Assert.Single(result);
    var merged = (PdfTextBlock)result[0];
    Assert.Equal("First line of paragraph. Second line of same paragraph. Third line.", merged.Text);
    Assert.Equal(700, merged.Top);
    Assert.Equal(630, merged.Bottom);
}

[Fact]
public void MergeParagraphs_DifferentFontSize_NoMerge()
{
    var blocks = new List<PdfContentBlock>
    {
        Txt(700, 680, 50, 400, "Heading text", 18.0),
        Txt(660, 640, 50, 400, "Body text", 12.0),
    };

    var result = PdfLayoutAnalyzer.MergeParagraphs(blocks, 12.0);
    Assert.Equal(2, result.Count);
}

[Fact]
public void MergeParagraphs_LargeGap_NoMerge()
{
    var blocks = new List<PdfContentBlock>
    {
        Txt(700, 680, 50, 400, "First paragraph.", 12.0),
        Txt(600, 580, 50, 400, "Second paragraph after gap.", 12.0),
    };

    var result = PdfLayoutAnalyzer.MergeParagraphs(blocks, 12.0);
    Assert.Equal(2, result.Count);
}

[Fact]
public void MergeParagraphs_ImageBlock_BreaksMerge()
{
    var blocks = new List<PdfContentBlock>
    {
        Txt(700, 680, 50, 400, "Text before image.", 12.0),
        Img(660, 600, 50, 400),
        Txt(580, 560, 50, 400, "Text after image.", 12.0),
    };

    var result = PdfLayoutAnalyzer.MergeParagraphs(blocks, 12.0);
    Assert.Equal(3, result.Count);
}

[Fact]
public void MergeParagraphs_DifferentAlignment_NoMerge()
{
    var blocks = new List<PdfContentBlock>
    {
        Txt(700, 680, 50, 400, "Left aligned text.", 12.0),
        Txt(675, 655, 200, 500, "Indented text.", 12.0),
    };

    var result = PdfLayoutAnalyzer.MergeParagraphs(blocks, 12.0);
    Assert.Equal(2, result.Count);
}

[Fact]
public void MergeParagraphs_EmptyInput_ReturnsEmpty()
{
    var result = PdfLayoutAnalyzer.MergeParagraphs([], 12.0);
    Assert.Empty(result);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore --filter "FullyQualifiedName~MergeParagraphs"`
Expected: FAIL — `MergeParagraphs` method doesn't exist yet.

- [ ] **Step 3: Implement MergeParagraphs in PdfLayoutAnalyzer**

Add to `PdfLayoutAnalyzer.cs`:

```csharp
/// <summary>
/// Merges consecutive body-text blocks into paragraphs when they share
/// the same font size, similar left alignment, and small vertical gap.
/// Inspired by OpenDataLoader's ParagraphProcessor.
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
            // Image breaks paragraph
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

        // Check merge criteria
        var sameFontSize = Math.Abs(currentParagraph.FontSize - text.FontSize) < fontSizeTolerance;
        var smallGap = currentParagraph.Bottom - text.Top < maxGap && currentParagraph.Bottom > text.Top;
        var sameAlignment = Math.Abs(currentParagraph.Left - text.Left) < alignmentTolerance;

        if (sameFontSize && smallGap && sameAlignment)
        {
            // Merge: concatenate text, expand bounding box
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
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore --filter "FullyQualifiedName~MergeParagraphs"`
Expected: All 6 new tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MarkItDown.Converters.Pdf/PdfLayoutAnalyzer.cs tests/MarkItDown.Converters.Pdf.Tests/PdfLayoutAnalyzerTests.cs
git commit -m "feat: add paragraph merging based on font size and alignment consistency"
```

---

### Task 3: Add TOC detection and page number filtering

**Files:**
- Modify: `src/MarkItDown.Converters.Pdf/PdfLayoutAnalyzer.cs`
- Modify: `tests/MarkItDown.Converters.Pdf.Tests/PdfLayoutAnalyzerTests.cs`

- [ ] **Step 1: Write tests for TOC detection**

```csharp
[Fact]
public void IsTocPage_MostlyPageNumbers_ReturnsTrue()
{
    var blocks = new List<PdfContentBlock>
    {
        Txt(700, 680, 50, 500, "Chapter 1 Introduction....... 1", 10),
        Txt(670, 650, 50, 500, "Chapter 2 Getting Started..... 15", 10),
        Txt(640, 620, 50, 500, "Chapter 3 Advanced Topics...... 33", 10),
        Txt(610, 590, 50, 500, "Chapter 4 API Reference........ 47", 10),
    };

    Assert.True(PdfLayoutAnalyzer.IsTocPage(blocks));
}

[Fact]
public void IsTocPage_NormalContent_ReturnsFalse()
{
    var blocks = new List<PdfContentBlock>
    {
        Txt(700, 680, 50, 500, "This is a normal paragraph with regular body text content.", 12),
        Txt(660, 640, 50, 500, "Another paragraph of normal content without page numbers.", 12),
        Txt(620, 600, 50, 500, "More body text that continues the discussion.", 12),
    };

    Assert.False(PdfLayoutAnalyzer.IsTocPage(blocks));
}

[Fact]
public void IsTocPage_EmptyPage_ReturnsFalse()
{
    Assert.False(PdfLayoutAnalyzer.IsTocPage([]));
}
```

- [ ] **Step 2: Write test for page number filtering**

```csharp
[Fact]
public void DetectHeadersFooters_RomanPageNumber_Marked()
{
    var pageHeight = 800.0;
    var page = new List<PdfContentBlock>
    {
        Txt(10, 5, 350, 450, "iv", 10),
        Txt(700, 680, 50, 500, "Content"),
    };
    var allPages = new List<List<PdfContentBlock>> { page };

    PdfLayoutAnalyzer.DetectHeadersFooters(allPages, pageHeight);

    Assert.True(((PdfTextBlock)page[0]).IsHeaderFooter);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore --filter "FullyQualifiedName~IsTocPage|FullyQualifiedName~RomanPageNumber"`
Expected: FAIL — methods don't exist yet.

- [ ] **Step 4: Implement IsTocPage and enhance page number filtering**

Add to `PdfLayoutAnalyzer.cs`:

```csharp
private static readonly Regex TocPageNumberPattern = new(@"[\.…]+\s*\d+\s*$|\s{3,}\d+\s*$", RegexOptions.Compiled);
private static readonly Regex RomanPageNumberPattern = new(@"^[ivxlc]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

/// <summary>
/// Detects whether a page is a Table of Contents.
/// A TOC page has > 50% of text lines ending with page number patterns
/// (e.g., "Chapter 1 .... 15" or "Section    23").
/// </summary>
internal static bool IsTocPage(List<PdfContentBlock> blocks)
{
    if (blocks.Count == 0) return false;

    var textBlocks = blocks.OfType<PdfTextBlock>().ToList();
    if (textBlocks.Count < 3) return false;

    var linesWithPageNumbers = textBlocks.Count(t => TocPageNumberPattern.IsMatch(t.Text.Trim()));
    var ratio = (double)linesWithPageNumbers / textBlocks.Count;

    return ratio > 0.5;
}
```

In the `DetectHeadersFooters` method, add roman numeral detection alongside the existing page number pattern:

Find the page number regex pattern and update it to also match roman numerals. Add after the existing page number check:

```csharp
// Roman numeral page numbers (iv, viii, xii, etc.)
if (RomanPageNumberPattern.IsMatch(text.Text.Trim()) && text.Text.Trim().Length <= 5)
{
    shouldMark = true;
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/MarkItDown.Converters.Pdf/PdfLayoutAnalyzer.cs tests/MarkItDown.Converters.Pdf.Tests/PdfLayoutAnalyzerTests.cs
git commit -m "feat: add TOC page detection and roman numeral page number filtering"
```

---

### Task 4: Integrate into pipeline — PdfContentGrouper and PdfConverter

**Files:**
- Modify: `src/MarkItDown.Converters.Pdf/PdfContentGrouper.cs`
- Modify: `src/MarkItDown.Converters.Pdf/PdfConverter.cs`

- [ ] **Step 1: Add paragraph merge step to PdfContentGrouper.RenderPage**

In `RenderPage`, after `AnalyzeReadingOrder` and before `DetectCaptions`, add:

```csharp
// Merge consecutive body paragraphs
ordered = PdfLayoutAnalyzer.MergeParagraphs(ordered, bodyFontSize);
```

- [ ] **Step 2: Add TOC page skip to PdfConverter**

In `PdfConverter.ConvertAsync`, inside the per-page loop (Pass 2), after building `allBlocks` and before calling `RenderPage`, add:

```csharp
// Skip TOC pages (unless it's the only content page)
if (pages.Count > 1 && PdfLayoutAnalyzer.IsTocPage(allBlocks))
{
    continue;
}
```

- [ ] **Step 3: Build and run all tests**

Run: `dotnet build src/MarkItDown.Converters.Pdf --no-restore`
Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore`
Expected: All tests pass.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test MarkItDown.sln --no-restore`
Expected: All 109+ tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/MarkItDown.Converters.Pdf/PdfContentGrouper.cs src/MarkItDown.Converters.Pdf/PdfConverter.cs
git commit -m "feat: integrate paragraph merging and TOC detection into PDF pipeline"
```

---

### Task 5: Verify with dpymst.pdf

**Files:** None (verification only)

- [ ] **Step 1: Convert dpymst.pdf**

```bash
dotnet run --project src/MarkItDown.Cli -- "D:\Test\dpymst.pdf" -o "D:\Test\dpymst-v2.md"
```

Verify:
- Body text is NOT italic (no `*...*` wrapping on normal paragraphs)
- Consecutive lines are merged into paragraphs
- TOC pages are skipped (or minimal)
- Page numbers (roman, arabic) don't appear in output
- Headings still detected (chapter titles get `##`)
- Tables still work
- Images still extracted correctly

- [ ] **Step 2: Also verify PuYu.pdf still works**

```bash
dotnet run --project src/MarkItDown.Cli -- "D:\Test\PuYu.pdf" -o "D:\Test\PuYu-v3.md"
```

Verify no regression:
- Images extracted and referenced correctly
- Multi-column content in reading order
- Headings detected

- [ ] **Step 3: Final commit if any fixes needed**

```bash
git add -A
git commit -m "fix: address issues found during verification"
```
