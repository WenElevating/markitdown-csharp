# PDF Image Extraction & Text Layout Improvement — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add image extraction to the PDF converter and improve text layout detection using font-size metadata.

**Architecture:** Decompose PdfConverter into focused components: content block types, image extractor, text classifier, and spatial grouper. Each component is a separate file. The refactored PdfConverter processes pages one at a time: per-page extract text + images → group by spatial proximity → render to Markdown → concatenate pages.

**Tech Stack:** C# / .NET 8, PdfPig 1.7.0-custom-5 (page.GetImages(), IPdfImage.TryGetPng, DefaultFilterProvider), xUnit

**Spec:** `docs/superpowers/specs/2026-04-12-pdf-image-extraction-design.md`

**PdfPig API notes (verified against 1.7.0-custom-5):**
- `DefaultFilterProvider` handles JPEG (DCTDecode) natively — no custom filter provider needed
- `PdfDocument.Open(path)` uses defaults — `ParsingOptions` has no `FilterProvider` property
- `IPdfImage.TryGetPng(out byte[])` returns PNG byte array
- `IPdfImage.TryGetBytes(out IReadOnlyList<byte>)` returns decoded bytes as IReadOnlyList
- `IPdfImage.RawBytes` is `IReadOnlyList<byte>` — the raw encoded bytes (NOT `byte[]`)
- `IPdfImage.Bounds` returns `PdfRectangle` with Top/Bottom/Left/Right
- `Page.Letters` returns `IReadOnlyList<Letter>`, `Letter.FontSize` is `double`

---

## File Structure

### New files

| File | Responsibility |
|------|---------------|
| `src/MarkItDown.Converters.Pdf/PdfContentBlock.cs` | Content block record types |
| `src/MarkItDown.Converters.Pdf/PdfImageExtractor.cs` | Extract images, save to disk, filter/dedup |
| `src/MarkItDown.Converters.Pdf/PdfTextClassifier.cs` | Font-size-based text row classification |
| `src/MarkItDown.Converters.Pdf/PdfContentGrouper.cs` | Spatial grouping + Markdown rendering |

### Modified files

| File | Change |
|------|--------|
| `src/MarkItDown.Core/DocumentConversionRequest.cs` | Add `AssetBasePath` property |
| `src/MarkItDown.Core/DocumentConversionResult.cs` | Add `AssetDirectory` positional parameter (default null) |
| `src/MarkItDown.Converters.Pdf/PdfConverter.cs` | Major refactor: per-page pipeline using new components |
| `src/MarkItDown.Cli/CliRunner.cs` | Set AssetBasePath on request, print AssetDirectory after conversion |
| `tests/MarkItDown.Converters.Pdf.Tests/PdfConverterTests.cs` | Add image and layout tests |

---

### Task 1: Core model changes + backward compatibility test

**Files:**
- Modify: `src/MarkItDown.Core/DocumentConversionRequest.cs`
- Modify: `src/MarkItDown.Core/DocumentConversionResult.cs`
- Test: `dotnet test MarkItDown.sln --no-restore`

- [ ] **Step 1: Add AssetBasePath to DocumentConversionRequest**

```csharp
// src/MarkItDown.Core/DocumentConversionRequest.cs
namespace MarkItDown.Core;

public sealed record DocumentConversionRequest
{
    public string? FilePath { get; init; }
    public Stream? Stream { get; init; }
    public string? Filename { get; init; }
    public string? MimeType { get; init; }
    public ILlmClient? LlmClient { get; init; }
    public string? AssetBasePath { get; init; }
}
```

- [ ] **Step 2: Add AssetDirectory to DocumentConversionResult**

```csharp
// src/MarkItDown.Core/DocumentConversionResult.cs
namespace MarkItDown.Core;

public sealed record DocumentConversionResult(
    string Kind,
    string Markdown,
    string? Title = null,
    string? AssetDirectory = null);
```

- [ ] **Step 3: Build and run ALL tests to verify backward compatibility**

Run: `dotnet test MarkItDown.sln --no-restore`
Expected: All existing tests pass. The default `null` on `AssetDirectory` preserves all 19 converter call sites.

- [ ] **Step 4: Commit**

```bash
git add src/MarkItDown.Core/DocumentConversionRequest.cs src/MarkItDown.Core/DocumentConversionResult.cs
git commit -m "feat: add AssetBasePath and AssetDirectory to conversion models"
```

---

