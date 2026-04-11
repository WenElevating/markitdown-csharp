# Phase 5: Web + MCP Server — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add web page conversion (URLs, Wikipedia) and an MCP protocol server exposing a `convert_to_markdown` tool.

**Architecture:** Two new packages. `MarkItDown.Converters.Web` detects http/https URLs, fetches via HttpClient, converts HTML to Markdown. `MarkItDown.McpServer` is a STDIO MCP server using the official C# MCP SDK, exposing a single tool that converts any supported file to Markdown. MCP server uses `MarkItDownEngine.CreateWithAllConverters()` to auto-discover converters.

**Tech Stack:** .NET 8, xUnit, HtmlAgilityPack, ModelContextProtocol 0.1.0-preview, Microsoft.Extensions.Hosting

**Spec:** `docs/superpowers/specs/2026-04-11-markitdown-csharp-full-alignment-design.md` (Phase 5)

---

## File Structure

### Files to Create

| File | Responsibility |
|------|---------------|
| `src/MarkItDown.Converters.Web/MarkItDown.Converters.Web.csproj` | Web converter package |
| `src/MarkItDown.Converters.Web/WebConverter.cs` | URL → fetch → HTML → Markdown |
| `src/MarkItDown.Converters.Web/WikipediaConverter.cs` | Wikipedia API → Markdown |
| `src/MarkItDown.McpServer/MarkItDown.McpServer.csproj` | MCP server package |
| `src/MarkItDown.McpServer/Program.cs` | MCP server entry point |
| `src/MarkItDown.McpServer/MarkItDownTools.cs` | MCP tool definitions |
| `tests/MarkItDown.Converters.Web.Tests/MarkItDown.Converters.Web.Tests.csproj` | Web test project |
| `tests/MarkItDown.Converters.Web.Tests/WebConverterTests.cs` | Web converter tests |
| `tests/MarkItDown.Converters.Web.Tests/WikipediaConverterTests.cs` | Wikipedia tests |
| `tests/MarkItDown.Converters.Web.Tests/FixturePath.cs` | Fixture helper |
| `tests/MarkItDown.McpServer.Tests/MarkItDown.McpServer.Tests.csproj` | MCP test project |
| `tests/MarkItDown.McpServer.Tests/MarkItDownToolsTests.cs` | MCP tools tests |

### Files to Modify

| File | Change |
|------|--------|
| `src/MarkItDown.Cli/MarkItDown.Cli.csproj` | Add Web converter reference |
| `src/MarkItDown.Cli/CliRunner.cs` | Register Web converters, add URL support |
| `MarkItDown.sln` | Add new projects |

---

## Task 1: Create Web Package + WebConverter

**Files:**
- Create: `src/MarkItDown.Converters.Web/MarkItDown.Converters.Web.csproj`
- Create: `src/MarkItDown.Converters.Web/WebConverter.cs`
- Create: `tests/MarkItDown.Converters.Web.Tests/MarkItDown.Converters.Web.Tests.csproj`
- Create: `tests/MarkItDown.Converters.Web.Tests/FixturePath.cs`
- Create: `tests/MarkItDown.Converters.Web.Tests/WebConverterTests.cs`

- [ ] **Step 1: Create csproj**

`src/MarkItDown.Converters.Web/MarkItDown.Converters.Web.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="HtmlAgilityPack" Version="1.11.71" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MarkItDown.Core\MarkItDown.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create WebConverter**

Key design:
- Overrides `CanConvert` to detect http/https URLs in `FilePath`
- Also accepts `.url` extension (Windows internet shortcut files)
- Fetches URL via `HttpClient`
- Detects content type from response headers
- For HTML: parse with HtmlAgilityPack, strip scripts/styles, convert to Markdown
- Returns `DocumentConversionResult("Web", markdown)`

`src/MarkItDown.Converters.Web/WebConverter.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MarkItDown.Core;

namespace MarkItDown.Converters.Web;

public sealed class WebConverter : BaseConverter
{
    private static readonly HttpClient HttpClient = new();

    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".url" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/html" };

    public override double Priority => 10.0; // Lower priority — only used for URLs

    public override bool CanConvert(DocumentConversionRequest request)
    {
        var path = request.FilePath ?? request.Filename ?? "";
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return true;

        return base.CanConvert(request);
    }

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var url = request.FilePath
            ?? throw new ConversionException("Web converter requires a URL.");

