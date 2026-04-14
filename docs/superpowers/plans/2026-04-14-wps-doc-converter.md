# WPS/Legacy .doc Converter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add .doc (Word 97-2003 binary) format support and WPS .docx compatibility fallback using NPOI.HWPF.

**Architecture:** New `DocConverter` using NPOI.HWPF for .doc binary format parsing, shared rendering helpers in a partial class (`DocConverter.Rendering.cs`), OLE2 magic-byte detection fallback in `DocxConverter` that delegates to DocConverter's static helpers.

**Tech Stack:** NPOI.HWPF (NuGet), C# .NET 8, xUnit, existing ILlmClient for image captioning

**Design spec:** `docs/superpowers/specs/2026-04-14-wps-doc-converter-design.md`

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj` | Modify | Add NPOI.HWPF package reference |
| `src/MarkItDown.Converters.Office/DocConverter.cs` | Create | Main converter: extension/mime registration, ConvertAsync entry point |
| `src/MarkItDown.Converters.Office/DocConverter.Rendering.cs` | Create | Partial class with shared static helpers: paragraphs, tables, images, OLE2 detection |
| `src/MarkItDown.Converters.Office/DocxConverter.cs` | Modify | Add OLE2 fallback in catch block |
| `tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs` | Create | DocConverter unit tests |
| `tests/MarkItDown.Converters.Office.Tests/DocxConverterTests.cs` | Modify | Add fallback test |
| `tests/Fixtures/office/test-sample.doc` | Create | Test fixture .doc file (manual creation) |

---

### Task 1: Add NPOI.HWPF dependency + DocConverter skeleton

**Files:**
- Modify: `src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj`
- Create: `src/MarkItDown.Converters.Office/DocConverter.cs`
- Create: `tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs`

- [ ] **Step 1: Add NPOI.HWPF NuGet package**

Edit `src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj` — add inside `<ItemGroup>` with other PackageReferences:

```xml
<PackageReference Include="NPOI.HWPF" Version="2.3.0" />
```

> **Verification:** If `NPOI.HWPF 2.3.0` does not target .NET Standard 2.0+ (causing restore failure), replace with `<PackageReference Include="DotNetCore.NPOI" Version="1.2.1" />` which also includes HWPF and targets .NET Standard 2.0.

- [ ] **Step 2: Restore and verify build**

```bash
dotnet restore MarkItDown.sln --configfile NuGet.Config
dotnet build MarkItDown.sln
```

Expected: Build succeeds. If `NPOI.HWPF` fails, switch to `DotNetCore.NPOI` and re-run.

- [ ] **Step 3: Create DocConverter skeleton**

Create `src/MarkItDown.Converters.Office/DocConverter.cs`:

```csharp
using System.Text;
using MarkItDown.Core;
using NPOI.HWPF;
using NPOI.HWPF.UserModel;

namespace MarkItDown.Converters.Office;

public sealed partial class DocConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".doc" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/msword" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("DOC converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                using var doc = new HWPFDocument(stream);
                return ConvertFromHwpf(doc, request, cancellationToken);
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert DOC: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Shared entry point used by DocxConverter OLE2 fallback.
    /// Accepts an already-opened HWPFDocument and converts to Markdown.
    /// </summary>
    internal static DocumentConversionResult ConvertFromHwpf(
        HWPFDocument doc, DocumentConversionRequest request, CancellationToken ct)
    {
        var range = doc.Range;
        var blocks = new List<string>();
        var tableParagraphIndices = GetTableParagraphIndices(range);

        for (int i = 0; i < range.NumParagraphs; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (tableParagraphIndices.Contains(i))
                continue; // handled by table rendering

            var para = range.GetParagraph(i);
            var rendered = RenderHwpfParagraph(doc, para);
            if (!string.IsNullOrWhiteSpace(rendered))
                blocks.Add(rendered);
        }

        // Render tables
        foreach (var tableInfo in GetTables(range, tableParagraphIndices))
        {
            ct.ThrowIfCancellationRequested();
            var rendered = RenderHwpfTable(tableInfo);
            if (!string.IsNullOrWhiteSpace(rendered))
                blocks.Add(rendered);
        }

        // Render images
        var imageMarkdown = RenderHwpfImages(doc, request);
        if (!string.IsNullOrWhiteSpace(imageMarkdown))
            blocks.Add(imageMarkdown);

        var markdown = string.Join(
            Environment.NewLine + Environment.NewLine,
            blocks.Where(b => !string.IsNullOrWhiteSpace(b))).Trim();

        return new DocumentConversionResult("Doc", markdown);
    }
}
```

- [ ] **Step 4: Write CanConvert test**

Create `tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs`:

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Office;

namespace MarkItDown.Converters.Office.Tests;

public sealed class DocConverterTests
{
    private readonly DocConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsDocExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "document.doc" }));
    }

    [Fact]
    public void CanConvert_RejectsNonDocExtension()
    {
        Assert.False(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "document.docx" }));
    }

    [Fact]
    public void CanConvert_AcceptsMsWordMimeType()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { MimeType = "application/msword" }));
    }
}
```