### Task 2: Content block types

**Files:**
- Create: `src/MarkItDown.Converters.Pdf/PdfContentBlock.cs`

- [ ] **Step 1: Create content block records**

```csharp
// src/MarkItDown.Converters.Pdf/PdfContentBlock.cs
namespace MarkItDown.Converters.Pdf;

internal abstract record PdfContentBlock(double Y, double Top, double Bottom);

internal sealed record PdfTextBlock(
    double Y, double Top, double Bottom,
    string Text,
    double FontSize) : PdfContentBlock(Y, Top, Bottom);

internal sealed record PdfImageBlock(
    double Y, double Top, double Bottom,
    int PageNumber,
    int ImageIndex,
    string FileName) : PdfContentBlock(Y, Top, Bottom);
```

Note: `PdfImageBlock` stores `FileName` (relative, e.g. `page1_img0.png`) instead of full path. The full path is computed from `AssetBasePath` at render time.

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build src/MarkItDown.Converters.Pdf --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/MarkItDown.Converters.Pdf/PdfContentBlock.cs
git commit -m "feat: add PdfContentBlock record types for PDF content model"
```

---

### Task 3: Font-size text classifier

**Files:**
- Create: `src/MarkItDown.Converters.Pdf/PdfTextClassifier.cs`

- [ ] **Step 1: Create PdfTextClassifier.cs**

```csharp
// src/MarkItDown.Converters.Pdf/PdfTextClassifier.cs
using System.Text;
using UglyToad.PdfPig.Content;

namespace MarkItDown.Converters.Pdf;

internal static class PdfTextClassifier
{
    private static readonly Regex ColumnSplitRegex = new(@"\s{2,}", RegexOptions.Compiled);

    /// <summary>
    /// Extracts text blocks from a page using font-size-based classification.
    /// Groups words into rows by Y-coordinate, produces PdfTextBlock records.
    /// </summary>
    internal static List<PdfTextBlock> ClassifyTextBlocks(Page page)
    {
        var letters = page.Letters;
        if (letters.Count == 0)
        {
            return [];
        }

        var rows = GroupWordsIntoRows(page);
        var blocks = new List<PdfTextBlock>();

        foreach (var row in rows)
        {
            var text = BuildRowText(row);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var rowFontSize = row.Average(w => w.FontSize);
            var top = row.Max(w => w.BoundingBox.Top);
            var bottom = row.Min(w => w.BoundingBox.Bottom);
            var y = (top + bottom) / 2.0;

            blocks.Add(new PdfTextBlock(
                Y: y,
                Top: top,
                Bottom: bottom,
                Text: text,
                FontSize: rowFontSize));
        }

        return blocks;
    }

    /// <summary>
    /// Computes the body font size as the mode (most frequent) of letter font sizes,
    /// rounded to 1 decimal place, computed per page.
    /// </summary>
    internal static double ComputeBodyFontSize(IReadOnlyList<Letter> letters)
    {
        return letters
            .Select(l => Math.Round(l.FontSize, 1))
            .GroupBy(size => size)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;
    }

    internal static List<List<Word>> GroupWordsIntoRows(Page page)
    {
        return page.GetWords()
            .GroupBy(word => Math.Round(word.BoundingBox.Bottom, 1))
            .OrderByDescending(group => group.Key)
            .Select(group => group.OrderBy(word => word.BoundingBox.Left).ToList())
            .ToList();
    }

    internal static string BuildRowText(List<Word> row)
    {
        var builder = new StringBuilder();
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

                builder.Append(' ', spaces);
            }

            builder.Append(word.Text);
            previous = word;
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Classifies a text block's role: "heading", "body", or "caption".
    /// Heading requires large font AND line length &lt; 40 chars.
    /// </summary>
    internal static string ClassifyRole(double fontSize, double bodyFontSize, string text)
    {
        if (bodyFontSize <= 0) return "body";

        var ratio = fontSize / bodyFontSize;

        if (ratio >= 1.5)
        {
            return text.Length < 40 ? "heading" : "body";
        }

        if (ratio < 0.7)
        {
            return "caption";
        }

        return "body";
    }
}
```

- [ ] **Step 2: Build and fix any type issues**

Run: `dotnet build src/MarkItDown.Converters.Pdf --no-restore`
Expected: Build succeeds. `Page.Letters` returns `IReadOnlyList<Letter>` where `Letter` is in `UglyToad.PdfPig.Graphics` namespace.

- [ ] **Step 3: Commit**

```bash
git add src/MarkItDown.Converters.Pdf/PdfTextClassifier.cs
git commit -m "feat: add PdfTextClassifier for font-size-based text classification"
```

---

### Task 4: Image extraction

**Files:**
- Create: `src/MarkItDown.Converters.Pdf/PdfImageExtractor.cs`

- [ ] **Step 1: Create PdfImageExtractor.cs**

```csharp
// src/MarkItDown.Converters.Pdf/PdfImageExtractor.cs
using System.Security.Cryptography;
using UglyToad.PdfPig.Content;

