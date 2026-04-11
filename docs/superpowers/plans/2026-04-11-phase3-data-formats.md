# Phase 3: Data Formats — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add JSON, JSONL, XML, RSS/Atom, IPYNB, EPUB, ZIP, and Markdown passthrough converters to the `MarkItDown.Converters.Data` package.

**Architecture:** New `MarkItDown.Converters.Data` package with 8 converters. All use BCL libraries only — no external NuGet dependencies. JSON/JSONL/IPYNB use `System.Text.Json`, XML/RSS use `System.Xml.Linq`, EPUB/ZIP use `System.IO.Compression`. ZIP and EPUB converters create a `MarkItDownEngine` via `CreateWithAllConverters()` for recursive sub-file conversion.

**Tech Stack:** .NET 8, xUnit, System.Text.Json, System.Xml.Linq, System.IO.Compression

**Spec:** `docs/superpowers/specs/2026-04-11-markitdown-csharp-full-alignment-design.md` (Phase 3)

---

## File Structure

### Files to Create

| File | Responsibility |
|------|---------------|
| `src/MarkItDown.Converters.Data/MarkItDown.Converters.Data.csproj` | Data converter NuGet package |
| `src/MarkItDown.Converters.Data/MarkdownConverter.cs` | Markdown passthrough |
| `src/MarkItDown.Converters.Data/JsonConverter.cs` | JSON → Markdown |
| `src/MarkItDown.Converters.Data/JsonlConverter.cs` | JSONL → Markdown |
| `src/MarkItDown.Converters.Data/XmlConverter.cs` | XML → Markdown |
| `src/MarkItDown.Converters.Data/IpynbConverter.cs` | Jupyter Notebook → Markdown |
| `src/MarkItDown.Converters.Data/RssConverter.cs` | RSS/Atom → Markdown |
| `src/MarkItDown.Converters.Data/EpubConverter.cs` | EPUB → Markdown |
| `src/MarkItDown.Converters.Data/ZipConverter.cs` | ZIP → recursive Markdown |
| `tests/MarkItDown.Converters.Data.Tests/MarkItDown.Converters.Data.Tests.csproj` | Test project |
| `tests/MarkItDown.Converters.Data.Tests/MarkdownConverterTests.cs` | Markdown tests |
| `tests/MarkItDown.Converters.Data.Tests/JsonConverterTests.cs` | JSON tests |
| `tests/MarkItDown.Converters.Data.Tests/JsonlConverterTests.cs` | JSONL tests |
| `tests/MarkItDown.Converters.Data.Tests/XmlConverterTests.cs` | XML tests |
| `tests/MarkItDown.Converters.Data.Tests/IpynbConverterTests.cs` | IPYNB tests |
| `tests/MarkItDown.Converters.Data.Tests/RssConverterTests.cs` | RSS tests |
| `tests/MarkItDown.Converters.Data.Tests/EpubConverterTests.cs` | EPUB tests |
| `tests/MarkItDown.Converters.Data.Tests/ZipConverterTests.cs` | ZIP tests |
| `tests/MarkItDown.Converters.Data.Tests/FixturePath.cs` | Fixture helper |

### Files to Modify

| File | Change |
|------|--------|
| `src/MarkItDown.Cli/MarkItDown.Cli.csproj` | Add Data converter reference |
| `src/MarkItDown.Cli/CliRunner.cs` | Register Data converters |
| `MarkItDown.sln` | Add new projects |

---

## Task 1: Create Data Package and Markdown + JSON + JSONL Converters

**Files:**
- Create: `src/MarkItDown.Converters.Data/MarkItDown.Converters.Data.csproj`
- Create: `src/MarkItDown.Converters.Data/MarkdownConverter.cs`
- Create: `src/MarkItDown.Converters.Data/JsonConverter.cs`
- Create: `src/MarkItDown.Converters.Data/JsonlConverter.cs`
- Create: `tests/MarkItDown.Converters.Data.Tests/MarkItDown.Converters.Data.Tests.csproj`
- Create: `tests/MarkItDown.Converters.Data.Tests/FixturePath.cs`
- Create: `tests/MarkItDown.Converters.Data.Tests/MarkdownConverterTests.cs`
- Create: `tests/MarkItDown.Converters.Data.Tests/JsonConverterTests.cs`
- Create: `tests/MarkItDown.Converters.Data.Tests/JsonlConverterTests.cs`

- [ ] **Step 1: Create csproj**

`src/MarkItDown.Converters.Data/MarkItDown.Converters.Data.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MarkItDown.Core\MarkItDown.Core.csproj" />
  </ItemGroup>
</Project>
```