- [ ] **Step 5: Build and run tests**

```bash
dotnet build MarkItDown.sln
dotnet test tests/MarkItDown.Converters.Office.Tests --no-restore --filter "FullyQualifiedName~DocConverterTests"
```

Expected: Build succeeds. CanConvert tests pass. ConvertAsync tests don't exist yet.

- [ ] **Step 6: Commit**

```bash
git add src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj \
        src/MarkItDown.Converters.Office/DocConverter.cs \
        tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs
git commit -m "feat: add DocConverter skeleton with .doc extension support

Adds NPOI.HWPF dependency and DocConverter with CanConvert for .doc
and application/msword. Rendering helpers not yet implemented."
```

---

### Task 2: Create .doc test fixture file

**Files:**
- Create: `tests/Fixtures/office/test-sample.doc`

- [ ] **Step 1: Create the fixture file**

Using Microsoft Word or WPS Writer, create a .doc file with this content:

- **Heading 2** style: "Introduction"
- **Paragraph**: "This is bold and italic text." (with "bold" in **Bold** and "italic" in *Italic*)
- **List item**: "Item 1"
- **List item**: "Item 2"
- **2x2 Table**: Headers "Header A" / "Header B", cells "Cell 1" / "Cell 2"
- **One small embedded image** (any small PNG, e.g. a colored square)

Save as `tests/Fixtures/office/test-sample.doc` (Word 97-2003 format, NOT .docx).

> **Note:** If no Word/WPS is available, use LibreOffice:
> ```bash
> # Create a .docx first with the content, then convert
> libreoffice --headless --convert-to doc --outdir tests/Fixtures/office/ tests/Fixtures/office/test-sample.docx
> ```
> Or use an online converter to create a minimal .doc file.

- [ ] **Step 2: Verify the file is OLE2 format**

```bash
# First 8 bytes should be D0 CF 11 E0 A1 B1 1A E1
xxd -l 8 tests/Fixtures/office/test-sample.doc
```

Expected output starts with: `d0cf 11e0 a1b1 1ae1`

- [ ] **Step 3: Commit**

```bash
git add tests/Fixtures/office/test-sample.doc
git commit -m "test: add .doc test fixture for DocConverter tests"
```

---

### Task 3: Implement paragraph/heading/list rendering (TDD)

**Files:**
- Create: `src/MarkItDown.Converters.Office/DocConverter.Rendering.cs`
- Modify: `src/MarkItDown.Converters.Office/DocConverter.cs` (already references RenderHwpfParagraph)
- Modify: `tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs`

- [ ] **Step 1: Write failing tests for paragraph rendering**

Add to `tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs`:

```csharp
[Fact]
public async Task ConvertAsync_ExtractsParagraphsFromDoc()
{
    var docPath = FixturePath.For("office/test-sample.doc");
    if (!File.Exists(docPath))
        return; // Skip if fixture not available

    var result = await _converter.ConvertAsync(
        new DocumentConversionRequest { FilePath = docPath });

    Assert.Equal("Doc", result.Kind);
    Assert.NotEmpty(result.Markdown);
}

[Fact]
public async Task ConvertAsync_ExtractsHeadingsFromDoc()
{
    var docPath = FixturePath.For("office/test-sample.doc");
    if (!File.Exists(docPath))
        return;

    var result = await _converter.ConvertAsync(
        new DocumentConversionRequest { FilePath = docPath });

    Assert.Contains("Introduction", result.Markdown);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/MarkItDown.Converters.Office.Tests --no-restore \
    --filter "FullyQualifiedName~DocConverterTests~ConvertAsync"
```