        try
        {
            var html = await HttpClient.GetStringAsync(url, cancellationToken);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Extract title
            var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "";

            // Remove scripts, styles, nav, footer
            foreach (var node in doc.DocumentNode.SelectNodes("//script|//style|//nav|//footer|//header") ?? Enumerable.Empty<HtmlNode>())
                node.Remove();

            // Get main content — prefer <main> or <article>, fallback to <body>
            var contentNode = doc.DocumentNode.SelectSingleNode("//main")
                ?? doc.DocumentNode.SelectSingleNode("//article")
                ?? doc.DocumentNode.SelectSingleNode("//body")
                ?? doc.DocumentNode;

            var markdown = HtmlToMarkdown(contentNode);

            if (!string.IsNullOrWhiteSpace(title))
                markdown = $"# {title}{Environment.NewLine}{Environment.NewLine}{markdown}";

            return new DocumentConversionResult("Web", markdown.TrimEnd(), title);
        }
        catch (ConversionException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to convert web page: {ex.Message}", ex);
        }
    }

    private static string HtmlToMarkdown(HtmlNode node)
    {
        var builder = new StringBuilder();
        RenderNode(node, builder);
        return builder.ToString();
    }

    private static void RenderNode(HtmlNode node, StringBuilder builder)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
            {
                builder.Append(child.InnerText);
                continue;
            }

            if (child.NodeType != HtmlNodeType.Element) continue;

            var tag = child.Name.ToLowerInvariant();
            switch (tag)
            {
                case "h1":
                    builder.AppendLine();
                    builder.AppendLine($"# {child.InnerText.Trim()}");
                    break;
                case "h2":
                    builder.AppendLine();
                    builder.AppendLine($"## {child.InnerText.Trim()}");
                    break;
                case "h3":
                    builder.AppendLine();
                    builder.AppendLine($"### {child.InnerText.Trim()}");
                    break;
                case "h4":
                    builder.AppendLine();
                    builder.AppendLine($"#### {child.InnerText.Trim()}");
                    break;
                case "p":
                    builder.AppendLine();
                    RenderChildren(child, builder);
                    builder.AppendLine();
                    break;
                case "a":
                    var href = child.GetAttributeValue("href", "");
                    var text = child.InnerText.Trim();
                    if (!string.IsNullOrEmpty(href) && !string.IsNullOrEmpty(text))
                        builder.Append($"[{text}]({href})");
                    else
                        builder.Append(text);
                    break;
                case "img":
                    var src = child.GetAttributeValue("src", "");
                    var alt = child.GetAttributeValue("alt", "");
                    if (!string.IsNullOrEmpty(src))
                        builder.Append($"![{alt}]({src})");
                    break;
                case "strong":
                case "b":
                    builder.Append($"**{child.InnerText.Trim()}**");
                    break;
                case "em":
                case "i":
                    builder.Append($"*{child.InnerText.Trim()}*");
                    break;
                case "ul":
                case "ol":
                    builder.AppendLine();
                    RenderListItems(child, builder, tag == "ol");
                    break;
                case "li":
                    builder.AppendLine($"- {child.InnerText.Trim()}");
                    break;
                case "br":
                    builder.AppendLine();
                    break;
                case "hr":
                    builder.AppendLine();
                    builder.AppendLine("---");
                    break;
                case "pre":
                    builder.AppendLine();
                    var code = child.InnerText;
                    builder.AppendLine($"```");
                    builder.AppendLine(code.TrimEnd());
                    builder.AppendLine("```");
                    break;
                case "code":
                    if (child.ParentNode?.Name == "pre") continue;
                    builder.Append($"`{child.InnerText}`");
                    break;
                case "blockquote":
                    var lines = child.InnerText.Trim().Split('\n');
                    foreach (var line in lines)
                        builder.AppendLine($"> {line.Trim()}");
                    break;
                case "table":
                    RenderTable(child, builder);
                    break;
                default:
                    RenderChildren(child, builder);
                    break;
            }
        }
    }

    private static void RenderChildren(HtmlNode node, StringBuilder builder) =>
        RenderNode(node, builder);

    private static void RenderListItems(HtmlNode listNode, StringBuilder builder, bool ordered)
    {
        var index = 1;
        foreach (var li in listNode.Elements("li"))
        {
            var prefix = ordered ? $"{index++}. " : "- ";
            builder.AppendLine($"{prefix}{li.InnerText.Trim()}");
        }
    }

    private static void RenderTable(HtmlNode tableNode, StringBuilder builder)
    {
        var rows = tableNode.Descendants("tr").ToList();
        if (rows.Count == 0) return;

        // Header row
        var headerCells = rows[0].Descendants("th").Select(c => c.InnerText.Trim()).ToList();
        if (headerCells.Count == 0)
            headerCells = rows[0].Descendants("td").Select(c => c.InnerText.Trim()).ToList();

        if (headerCells.Count == 0) return;

        builder.AppendLine($"| {string.Join(" | ", headerCells)} |");
        builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", headerCells.Count))} |");

        // Data rows
        foreach (var row in rows.Skip(1))
        {
            var cells = row.Descendants("td").Select(c => c.InnerText.Trim()).ToList();
            if (cells.Count > 0)
                builder.AppendLine($"| {string.Join(" | ", cells)} |");
        }
    }
}
```

- [ ] **Step 3: Create test project and FixturePath**

`tests/MarkItDown.Converters.Web.Tests/MarkItDown.Converters.Web.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\MarkItDown.Converters.Web\MarkItDown.Converters.Web.csproj" />
  </ItemGroup>