No external NuGet packages — only BCL (`System.Text.Json`, `System.Xml.Linq`, `System.IO.Compression`).

- [ ] **Step 2: Create MarkdownConverter**

`src/MarkItDown.Converters.Data/MarkdownConverter.cs`:

```csharp
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class MarkdownConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/markdown", "text/x-markdown" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("Markdown converter requires a file path.");

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            return new DocumentConversionResult("Markdown", content);
        }
        catch (ConversionException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to read Markdown: {ex.Message}", ex);
        }
    }
}
```

- [ ] **Step 3: Create JsonConverter**

`src/MarkItDown.Converters.Data/JsonConverter.cs`:

```csharp
using System.Text;
using System.Text.Json;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class JsonConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/json" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("JSON converter requires a file path.");

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);

            // Parse and re-serialize with indentation for readability
            var jsonDoc = JsonDocument.Parse(content);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var pretty = JsonSerializer.Serialize(jsonDoc, options);

            var markdown = $"```json{Environment.NewLine}{pretty}{Environment.NewLine}```";
            return new DocumentConversionResult("Json", markdown);
        }
        catch (ConversionException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to convert JSON: {ex.Message}", ex);
        }
    }
}
```

- [ ] **Step 4: Create JsonlConverter**

`src/MarkItDown.Converters.Data/JsonlConverter.cs`:

```csharp
using System.Text;
using System.Text.Json;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class JsonlConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jsonl" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/jsonl", "application/x-jsonlines" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("JSONL converter requires a file path.");

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length == 0)
                return new DocumentConversionResult("Jsonl", string.Empty);

            var builder = new StringBuilder();
            builder.AppendLine("```jsonl");

            var options = new JsonSerializerOptions { WriteIndented = true };

            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                try
                {
                    var jsonDoc = JsonDocument.Parse(trimmed);
                    var pretty = JsonSerializer.Serialize(jsonDoc, options);
                    builder.AppendLine(pretty);
                }
                catch (JsonException)
                {
                    builder.AppendLine(trimmed);
                }
            }

            builder.Append("```");
            return new DocumentConversionResult("Jsonl", builder.ToString());
        }
        catch (ConversionException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to convert JSONL: {ex.Message}", ex);
        }
    }
}
```

- [ ] **Step 5: Create test project**

`tests/MarkItDown.Converters.Data.Tests/MarkItDown.Converters.Data.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\MarkItDown.Converters.Data\MarkItDown.Converters.Data.csproj" />
  </ItemGroup>
</Project>
```

`tests/MarkItDown.Converters.Data.Tests/FixturePath.cs`:

```csharp
namespace MarkItDown.Converters.Data.Tests;

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

- [ ] **Step 6: Write tests**

`tests/MarkItDown.Converters.Data.Tests/MarkdownConverterTests.cs`:

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Data;

namespace MarkItDown.Converters.Data.Tests;

