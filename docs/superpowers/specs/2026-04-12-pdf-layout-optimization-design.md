# PDF Layout Optimization — Projection Profile Reading Order & Content Analysis

**Date:** 2026-04-12
**Status:** Approved

## Problem

The current PDF converter sorts content blocks by Y-coordinate and groups by spatial proximity. This produces poor output for:

- **Multi-column layouts** — left and right column content gets interleaved
- **Reading order** — content appears in visual order, not reading order
- **Headers/footers** — repeated across every page
- **Captions** — not associated with their images
- **Lists** — numbered and bulleted items not detected

PuYu.pdf is a representative example: 23-page presentation with multi-column layouts, repeated headers, image-caption pairs, and numbered lists — most of which are incorrectly rendered.

## Goal

Implement a projection-profile-based layout engine inspired by OpenDataLoader PDF (Java)'s XY-Cut++ approach, but using an iterative two-pass algorithm instead of recursion. This fixes reading order and multi-column support. Additionally, add header/footer removal, caption association, and list detection — all purely algorithmic, no AI dependency.

## Design

### Projection Profile Layout Algorithm

Instead of recursive XY-Cut splitting, use a two-pass projection profile approach:

**Pass 1 — Y-axis projection (horizontal bands):**
1. Project all content block bounding boxes onto the Y axis
2. Find horizontal gaps where no block occupies that Y range
3. Gaps larger than 1.5x body font size → split into horizontal bands
4. Result: ordered list of bands from top to bottom

**Pass 2 — X-axis projection (columns within each band):**
1. For each band, project block bounding boxes onto the X axis
2. Find vertical gaps where no block occupies that X range
3. Gaps larger than 1.5x body font size → split into columns
4. Result: ordered list of columns from left to right within each band

**Reading order:** bands top-to-bottom, columns left-to-right within each band. This is the natural reading order for most documents.

**Full-width element extraction (before projection):**
- Elements spanning >80% of page width (large headings, full-width tables) are extracted first
- These are always assigned to the top of the current band, never split across columns

**Example result:**
```
Band 1: Full-width heading → "## 杭州谱育科技发展有限公司"
Band 2: Two columns
  Column 1: Block A, Block B
  Column 2: Block C, Block D
Band 3: Full-width table → markdown table
```

**Why projection profile over recursive XY-Cut:**
- No recursion — no stack overflow risk, no per-level list allocation
- O(n log n) total (dominated by sorting) — same as XY-Cut but with smaller constant
- Cache-friendly — two linear scans over block coordinates
- Simpler to implement and test — no tree structure, just sorted lists of bands and columns
- Handles 99% of real-world layouts (single/double/triple column, mixed)

### Header/Footer Detection

Cross-page preprocessing before the per-page pipeline:

1. From each page, collect text blocks in the top 10% and bottom 10% of the page height
2. Normalize text (trim whitespace, collapse multiple spaces)
3. Hash each normalized text string; track page occurrence count
4. Text appearing on 3+ pages → mark as header/footer
5. Page numbers detected by pattern: `^\d+$`, `^\d+[/\-–—]\d+$`
6. First-page headers preserved if they appear only once (unique title)

Marked blocks get `IsHeaderFooter = true` on `PdfContentBlock` and are excluded from the content pipeline.

**Performance**: O(n) where n = total header/footer candidate blocks. Uses `Dictionary<string, int>` for counting, not pairwise comparison.

### Caption Association

After reading order is established, scan for image-caption pairs:

- Text block must be within 1.5x body line-height below the image's bottom edge
- Text block must overlap the image horizontally by ≥ 50%
- Text block font size < body font size (captions are typically smaller)
- Associated captions are rendered as `*caption text*` directly below the image reference

### List Detection

Analyze text block content for list patterns:

- Numbered: `^\d+[.)、]\s` — e.g., "1. item", "2) item", "3、item"
- Bulleted: `^[•·●◇◆\-–—]\s` — common bullet characters
- 2+ consecutive matching blocks → render as Markdown list
- Single isolated match → treat as body text
- No nesting support in this iteration (YAGNI)