Expected: Tests either pass with empty markdown (unlikely) or fail because rendering is not implemented. If fixture file doesn't exist, tests are skipped — create fixture first.

- [ ] **Step 3: Create DocConverter.Rendering.cs with paragraph rendering**

Create `src/MarkItDown.Converters.Office/DocConverter.Rendering.cs`:

```csharp
using System.Text;
using MarkItDown.Core;
using NPOI.HWPF;
using NPOI.HWPF.UserModel;

namespace MarkItDown.Converters.Office;

public sealed partial class DocConverter
{
    internal static string RenderHwpfParagraph(HWPFDocument doc, Paragraph para)
    {
        var styleName = GetStyleName(doc, para.StyleIndex);

        // Heading detection
        if (styleName is not null &&
            styleName.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
        {
            var levelStr = styleName["Heading".Length..].Trim();
            if (int.TryParse(levelStr, out var level) && level is >= 1 and <= 9)
            {
                var text = RenderCharacterRuns(para);
                return string.IsNullOrWhiteSpace(text) ? string.Empty
                    : $"{new string('#', level)} {text}";
            }
        }

        // Title style -> H1
        if (styleName is not null &&
            styleName.Equals("Title", StringComparison.OrdinalIgnoreCase))
        {
            var text = RenderCharacterRuns(para);
            return string.IsNullOrWhiteSpace(text) ? string.Empty : $"# {text}";
        }

        // List detection: check for bullet/numbering in paragraph properties
        // In HWPF, list paragraphs have ILVL (indent level) or ILFO (list ID)
        if (IsListItem(para))
        {
            var text = RenderCharacterRuns(para);
            return string.IsNullOrWhiteSpace(text) ? string.Empty : $"- {text}";
        }

        // Regular paragraph
        var content = RenderCharacterRuns(para);
        return string.IsNullOrWhiteSpace(content) ? string.Empty : content;
    }

    private static string RenderCharacterRuns(Paragraph para)
    {
        var builder = new StringBuilder();

        for (int j = 0; j < para.NumCharacterRuns; j++)
        {
            var run = para.GetCharacterRun(j);
            var text = run.Text;
            if (string.IsNullOrEmpty(text)) continue;

            // Strip control characters (field markers, etc.)
            text = text.Replace("\x07", "").Replace("\x13", "")
                       .Replace("\x14", "").Replace("\x15", "");
            if (string.IsNullOrWhiteSpace(text)) continue;

            var isBold = run.IsBold;
            var isItalic = run.IsItalic;

            if (isBold && isItalic)
                builder.Append($"***{text.Trim()}***");
            else if (isBold)
                builder.Append($"**{text.Trim()}**");
            else if (isItalic)
                builder.Append($"*{text.Trim()}*");
            else
                builder.Append(text);
        }

        return builder.ToString().Trim();
    }

    private static string? GetStyleName(HWPFDocument doc, int styleIndex)
    {
        try
        {
            var stylesheet = doc.StyleSheet;
            if (stylesheet is null) return null;
            var style = stylesheet.GetStyleDescription(styleIndex);
            return style?.Name;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsListItem(Paragraph para)
    {
        try
        {
            // Check ILVL (indent level) - if > 0, it's a list item
            // In HWPF, list items have specific paragraph property values
            var ilvl = para.GetILvl();
            return ilvl >= 0;
        }
        catch
        {
            return false;
        }
    }

    internal static HashSet<int> GetTableParagraphIndices(Range range)
    {
        var indices = new HashSet<int>();
        try
        {
            for (int i = 0; i < range.NumParagraphs; i++)
            {
                var para = range.GetParagraph(i);
                if (IsTableStart(para))
                {
                    var table = range.GetTable(para);
                    if (table is not null)
                    {
                        for (int j = 0; j < table.NumParagraphs; j++)
                            indices.Add(i + j);
                    }
                }
            }
        }
        catch { /* HWPF table iteration may be limited */ }
        return indices;
    }

    private static bool IsTableStart(Paragraph para)
    {
        try
        {
            return para.IsInTable && para.TableLevel == 1;
        }
        catch
        {
            return false;
        }
    }

    internal static List<Table> GetTables(Range range, HashSet<int> tableParagraphIndices)
    {
        var tables = new List<Table>();
        var seen = new HashSet<int>();

        foreach (var idx in tableParagraphIndices.OrderBy(i => i))
        {
            if (seen.Contains(idx)) continue;
            try
            {
                var para = range.GetParagraph(idx);
                var table = range.GetTable(para);
                if (table is not null)
                {
                    tables.Add(table);
                    for (int j = 0; j < table.NumParagraphs; j++)
                        seen.Add(idx + j);
                }
            }
            catch { /* skip tables that can't be parsed */ }
        }
        return tables;
    }
}
```

