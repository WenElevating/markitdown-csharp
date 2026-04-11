# MarkItDown C# — Full Feature Alignment Design

> Goal: Align the C# reimplementation with Microsoft's official Python `markitdown` project in features, architecture, and extensibility.

## 1. Current State

The C# version is a clean MVP supporting only HTML and PDF conversion. The official Python version supports 20+ file formats, a plugin system, MCP server, LLM integration (image captioning, OCR), and Azure Document Intelligence.

### Gap Summary

| Category | Official | C# Status |
|----------|----------|-----------|
| Office (DOCX/PPTX/XLSX/XLS/MSG/CSV) | Full support | Missing |
| Images (JPG/PNG) | Full + LLM captioning | Missing |
| Data (JSON/JSONL/XML/EPUB/RSS/IPYNB/ZIP) | Full support | Missing |
| Audio (WAV/MP3/M4A) | Metadata + transcription | Missing |
| Web (URLs/YouTube/Wikipedia) | Full support | Missing |
| Plugin system | Python entry points | Missing |
| MCP Server | STDIO/HTTP/SSE | Missing |
| LLM integration | Optional OpenAI client | Missing |
| Stream input | Supported | File path only |
| Priority-based dispatch | Supported | Missing |
| MIME detection | magika (AI-driven) | Extension only |

## 2. Architecture: NuGet Modular Design

### 2.1 Solution Structure

```
MarkItDown.sln
│
├── src/
│   ├── MarkItDown.Core/                    # Abstractions + engine (zero converter deps)
│   ├── MarkItDown.Converters.Html/         # HTML → Markdown
│   ├── MarkItDown.Converters.Pdf/          # PDF → Markdown
│   ├── MarkItDown.Converters.Office/       # DOCX/PPTX/XLSX/XLS/MSG/CSV
│   ├── MarkItDown.Converters.Data/         # JSON/JSONL/XML/EPUB/RSS/IPYNB/ZIP/MD
│   ├── MarkItDown.Converters.Media/        # Images + Audio (optional LLM)
│   ├── MarkItDown.Converters.Web/          # URLs/YouTube/Wikipedia
│   ├── MarkItDown.Llm/                     # ILlmClient interface + OpenAI impl
│   ├── MarkItDown.McpServer/              # MCP protocol server
│   ├── MarkItDown.Cli/                     # CLI entry point
│   └── MarkItDown.All/                     # Meta-package referencing all converters
│
└── tests/
    ├── MarkItDown.Core.Tests/
    ├── MarkItDown.Converters.Html.Tests/
    ├── MarkItDown.Converters.Pdf.Tests/
    ├── MarkItDown.Converters.Office.Tests/
    ├── MarkItDown.Converters.Data.Tests/
    ├── MarkItDown.Converters.Media.Tests/
    ├── MarkItDown.Converters.Web.Tests/
    ├── MarkItDown.Llm.Tests/
    ├── MarkItDown.McpServer.Tests/
    └── MarkItDown.Integration.Tests/       # End-to-end across all formats
```

### 2.2 Package Dependencies

| Package | Depends On | Key Libraries |
|---------|-----------|---------------|
| Core | (none) | — |
| Converters.Html | Core | HtmlAgilityPack |
| Converters.Pdf | Core | PdfPig |
| Converters.Office | Core | DocumentFormat.OpenXml, MsgReader |
| Converters.Data | Core | System.Text.Json, CsvHelper |
| Converters.Media | Core | SixLabors.ImageSharp, TagLib# (LLM is runtime-only via ILlmClient on request, not a NuGet dep) |
| Converters.Web | Core | BCL HttpClient (System.Net.Http) |
| Llm | Core | OpenAI (optional) |
| McpServer | Core | ModelContextProtocol SDK |
| Cli | Core + all converters | System.CommandLine |
| All | All converter packages | — |

## 3. Core Layer Design

### 3.1 IConverter Interface