public sealed class MarkdownConverterTests
{
    private readonly MarkdownConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsMdExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "readme.md" }));
    }

    [Fact]
    public void CanConvert_AcceptsMarkdownExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "doc.markdown" }));
    }

    [Fact]
    public async Task ConvertAsync_ReturnsContentVerbatim()
    {
        var content = "# Hello\n\nThis is **markdown**.";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(path, content);

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Equal(content, result.Markdown);
            Assert.Equal("Markdown", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

`tests/MarkItDown.Converters.Data.Tests/JsonConverterTests.cs`:

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Data;

namespace MarkItDown.Converters.Data.Tests;

public sealed class JsonConverterTests
{
    private readonly JsonConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsJsonExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "data.json" }));
    }

    [Fact]
    public async Task ConvertAsync_WrapsInCodeFence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{\"name\":\"Alice\",\"age\":30}");

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("```json", result.Markdown);
            Assert.Contains("\"name\"", result.Markdown);
            Assert.Contains("Alice", result.Markdown);
            Assert.Equal("Json", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

`tests/MarkItDown.Converters.Data.Tests/JsonlConverterTests.cs`:

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Data;

namespace MarkItDown.Converters.Data.Tests;

public sealed class JsonlConverterTests
{
    private readonly JsonlConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsJsonlExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "data.jsonl" }));
    }

    [Fact]
    public async Task ConvertAsync_PrettyPrintsEachLine()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(path, "{\"name\":\"Alice\"}\n{\"name\":\"Bob\"}");

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("```jsonl", result.Markdown);
            Assert.Contains("Alice", result.Markdown);
            Assert.Contains("Bob", result.Markdown);
            Assert.Equal("Jsonl", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_HandlesEmptyFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(path, "");

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Equal(string.Empty, result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

- [ ] **Step 7: Run tests — verify 7 pass**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Converters.Data.Tests -v minimal`

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat: add Data package with Markdown, JSON, JSONL converters

- New package: MarkItDown.Converters.Data (zero external deps)
- MarkdownConverter: passthrough, returns content verbatim
- JsonConverter: pretty-print wrapped in ```json code fence
- JsonlConverter: each line pretty-printed in ```jsonl block
- 7 tests passing"
```

---

## Task 2: XML Converter

**Files:**
- Create: `src/MarkItDown.Converters.Data/XmlConverter.cs`
- Create: `tests/MarkItDown.Converters.Data.Tests/XmlConverterTests.cs`

- [ ] **Step 1: Write failing tests**

`tests/MarkItDown.Converters.Data.Tests/XmlConverterTests.cs`:

```csharp
using System.Xml.Linq;
using MarkItDown.Core;
using MarkItDown.Converters.Data;

namespace MarkItDown.Converters.Data.Tests;

public sealed class XmlConverterTests
{
    private readonly XmlConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsXmlExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "data.xml" }));
    }

    [Fact]
    public async Task ConvertAsync_WrapsInCodeFence()
    {
        var xml = "<root><item key=\"a\">Value A</item><item key=\"b\">Value B</item></root>";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(path, xml);

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("```xml", result.Markdown);
            Assert.Contains("<root>", result.Markdown);
            Assert.Contains("Value A", result.Markdown);
            Assert.Equal("Xml", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_FormatsWithIndentation()
    {
        var xml = "<root><child>text</child></root>";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(path, xml);

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            // Should be indented (not all on one line)
            Assert.Contains(Environment.NewLine, result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Converters.Data.Tests --filter "XmlConverterTests" --no-restore -v minimal 2>&1 | tail -5`

- [ ] **Step 3: Create XmlConverter**

`src/MarkItDown.Converters.Data/XmlConverter.cs`:

```csharp
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class XmlConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xml" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/xml", "text/xml"
        };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("XML converter requires a file path.");

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var doc = XDocument.Parse(content);

            var builder = new StringBuilder();
            builder.AppendLine("```xml");

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = doc.Declaration is null,
            };

            using var writer = XmlWriter.Create(builder, settings);
            doc.WriteTo(writer);

            builder.AppendLine();
            builder.Append("```");

            return new DocumentConversionResult("Xml", builder.ToString());
        }
        catch (ConversionException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to convert XML: {ex.Message}", ex);
        }
    }
}
```

- [ ] **Step 4: Run tests, commit**

```bash
cd <worktree> && dotnet test tests/MarkItDown.Converters.Data.Tests --filter "XmlConverterTests" -v minimal
git add -A && git commit -m "feat: add XmlConverter with formatted output in code fence"
```

---

## Task 3: IPYNB (Jupyter Notebook) Converter

**Files:**
- Create: `src/MarkItDown.Converters.Data/IpynbConverter.cs`
- Create: `tests/MarkItDown.Converters.Data.Tests/IpynbConverterTests.cs`

- [ ] **Step 1: Write failing tests**

Tests generate a valid notebook JSON structure programmatically.

`tests/MarkItDown.Converters.Data.Tests/IpynbConverterTests.cs`:

```csharp
using System.Text.Json;
using MarkItDown.Core;
using MarkItDown.Converters.Data;

namespace MarkItDown.Converters.Data.Tests;