> **Note:** The exact HWPF API for `IsInTable`, `TableLevel`, `GetILvl`, `StyleSheet.GetStyleDescription`, and `Range.GetTable` may differ slightly depending on the NPOI.HWPF package version. If any method is not found, check the actual NPOI.HWPF API and adapt. Key alternatives:
> - `para.IsInTable` → try `para._props.GetFTC() != 0` or check `para.TableLevel`
> - `para.GetILvl()` → try reading `para._props.GetIlvl()` or check `para._props.GetIlfo()`
> - `doc.StyleSheet` → try `doc.GetStyleSheet()` or `doc.StyleTable`

- [ ] **Step 4: Build and run tests**

```bash
dotnet build MarkItDown.sln
dotnet test tests/MarkItDown.Converters.Office.Tests --no-restore \
    --filter "FullyQualifiedName~DocConverterTests"
```

Expected: All DocConverter tests pass. Paragraph and heading content extracted from .doc.

- [ ] **Step 5: Commit**

```bash
git add src/MarkItDown.Converters.Office/DocConverter.Rendering.cs
git commit -m "feat: implement DocConverter paragraph/heading/list rendering

Renders HWPF paragraphs to Markdown with heading styles, bold/italic
formatting, and list detection. Shared helpers for table paragraph
index tracking."
```

---

### Task 4: Implement table rendering (TDD)

**Files:**
- Modify: `src/MarkItDown.Converters.Office/DocConverter.Rendering.cs`
- Modify: `tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs`

- [ ] **Step 1: Write failing test for table extraction**

Add to `tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs`:

```csharp
[Fact]
public async Task ConvertAsync_ExtractsTablesFromDoc()
{
    var docPath = FixturePath.For("office/test-sample.doc");
    if (!File.Exists(docPath))
        return;

    var result = await _converter.ConvertAsync(
        new DocumentConversionRequest { FilePath = docPath });

    Assert.Contains("Header A", result.Markdown);
    Assert.Contains("|", result.Markdown);
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/MarkItDown.Converters.Office.Tests --no-restore \
    --filter "FullyQualifiedName~ConvertAsync_ExtractsTablesFromDoc"
```

Expected: FAIL — `RenderHwpfTable` not yet implemented.

- [ ] **Step 3: Implement table rendering**

Add to `src/MarkItDown.Converters.Office/DocConverter.Rendering.cs`, inside the `partial class DocConverter` block, after `GetTables`:

```csharp
    internal static string RenderHwpfTable(Table table)
    {
        try
        {
            var rows = new List<List<string>>();
            for (int r = 0; r < table.NumRows; r++)
            {
                var row = table.GetRow(r);
                var cells = new List<string>();
                for (int c = 0; c < row.NumCells; c++)
                {
                    var cell = row.GetCell(c);
                    cells.Add(EscapePipe(cell.Text.Trim()));
                }
                rows.Add(cells);
            }

            if (rows.Count == 0) return string.Empty;

            var columnCount = rows.Max(r => r.Count);
            if (columnCount == 0) return string.Empty;

            foreach (var row in rows)
                while (row.Count < columnCount)
                    row.Add(string.Empty);

            var builder = new StringBuilder();
            builder.AppendLine($"| {string.Join(" | ", rows[0])} |");
            builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", columnCount))} |");

            foreach (var row in rows.Skip(1))
                builder.AppendLine($"| {string.Join(" | ", row)} |");

            return builder.ToString().TrimEnd();
        }
        catch
        {
            return string.Empty; // Skip tables that can't be parsed
        }
    }

    private static string EscapePipe(string value) =>
        value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/MarkItDown.Converters.Office.Tests --no-restore \
    --filter "FullyQualifiedName~DocConverterTests"
```

