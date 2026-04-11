# Phase 2: Office Formats — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add DOCX, PPTX, XLSX, CSV, and MSG converters to align with Microsoft's official markitdown Office format support.

**Architecture:** Each format gets its own converter class inside `MarkItDown.Converters.Office` package, all extending `BaseConverter`. Uses `DocumentFormat.OpenXml` for DOCX/PPTX/XLSX, `CsvHelper` for CSV, and `MSGReader` for MSG.

**Tech Stack:** .NET 8, xUnit, DocumentFormat.OpenXml 3.5.1, CsvHelper 33.1.0, MSGReader 6.0.9

**Spec:** `docs/superpowers/specs/2026-04-11-markitdown-csharp-full-alignment-design.md` (Phase 2)

---

## File Structure

### Files to Create

| File | Responsibility |
|------|---------------|
| `src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj` | Office converter NuGet package |
| `src/MarkItDown.Converters.Office/DocxConverter.cs` | DOCX → Markdown |
| `src/MarkItDown.Converters.Office/PptxConverter.cs` | PPTX → Markdown |
| `src/MarkItDown.Converters.Office/XlsxConverter.cs` | XLSX → Markdown |
| `src/MarkItDown.Converters.Office/CsvConverter.cs` | CSV → Markdown |
| `src/MarkItDown.Converters.Office/MsgConverter.cs` | MSG → Markdown |
| `tests/MarkItDown.Converters.Office.Tests/MarkItDown.Converters.Office.Tests.csproj` | Test project |
| `tests/MarkItDown.Converters.Office.Tests/DocxConverterTests.cs` | DOCX tests |
| `tests/MarkItDown.Converters.Office.Tests/PptxConverterTests.cs` | PPTX tests |
| `tests/MarkItDown.Converters.Office.Tests/XlsxConverterTests.cs` | XLSX tests |
| `tests/MarkItDown.Converters.Office.Tests/CsvConverterTests.cs` | CSV tests |
| `tests/MarkItDown.Converters.Office.Tests/MsgConverterTests.cs` | MSG tests |
| `tests/MarkItDown.Converters.Office.Tests/FixturePath.cs` | Fixture helper |
| `tests/Fixtures/office/` | Test fixture files |

### Files to Modify

| File | Change |
|------|--------|
| `src/MarkItDown.Cli/MarkItDown.Cli.csproj` | Add Office converter reference |
| `src/MarkItDown.Cli/CliRunner.cs` | Register Office converters |
| `MarkItDown.sln` | Add new projects |

---

## Task 1: Create Office Package and CSV Converter

**Files:**
- Create: `src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj`
- Create: `src/MarkItDown.Converters.Office/CsvConverter.cs`
- Create: `tests/MarkItDown.Converters.Office.Tests/MarkItDown.Converters.Office.Tests.csproj`
- Create: `tests/MarkItDown.Converters.Office.Tests/CsvConverterTests.cs`
- Create: `tests/MarkItDown.Converters.Office.Tests/FixturePath.cs`
- Create: `tests/Fixtures/office/sample.csv`

- [ ] **Step 1: Create csproj**

`src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CsvHelper" Version="33.1.0" />
    <PackageReference Include="DocumentFormat.OpenXml" Version="3.5.1" />
    <PackageReference Include="MSGReader" Version="6.0.9" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MarkItDown.Core\MarkItDown.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create CSV test fixture**

Create `tests/Fixtures/office/sample.csv`:

```csv
Name,Department,Salary
Alice,Engineering,95000
Bob,Marketing,72000
Charlie,Engineering,88000
```

- [ ] **Step 3: Write failing tests**

Create `tests/MarkItDown.Converters.Office.Tests/CsvConverterTests.cs`:

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Office;

namespace MarkItDown.Converters.Office.Tests;

public sealed class CsvConverterTests
{
    private readonly CsvConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsCsvExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "data.csv" }));
    }

    [Fact]
    public async Task ConvertAsync_ConvertsCsvToMarkdownTable()
    {
        var result = await _converter.ConvertAsync(
            new DocumentConversionRequest { FilePath = FixturePath.For("office/sample.csv") });

        Assert.Contains("| Name | Department | Salary |", result.Markdown);
        Assert.Contains("| Alice | Engineering | 95000 |", result.Markdown);
        Assert.Equal("Csv", result.Kind);
    }

    [Fact]
    public async Task ConvertAsync_HandlesEmptyCsv()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(csvPath, "");

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = csvPath });

            Assert.Equal(string.Empty, result.Markdown.Trim());
        }
        finally
        {
            File.Delete(csvPath);
        }
    }
}
```