public sealed class IpynbConverterTests
{
    private readonly IpynbConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsIpynbExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "notebook.ipynb" }));
    }

    [Fact]
    public async Task ConvertAsync_ConvertsMarkdownAndCodeCells()
    {
        var path = CreateTestNotebook();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("# Test Notebook", result.Markdown);
            Assert.Contains("```python", result.Markdown);
            Assert.Contains("print(", result.Markdown);
            Assert.Equal("Ipynb", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_PreservesMarkdownCells()
    {
        var path = CreateTestNotebook();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("This is a **markdown** cell.", result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTestNotebook()
    {
        var notebook = new
        {
            nbformat = 4,
            nbformat_minor = 5,
            metadata = new { },
            cells = new object[]
            {
                new
                {
                    cell_type = "markdown",
                    metadata = new { },
                    source = new[] { "# Test Notebook" }
                },
                new
                {
                    cell_type = "markdown",
                    metadata = new { },
                    source = new[] { "This is a **markdown** cell." }
                },
                new
                {
                    cell_type = "code",
                    metadata = new { },
                    source = new[] { "print(\"hello world\")" },
                    execution_count = 1,
                    outputs = Array.Empty<object>()
                }
            }
        };

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.ipynb");
        var json = JsonSerializer.Serialize(notebook);
        File.WriteAllText(path, json);
        return path;
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

- [ ] **Step 3: Create IpynbConverter**

`src/MarkItDown.Converters.Data/IpynbConverter.cs`:

```csharp
using System.Text;
using System.Text.Json;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class IpynbConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ipynb" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/x-ipynb+json" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("IPYNB converter requires a file path.");

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var builder = new StringBuilder();

            if (root.TryGetProperty("cells", out var cells))
            {
                foreach (var cell in cells.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var cellType = cell.TryGetProperty("cell_type", out var typeEl)
                        ? typeEl.GetString() ?? "" : "";

                    var source = cell.TryGetProperty("source", out var sourceEl)
                        ? ExtractSource(sourceEl) : "";

                    if (string.IsNullOrWhiteSpace(source))
                        continue;

                    switch (cellType)
                    {
                        case "markdown":
                            builder.AppendLine(source);
                            builder.AppendLine();
                            break;
                        case "code":
                            var language = "python";
                            if (root.TryGetProperty("metadata", out var meta)
                                && meta.TryGetProperty("language_info", out var langInfo)
                                && langInfo.TryGetProperty("name", out var langName))
                            {
                                language = langName.GetString() ?? "python";
                            }
                            builder.AppendLine($"```{language}");
                            builder.AppendLine(source);
                            builder.AppendLine("```");
                            builder.AppendLine();
                            break;
                        default:
                            builder.AppendLine("```");
                            builder.AppendLine(source);
                            builder.AppendLine("```");
                            builder.AppendLine();
                            break;
                    }
                }
            }

            var markdown = builder.ToString().TrimEnd();
            return new DocumentConversionResult("Ipynb", markdown);
        }
        catch (ConversionException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to convert IPYNB: {ex.Message}", ex);
        }
    }

    private static string ExtractSource(JsonElement sourceEl)
    {
        if (sourceEl.ValueKind == JsonValueKind.Array)
            return string.Join("", sourceEl.EnumerateArray()
                .Select(e => e.GetString() ?? ""));

        return sourceEl.GetString() ?? "";
    }
}
```

- [ ] **Step 4: Run tests, commit**

```bash
cd <worktree> && dotnet test tests/MarkItDown.Converters.Data.Tests --filter "IpynbConverterTests" -v minimal
git add -A && git commit -m "feat: add IpynbConverter for Jupyter Notebooks with cell type handling"
```

---

## Task 4: RSS/Atom Converter

**Files:**
- Create: `src/MarkItDown.Converters.Data/RssConverter.cs`
- Create: `tests/MarkItDown.Converters.Data.Tests/RssConverterTests.cs`

- [ ] **Step 1: Write failing tests**

Tests generate RSS and Atom XML fixtures programmatically.

`tests/MarkItDown.Converters.Data.Tests/RssConverterTests.cs`:

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Data;

namespace MarkItDown.Converters.Data.Tests;

public sealed class RssConverterTests
{
    private readonly RssConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsRssExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "feed.rss" }));
    }

    [Fact]
    public void CanConvert_AcceptsAtomExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "feed.atom" }));
    }

    [Fact]
    public async Task ConvertAsync_ExtractsRssFeedItems()
    {
        var path = CreateRssFeed();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("# Tech News", result.Markdown);
            Assert.Contains("## First Article", result.Markdown);
            Assert.Contains("## Second Article", result.Markdown);
            Assert.Equal("Rss", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_ExtractsAtomFeedItems()
    {
        var path = CreateAtomFeed();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("# Atom Blog", result.Markdown);
            Assert.Contains("## Atom Entry", result.Markdown);
            Assert.Equal("Rss", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateRssFeed()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
  <channel>
    <title>Tech News</title>
    <description>Latest tech news</description>
    <item>
      <title>First Article</title>
      <description>Description of the first article</description>
      <pubDate>Mon, 01 Jan 2024 00:00:00 GMT</pubDate>
    </item>
    <item>
      <title>Second Article</title>
      <description>Description of the second article</description>
      <pubDate>Tue, 02 Jan 2024 00:00:00 GMT</pubDate>
    </item>
  </channel>
</rss>";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.rss");
        File.WriteAllText(path, xml);
        return path;
    }

    private static string CreateAtomFeed()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<feed xmlns=""http://www.w3.org/2005/Atom"">
  <title>Atom Blog</title>
  <subtitle>An atom feed</subtitle>
  <entry>
    <title>Atom Entry</title>
    <summary>Summary of the entry</summary>
    <updated>2024-01-01T00:00:00Z</updated>
  </entry>
</feed>";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.atom");
        File.WriteAllText(path, xml);
        return path;
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

- [ ] **Step 3: Create RssConverter**

`src/MarkItDown.Converters.Data/RssConverter.cs`:

```csharp
using System.Text;
using System.Xml.Linq;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class RssConverter : BaseConverter
{
    private static readonly XNamespace AtomNs = "http://www.w3.org/2005/Atom";

    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".rss", ".atom" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/rss+xml", "application/atom+xml"
        };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("RSS converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var doc = XDocument.Parse(content);
                var root = doc.Root ?? throw new ConversionException("Empty RSS/Atom document.");

                var builder = new StringBuilder();

                if (root.Name.LocalName == "rss" || root.Name.LocalName == "Rss")
                {
                    RenderRss(root, builder);
                }
                else if (root.Name.LocalName == "feed")
                {
                    RenderAtom(root, builder);
                }
                else
                {
                    throw new ConversionException(
                        $"Unrecognized feed format: {root.Name.LocalName}");
                }

                var markdown = builder.ToString().TrimEnd();
                return new DocumentConversionResult("Rss", markdown);
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert RSS: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    private static void RenderRss(XElement root, StringBuilder builder)
    {
        var channel = root.Element("channel");
        if (channel is null) return;

        var title = channel.Element("title")?.Value ?? "Untitled Feed";
        var description = channel.Element("description")?.Value;

        builder.AppendLine($"# {title}");
        if (!string.IsNullOrWhiteSpace(description))
            builder.AppendLine(description);
        builder.AppendLine();

        foreach (var item in channel.Elements("item"))
        {
            var itemTitle = item.Element("title")?.Value;
            if (itemTitle is not null)
            {
                builder.AppendLine($"## {itemTitle}");
            }

            var date = item.Element("pubDate")?.Value;
            if (date is not null)
                builder.AppendLine($"Published: {date}");

            var desc = item.Element("description")?.Value;
            if (!string.IsNullOrWhiteSpace(desc))
                builder.AppendLine(desc);

            builder.AppendLine();
        }
    }

    private static void RenderAtom(XElement root, StringBuilder builder)
    {
        var title = root.Element(AtomNs + "title")?.Value
            ?? root.Element("title")?.Value
            ?? "Untitled Feed";
        var subtitle = root.Element(AtomNs + "subtitle")?.Value
            ?? root.Element("subtitle")?.Value;

        builder.AppendLine($"# {title}");
        if (!string.IsNullOrWhiteSpace(subtitle))
            builder.AppendLine(subtitle);
        builder.AppendLine();

        foreach (var entry in root.Elements(AtomNs + "entry").Concat(root.Elements("entry")))
        {
            var entryTitle = entry.Element(AtomNs + "title")?.Value
                ?? entry.Element("title")?.Value;
            if (entryTitle is not null)
                builder.AppendLine($"## {entryTitle}");

            var updated = entry.Element(AtomNs + "updated")?.Value
                ?? entry.Element("updated")?.Value;
            if (updated is not null)
                builder.AppendLine($"Updated: {updated}");

            var summary = entry.Element(AtomNs + "summary")?.Value
                ?? entry.Element("summary")?.Value;
            if (!string.IsNullOrWhiteSpace(summary))
                builder.AppendLine(summary);

            builder.AppendLine();
        }
    }
}
```

- [ ] **Step 4: Run tests, commit**

```bash
cd <worktree> && dotnet test tests/MarkItDown.Converters.Data.Tests --filter "RssConverterTests" -v minimal
git add -A && git commit -m "feat: add RssConverter with RSS 2.0 and Atom feed support"
```

---

## Task 5: EPUB Converter

**Files:**
- Create: `src/MarkItDown.Converters.Data/EpubConverter.cs`
- Create: `tests/MarkItDown.Converters.Data.Tests/EpubConverterTests.cs`

- [ ] **Step 1: Write failing tests**

Tests generate a minimal EPUB (ZIP with correct structure) programmatically.

`tests/MarkItDown.Converters.Data.Tests/EpubConverterTests.cs`:

```csharp
using System.IO.Compression;
using System.Xml.Linq;
using MarkItDown.Core;
using MarkItDown.Converters.Data;