</Project>
```

`tests/MarkItDown.Converters.Web.Tests/FixturePath.cs`:

```csharp
namespace MarkItDown.Converters.Web.Tests;

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

- [ ] **Step 4: Write tests**

`tests/MarkItDown.Converters.Web.Tests/WebConverterTests.cs`:

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Web;

namespace MarkItDown.Converters.Web.Tests;

public sealed class WebConverterTests
{
    private readonly WebConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsHttpUrl()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "http://example.com" }));
    }

    [Fact]
    public void CanConvert_AcceptsHttpsUrl()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "https://example.com/page.html" }));
    }

    [Fact]
    public void CanConvert_AcceptsUrlExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "shortcut.url" }));
    }

    [Fact]
    public void CanConvert_RejectsNonWebPaths()
    {
        Assert.False(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "document.pdf" }));
    }
}
```

Note: Actual HTTP tests are integration tests. Unit tests only verify `CanConvert` logic. HTTP tests can be added with mocked HttpClient later.

- [ ] **Step 5: Run tests — verify 4 pass**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Converters.Web.Tests -v minimal`

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add WebConverter with URL detection and HTML-to-Markdown

- Detects http/https URLs and .url files
- Fetches and converts HTML pages to Markdown
- Handles headings, links, images, lists, tables, blockquotes, code
- 4 tests passing"
```

---

## Task 2: Wikipedia Converter

**Files:**
- Create: `src/MarkItDown.Converters.Web/WikipediaConverter.cs`
- Create: `tests/MarkItDown.Converters.Web.Tests/WikipediaConverterTests.cs`

- [ ] **Step 1: Write failing tests**

`tests/MarkItDown.Converters.Web.Tests/WikipediaConverterTests.cs`:

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Web;

namespace MarkItDown.Converters.Web.Tests;

public sealed class WikipediaConverterTests
{
    private readonly WikipediaConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsWikipediaUrl()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "https://en.wikipedia.org/wiki/C_Sharp_(programming_language)" }));
    }

    [Fact]
    public void CanConvert_RejectsNonWikipediaUrl()
    {
        Assert.False(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "https://example.com/page" }));
    }

    [Fact]
    public void CanConvert_RejectsFilePath()
    {
        Assert.False(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "document.pdf" }));
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

- [ ] **Step 3: Create WikipediaConverter**

Uses Wikipedia REST API for clean content extraction.

`src/MarkItDown.Converters.Web/WikipediaConverter.cs`:

```csharp
using System.Text.Json;
using MarkItDown.Core;

namespace MarkItDown.Converters.Web;

public sealed class WikipediaConverter : BaseConverter
{
    private static readonly HttpClient HttpClient = new();

    // No file extensions — detected by URL pattern
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public override double Priority => -1.0; // Higher priority than WebConverter for Wikipedia URLs

    public override bool CanConvert(DocumentConversionRequest request)
    {
        var path = (request.FilePath ?? request.Filename ?? "").ToLowerInvariant();
        return path.Contains("wikipedia.org/wiki/");
    }

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var url = request.FilePath
            ?? throw new ConversionException("Wikipedia converter requires a URL.");