- [ ] **Step 4: Run tests — verify they fail**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Converters.Office.Tests --filter "CsvConverterTests" --no-restore -v minimal 2>&1 | tail -5`

- [ ] **Step 5: Create CsvConverter**

```csharp
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using MarkItDown.Core;

namespace MarkItDown.Converters.Office;

public sealed class CsvConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".csv" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/csv", "text/comma-separated-values" };

    public override double Priority => 0.0;

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("CSV converter requires a file path.");

        try
        {
            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            csv.Read();
            csv.ReadHeader();

            var headers = csv.HeaderRecord;
            if (headers is null || headers.Length == 0)
            {
                return new DocumentConversionResult("Csv", string.Empty);
            }

            var builder = new StringBuilder();
            builder.AppendLine($"| {string.Join(" | ", headers)} |");
            builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", headers.Length))} |");

            while (csv.Read())
            {
                var fields = new List<string>();
                for (var i = 0; i < headers.Length; i++)
                {
                    var field = csv.GetField(i) ?? string.Empty;
                    fields.Add(EscapePipe(field));
                }

                builder.AppendLine($"| {string.Join(" | ", fields)} |");
            }

            var markdown = builder.ToString().TrimEnd();
            return new DocumentConversionResult("Csv", markdown);
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to convert CSV to Markdown: {ex.Message}", ex);
        }
    }

    private static string EscapePipe(string value) =>
        value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
```

- [ ] **Step 6: Create test project and FixturePath**

`tests/MarkItDown.Converters.Office.Tests/MarkItDown.Converters.Office.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MarkItDown.Converters.Office\MarkItDown.Converters.Office.csproj" />
  </ItemGroup>
</Project>
```

`tests/MarkItDown.Converters.Office.Tests/FixturePath.cs`:

```csharp
namespace MarkItDown.Converters.Office.Tests;

internal static class FixturePath
{
    public static string For(string fileName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var root = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(root, "tests", "Fixtures", fileName);
    }
}
```

- [ ] **Step 7: Run tests — verify 3 pass**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Converters.Office.Tests --filter "CsvConverterTests" -v minimal`

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat: add CsvConverter with CsvHelper, Markdown table output