namespace MarkItDown.Converters.Data.Tests;

public sealed class EpubConverterTests
{
    private readonly EpubConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsEpubExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "book.epub" }));
    }

    [Fact]
    public async Task ConvertAsync_ExtractsMetadataAndChapters()
    {
        var path = CreateTestEpub();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("**Title:** Test Book", result.Markdown);
            Assert.Contains("**Author:** John Doe", result.Markdown);
            Assert.Contains("## Chapter 1", result.Markdown);
            Assert.Contains("Hello world", result.Markdown);
            Assert.Equal("Epub", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTestEpub()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.epub");

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            // mimetype must be first entry, stored (no compression)
            var mimetypeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(mimetypeEntry.Open()))
                writer.Write("application/epub+zip");

            // META-INF/container.xml
            var containerXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"">
  <rootfiles>
    <rootfile full-path=""OEBPS/content.opf"" media-type=""application/oebps-package+xml""/>
  </rootfiles>
</container>";
            var containerEntry = zip.CreateEntry("META-INF/container.xml");
            using (var writer = new StreamWriter(containerEntry.Open()))
                writer.Write(containerXml);

            // OEBPS/content.opf
            var opfXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<package xmlns=""http://www.idpf.org/2007/opf"" version=""3.0"">
  <metadata xmlns:dc=""http://purl.org/dc/elements/1.1/"">
    <dc:title>Test Book</dc:title>
    <dc:creator>John Doe</dc:creator>
    <dc:language>en</dc:language>
  </metadata>
  <manifest>
    <item id=""chapter1"" href=""chapter1.xhtml"" media-type=""application/xhtml+xml""/>
    <item id=""nav"" href=""nav.xhtml"" media-type=""application/xhtml+xml""/>
  </manifest>
  <spine>
    <itemref idref=""chapter1""/>
  </spine>
</package>";
            var opfEntry = zip.CreateEntry("OEBPS/content.opf");
            using (var writer = new StreamWriter(opfEntry.Open()))
                writer.Write(opfXml);

            // OEBPS/chapter1.xhtml
            var chapter1 = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<head><title>Chapter 1</title></head>
<body>
  <h2>Chapter 1</h2>
  <p>Hello world from the test EPUB.</p>
</body>
</html>";
            var chapterEntry = zip.CreateEntry("OEBPS/chapter1.xhtml");
            using (var writer = new StreamWriter(chapterEntry.Open()))
                writer.Write(chapter1);
        }

        return path;
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