namespace MarkItDown.Converters.Pdf;

internal static class PdfImageExtractor
{
    private const int MinImageDimension = 20;
    private const double MinAreaRatio = 0.01;

    /// <summary>
    /// Extracts images from a PDF page, saves them to disk, returns PdfImageBlock records.
    /// seenHashes is passed across pages for cross-page deduplication.
    /// </summary>
    internal static List<PdfImageBlock> ExtractImages(
        Page page,
        int pageNumber,
        string assetBasePath,
        double pageArea,
        Dictionary<string, string> seenHashes)
    {
        var images = page.GetImages().ToList();
        if (images.Count == 0)
        {
            return [];
        }

        Directory.CreateDirectory(assetBasePath);
        var blocks = new List<PdfImageBlock>();

        for (var i = 0; i < images.Count; i++)
        {
            var pdfImage = images[i];

            if (!MeetsSizeThreshold(pdfImage, pageArea))
            {
                continue;
            }

            var saved = SaveImage(pdfImage, pageNumber, i, assetBasePath, seenHashes);
            if (saved is null)
            {
                continue;
            }

            var bounds = pdfImage.Bounds;
            var y = (bounds.Top + bounds.Bottom) / 2.0;

            blocks.Add(new PdfImageBlock(
                Y: y,
                Top: bounds.Top,
                Bottom: bounds.Bottom,
                PageNumber: pageNumber,
                ImageIndex: i,
                FileName: saved));
        }

        return blocks;
    }