```csharp
public interface IConverter
{
    IReadOnlySet<string> SupportedExtensions { get; }
    IReadOnlySet<string> SupportedMimeTypes { get; }
    double Priority { get; }  // Lower = higher priority

    bool CanConvert(DocumentConversionRequest request);
    Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request,
        CancellationToken ct = default);
}
```

### 3.2 ConverterRegistry (Immutable via Builder)

The registry is built once and then frozen. This ensures thread safety and aligns with the project's immutability principle.

```csharp
/// Mutable builder used during engine construction. Produces an immutable registry.
public class ConverterRegistryBuilder
{
    private readonly List<IConverter> _converters = new();

    public ConverterRegistryBuilder Add(IConverter converter);
    public ConverterRegistryBuilder Add(IEnumerable<IConverter> converters);

    /// Scans an assembly for all IConverter implementations and registers them.
    public ConverterRegistryBuilder AddFromAssembly(Assembly assembly);

    /// Builds the immutable registry. No further modifications possible.
    public ConverterRegistry Build();
}

/// Immutable, thread-safe converter registry. Created only via ConverterRegistryBuilder.
public sealed class ConverterRegistry
{
    private readonly IReadOnlyList<IConverter> _converters;  // sorted by Priority

    internal ConverterRegistry(IReadOnlyList<IConverter> converters);

    /// Returns the highest-priority converter that can handle the request, or null.
    public IConverter? FindConverter(DocumentConversionRequest request);

    /// Returns all registered converters ordered by priority.
    public IEnumerable<IConverter> GetAllConverters();
}
```

> **Security note**: `AddFromAssembly` scans loaded assemblies only. Directory-based DLL loading (`AddFromAssembliesInDirectory`) is deferred to a later design that will include assembly trust verification (strong-name checking, allow-list filtering). The initial implementation does not support loading arbitrary DLLs from disk.

### 3.3 Enhanced DocumentConversionRequest

Stays as a `sealed record` (consistent with current codebase). The positional parameter is replaced with init-only properties.

**Before (current)**:
```csharp
public sealed record DocumentConversionRequest(string FilePath);
```

**After**:
```csharp
public sealed record DocumentConversionRequest
{
    // File-based input
    public string? FilePath { get; init; }

    // Stream-based input
    public Stream? Stream { get; init; }

    // Hints for format detection
    public string? Filename { get; init; }
    public string? MimeType { get; init; }

    // Optional LLM client for converters that need it
    public ILlmClient? LlmClient { get; init; }
}
```

### 3.4 Enhanced MarkItDownEngine

```csharp
public class MarkItDownEngine
{
    private readonly ConverterRegistry _registry;

    public MarkItDownEngine(Action<ConverterRegistryBuilder> configure);

    /// Factory: registers all converters from referenced assemblies.
    public static MarkItDownEngine CreateWithAllConverters();

    /// Convert from file path.
    public Task<DocumentConversionResult> ConvertAsync(
        string filePath, CancellationToken ct = default);

    /// Convert from stream.
    public Task<DocumentConversionResult> ConvertAsync(
        Stream stream, string? filename = null,
        string? mimeType = null, CancellationToken ct = default);
}
```

### 3.5 Engine Error Handling

The engine wraps all converter errors in a consistent exception hierarchy:

| Scenario | Exception | Message Pattern |
|----------|-----------|-----------------|
| File not found | `FileNotFoundException` | Standard .NET message |
| No converter found for format | `UnsupportedFormatException` | "No converter registered for format '{extension}'" |
| Converter throws during conversion | `ConversionException` (wrapping inner) | "Failed to convert '{filename}': {inner.Message}" |
| Stream with no filename/MIME hint | `ArgumentException` | "Stream input requires at least a filename or MIME type hint" |
| Converter requires LLM but none provided | `ConversionException` | "Converter '{name}' requires an LLM client but none was provided" |

All domain-specific exceptions (`UnsupportedFormatException`, etc.) inherit from `ConversionException` for unified catch-all handling. Standard BCL exceptions (`FileNotFoundException`, `ArgumentException`) are thrown directly.

