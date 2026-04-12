# PDF Text Classification Improvement — Paragraph Merging & Smarter Roles

**Date:** 2026-04-12
**Status:** Approved

## Problem

The current PDF converter produces poor output for technical documents (e.g., IBM Z NetView Programmer Guide, `dpymst.pdf`):

1. **Italic over-detection** — most body text rendered as `*italic*` because `ClassifyRole` marks anything with font size < 0.7x body as "caption"
2. **No paragraph merging** — each text line is a separate paragraph; sentences spanning lines are not joined
3. **No TOC detection** — table of contents pages rendered as regular body text
4. **Page numbers not filtered** — standalone roman/arabic page numbers ("iii", "iv", "137") appear in output

## Goal

Improve text classification by learning from OpenDataLoader PDF (Java):
- Use **spatial proximity only** for caption detection (remove font-size-ratio caption logic)
- Add **paragraph merging** based on font size consistency + text alignment
- Improve **heading detection** with font weight (bold) analysis
- Add **TOC detection** for cleaner output

## Design

### 1. Remove Caption from ClassifyRole

**Current:** `PdfTextClassifier.ClassifyRole` returns "caption" when `fontSize / bodyFontSize < 0.7`.

**New:** Remove the "caption" case entirely from `ClassifyRole`. The method only returns "heading" or "body". All caption detection is handled by `PdfLayoutAnalyzer.DetectCaptions`, which uses spatial proximity to images — the same approach as OpenDataLoader's `CaptionProcessor`.

**Rationale:** Font-size-ratio caption detection causes massive false positives in documents with uniform small fonts (technical manuals). OpenDataLoader uses spatial proximity only (probability threshold 0.75), never font size alone.

### 2. Paragraph Merging

**Approach (from OpenDataLoader's `ParagraphProcessor`):**

After text blocks are ordered by reading order, merge consecutive body-text blocks into paragraphs when:

1. **Same font size** — blocks have the same font size (within 0.5pt tolerance, matching OpenDataLoader's `areCloseNumbers`)
2. **No large gap** — vertical gap between blocks < 1.2x body line height
3. **Left alignment consistency** — blocks have similar left X coordinate (within 5pt tolerance)
4. **Neither is a heading** — only merge blocks classified as "body"

**Implementation:** Add a `MergeParagraphs(List<PdfContentBlock> ordered, double bodyFontSize)` method to `PdfLayoutAnalyzer`. It iterates ordered blocks, and when consecutive text blocks meet all 3 criteria, merges them by concatenating text with a space separator. The merged block takes the Y/Top of the first block and Bottom of the last.

**Not merged:**
- Different font sizes (headings stay separate)
- Blocks with large vertical gaps (separate paragraphs)
- Image blocks (break merge chains)
- Heading-classified blocks

### 3. Improved Heading Detection

**Current:** Heading when `fontSize / bodyFontSize >= 1.5 AND text.Length < 40`.

**New (from OpenDataLoader's `TextNodeStatistics`):** Add font weight as a second signal. PdfPig provides `Letter.Font` with `Bold` property. If available, check:

- **Large + bold** → heading (high confidence)
- **Large but not bold** → heading only if ratio >= 1.8 and length < 60
- **Bold but same size** → heading only if it's a short isolated line (< 60 chars)

**Implementation:** In `PdfTextClassifier.ClassifyTextBlocks`, compute per-row whether letters are bold (`row.Letters.Any(l => l.Font?.Bold == true)` → store as boolean on `PdfTextBlock`). Then `ClassifyRole` uses both font size ratio and bold flag.

Add `IsBold` boolean to `PdfTextBlock` record.

### 4. TOC Detection

**Pattern:** A page is a TOC if:
- > 50% of text lines contain a page number pattern at the end: `(\.\.\s*\d+)$` or `(\s{3,}\d+)$`
- Lines are predominantly short (< 80 chars average)

**Handling:** TOC pages are identified in the per-page pipeline. Options:
- **Remove TOC entirely** — skip TOC pages (cleaner output for LLM consumption)
- **Mark with header** — render as `## Table of Contents` followed by the TOC text

**Chosen: Remove TOC pages** — they add no value for Markdown/LLM use cases and are usually inaccurate (page numbers don't match Markdown output).

**Implementation:** Add `IsTocPage(List<PdfContentBlock> blocks)` method to `PdfLayoutAnalyzer`. Called in `PdfConverter` per-page pipeline. If page is TOC and not the only page, skip it.

### 5. Page Number Filtering Enhancement

**Current:** `DetectHeadersFooters` already filters page numbers via regex `^\d+([/\-–—]\d+)?$`.

**Enhancement:** Also filter:
- Roman numeral page numbers: `^[ivxlc]+$` (case-insensitive)
- Standalone numbers in header/footer regions (top/bottom 10%)

## Pipeline Change

Updated per-page pipeline:
```
Per-page pipeline:
  1. Extract text + image blocks
  2. Filter header/footer blocks (existing)
  3. Analyze reading order via projection profile (existing)
  4. Merge consecutive body paragraphs (NEW)
  5. Detect captions via spatial proximity (existing)
  6. Detect lists (existing)
  7. Check if page is TOC → skip if true (NEW)
  8. Render to Markdown
```

## Files

| File | Change |
|------|--------|
| `PdfTextBlock` (in `PdfContentBlock.cs`) | Add `IsBold` boolean |
| `PdfTextClassifier.cs` | Remove caption from ClassifyRole, add bold detection, adjust heading thresholds |
| `PdfLayoutAnalyzer.cs` | Add `MergeParagraphs`, `IsTocPage`, enhance page number regex |
| `PdfContentGrouper.cs` | Update rendering for merged paragraphs |
| `PdfConverter.cs` | Add TOC page skip in per-page pipeline |
| `PdfLayoutAnalyzerTests.cs` | Add tests for paragraph merging, TOC detection |
| `PdfConverterTests.cs` | Update assertions |

## Test Strategy

- **Paragraph merge tests**: consecutive same-font lines merged; different-font lines not merged; gap breaks merge
- **TOC detection tests**: TOC page identified; content page not misidentified as TOC
- **Heading tests**: bold text detected as heading; large non-bold text with high ratio detected
- **Caption test**: no caption without nearby image
- **Regression**: convert dpymst.md and verify body text is NOT italic, paragraphs are merged, TOC pages skipped
