# PDF Image Extraction & Text Layout Improvement

**Date:** 2026-04-12
**Status:** Approved

## Problem

The current `PdfConverter` only extracts text from PDFs via `page.GetWords()` and `page.Letters`. Images are completely ignored. For presentation-style PDFs (e.g. PuYu.pdf), the output is a wall of scattered text fragments with no visual content.

## Goal

1. Extract images from PDF pages and save them as files
2. Interleave images into Markdown output at correct reading positions
3. Improve text layout detection (headings, body, captions) using font size metadata

## Design

### Content Block Model

Replace the text-only extraction with a unified content block model. Each page produces an ordered list of blocks sorted by vertical position.

```
PdfPage → [TextBlock, ImageBlock, TextBlock, ImageBlock, ...]
                ↓ grouped by spatial proximity
              [Group1, Group2, Group3, ...]
                ↓ group-internal order: image first, then text
                ↓ group-external order: by Y coordinate
              Markdown output
```

**Types:**

```csharp
internal abstract class PdfContentBlock
{
    public required double Y { get; init; }
    public required double Top { get; init; }
    public required double Bottom { get; init; }
}

internal sealed class PdfTextBlock : PdfContentBlock
{
    public required string Text { get; init; }
    public required double FontSize { get; init; }
}

internal sealed class PdfImageBlock : PdfContentBlock
{
    public required int PageNumber { get; init; }
    public required int ImageIndex { get; init; }
    public required string FilePath { get; init; }
}
```

### Spatial Grouping

Blocks that overlap or are adjacent in Y space are grouped into a logical unit:

1. **Text on image** — text coordinates fall within an image's `Bounds` → image first, text after
2. **Caption below image** — text Y is slightly below image bottom, X range overlaps → group as figure with caption (`![...](path)\n*caption text*`)
3. **Independent** — no spatial relationship → separate groups

Grouping algorithm:
- Sort all blocks by Y coordinate
- Iterate and merge: if next block's Top overlaps current group's Bottom (within a threshold), merge into same group
- Within each group: image blocks first, then text blocks

### Image Extraction

Use PdfPig's `page.GetImages()` API:

1. Call `page.GetImages()` per page → `IEnumerable<IPdfImage>`
2. Try `TryGetPng()` first for PNG output
3. If PNG fails, try `TryGetBytes()` + detect JPEG magic bytes (`0xFF 0xD8`) → save as `.jpg`
4. If both fail → insert `<!-- image extraction failed: page N, img M -->` comment

**Filter provider:** Register a custom `IFilterProvider` with DCTDecode (JPEG), JBIG2, and JPX (JPEG2000) filters to handle all common image compression formats.

**Image filtering:**
- Width or height < 20px → discard (decorative lines, bullet icons)
- Area < 1% of page area → discard (tiny icons)
- Duplicate images (same byte hash) → save once, reference multiple times

### Text Layout Heuristics

Replace the current `LooksLikeHeading` heuristic with font-size-based detection:

1. Collect all `Letter` objects per page
2. Compute font size **mode** (most frequent size) = "body baseline"
3. Classify by ratio to baseline:
   - Size >= baseline * 1.5 → **heading** → `## text`
   - Size between baseline * 0.7 and 1.5 → **body** → normal paragraph
   - Size < baseline * 0.7 → **caption/footnote** → `*text*` (italic)
4. Heading criteria: large font + line length < 40 chars

**Paragraph merging:** Consecutive body-text lines with similar font size are merged into a single paragraph (space-separated).

**Table detection:** Retain existing `CollectTableRows` logic. Add Y-coordinate proximity check for multi-column text detection.

### Image Output Strategy

**Directory structure** when `-o output.md` is specified:

```
output.md
output_files/
  page1_img0.png
  page1_img1.png
  page2_img0.png
```

Naming: `page{page-number}_img{page-internal-index}.png`, page numbers start at 1.

**Stdout mode:** When no `-o` is specified, create `{pdf-stem}_files/` in the same directory as the input PDF. Markdown references use relative paths.

### DocumentConversionResult Extension

```csharp
public sealed record DocumentConversionResult(
    string Kind,
    string Markdown,
    string? Title = null,
    string? AssetDirectory = null  // directory containing extracted images
);
```

CLI layer checks `AssetDirectory` after conversion and prints: `Images saved to: {path}/`

### Scanned PDF Handling

Current: total letters < 20 → throw `ConversionException`.

New:
- Letters < 20 but has images → extract images, add OCR-placeholder comments
- Letters < 20 and no images → keep existing error

### Error Handling

- Image extraction failure per-image → HTML comment placeholder, don't abort
- Missing filter decoder → same, comment placeholder
- Empty page (no text, no images) → skip
- Page with only images, no text → output only image references

### Out of Scope

- OCR
- PDF form fields / annotations
- Vector graphics (SVG paths) to image conversion
- Full-page rendering as image
- Async image extraction (PdfPig is synchronous)

## Files to Modify

| File | Change |
|------|--------|
| `src/MarkItDown.Core/DocumentConversionResult.cs` | Add `AssetDirectory` field |
| `src/MarkItDown.Converters.Pdf/PdfConverter.cs` | Major refactor: content block model, image extraction, spatial grouping, font-size layout |
| `src/MarkItDown.Cli/CliRunner.cs` | Print asset directory info after conversion |
| `tests/MarkItDown.Converters.Pdf.Tests/PdfConverterTests.cs` | Add image extraction tests, layout tests |

## Test Strategy

- Unit tests with a fixture PDF containing text + images
- Test: images extracted and saved to correct directory
- Test: Markdown contains image references at correct positions
- Test: heading/body/caption classification by font size
- Test: spatial grouping of overlapping text+image
- Test: small images filtered out
- Test: duplicate images deduplicated
- Test: scanned PDF with images produces image-only output
- Test: stdout mode creates image directory next to input PDF
