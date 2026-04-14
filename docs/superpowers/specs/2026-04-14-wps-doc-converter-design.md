# WPS/Legacy .doc Converter Design

**Date**: 2026-04-14
**Status**: Approved
**Scope**: Add .doc (Word 97-2003 binary) format support + WPS .docx compatibility fallback

## Problem

1. `.doc` files (Word 97-2003 binary format) produce "No converter registered for format '.doc'" error
2. WPS Office-saved `.docx` files may actually be OLE2 binary format internally, causing OpenXml to throw "Encrypted packages are not supported"

Both formats use the same underlying OLE2/Compound Binary structure. The solution uses **NPOI** (Apache POI .NET port) which supports reading this format via its HWPF component.

## Approach

- **Approach A (chosen)**: New `DocConverter` + `DocxConverter` fallback
- Separate converter for `.doc`, fallback in `DocxConverter` for misidentified `.doc` files

## Architecture

### DocConverter (new)

```
DocConverter : BaseConverter
  SupportedExtensions: { ".doc" }
  SupportedMimeTypes: { "application/msword" }
  ConvertAsync():
    - Open with NPOI HWPFDocument
    - Extract paragraphs (headings, lists, bold/italic)
    - Extract tables → Markdown tables
    - Extract images via PicturesTable
    - Return DocumentConversionResult
```

### DocxConverter Fallback (modify existing)

```
DocxConverter.ConvertAsync():
  try:
    WordprocessingDocument.Open() (existing logic)
  catch when OLE2 detected:
    - Check file header for OLE2 magic bytes (D0 CF 11 E0 A1 B1 1A E1)
    - Fall back to NPOI HWPFDocument parsing
    - Reuse DocConverter rendering helpers
```

### Shared Rendering (new)

`DocConverter.Rendering.cs` contains shared helpers used by both:
- `RenderHwpfParagraph()` — paragraph/heading/list rendering
- `RenderHwpfTable()` — table to Markdown
- `RenderHwpfImages()` — image extraction and optional LLM captioning

## File Changes

| File | Action |
|------|--------|
| `src/MarkItDown.Converters.Office/DocConverter.cs` | New — main converter |
| `src/MarkItDown.Converters.Office/DocConverter.Rendering.cs` | New — shared rendering helpers |
| `src/MarkItDown.Converters.Office/DocxConverter.cs` | Modify — add OLE2 fallback |
| `src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj` | Modify — add NPOI package |
| `tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs` | New |
| `tests/MarkItDown.Converters.Office.Tests/DocxConverterTests.cs` | Modify — add fallback tests |
| `tests/Fixtures/` | New — add .doc test fixture |

## Dependencies

- **NPOI** (NuGet) — reads .doc binary format via HWPF component, ~5MB
- Existing `DocumentFormat.OpenXml` remains for .docx files
- Optional: `ILlmClient` for image captioning (already in project)

## Markdown Output Rules

Consistent with existing DocxConverter:
- Headings: `# H1`, `## H2`, etc.
- Lists: `- item`
- Bold: `**text**`, Italic: `*text*`
- Tables: `| col1 | col2 |` with separator row

## Image Handling

1. Extract images via `PicturesTable.GetAllPictures()`
2. If `AssetBasePath` provided: save to `{AssetBasePath}/doc-image-{index}.png`, output `![image-N](path)`
3. If `LlmClient` provided: send image bytes to LLM, append description after image reference
4. If neither available: output `[image-N ({width}x{height})]` placeholder

## Error Handling

- Invalid/corrupt .doc files → `ConversionException` with descriptive message
- OLE2 detection in DocxConverter is transparent — user never sees the fallback
- NPOI parse failures are caught and wrapped in `ConversionException`

## Testing

- Unit tests for paragraph, heading, list, table extraction from .doc
- Image extraction with and without LlmClient/AssetBasePath
- DocxConverter fallback test: rename .doc to .docx, verify successful conversion
- Error cases: corrupt file, non-.doc file with .doc extension

## Out of Scope

- .wps/.et/.dps native WPS formats (future phase)
- .xls/.ppt legacy formats (future phase)
- Complex .doc features: embedded objects, macros, revision tracking