- New package: MarkItDown.Converters.Office
- CSV parsed via CsvHelper, outputs Markdown pipe tables
- Handles empty files, escapes pipe characters
- 3 tests passing"
```

---

## Task 2: XLSX Converter

**Files:**
- Create: `src/MarkItDown.Converters.Office/XlsxConverter.cs`
- Create: `tests/MarkItDown.Converters.Office.Tests/XlsxConverterTests.cs`
- Create: `tests/Fixtures/office/sample.xlsx` (generated programmatically)

- [ ] **Step 1: Generate test fixture**

Create a simple console snippet to generate `tests/Fixtures/office/sample.xlsx` with OpenXml, or create it manually with a known structure. The test fixture should have:
- Sheet "Employees": columns Name/Department/Salary with 2-3 rows
- Sheet "Summary": 1 row of data

Alternative: generate the xlsx file in the test setup using OpenXml SDK:

```csharp
// In test, generate a temp xlsx:
using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
// ... add workbook, worksheet, rows
```

- [ ] **Step 2: Write failing tests**

```csharp
public sealed class XlsxConverterTests
{
    private readonly XlsxConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsXlsxExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "data.xlsx" }));
    }

    [Fact]
    public async Task ConvertAsync_ConvertsXlsxToMarkdown()
    {
        var xlsxPath = CreateTestXlsx();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = xlsxPath });

            Assert.Contains("## Employees", result.Markdown);
            Assert.Contains("| Name |", result.Markdown);
            Assert.Equal("Xlsx", result.Kind);
        }
        finally
        {
            File.Delete(xlsxPath);
        }
    }

    private static string CreateTestXlsx()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        // Generate using DocumentFormat.OpenXml
        // Sheet "Employees" with columns: Name, Department, Salary
        // Rows: Alice/Engineering/95000, Bob/Marketing/72000
        return path;
    }
}
```

- [ ] **Step 3: Create XlsxConverter**

Key implementation:
- Open via `SpreadsheetDocument.Open`
- Iterate `WorkbookPart.WorksheetParts`
- For each sheet, get sheet name from `WorkbookPart.Workbook.Sheets`
- Resolve shared strings via `SharedStringTablePart`
- First row becomes table header, remaining rows become data
- Each sheet separated by `## SheetName` heading

```csharp
public sealed class XlsxConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xlsx" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("XLSX converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                using var doc = SpreadsheetDocument.Open(filePath, false);
                var workbookPart = doc.WorkbookPart
                    ?? throw new ConversionException("Invalid XLSX file: no workbook part.");

                var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
                var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToList()
                    ?? new List<Sheet>();

                var sections = new List<string>();

                foreach (var worksheetPart in workbookPart.WorksheetParts)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var sheetName = sheets.FirstOrDefault(s =>
                        s.Id == workbookPart.GetIdOfPart(worksheetPart))?.Name?.Value ?? "Sheet";

                    var sheetData = worksheetPart.Worksheet.Elements<SheetData>().FirstOrDefault();
                    if (sheetData is null) continue;

                    var rows = sheetData.Elements<Row>().ToList();
                    if (rows.Count == 0) continue;

                    var builder = new StringBuilder();
                    builder.AppendLine($"## {sheetName}");

                    // First row as header
                    var headerCells = rows[0].Elements<Cell>()
                        .Select(c => GetCellValue(c, sharedStrings)).ToList();

                    if (headerCells.Count == 0) continue;

                    builder.AppendLine($"| {string.Join(" | ", headerCells.Select(EscapePipe))} |");
                    builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", headerCells.Count))} |");

                    // Data rows
                    foreach (var row in rows.Skip(1))
                    {
                        var cells = new List<string>();
                        var cellList = row.Elements<Cell>().ToList();

                        // Fill gaps for empty cells
                        for (var i = 0; i < headerCells.Count; i++)
                        {
                            var cell = cellList.FirstOrDefault(c =>
                                GetColumnIndex(c.CellReference?.Value) == i + 1);
                            cells.Add(EscapePipe(cell is not null
                                ? GetCellValue(cell, sharedStrings)
                                : string.Empty));
                        }

                        builder.AppendLine($"| {string.Join(" | ", cells)} |");
                    }

                    sections.Add(builder.ToString().TrimEnd());
                }

                var markdown = string.Join(Environment.NewLine + Environment.NewLine, sections);
                return new DocumentConversionResult("Xlsx", markdown);
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert XLSX: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    private static string GetCellValue(Cell cell, SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.Text ?? string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString && sharedStrings is not null)
        {
            if (int.TryParse(value, out var index) && index < sharedStrings.Count())
            {
                value = sharedStrings.ElementAt(index).InnerText;
            }
        }

        return value;
    }

    private static int GetColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return 0;
        var col = 0;
        foreach (var c in cellRef)
        {
            if (char.IsLetter(c))
                col = col * 26 + (c - 'A' + 1);
            else
                break;
        }
        return col;
    }

    private static string EscapePipe(string value) =>
        value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
```

- [ ] **Step 4: Run tests, commit**