    private static bool MeetsSizeThreshold(IPdfImage image, double pageArea)
    {
        if (image.WidthInSamples < MinImageDimension || image.HeightInSamples < MinImageDimension)
        {
            return false;
        }

        var imageArea = image.Bounds.Width * image.Bounds.Height;
        if (pageArea > 0 && imageArea / pageArea < MinAreaRatio)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Saves an image to disk. Returns the filename (relative) or null if extraction failed.
    /// Deduplicates via SHA256 — if the same image bytes were saved before, returns the existing filename.
    /// </summary>
    private static string? SaveImage(
        IPdfImage image,
        int pageNumber,
        int imageIndex,
        string assetBasePath,
        Dictionary<string, string> seenHashes)
    {
        byte[]? imageBytes = null;
        string extension;

        if (image.TryGetPng(out var pngBytes))
        {
            imageBytes = pngBytes;
            extension = ".png";
        }
        else if (TryGetJpegBytes(image, out var jpegBytes))
        {
            imageBytes = jpegBytes;
            extension = ".jpg";
        }
        else
        {
            return null;
        }

        // Deduplication via SHA256
        var hash = Convert.ToHexString(SHA256.HashData(imageBytes));
        if (seenHashes.TryGetValue(hash, out var existingFileName))
        {
            return existingFileName;
        }

        var fileName = $"page{pageNumber}_img{imageIndex}{extension}";
        var fullPath = Path.Combine(assetBasePath, fileName);
        File.WriteAllBytes(fullPath, imageBytes);

        seenHashes[hash] = fileName;
        return fileName;
    }

    /// <summary>
    /// Attempts to get JPEG bytes from an IPdfImage.
    /// Checks RawBytes first (may be the original JPEG), then TryGetBytes as fallback.
    /// Note: RawBytes is IReadOnlyList&lt;byte&gt;, TryGetBytes is on IPdfImage interface directly.
    /// </summary>
    private static bool TryGetJpegBytes(IPdfImage image, out byte[] bytes)
    {
        // RawBytes is IReadOnlyList<byte> — check JPEG magic bytes via indexing
        var raw = image.RawBytes;
        if (raw is not null && raw.Count >= 2 && raw[0] == 0xFF && raw[1] == 0xD8)
        {
            bytes = raw.ToArray();
            return true;
        }

        // TryGetBytes is on IPdfImage interface (not just InlineImage)
        if (image.TryGetBytes(out var decoded))
        {
            if (decoded.Count >= 2 && decoded[0] == 0xFF && decoded[1] == 0xD8)
            {
                bytes = decoded.ToArray();
                return true;
            }
        }

        bytes = [];
        return false;
    }
}
```

Note: `IPdfImage.RawBytes` is `IReadOnlyList<byte>` — use `.Count` and indexing for checks, `.ToArray()` when `byte[]` is needed. `TryGetBytes(out IReadOnlyList<byte>)` is on the `IPdfImage` interface directly — no cast needed.

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build src/MarkItDown.Converters.Pdf --no-restore`
Expected: Build succeeds. If `InlineImage` type is not accessible or `TryGetBytes` is on the interface, adjust accordingly based on compiler errors.

- [ ] **Step 3: Commit**

```bash
git add src/MarkItDown.Converters.Pdf/PdfImageExtractor.cs
git commit -m "feat: add PdfImageExtractor for image extraction and deduplication"
```

---

### Task 5: Spatial content grouper + table detection

**Files:**
- Create: `src/MarkItDown.Converters.Pdf/PdfContentGrouper.cs`

This component handles spatial grouping AND integrates the existing table detection logic.

- [ ] **Step 1: Create PdfContentGrouper.cs**

```csharp
// src/MarkItDown.Converters.Pdf/PdfContentGrouper.cs
using System.Text;
using System.Text.RegularExpressions;

namespace MarkItDown.Converters.Pdf;

internal static class PdfContentGrouper
{
    private static readonly Regex ColumnSplitRegex = new(@"\s{2,}", RegexOptions.Compiled);

    /// <summary>
    /// Groups content blocks from a single page by spatial proximity,
    /// then renders each group to Markdown. Processes one page at a time.
    /// </summary>
    internal static string RenderPage(
        List<PdfContentBlock> blocks,
        double bodyFontSize)
    {
        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        // Sort descending by Y (top of page first in PDF coordinates)
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
            // Gap between current group's bottom edge and next block's top edge
            // In PDF coords: higher Y = higher on page. Top > Bottom.
            // groupBottom is the lowest Y in the group; block.Top is the highest Y of the new block.
            // If block.Top > groupBottom, they overlap. If block.Top < groupBottom, there's a gap.
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

    /// <summary>
    /// Detects if consecutive text blocks form a table (3+ columns, 2+ rows).
    /// Returns rendered Markdown table or null if not a table.
    /// </summary>
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
                break; // Non-table row breaks the sequence
            }
        }

        if (tableRows.Count < 2) return null;

        // Normalize column count
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

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build src/MarkItDown.Converters.Pdf --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/MarkItDown.Converters.Pdf/PdfContentGrouper.cs
git commit -m "feat: add PdfContentGrouper with spatial grouping and table detection"
```

---

### Task 6: Refactor PdfConverter — per-page pipeline

**Files:**
- Modify: `src/MarkItDown.Converters.Pdf/PdfConverter.cs`

Key design: **Process pages one at a time.** Each page produces its own set of text + image blocks, which are grouped and rendered independently. Page outputs are concatenated. This avoids the page-number grouping bug and keeps the pipeline simple.

- [ ] **Step 1: Rewrite PdfConverter.cs**

```csharp
// src/MarkItDown.Converters.Pdf/PdfConverter.cs
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
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
            // Open with default options — DefaultFilterProvider handles JPEG natively
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
            var pageMarkdowns = new List<string>();
            double? bodyFontSize = null;
            var seenHashes = new Dictionary<string, string>(); // Cross-page image deduplication

            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = pages[pageIndex];
                var pageNumber = pageIndex + 1;

                // Compute body font size from first page that has letters
                if (bodyFontSize is null && page.Letters.Count > 0)
                {
                    bodyFontSize = PdfTextClassifier.ComputeBodyFontSize(page.Letters);
                }

                var pageSize = page.Size;
                var pageArea = pageSize.Width * pageSize.Height;

                // Per-page pipeline: extract text blocks + image blocks
                var textBlocks = PdfTextClassifier.ClassifyTextBlocks(page);

                var imageBlocks = new List<PdfImageBlock>();
                if (assetBasePath is not null)
                {
                    imageBlocks = PdfImageExtractor.ExtractImages(
                        page, pageNumber, assetBasePath, pageArea, seenHashes);
                }

                // Merge and render this page
                var allBlocks = textBlocks.Cast<PdfContentBlock>()
                    .Concat(imageBlocks.Cast<PdfContentBlock>())
                    .ToList();

                var fontSize = bodyFontSize ?? 12.0;
                var pageMarkdown = PdfContentGrouper.RenderPage(allBlocks, fontSize);

                if (!string.IsNullOrWhiteSpace(pageMarkdown))
                {
                    pageMarkdowns.Add(pageMarkdown);
                }
            }

            var markdown = string.Join(
                $"{Environment.NewLine}{Environment.NewLine}", pageMarkdowns).Trim();

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

Key changes from current code:
- No custom `ParsingOptions` — defaults work for image extraction
- Per-page pipeline: classify text → extract images → group → render
- Scanned PDF check considers images
- Sets `AssetDirectory` on result
- Table detection is handled inside `PdfContentGrouper.RenderGroup`
- Old helpers (`ExtractPageText`, `ConvertPageTextToMarkdown`, `LooksLikeHeading`, etc.) are all removed

- [ ] **Step 2: Build**

Run: `dotnet build src/MarkItDown.Converters.Pdf --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Run existing PDF tests**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore`
Expected: The 3 existing tests should still pass:
- `ConvertAsync_ExtractsTextFromPdf` — text extraction still works
- `ConvertAsync_ExtractsTableLikeMarkdown` — table detection preserved in PdfContentGrouper
- `ConvertAsync_RejectsScannedPdf` — scanned PDF rejection still works (no images in fixture)

If `ConvertAsync_ExtractsTextFromPdf` assertion fails because heading format changed (now `## ` instead of raw text), update the assertion to match the new output format.

- [ ] **Step 4: Commit**

```bash
git add src/MarkItDown.Converters.Pdf/PdfConverter.cs
git commit -m "refactor: rewrite PdfConverter with per-page image extraction and font-size layout"
```

---

### Task 7: CLI integration

**Files:**
- Modify: `src/MarkItDown.Cli/CliRunner.cs`

- [ ] **Step 1: Add ComputeAssetPath helper and set AssetBasePath on requests**

Add this method to `CliRunner`:

```csharp
private static string? ComputeAssetPath(string inputPath, string? outputPath)
{
    if (!string.IsNullOrWhiteSpace(outputPath))
    {
        // -o mode: images next to output file
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        var stem = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(dir ?? ".", stem + "_files");
    }

    // Stdout mode: images next to input file
    var inputDir = Path.GetDirectoryName(Path.GetFullPath(inputPath));
    var inputStem = Path.GetFileNameWithoutExtension(inputPath);
    return Path.Combine(inputDir ?? ".", inputStem + "_files");
}
```

Update all request construction sites (4 places: `ConvertSingleInvokeAsync`, `ConvertSingleAsync`, and the two batch methods) to add `AssetBasePath`:

```csharp
var request = new DocumentConversionRequest
{
    FilePath = inputPath,
    LlmClient = llmClient,
    AssetBasePath = ComputeAssetPath(inputPath, outputPath)
};
```

- [ ] **Step 2: Print asset directory after conversion**

In `ConvertSingleInvokeAsync` and `ConvertSingleAsync`, after the conversion result is obtained:

```csharp
if (!string.IsNullOrWhiteSpace(result.AssetDirectory))
{
    Console.WriteLine($"Images saved to: {Path.GetFullPath(result.AssetDirectory)}");
}
```

- [ ] **Step 3: Build**

Run: `dotnet build MarkItDown.sln --no-restore`
Expected: Build succeeds.

- [ ] **Step 4: Run CLI tests**

Run: `dotnet test tests/MarkItDown.Cli.Tests --no-restore`
Expected: All CLI tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/MarkItDown.Cli/CliRunner.cs
git commit -m "feat: integrate image asset path into CLI conversion flow"
```

---

### Task 8: Tests — image extraction and layout

**Files:**
- Modify: `tests/MarkItDown.Converters.Pdf.Tests/PdfConverterTests.cs`

**Fixture strategy:** Use the existing `sample.pdf` for text layout tests. For image tests, convert the user's `PuYu.pdf` manually first to verify images exist, then create a minimal test. Since creating PDFs with embedded images requires a PDF writing library not in this project, use a pragmatic approach: test with existing PDFs for text layout, and test image extraction with any PDF known to contain images.

- [ ] **Step 1: Add font-size classification test (uses existing sample.pdf)**

```csharp
[Fact]
public async Task ConvertAsync_ClassifiesHeadingsByFontSize()
{
    var result = await _converter.ConvertAsync(
        new DocumentConversionRequest { FilePath = FixturePath.For("sample.pdf") });

    // Text should be extracted
    Assert.False(string.IsNullOrWhiteSpace(result.Markdown));

    // Not every line should be a heading (old behavior had ## on every short line)
    var lines = result.Markdown.Split('\n')
        .Where(l => !string.IsNullOrWhiteSpace(l))
        .ToList();
    var headingCount = lines.Count(l => l.StartsWith("## "));
    var nonHeadingCount = lines.Count - headingCount;
    Assert.True(nonHeadingCount > 0, "Expected some non-heading body text lines");
}
```

- [ ] **Step 2: Add image extraction test (uses existing PDFs to verify no crash)**

```csharp
[Fact]
public async Task ConvertAsync_WithAssetBasePath_DoesNotCrash()
{
    // Verify that providing AssetBasePath doesn't break conversion
    // even if the PDF has no images
    var tempDir = Path.Combine(Path.GetTempPath(), $"pdf-test-{Guid.NewGuid():N}");
    try
    {
        var assetPath = Path.Combine(tempDir, "output_files");
        var result = await _converter.ConvertAsync(
            new DocumentConversionRequest
            {
                FilePath = FixturePath.For("sample.pdf"),
                AssetBasePath = assetPath
            });

        Assert.False(string.IsNullOrWhiteSpace(result.Markdown));
    }
    finally
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }
}
```

- [ ] **Step 3: Add scanned PDF test update**

```csharp
[Fact]
public async Task ConvertAsync_RejectsScannedPdfWithoutImages()
{
    // scanned.pdf has no images and < 20 letters — should still throw
    var exception = await Assert.ThrowsAsync<ConversionException>(() =>
        _converter.ConvertAsync(new DocumentConversionRequest
        {
            FilePath = FixturePath.For("scanned.pdf")
        }));

    Assert.Contains("Scanned or image-only PDFs are not supported", exception.Message);
}
```

- [ ] **Step 4: Add null AssetBasePath test**

```csharp
[Fact]
public async Task ConvertAsync_WithoutAssetBasePath_ProducesTextOnly()
{
    var result = await _converter.ConvertAsync(
        new DocumentConversionRequest { FilePath = FixturePath.For("sample.pdf") });

    // AssetDirectory should be null when no AssetBasePath provided
    Assert.Null(result.AssetDirectory);
    Assert.False(string.IsNullOrWhiteSpace(result.Markdown));
}
```

- [ ] **Step 5: Run all PDF tests**

Run: `dotnet test tests/MarkItDown.Converters.Pdf.Tests --no-restore`
Expected: All tests pass.

- [ ] **Step 6: Run full test suite**

Run: `dotnet test MarkItDown.sln --no-restore`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add tests/MarkItDown.Converters.Pdf.Tests/PdfConverterTests.cs
git commit -m "test: add PDF layout classification and image extraction tests"
```

