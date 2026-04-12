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

### Architectural Decision: Image Output Path

The converter needs to know where to write image files. Two approaches were considered:

1. **Pass output path into converter** — add `AssetBasePath` to `DocumentConversionRequest`
2. **Return image data from converter** — converter returns bytes, CLI handles file I/O

**Chosen: Option 1 — `AssetBasePath` on the request.** Reason: the converter already does file I/O (opens the PDF from `FilePath`). Adding one more path field is consistent with the existing pattern. The CLI knows the output location and passes it through. When `AssetBasePath` is null (stream-only input), image extraction is skipped and only text is produced.

```csharp
// DocumentConversionRequest addition
public string? AssetBasePath { get; init; }  // e.g. "output_files/" or null to skip images
```

CLI sets this field:
- `-o output.md` → `AssetBasePath = Path.GetDirectoryName(outputPath) + "/" + stem + "_files"`
- stdout mode → `AssetBasePath = input directory + "/" + input_stem + "_files"`

**Stream-only input** (no `FilePath`): image extraction is skipped. Only text is extracted. This covers MCP server and programmatic API usage.

### Content Block Model

Replace the text-only extraction with a unified content block model. Each page produces an ordered list of blocks.

```
PdfPage → [TextBlock, ImageBlock, TextBlock, ImageBlock, ...]
                ↓ grouped by spatial proximity
              [Group1, Group2, Group3, ...]
                ↓ group-internal order: image first, then text
                ↓ group-external order: by Y coordinate (descending, PDF Y increases upward)
              Markdown output
```

**Types** (records, per project convention for immutable data models):

```csharp
internal abstract record PdfContentBlock(double Y, double Top, double Bottom);

internal sealed record PdfTextBlock(
    double Y, double Top, double Bottom,
    string Text, double FontSize) : PdfContentBlock(Y, Top, Bottom);

internal sealed record PdfImageBlock(
    double Y, double Top, double Bottom,
    int PageNumber, int ImageIndex, string FilePath) : PdfContentBlock(Y, Top, Bottom);
```

### Spatial Grouping

Blocks that overlap or are adjacent in Y space are grouped into a logical unit:

1. **Text on image** — text coordinates fall within an image's `Bounds` → image first, text after
2. **Caption below image** — text Y is slightly below image bottom, X range overlaps → group as figure with caption (`![...](path)\n*caption text*`)
3. **Independent** — no spatial relationship → separate groups

**Y-coordinate convention:** PDF coordinates have origin at bottom-left (Y increases upward). `Top` and `Bottom` use PDF-native space. Sort **descending by Y** for reading order (top of page = highest Y value).

**Grouping algorithm:**
- Sort all blocks by Y coordinate descending (top of page first)
- Iterate: if next block's Top overlaps current group's Bottom (gap < 1.5x body font size), merge into same group
- Within each group: image blocks first, then text blocks

**Threshold:** merge if Y gap < 1.5x the page's body font size (the mode font size computed for that page). This adapts to different document scales.

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
- Duplicate images (same SHA256 hash on raw bytes) → save once, reference multiple times

### Text Layout Heuristics

Replace the current `LooksLikeHeading` heuristic with font-size-based detection:

1. Collect all `Letter` objects per page
2. Round each `Letter.FontSize` to 1 decimal place, then compute font size **mode** (most frequent rounded size) per page = "body baseline"
3. Classify by ratio to baseline:
   - Size >= baseline * 1.5 **AND** line length < 40 chars → **heading** → `## text`
   - Size >= baseline * 1.5 but line length >= 40 chars → **body** (large text, not a heading)
   - Size between baseline * 0.7 and 1.5 → **body** → normal paragraph
   - Size < baseline * 0.7 → **caption/footnote** → `*text*` (italic)

**Paragraph merging:** Consecutive body-text lines with similar font size (within 1pt) are merged into a single paragraph (space-separated).

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

**Stream-only mode:** When `FilePath` is null (stream input), image extraction is skipped. Only text is extracted and returned.

### DocumentConversionResult Extension

```csharp
public sealed record DocumentConversionResult(
    string Kind,
    string Markdown,
    string? Title = null,
    string? AssetDirectory = null  // directory containing extracted images, null if no images
);
```

**Backward compatibility:** The default `null` preserves all existing call sites. All 19 converters in the codebase continue to work without changes. Only `PdfConverter` will set this field.

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
- Stream-only input (no FilePath) → skip image extraction, text-only output

### Out of Scope

- OCR
- PDF form fields / annotations
- Vector graphics (SVG paths) to image conversion
- Full-page rendering as image
- Async image extraction (PdfPig is synchronous)

## Files to Modify

| File | Change |
|------|--------|
| `src/MarkItDown.Core/DocumentConversionRequest.cs` | Add `AssetBasePath` property |
| `src/MarkItDown.Core/DocumentConversionResult.cs` | Add `AssetDirectory` field (default null, backward compatible) |
| `src/MarkItDown.Converters.Pdf/PdfConverter.cs` | Major refactor: content block model, image extraction, spatial grouping, font-size layout |
| `src/MarkItDown.Cli/CliRunner.cs` | Set `AssetBasePath` on request, print asset directory info after conversion |
| `tests/MarkItDown.Converters.Pdf.Tests/PdfConverterTests.cs` | Add image extraction tests, layout tests |

**Note on backward compatibility:** The `AssetDirectory` field defaults to `null`. All existing converters across all projects (Html, Pdf, Office, Data, Media, Web) continue to compile and run without modification.

## Test Strategy

- **Fixture PDF:** Create a 3-page test PDF:
  - Page 1: heading (large font) + body text (normal font) — tests font-size classification
  - Page 2: embedded PNG image with a caption line below (small font) — tests image extraction, spatial grouping, caption detection
  - Page 3: text overlaid on an image + a small icon (< 20px) — tests text-on-image grouping and small-image filtering

- Test: images extracted and saved to correct directory
- Test: Markdown contains image references at correct positions
- Test: heading/body/caption classification by font size
- Test: large-font text >= 40 chars is NOT treated as heading
- Test: spatial grouping of overlapping text+image
- Test: small images (< 20px) filtered out
- Test: duplicate images deduplicated (same file referenced twice)
- Test: scanned PDF with images produces image-only output
- Test: stdout mode creates image directory next to input PDF
- Test: stream-only input skips image extraction, produces text-only output