### 3.6 ILlmClient Interface

```csharp
public interface ILlmClient
{
    Task<string> CompleteAsync(
        string prompt,
        byte[]? imageData = null,
        string? imageMimeType = null,
        CancellationToken ct = default);
}
```

### 3.7 FileFormatClassifier Enhancement

- Current: extension-only detection
- Target: extension + MIME sniffing via `System.Net.Mime` or byte-signature detection
- Priority: extension match first, MIME match second, content sniffing last
- For stream inputs: requires either `Filename` or `MimeType` hint. If neither is provided, the engine throws `ArgumentException`. Byte-signature sniffing is a future enhancement requiring seekable streams.

### 3.8 Breaking Changes & Migration

Phase 1 introduces breaking API changes. This section documents every affected type.

| Type | Before | After | Migration |
|------|--------|-------|-----------|
| `IConverter` | `DocumentKind Kind { get; }` | `IReadOnlySet<string> SupportedExtensions`, `SupportedMimeTypes`, `double Priority` | Implement new properties; remove `Kind` |
| `DocumentKind` | Enum used in `IConverter.Kind` and `DocumentConversionResult.Kind` | **Retained** in `DocumentConversionResult` for informational purposes; removed from `IConverter` | No code change for consumers; converters no longer use it |
| `DocumentConversionResult` | `(DocumentKind Kind, string Markdown, string? Title)` | Same fields retained — `Kind` stays as informational metadata | No change needed |
| `DocumentConversionRequest` | `sealed record(string FilePath)` | `sealed record` with init-only properties (`FilePath`, `Stream`, `Filename`, `MimeType`, `LlmClient`) | Update call sites: `new(filePath)` becomes `new() { FilePath = filePath }` |
| `MarkItDownEngine` | Static `CreateDefault()` method | Constructor takes `Action<ConverterRegistryBuilder>` | Replace `CreateDefault()` with `new MarkItDownEngine(builder => ...)` |
| `ConversionException` | Single exception type | Hierarchy: `ConversionException` (base), `UnsupportedFormatException` | Catch `ConversionException` for all conversion errors |

## 4. Converter Design Patterns

### 4.1 Base Converter

All converters inherit from an abstract base to reduce boilerplate:

```csharp
public abstract class BaseConverter : IConverter
{
    public abstract IReadOnlySet<string> SupportedExtensions { get; }
    public abstract IReadOnlySet<string> SupportedMimeTypes { get; }
    public virtual double Priority => 0.0;

    public virtual bool CanConvert(DocumentConversionRequest request)
    {
        var ext = Path.GetExtension(request.Filename ?? request.FilePath)
            ?.ToLowerInvariant();

        if (ext is not null && SupportedExtensions.Contains(ext))
            return true;

        if (request.MimeType is not null && SupportedMimeTypes.Any(
            mt => request.MimeType.StartsWith(mt, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public abstract Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken ct);
}
```

### 4.2 Converter Priority Convention

| Priority | Use Case | Example |
|----------|----------|---------|
| 0.0 (default) | Specific format converters | HtmlConverter, DocxConverter |
| 10.0 | Generic/fallback converters | TextConverter, XmlConverter |
| -1.0 | Override built-ins | Plugin converters replacing defaults |

## 5. Phase Roadmap

### Phase 1: Architecture Foundation

**Scope**: Core refactoring, no new format support.

1. Extract `IConverter` enhanced interface with priority and MIME support
2. Implement `ConverterRegistry` with priority-based dispatch
3. Refactor `MarkItDownEngine` to be registry-driven
4. Add `Stream` support to `DocumentConversionRequest`
5. Add `ILlmClient` interface (no implementation yet)
6. Move `HtmlConverter` to `MarkItDown.Converters.Html`
7. Move `PdfConverter` to `MarkItDown.Converters.Pdf`
8. Adapt CLI to new engine API
9. Ensure all existing tests pass