---

### Task 9: Manual verification with PuYu.pdf

**Files:** None (verification only)

- [ ] **Step 1: Convert PuYu.pdf with output file**

```bash
dotnet run --project src/MarkItDown.Cli -- "D:\Test\PuYu.pdf" -o "D:\Test\PuYu-new.md"
```

Verify:
- `D:\Test\PuYu-new_files/` directory created with PNG/JPG images
- Console prints `Images saved to: ...` message
- Markdown contains `![image](./PuYu-new_files/pageN_imgM.png)` references
- Text layout is cleaner: headings from large fonts, body text as paragraphs
- Images appear at correct positions relative to surrounding text

- [ ] **Step 2: Convert PuYu.pdf to stdout**

```bash
dotnet run --project src/MarkItDown.Cli -- "D:\Test\PuYu.pdf"
```

Verify:
- Markdown output includes image references
- `PuYu_files/` directory created next to the input PDF

- [ ] **Step 3: Compare with original conversion**

Compare `D:\Test\PuYu-new.md` with `D:\Test\PuYu.md`. The new output should:
- Have image references where images exist in the PDF
- Have cleaner heading/body separation
- Not lose any text content that was present before

- [ ] **Step 4: Final commit if any fixes needed**

```bash
git add -A
git commit -m "fix: address issues found during manual verification"
```