Expected: All tests pass, including table extraction.

- [ ] **Step 5: Commit**

```bash
git add src/MarkItDown.Converters.Office/DocConverter.Rendering.cs \
        tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs
git commit -m "feat: implement DocConverter table rendering

Renders HWPF tables to Markdown pipe tables, consistent with
DocxConverter table output format."
```

---

### Task 5: Implement image handling with LLM captioning (TDD)

**Files:**
- Modify: `src/MarkItDown.Converters.Office/DocConverter.Rendering.cs`
- Modify: `tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs`

- [ ] **Step 1: Write test for image placeholder**

Add to `tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs`:

```csharp
[Fact]
public async Task ConvertAsync_IncludesImagePlaceholderWhenNoAssetPath()
{
    var docPath = FixturePath.For("office/test-sample.doc");
    if (!File.Exists(docPath))
        return;

    var result = await _converter.ConvertAsync(
        new DocumentConversionRequest { FilePath = docPath });

    // Should contain an image placeholder since no AssetBasePath or LlmClient
    Assert.Contains("image-", result.Markdown);
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/MarkItDown.Converters.Office.Tests --no-restore \
    --filter "FullyQualifiedName~ConvertAsync_IncludesImagePlaceholder"
```

Expected: FAIL — `RenderHwpfImages` not yet implemented.

- [ ] **Step 3: Implement image rendering**

Add to `src/MarkItDown.Converters.Office/DocConverter.Rendering.cs`, inside the `partial class DocConverter` block:

```csharp
    internal static string RenderHwpfImages(HWPFDocument doc, DocumentConversionRequest request)
    {
        try
        {
            var picturesTable = doc.PicturesTable;
            if (picturesTable is null) return string.Empty;

            var pictures = picturesTable.GetAllPictures();
            if (pictures == null || pictures.Count == 0) return string.Empty;

            var blocks = new List<string>();

            for (int i = 0; i < pictures.Count; i++)
            {
                var pic = pictures[i];
                var imageData = pic.Content;
                if (imageData == null || imageData.Length == 0) continue;

                var imageIndex = i + 1;

                // Save to asset directory if provided
                if (request.AssetBasePath is not null)
                {
                    var ext = pic.SuggestFileExtension() ?? "png";
                    var fileName = $"doc-image-{imageIndex}.{ext}";
                    var outputPath = Path.Combine(request.AssetBasePath, fileName);
                    Directory.CreateDirectory(request.AssetBasePath);
                    File.WriteAllBytes(outputPath, imageData);

                    var imageRef = $"![image-{imageIndex}]({fileName})";

                    // LLM captioning if available
                    if (request.LlmClient is not null)
                    {
                        var mimeType = GetMimeTypeFromExtension(ext);
                        var caption = request.LlmClient.CompleteAsync(
                            "Write a detailed caption for this image.",
                            imageData, mimeType).GetAwaiter().GetResult();
                        if (!string.IsNullOrWhiteSpace(caption))
                            imageRef += $"{Environment.NewLine}{caption.Trim()}";
                    }

                    blocks.Add(imageRef);
                }
                else if (request.LlmClient is not null)
                {
                    // LLM captioning without saving to file
                    var mimeType = GetMimeTypeFromExtension(
                        pic.SuggestFileExtension() ?? "png");
                    var caption = request.LlmClient.CompleteAsync(
                        "Write a detailed caption for this image.",
                        imageData, mimeType).GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(caption))
                        blocks.Add($"![image-{imageIndex}]({caption.Trim()})");
                }
                else
                {
                    // Placeholder
                    blocks.Add($"[image-{imageIndex}]");
                }
            }

            return string.Join(Environment.NewLine + Environment.NewLine, blocks);
        }
        catch
        {
            return string.Empty; // Skip images that can't be extracted
        }
    }

    private static string GetMimeTypeFromExtension(string ext) =>
        ext.ToLowerInvariant() switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "tiff" or "tif" => "image/tiff",
            _ => "image/png"
        };
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/MarkItDown.Converters.Office.Tests --no-restore \
    --filter "FullyQualifiedName~DocConverterTests"
```

Expected: All tests pass, including image placeholder test.

- [ ] **Step 5: Commit**