### Pipeline

```
Cross-page preprocessing:
  1. For all pages: extract text blocks (PdfTextClassifier)
  2. Collect top/bottom blocks → detect headers/footers → mark

Per-page pipeline:
  3. Extract text blocks + image blocks (existing components)
  4. Filter out header/footer blocks
  5. Extract full-width elements (span > 80% page width)
  6. Y-axis projection → find horizontal gaps → split into bands
  7. For each band: X-axis projection → find vertical gaps → split into columns
  8. Flatten bands/columns to ordered block list (reading order)
  9. Associate captions to nearby images
  10. Detect lists in consecutive text blocks
  11. Detect tables (existing ColumnSplitRegex logic)
  12. Render to Markdown in reading order
```

Pages separated by `---` (horizontal rule) for visual clarity.

### Performance Constraints

1. **Projection profile**: two linear scans over block coordinates + sort — O(n log n), no recursion, no tree allocation
2. **Gap finding**: iterate sorted coordinate arrays, find runs of empty space — O(n)
3. **Header/footer matching**: `Dictionary<string, int>` hash lookup — O(n), not O(n²)
4. **Single-pass block creation**: each page's blocks are created once and reused through the pipeline
5. **No LINQ in hot paths**: projection and gap-finding use `for` loops over sorted arrays
6. **Benchmark target**: overhead < 10% vs current implementation. Projection profile typically < 0.5ms per page.

### Files

| File | Change |
|------|--------|
| `src/MarkItDown.Converters.Pdf/PdfLayoutAnalyzer.cs` | NEW — projection profile algorithm, band/column detection, header/footer detection, caption association, list detection |
| `src/MarkItDown.Converters.Pdf/PdfContentBlock.cs` | MODIFY — add `IsHeaderFooter` boolean |
| `src/MarkItDown.Converters.Pdf/PdfContentGrouper.cs` | MODIFY — use PdfLayoutAnalyzer for ordering, keep table rendering |
| `src/MarkItDown.Converters.Pdf/PdfConverter.cs` | MODIFY — two-pass: cross-page preprocessing, then per-page pipeline |
| `tests/MarkItDown.Converters.Pdf.Tests/PdfLayoutAnalyzerTests.cs` | NEW — unit tests for projection profile, header/footer, captions, lists |
| `tests/MarkItDown.Converters.Pdf.Tests/PdfConverterTests.cs` | MODIFY — add quality regression tests |

**Unchanged:**
- `PdfTextClassifier.cs` — font-size classification still valid
- `PdfImageExtractor.cs` — image extraction unchanged
- `CliRunner.cs` — no CLI changes
- `DocumentConversionRequest.cs`, `DocumentConversionResult.cs` — no model changes

### Error Handling

- If projection profile produces no columns on a page (single column), fall back to current Y-sort behavior within that band
- If header/footer detection finds no repeated text, proceed normally (no filtering)
- If caption/list detection produces no matches, blocks render as body text
- All pipeline stages are additive — none can break the base text extraction

### Out of Scope

- AI/LLM-assisted processing (OCR, formula recognition)
- Nested list support
- Nested column-in-column layouts (extremely rare in practice)
- Vector graphics extraction
- Full-page rendering as image
- PDF form fields / annotations
- Parallel page processing (single-threaded is fast enough)

## Test Strategy

- **Projection profile unit tests**: single column, double column, triple column, mixed layout
- **Header/footer tests**: repeated text across pages, page numbers, unique first-page header
- **Caption tests**: image with caption below, image without caption, caption without image
- **List tests**: numbered list, bulleted list, mixed list/body
- **Integration test**: convert PuYu.pdf and verify:
  - Multi-column content appears in correct reading order
  - Headers/footers removed from output
  - Captions associated with images
  - Lists properly formatted
  - Tables still detected
  - No text content lost vs current output