```bash
git add -A && git commit -m "feat: add XlsxConverter with OpenXml SDK, multi-sheet Markdown output"
```

---

## Task 3: DOCX Converter

**Files:**
- Create: `src/MarkItDown.Converters.Office/DocxConverter.cs`
- Create: `tests/MarkItDown.Converters.Office.Tests/DocxConverterTests.cs`
- Create: `tests/Fixtures/office/sample.docx` (generated)

- [ ] **Step 1: Generate test fixture**

Generate a `tests/Fixtures/office/sample.docx` with:
- Title paragraph
- Heading 2: "Introduction"
- Body paragraph with bold/italic
- Bullet list (2 items)
- Table (2 columns, 2 rows)

- [ ] **Step 2: Write failing tests**

```csharp
public sealed class DocxConverterTests
{
    private readonly DocxConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsDocxExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "doc.docx" }));
    }

    [Fact]
    public async Task ConvertAsync_ExtractsHeadingsAndParagraphs()
    {
        var docxPath = CreateTestDocx();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = docxPath });

            Assert.Contains("## Introduction", result.Markdown);
            Assert.Contains("bold", result.Markdown);
            Assert.Equal("Docx", result.Kind);
        }
        finally
        {
            File.Delete(docxPath);
        }
    }
}
```

- [ ] **Step 3: Create DocxConverter**

Key implementation:
- Open via `WordprocessingDocument.Open`
- Iterate `Body.Elements()` — handle `Paragraph`, `Table`
- Headings: detect via `ParagraphProperties.ParagraphStyleId` matching "Heading1"-"Heading9" → map to `#`-`#########`
- Bold/italic: detect via `RunProperties.Bold`/`RunProperties.Italic` on `Run` elements
- Lists: detect via `ParagraphProperties.NumberingProperties`
- Tables: iterate `TableRow` > `TableCell`, output as Markdown pipe tables

```csharp
public sealed class DocxConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".docx" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken)
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
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert DOCX: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    private static string RenderParagraph(Paragraph para)
    {
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Value;

        // Heading detection
        if (styleId is not null && styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
        {
            var levelStr = styleId["Heading".Length..];
            if (int.TryParse(levelStr, out var level) && level is >= 1 and <= 9)
            {
                var text = RenderRuns(para);
                return string.IsNullOrWhiteSpace(text) ? string.Empty
                    : $"{new string('#', level)} {text}";
            }
        }

        // List detection
        var numbering = para.ParagraphProperties?.NumberingProperties;
        if (numbering is not null)
        {
            var text = RenderRuns(para);
            return string.IsNullOrWhiteSpace(text) ? string.Empty
                : $"- {text}";
        }

        // Regular paragraph
        var content = RenderRuns(para);
        return string.IsNullOrWhiteSpace(content) ? string.Empty : content;
    }

    private static string RenderRuns(Paragraph para)
    {
        var builder = new StringBuilder();

        foreach (var run in para.Elements<Run>())
        {
            var text = run.InnerText;
            if (string.IsNullOrEmpty(text)) continue;

            var isBold = run.RunProperties?.Bold is not null;
            var isItalic = run.RunProperties?.Italic is not null;

            if (isBold && isItalic)
                builder.Append($"***{text}***");
            else if (isBold)
                builder.Append($"**{text}**");
            else if (isItalic)
                builder.Append($"*{text}*");
            else
                builder.Append(text);
        }

        return builder.ToString();
    }

    private static string RenderTable(Table table)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count == 0) return string.Empty;

        var data = rows.Select(row =>
            row.Elements<TableCell>()
                .Select(cell => EscapePipe(cell.InnerText.Trim()))
                .ToList()
        ).ToList();

        var columnCount = data.Max(r => r.Count);
        if (columnCount == 0) return string.Empty;

        // Pad rows
        foreach (var row in data)
            while (row.Count < columnCount)
                row.Add(string.Empty);

        var builder = new StringBuilder();
        builder.AppendLine($"| {string.Join(" | ", data[0])} |");
        builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", columnCount))} |");

        foreach (var row in data.Skip(1))
            builder.AppendLine($"| {string.Join(" | ", row)} |");

        return builder.ToString().TrimEnd();
    }

    private static string EscapePipe(string value) =>
        value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
```