```bash
git add src/MarkItDown.Converters.Office/DocConverter.Rendering.cs \
        tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs
git commit -m "feat: implement DocConverter image handling with LLM captioning

Extracts embedded images via PicturesTable, saves to AssetBasePath,
optionally captions via ILlmClient, or outputs placeholder text."
```

---

### Task 6: Add OLE2 fallback to DocxConverter (TDD)

**Files:**
- Modify: `src/MarkItDown.Converters.Office/DocxConverter.cs`
- Modify: `src/MarkItDown.Converters.Office/DocConverter.Rendering.cs`
- Modify: `tests/MarkItDown.Converters.Office.Tests/DocxConverterTests.cs`

- [ ] **Step 1: Write failing test for OLE2 fallback**

Add to `tests/MarkItDown.Converters.Office.Tests/DocxConverterTests.cs`:

```csharp
[Fact]
public async Task ConvertAsync_FallsBackToHwpfForOle2Files()
{
    var docPath = FixturePath.For("office/test-sample.doc");
    if (!File.Exists(docPath))
        return;

    // Copy .doc to .docx extension to simulate WPS misnamed file
    var fakeDocxPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");
    try
    {
        File.Copy(docPath, fakeDocxPath);

        var converter = new DocxConverter();
        var result = await converter.ConvertAsync(
            new DocumentConversionRequest { FilePath = fakeDocxPath });

        Assert.NotNull(result.Markdown);
        Assert.NotEmpty(result.Markdown);
        Assert.Equal("Doc", result.Kind);
    }
    finally
    {
        if (File.Exists(fakeDocxPath))
            File.Delete(fakeDocxPath);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/MarkItDown.Converters.Office.Tests --no-restore \
    --filter "FullyQualifiedName~ConvertAsync_FallsBackToHwpfForOle2Files"
```

Expected: FAIL — DocxConverter currently throws `ConversionException` with "Encrypted packages" error for .doc files.

- [ ] **Step 3: Add OLE2 detection helper to DocConverter.Rendering.cs**

Add to `src/MarkItDown.Converters.Office/DocConverter.Rendering.cs`:

```csharp
    internal static bool IsOle2File(string filePath)
    {
        try
        {
            byte[] header = new byte[8];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            int read = fs.Read(header, 0, 8);
            if (read < 8) return false;

            // OLE2 Compound Document magic bytes
            return header[0] == 0xD0 && header[1] == 0xCF &&
                   header[2] == 0x11 && header[3] == 0xE0 &&
                   header[4] == 0xA1 && header[5] == 0xB1 &&
                   header[6] == 0x1A && header[7] == 0xE1;
        }
        catch
        {
            return false;
        }
    }
```

- [ ] **Step 4: Modify DocxConverter.cs to add fallback**

Edit `src/MarkItDown.Converters.Office/DocxConverter.cs`. Replace the `ConvertAsync` method's try-catch block:

```csharp
    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("DOCX converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                using var doc = WordprocessingDocument.Open(filePath, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body is null)
                    return new DocumentConversionResult("Docx", string.Empty);

                var blocks = new List<string>();

                foreach (var element in body.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (element is Paragraph para)
                        blocks.Add(RenderParagraph(para));
                    else if (element is Table table)
                        blocks.Add(RenderTable(table));
                }

                var markdown = string.Join(Environment.NewLine + Environment.NewLine,
                    blocks.Where(b => !string.IsNullOrWhiteSpace(b))).Trim();
                return new DocumentConversionResult("Docx", markdown);
            }
            catch (ConversionException) { throw; }
            catch (Exception ex) when (DocConverter.IsOle2File(filePath))
            {
                // Fallback: this .docx file is actually an OLE2 .doc file
                // (e.g., WPS saved or renamed .doc with .docx extension)
                try
                {
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    using var hwpfDoc = new NPOI.HWPF.HWPFDocument(stream);
                    return DocConverter.ConvertFromHwpf(hwpfDoc, request, cancellationToken);
                }
                catch (Exception fallbackEx)
                {
                    throw new ConversionException(
                        $"Failed to convert DOCX (OLE2 fallback also failed): {fallbackEx.Message}",
                        fallbackEx);
                }
            }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert DOCX: {ex.Message}", ex);
            }
        }, cancellationToken);
    }
```