- [ ] **Step 3: Create EpubConverter**

`src/MarkItDown.Converters.Data/EpubConverter.cs`:

```csharp
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class EpubConverter : BaseConverter
{
    private static readonly XNamespace OpfNs = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace ContainerNs = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";

    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".epub" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/epub+zip" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("EPUB converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                using var archive = ZipFile.OpenRead(filePath);

                // 1. Find OPF path from container.xml
                var opfPath = FindOpfPath(archive);

                // 2. Parse OPF for metadata and spine
                var opfEntry = archive.GetEntry(opfPath)
                    ?? throw new ConversionException($"OPF file not found: {opfPath}");
                var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? "";

                XDocument opfDoc;
                using (var opfStream = opfEntry.Open())
                    opfDoc = XDocument.Load(opfStream);

                var builder = new StringBuilder();
                RenderMetadata(opfDoc, builder);
                builder.AppendLine();

                // 3. Process spine items in reading order
                var manifest = GetManifest(opfDoc);
                var spineIds = GetSpineIds(opfDoc);

                foreach (var id in spineIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!manifest.TryGetValue(id, out var href)) continue;

                    var entryPath = string.IsNullOrEmpty(opfDir)
                        ? href : $"{opfDir}/{href}";

                    var entry = archive.GetEntry(entryPath);
                    if (entry is null) continue;

                    using var stream = entry.Open();
                    var chapterText = ExtractHtmlText(stream);
                    if (!string.IsNullOrWhiteSpace(chapterText))
                    {
                        builder.AppendLine(chapterText);
                        builder.AppendLine();
                    }
                }

                var markdown = builder.ToString().TrimEnd();
                return new DocumentConversionResult("Epub", markdown);
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert EPUB: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    private static string FindOpfPath(ZipArchive archive)
    {
        var containerEntry = archive.GetEntry("META-INF/container.xml")
            ?? throw new ConversionException("Not a valid EPUB: missing META-INF/container.xml.");

        using var stream = containerEntry.Open();
        var doc = XDocument.Load(stream);

        var rootFile = doc.Descendants(ContainerNs + "rootfile").FirstOrDefault()
            ?? doc.Descendants("rootfile").FirstOrDefault()
            ?? throw new ConversionException("No rootfile found in container.xml.");

        return rootFile.Attribute("full-path")?.Value
            ?? throw new ConversionException("No full-path in rootfile element.");
    }

    private static void RenderMetadata(XDocument opfDoc, StringBuilder builder)
    {
        var title = opfDoc.Descendants(DcNs + "title").FirstOrDefault()?.Value
            ?? opfDoc.Descendants("title").FirstOrDefault()?.Value;

        var author = opfDoc.Descendants(DcNs + "creator").FirstOrDefault()?.Value
            ?? opfDoc.Descendants("creator").FirstOrDefault()?.Value;

        var language = opfDoc.Descendants(DcNs + "language").FirstOrDefault()?.Value
            ?? opfDoc.Descendants("language").FirstOrDefault()?.Value;

        if (title is not null)
            builder.AppendLine($"**Title:** {title}");
        if (author is not null)
            builder.AppendLine($"**Author:** {author}");
        if (language is not null)
            builder.AppendLine($"**Language:** {language}");
    }

    private static Dictionary<string, string> GetManifest(XDocument opfDoc)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var items = opfDoc.Descendants(OpfNs + "item")
            .Concat(opfDoc.Descendants("item"));

        foreach (var item in items)
        {
            var id = item.Attribute("id")?.Value;
            var href = item.Attribute("href")?.Value;
            if (id is not null && href is not null)
                result[id] = href;
        }

        return result;
    }

    private static List<string> GetSpineIds(XDocument opfDoc)
    {
        return opfDoc.Descendants(OpfNs + "itemref")
            .Concat(opfDoc.Descendants("itemref"))
            .Select(e => e.Attribute("idref")?.Value)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();
    }

    private static string ExtractHtmlText(Stream stream)
    {
        try
        {
            var doc = XDocument.Load(stream);
            var body = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "body");

            if (body is null) return "";

            var builder = new StringBuilder();
            RenderHtmlElement(body, builder);
            return builder.ToString().TrimEnd();
        }
        catch
        {
            return "";
        }
    }

    private static void RenderHtmlElement(XElement element, StringBuilder builder)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                builder.Append(text.Value);
            }
            else if (node is XElement child)
            {
                var tag = child.Name.LocalName.ToLowerInvariant();
                switch (tag)
                {
                    case "h1":
                        builder.AppendLine();
                        builder.AppendLine($"# {child.Value.Trim()}");
                        break;
                    case "h2":
                        builder.AppendLine();
                        builder.AppendLine($"## {child.Value.Trim()}");
                        break;
                    case "h3":
                        builder.AppendLine();
                        builder.AppendLine($"### {child.Value.Trim()}");
                        break;
                    case "p":
                        builder.AppendLine();
                        builder.Append(child.Value.Trim());
                        builder.AppendLine();
                        break;
                    case "li":
                        builder.AppendLine($"- {child.Value.Trim()}");
                        break;
                    case "br":
                        builder.AppendLine();
                        break;
                    default:
                        RenderHtmlElement(child, builder);
                        break;
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run tests, commit**

```bash
cd <worktree> && dotnet test tests/MarkItDown.Converters.Data.Tests --filter "EpubConverterTests" -v minimal
git add -A && git commit -m "feat: add EpubConverter with metadata and chapter extraction"
```

---

## Task 6: ZIP Converter

**Files:**
- Create: `src/MarkItDown.Converters.Data/ZipConverter.cs`
- Create: `tests/MarkItDown.Converters.Data.Tests/ZipConverterTests.cs`

- [ ] **Step 1: Write failing tests**

`tests/MarkItDown.Converters.Data.Tests/ZipConverterTests.cs`:

```csharp
using System.IO.Compression;
using MarkItDown.Core;
using MarkItDown.Converters.Data;