        try
        {
            // Extract article title from URL
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/');
            var title = segments.LastOrDefault() ?? "";
            title = Uri.UnescapeDataString(title).Replace("_", " ");

            // Use Wikipedia REST API for summary + HTML
            var lang = uri.Host.Split('.')[0];
            var apiUrl = $"https://{lang}.wikipedia.org/api/rest_v1/page/html/{Uri.EscapeDataString(title)}";

            var html = await HttpClient.GetStringAsync(apiUrl, cancellationToken);

            // Parse HTML to extract article content
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            // Remove unwanted sections
            foreach (var node in doc.DocumentNode.SelectNodes(
                "//script|//style|//nav|//sup[contains(@class,'reference')]|//span[contains(@class,'mw-editsection')]"
                ) ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
                node.Remove();

            var article = doc.DocumentNode.SelectSingleNode("//body")
                ?? doc.DocumentNode;

            // Reuse HTML-to-Markdown from WebConverter
            var webConverter = new WebConverter();
            // Simple approach: extract text with basic formatting
            var markdown = ExtractText(article);

            if (!string.IsNullOrWhiteSpace(title))
                markdown = $"# {title}{Environment.NewLine}{Environment.NewLine}{markdown}";

            return new DocumentConversionResult("Wikipedia", markdown.TrimEnd(), title);
        }
        catch (ConversionException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to convert Wikipedia page: {ex.Message}", ex);
        }
    }

    private static string ExtractText(HtmlAgilityPack.HtmlNode node)
    {
        var builder = new StringBuilder();

        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Text)
            {
                var text = child.InnerText.Trim();
                if (!string.IsNullOrEmpty(text))
                    builder.Append(text + " ");
                continue;
            }

            if (child.NodeType != HtmlAgilityPack.HtmlNodeType.Element) continue;

            var tag = child.Name.ToLowerInvariant();
            if (tag is "h1" or "h2" or "h3" or "h4")
            {
                var level = tag[1] - '0';
                builder.AppendLine();
                builder.AppendLine($"{new string('#', level)} {child.InnerText.Trim()}");
            }
            else if (tag == "p")
            {
                builder.AppendLine();
                builder.AppendLine(child.InnerText.Trim());
            }
            else if (tag == "li")
            {
                builder.AppendLine($"- {child.InnerText.Trim()}");
            }
            else
            {
                builder.Append(ExtractText(child));
            }
        }

        return builder.ToString();
    }
}
```

- [ ] **Step 4: Run tests, commit**

```bash
cd <worktree> && dotnet test tests/MarkItDown.Converters.Web.Tests --filter "WikipediaConverterTests" -v minimal
git add -A && git commit -m "feat: add WikipediaConverter with REST API content extraction"
```

---

## Task 3: MCP Server

**Files:**
- Create: `src/MarkItDown.McpServer/MarkItDown.McpServer.csproj`
- Create: `src/MarkItDown.McpServer/Program.cs`
- Create: `src/MarkItDown.McpServer/MarkItDownTools.cs`
- Create: `tests/MarkItDown.McpServer.Tests/MarkItDown.McpServer.Tests.csproj`
- Create: `tests/MarkItDown.McpServer.Tests/MarkItDownToolsTests.cs`

- [ ] **Step 1: Create csproj**

`src/MarkItDown.McpServer/MarkItDown.McpServer.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="0.1.0-preview*" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MarkItDown.Core\MarkItDown.Core.csproj" />
    <!-- Reference all converter packages so they're loaded -->
    <ProjectReference Include="..\MarkItDown.Converters.Html\MarkItDown.Converters.Html.csproj" />
    <ProjectReference Include="..\MarkItDown.Converters.Pdf\MarkItDown.Converters.Pdf.csproj" />
    <ProjectReference Include="..\MarkItDown.Converters.Office\MarkItDown.Converters.Office.csproj" />
    <ProjectReference Include="..\MarkItDown.Converters.Data\MarkItDown.Converters.Data.csproj" />
    <ProjectReference Include="..\MarkItDown.Converters.Media\MarkItDown.Converters.Media.csproj" />
    <ProjectReference Include="..\MarkItDown.Converters.Web\MarkItDown.Converters.Web.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create MarkItDownTools**

`src/MarkItDown.McpServer/MarkItDownTools.cs`:

```csharp
using System.ComponentModel;
using MarkItDown.Core;
using ModelContextProtocol.Server;

namespace MarkItDown.McpServer;

[McpServerToolType]
public static class MarkItDownTools
{
    [McpServerTool, Description("Converts a file or URL to Markdown. Supports DOCX, PPTX, XLSX, CSV, MSG, JSON, JSONL, XML, RSS, IPYNB, EPUB, ZIP, Markdown, HTML, PDF, images, audio, and web URLs.")]
    public static string ConvertToMarkdown(
        [Description("Path to a file or URL (http/https) to convert")] string path)
    {
        var engine = MarkItDownEngine.CreateWithAllConverters();

        try
        {
            var result = engine.ConvertAsync(path).GetAwaiter().GetResult();
            return result.Markdown;
        }
        catch (FileNotFoundException ex)
        {
            return $"Error: File not found: {ex.Message}";
        }
        catch (UnsupportedFormatException ex)
        {
            return $"Error: Unsupported format: {ex.Message}";
        }
        catch (ConversionException ex)
        {
            return $"Error: Conversion failed: {ex.Message}";
        }
    }
}
```

- [ ] **Step 3: Create Program.cs**

`src/MarkItDown.McpServer/Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

- [ ] **Step 4: Create test project**

`tests/MarkItDown.McpServer.Tests/MarkItDown.McpServer.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\MarkItDown.McpServer\MarkItDown.McpServer.csproj" />
  </ItemGroup>
</Project>
```

`tests/MarkItDown.McpServer.Tests/MarkItDownToolsTests.cs`:

```csharp
using MarkItDown.McpServer;

namespace MarkItDown.McpServer.Tests;

public sealed class MarkItDownToolsTests
{
    [Fact]
    public void ConvertToMarkdown_ReturnsErrorForMissingFile()
    {
        var result = MarkItDownTools.ConvertToMarkdown(
            "/nonexistent/path/file.txt");

        Assert.Contains("Error:", result);
    }

    [Fact]
    public void ConvertToMarkdown_ConvertsMarkdownFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md");
        File.WriteAllText(path, "# Hello World");

        try
        {
            var result = MarkItDownTools.ConvertToMarkdown(path);
            Assert.Contains("# Hello World", result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConvertToMarkdown_ConvertsJsonFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"key\": \"value\"}");

        try
        {
            var result = MarkItDownTools.ConvertToMarkdown(path);
            Assert.Contains("```json", result);
            Assert.Contains("key", result);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

- [ ] **Step 5: Run tests**

Run: `cd <worktree> && dotnet test tests/MarkItDown.McpServer.Tests -v minimal`

Note: If `ModelContextProtocol` package version doesn't resolve, check NuGet for the exact version name. The package may use a different naming convention.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add MCP server with STDIO transport and convert_to_markdown tool

- Exposes single tool: convert_to_markdown(path)
- Uses MarkItDownEngine.CreateWithAllConverters()
- References all converter packages for full format support
- 3 tests passing"
```

---

## Task 4: Update CLI and Solution, Final Verification

- [ ] **Step 1: Update CLI csproj**

Add to `src/MarkItDown.Cli/MarkItDown.Cli.csproj`:
```xml
<ProjectReference Include="..\MarkItDown.Converters.Web\MarkItDown.Converters.Web.csproj" />
```

- [ ] **Step 2: Update CliRunner.cs**

Add using and register converters:
```csharp
using MarkItDown.Converters.Web;
// In engine construction (before other converters, since Web has priority 10):
.Add(new WikipediaConverter())
.Add(new WebConverter())
```

- [ ] **Step 3: Add projects to solution**

```bash
dotnet sln add src/MarkItDown.Converters.Web/MarkItDown.Converters.Web.csproj
dotnet sln add tests/MarkItDown.Converters.Web.Tests/MarkItDown.Converters.Web.Tests.csproj
dotnet sln add src/MarkItDown.McpServer/MarkItDown.McpServer.csproj
dotnet sln add tests/MarkItDown.McpServer.Tests/MarkItDown.McpServer.Tests.csproj
```

- [ ] **Step 4: Run all tests**

Run: `cd <worktree> && dotnet test -v minimal`

- [ ] **Step 5: Verify build clean**

Run: `cd <worktree> && dotnet build -v minimal`

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: integrate Web converters and MCP server into CLI, add to solution

Phase 5 complete: Web (URLs, Wikipedia), MCP Server (STDIO, convert_to_markdown tool)"
```

---

## Acceptance Criteria

- [ ] WebConverter detects http/https URLs and converts HTML to Markdown
- [ ] WikipediaConverter extracts article content via Wikipedia REST API
- [ ] MCP server exposes `convert_to_markdown` tool via STDIO transport
- [ ] MCP server uses all registered converters
- [ ] All converters registered in CLI
- [ ] All previous tests still pass (no regression)
- [ ] `dotnet build` succeeds with 0 errors