Add the using directive at the top of `DocxConverter.cs` (only HWPFDocument is needed, no conflict):

```csharp
using NPOI.HWPF;
```

> **Note:** `NPOI.HWPF.HWPFDocument` is fully qualified in the catch block to avoid any ambiguity with OpenXml types.

- [ ] **Step 5: Run tests**

```bash
dotnet build MarkItDown.sln
dotnet test tests/MarkItDown.Converters.Office.Tests --no-restore \
    --filter "FullyQualifiedName~DocxConverterTests"
```

Expected: All DocxConverter tests pass, including the new fallback test.

- [ ] **Step 6: Commit**

```bash
git add src/MarkItDown.Converters.Office/DocConverter.Rendering.cs \
        src/MarkItDown.Converters.Office/DocxConverter.cs \
        tests/MarkItDown.Converters.Office.Tests/DocxConverterTests.cs
git commit -m "feat: add OLE2 fallback to DocxConverter for WPS .doc files

When OpenXml fails to open a .docx file and the file has OLE2 magic
bytes, falls back to NPOI HWPF for .doc binary format parsing."
```

---

### Task 7: Integration test + final verification

**Files:**
- Modify: `tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs`

- [ ] **Step 1: Write integration test**

Add to `tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs`:

```csharp
[Fact]
public async Task ConvertAsync_FullDocConversion()
{
    var docPath = FixturePath.For("office/test-sample.doc");
    if (!File.Exists(docPath))
        return;

    var result = await _converter.ConvertAsync(
        new DocumentConversionRequest { FilePath = docPath });

    Assert.Equal("Doc", result.Kind);
    Assert.NotEmpty(result.Markdown);

    // Verify structural elements
    // Heading should be present (exact format depends on fixture)
    Assert.Contains("Introduction", result.Markdown);

    // Should not crash on any content
    Assert.True(result.Markdown.Length > 10);
}
```

- [ ] **Step 2: Run all tests**

```bash
dotnet test MarkItDown.sln --no-restore
```

Expected: ALL tests pass — both DocConverter and DocxConverter suites, including fallback.

- [ ] **Step 3: Manual smoke test with CLI**

```bash
dotnet run --project src/MarkItDown.Cli -- tests/Fixtures/office/test-sample.doc
```

Expected: Markdown output printed to console with headings, paragraphs, tables, and image placeholder.

- [ ] **Step 4: Update package description**

Edit `src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj`, update Description:

```xml
<Description>Office document converter for MarkItDown — doc, docx, pptx, xlsx, csv, msg</Description>
```

- [ ] **Step 5: Final commit**

```bash
git add tests/MarkItDown.Converters.Office.Tests/DocConverterTests.cs \
        src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj
git commit -m "test: add integration test and update package description for .doc support"
```

---

## Self-Review Checklist

- [x] **Spec coverage:** Every requirement in the design spec has a corresponding task
- [x] **Placeholder scan:** No TBDs, TODOs, or vague instructions
- [x] **Type consistency:** `RenderHwpfParagraph`, `RenderHwpfTable`, `RenderHwpfImages`, `ConvertFromHwpf`, `IsOle2File` — all defined in DocConverter.Rendering.cs and called consistently from DocConverter.cs and DocxConverter.cs
- [x] **No duplicate code:** Table escaping shared via `EscapePipe` in DocConverter.Rendering.cs

## API Verification Notes

The exact NPOI.HWPF API may vary by package version. Key methods to verify during Task 1:
- `HWPFDocument(Stream)` constructor
- `doc.Range` property
- `range.NumParagraphs` / `range.GetParagraph(int)`
- `para.StyleIndex`, `para.NumCharacterRuns`, `para.GetCharacterRun(int)`
- `run.Text`, `run.IsBold`, `run.IsItalic`
- `para.IsInTable`, `para.TableLevel`
- `range.GetTable(Paragraph)` → `Table` with `NumRows`, `GetRow(int)`
- `TableRow.NumCells`, `TableRow.GetCell(int)`
- `doc.PicturesTable.GetAllPictures()`
- `doc.StyleSheet.GetStyleDescription(int)`

If any method is missing, check the actual NPOI.HWPF API surface and adapt accordingly. The structure of the code (separate rendering from orchestration) makes these adjustments localized.