- [ ] **Step 4: Run tests, commit**

```bash
git add -A && git commit -m "feat: add DocxConverter with heading/list/table/bold/italic support"
```

---

## Task 4: PPTX Converter

**Files:**
- Create: `src/MarkItDown.Converters.Office/PptxConverter.cs`
- Create: `tests/MarkItDown.Converters.Office.Tests/PptxConverterTests.cs`

- [ ] **Step 1: Write failing tests**

Tests generate a PPTX fixture programmatically and verify slide title and content extraction.

- [ ] **Step 2: Create PptxConverter**

Key implementation:
- Open via `PresentationDocument.Open`
- Iterate `PresentationPart.SlideParts`
- For each slide: extract title (first shape with Title placeholder), body text (paragraphs), notes
- Separate slides with `---` horizontal rule
- Title → `## Slide Title`
- Body paragraphs → bullet items
- Notes → `> Notes:` blockquote

- [ ] **Step 3: Run tests, commit**

```bash
git add -A && git commit -m "feat: add PptxConverter with slide/notes extraction"
```

---

## Task 5: MSG Converter

**Files:**
- Create: `src/MarkItDown.Converters.Office/MsgConverter.cs`
- Create: `tests/MarkItDown.Converters.Office.Tests/MsgConverterTests.cs`

- [ ] **Step 1: Write failing tests**

Tests use a .msg fixture or mock the MSGReader.

- [ ] **Step 2: Create MsgConverter**

Key implementation:
- Open via `Storage.Message(filePath)`
- Extract Subject, From, To, Date as metadata headers
- Extract body (prefer HTML → use HtmlConverter logic, fallback to plain text)
- Output structured Markdown:

```markdown
# {Subject}

**From:** {sender}
**To:** {recipients}
**Date:** {date}

---

{body content}
```

- [ ] **Step 3: Run tests, commit**

```bash
git add -A && git commit -m "feat: add MsgConverter for Outlook email files"
```

---

## Task 6: Update CLI and Solution, Final Verification

- [ ] **Step 1: Update CLI csproj**

Add to `src/MarkItDown.Cli/MarkItDown.Cli.csproj`:
```xml
<ProjectReference Include="..\MarkItDown.Converters.Office\MarkItDown.Converters.Office.csproj" />
```

- [ ] **Step 2: Update CliRunner.cs**

Add using and register converters:
```csharp
using MarkItDown.Converters.Office;
// In engine construction:
.Add(new CsvConverter())
.Add(new XlsxConverter())
.Add(new DocxConverter())
.Add(new PptxConverter())
.Add(new MsgConverter())
```

- [ ] **Step 3: Add projects to solution**

```bash
dotnet sln add src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj
dotnet sln add tests/MarkItDown.Converters.Office.Tests/MarkItDown.Converters.Office.Tests.csproj
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test --filter "FullyQualifiedName~MarkItDown" -v minimal`

- [ ] **Step 5: Verify build clean**

Run: `dotnet build -v minimal`

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: integrate Office converters into CLI, add to solution

Phase 2 complete: DOCX, PPTX, XLSX, CSV, MSG all supported"
```

---

## Acceptance Criteria

- [ ] CSV converts to Markdown table with headers and rows
- [ ] XLSX converts with multi-sheet support (## SheetName headings)
- [ ] DOCX preserves headings (H1-H9), bold, italic, lists, tables
- [ ] PPTX extracts slide titles, body text, and notes
- [ ] MSG extracts subject, from, to, date, and body
- [ ] All Office formats registered in CLI
- [ ] Core tests still pass (no regression)
- [ ] `dotnet build` succeeds with 0 errors