namespace MarkItDown.Converters.Data.Tests;

public sealed class ZipConverterTests
{
    private readonly ZipConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsZipExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "archive.zip" }));
    }

    [Fact]
    public async Task ConvertAsync_RecursivelyConvertsFiles()
    {
        var path = CreateTestZip();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("archive.zip", result.Markdown);
            Assert.Contains("## File: readme.md", result.Markdown);
            Assert.Contains("Hello from ZIP", result.Markdown);
            Assert.Equal("Zip", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_SkipsDirectories()
    {
        var path = CreateTestZip();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            // Should have content for files but not for directories
            Assert.DoesNotContain("## File: subdir/", result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTestZip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var readmeEntry = zip.CreateEntry("readme.md");
            using (var writer = new StreamWriter(readmeEntry.Open()))
                writer.Write("# Hello from ZIP\n\nThis was inside a zip file.");

            var dataEntry = zip.CreateEntry("data.json");
            using (var writer = new StreamWriter(dataEntry.Open()))
                writer.Write("{\"key\": \"value\"}");

            // Add a directory entry (empty name = directory)
            var dirEntry = zip.CreateEntry("subdir/");
        }

        return path;
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

- [ ] **Step 3: Create ZipConverter**

`src/MarkItDown.Converters.Data/ZipConverter.cs`:

```csharp
using System.IO.Compression;
using System.Text;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class ZipConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/zip" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("ZIP converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                using var archive = ZipFile.OpenRead(filePath);
                var engine = MarkItDownEngine.CreateWithAllConverters();

                var sections = new List<string>();
                sections.Add($"Content from the zip file `{Path.GetFileName(filePath)}`:");

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Skip directories (entries with empty name or ending with /)
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var tempPath = Path.Combine(Path.GetTempPath(),
                        $"{Guid.NewGuid():N}{Path.GetExtension(entry.Name)}");

                    try
                    {
                        entry.ExtractToFile(tempPath, overwrite: true);

                        try
                        {
                            var result = engine.ConvertAsync(tempPath, cancellationToken)
                                .GetAwaiter().GetResult();

                            sections.Add($"## File: {entry.FullName}");
                            sections.Add(result.Markdown);
                        }
                        catch (UnsupportedFormatException)
                        {
                            sections.Add($"## File: {entry.FullName}");
                            sections.Add($"*(Unsupported format: {Path.GetExtension(entry.Name)})*");
                        }
                    }
                    finally
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                }

                var markdown = string.Join(Environment.NewLine + Environment.NewLine, sections);
                return new DocumentConversionResult("Zip", markdown);
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert ZIP: {ex.Message}", ex);
            }
        }, cancellationToken);
    }
}
```

- [ ] **Step 4: Run tests, commit**

```bash
cd <worktree> && dotnet test tests/MarkItDown.Converters.Data.Tests --filter "ZipConverterTests" -v minimal
git add -A && git commit -m "feat: add ZipConverter with recursive sub-file conversion"
```

---

## Task 7: Update CLI and Solution, Final Verification

- [ ] **Step 1: Update CLI csproj**

Add to `src/MarkItDown.Cli/MarkItDown.Cli.csproj`:
```xml
<ProjectReference Include="..\MarkItDown.Converters.Data\MarkItDown.Converters.Data.csproj" />
```

- [ ] **Step 2: Update CliRunner.cs**

Add using and register converters:
```csharp
using MarkItDown.Converters.Data;
// In engine construction:
.Add(new MarkdownConverter())
.Add(new JsonConverter())
.Add(new JsonlConverter())
.Add(new XmlConverter())
.Add(new IpynbConverter())
.Add(new RssConverter())
.Add(new EpubConverter())
.Add(new ZipConverter())
```

- [ ] **Step 3: Add projects to solution**

```bash
dotnet sln add src/MarkItDown.Converters.Data/MarkItDown.Converters.Data.csproj
dotnet sln add tests/MarkItDown.Converters.Data.Tests/MarkItDown.Converters.Data.Tests.csproj
```

- [ ] **Step 4: Run all tests**

Run: `cd <worktree> && dotnet test -v minimal`

- [ ] **Step 5: Verify build clean**

Run: `cd <worktree> && dotnet build -v minimal`

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: integrate Data converters into CLI, add to solution

Phase 3 complete: JSON, JSONL, XML, RSS/Atom, IPYNB, EPUB, ZIP, Markdown all supported"
```

---

## Acceptance Criteria

- [ ] Markdown files pass through unchanged
- [ ] JSON pretty-printed in code fence
- [ ] JSONL each line pretty-printed
- [ ] XML formatted with indentation in code fence
- [ ] IPYNB converts markdown cells and code cells with language tags
- [ ] RSS 2.0 and Atom feeds extract title, items, descriptions
- [ ] EPUB extracts metadata and chapters in spine order
- [ ] ZIP recursively converts each file using registered converters
- [ ] All converters registered in CLI
- [ ] Core + Office tests still pass (no regression)
- [ ] `dotnet build` succeeds with 0 errors