**Acceptance**: All existing test scenarios are preserved under the new API. Tests are updated to use the new interfaces (record properties, builder-based engine construction). No regression in HTML/PDF conversion behavior. New architecture in place.

### Phase 2: Office Formats

**Scope**: Most commonly requested formats.

10. Implement DOCX converter (DocumentFormat.OpenXml)
11. Implement PPTX converter (DocumentFormat.OpenXml)
12. Implement XLSX converter (DocumentFormat.OpenXml or ClosedXML)
13. Implement CSV converter (CsvHelper)
14. Implement MSG converter (MsgReader)
15. Add test fixtures for each format

**Acceptance**: Office files convert correctly with structure preservation.

### Phase 3: Data Formats

**Scope**: Structured data and archive formats.

16. JSON / JSONL converter
17. XML converter
18. EPUB converter
19. RSS converter
20. IPYNB (Jupyter Notebook) converter
21. ZIP converter (recursive content processing)
22. Markdown passthrough converter

**Acceptance**: Data formats convert with proper structure.

### Phase 4: Media + LLM Integration

**Scope**: Image and audio with optional AI enhancement.

23. Image metadata extraction (EXIF via ImageSharp)
24. Image → Markdown with optional LLM captioning
25. Audio metadata extraction (TagLib#)
26. `MarkItDown.Llm` package with OpenAI default implementation
27. PDF OCR for scanned documents (via LLM only — no native Tesseract dependency)

**Acceptance**: Images get descriptions, audio gets metadata. LLM is optional.

### Phase 5: Web + MCP

**Scope**: Network content and AI tool integration.

28. Web page fetching and conversion
29. YouTube transcript extraction
30. Wikipedia page conversion
31. `MarkItDown.McpServer` with STDIO and HTTP transports
32. CLI integration with MCP options

**Acceptance**: Web content converts. MCP server is functional.

## 6. Testing Strategy

- **Framework**: xUnit (consistent with current project)
- **Unit tests per converter**: Each converter gets its own test project with fixture files
- **Integration tests**: End-to-end tests across all formats via `MarkItDown.All`
- **Test fixtures**: Committed binary test files organized under `tests/Fixtures/{format}/`
- **External service mocking**: LLM, HTTP, YouTube — mocked via interfaces (`ILlmClient`, `HttpMessageHandler`)
- **Coverage target**: 80%+ per package
- **CI**: GitHub Actions with `dotnet test` across all test projects

## 7. Key Technical Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| HTML parsing | HtmlAgilityPack | Existing, proven, .NET standard |
| PDF extraction | PdfPig | Existing, active maintenance |
| Office formats | DocumentFormat.OpenXml | Microsoft official, first-class .NET |
| Image processing | SixLabors.ImageSharp | Cross-platform, no native deps |
| Audio metadata | TagLib# (taglib-sharp) | Cross-platform, mature, no native deps |
| CSV parsing | CsvHelper | De facto standard in .NET |
| CLI framework | System.CommandLine (preview) | Microsoft official; pin specific preview version |
| MCP protocol | ModelContextProtocol SDK | .NET MCP implementation |
| LLM client | OpenAI .NET SDK | Official OpenAI SDK for .NET |

## 8. Versioning Strategy

All packages in the repo share a **unified version number** (e.g., `1.0.0`, `1.1.0`). This simplifies coordination and avoids version matrix confusion. Semantic versioning rules apply:
- **Major**: Breaking changes to `IConverter` or `MarkItDownEngine` public API
- **Minor**: New converter packages or new features
- **Patch**: Bug fixes, no API changes

Each converter package specifies a minimum `MarkItDown.Core` version dependency.

## 9. Non-Goals (Explicitly Out of Scope for Now)

- Azure Document Intelligence integration (deferred to future phase)
- Excel legacy format (.xls) — .xlsx covers the vast majority of use cases
- Audio transcription (requires significant ML infrastructure)
- Source Generator plugin system (over-engineering for current needs)
- Tesseract OCR (native dependency complexity; LLM-based OCR is sufficient)
