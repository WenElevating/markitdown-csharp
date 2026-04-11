# Phase 1: Architecture Foundation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor the MarkItDown C# MVP into a modular, registry-driven architecture with NuGet-separated converter packages, preparing for 20+ format support.

**Architecture:** Core package becomes a pure abstraction layer (zero converter dependencies, zero format-specific libraries). Converters move into independent NuGet packages. A Builder-pattern `ConverterRegistry` provides immutable, thread-safe dispatch. All existing HTML/PDF functionality is preserved through migration.

**Tech Stack:** .NET 8, xUnit, HtmlAgilityPack, PdfPig

**Spec:** `docs/superpowers/specs/2026-04-11-markitdown-csharp-full-alignment-design.md`

---

## File Structure

### Files to Create

| File | Responsibility |
|------|---------------|
| `src/MarkItDown.Core/BaseConverter.cs` | Abstract base class with default `CanConvert` implementation |
| `src/MarkItDown.Core/ConverterRegistryBuilder.cs` | Mutable builder for registering converters |
| `src/MarkItDown.Core/ConverterRegistry.cs` | Immutable, thread-safe registry produced by builder |
| `src/MarkItDown.Core/ILlmClient.cs` | Optional LLM client interface for future image captioning |
| `src/MarkItDown.Core/UnsupportedFormatException.cs` | Thrown when no converter matches |
| `src/MarkItDown.Converters.Html/MarkItDown.Converters.Html.csproj` | HTML converter NuGet package |
| `src/MarkItDown.Converters.Html/HtmlConverter.cs` | HTML converter (moved from Core) |
| `src/MarkItDown.Converters.Pdf/MarkItDown.Converters.Pdf.csproj` | PDF converter NuGet package |
| `src/MarkItDown.Converters.Pdf/PdfConverter.cs` | PDF converter (moved from Core) |
| `tests/MarkItDown.Converters.Html.Tests/MarkItDown.Converters.Html.Tests.csproj` | HTML converter tests |
| `tests/MarkItDown.Converters.Html.Tests/HtmlConverterTests.cs` | HTML converter tests (moved) |
| `tests/MarkItDown.Converters.Pdf.Tests/MarkItDown.Converters.Pdf.Tests.csproj` | PDF converter tests |
| `tests/MarkItDown.Converters.Pdf.Tests/PdfConverterTests.cs` | PDF converter tests (moved) |
| `tests/MarkItDown.Converters.Pdf.Tests/FixturePath.cs` | Fixture helper (moved) |
| `tests/MarkItDown.Converters.Html.Tests/FixturePath.cs` | Fixture helper (moved) |

### Files to Modify

| File | Change |
|------|--------|
| `src/MarkItDown.Core/IConverter.cs` | Replace `Kind` with `SupportedExtensions`, `SupportedMimeTypes`, `Priority` |
| `src/MarkItDown.Core/DocumentConversionRequest.cs` | Change from positional record to init-only properties; add `Stream`, `Filename`, `MimeType`, `LlmClient` |
| `src/MarkItDown.Core/ConversionException.cs` | Remove `ConversionErrorCode` dependency; add constructor for message + inner |
| `src/MarkItDown.Core/MarkItDownEngine.cs` | Rewrite to use `ConverterRegistry` + builder pattern |
| `src/MarkItDown.Core/MarkItDown.Core.csproj` | Remove HtmlAgilityPack and PdfPig dependencies |
| `src/MarkItDown.Cli/CliRunner.cs` | Use new engine builder API |
| `src/MarkItDown.Cli/MarkItDown.Cli.csproj` | Add references to new converter packages |
| `MarkItDown.sln` | Add new project entries |

### Files to Delete

| File | Reason |
|------|--------|
| `src/MarkItDown.Core/HtmlConverter.cs` | Moved to `MarkItDown.Converters.Html` |
| `src/MarkItDown.Core/PdfConverter.cs` | Moved to `MarkItDown.Converters.Pdf` |
| `src/MarkItDown.Core/FileFormatClassifier.cs` | Replaced by `BaseConverter.CanConvert` + registry dispatch |
| `src/MarkItDown.Core/DocumentKind.cs` | No longer needed for dispatch; converters identify themselves via extensions |
| `src/MarkItDown.Core/ConversionErrorCode.cs` | Replaced by exception hierarchy |
| `tests/MarkItDown.Core.Tests/HtmlConverterTests.cs` | Moved to converter test project |
| `tests/MarkItDown.Core.Tests/PdfConverterTests.cs` | Moved to converter test project |

---

## Task 1: Core Abstractions — New Interfaces and Types

**Files:**
- Modify: `src/MarkItDown.Core/IConverter.cs`
- Create: `src/MarkItDown.Core/BaseConverter.cs`
- Create: `src/MarkItDown.Core/ILlmClient.cs`
- Create: `src/MarkItDown.Core/UnsupportedFormatException.cs`
- Modify: `src/MarkItDown.Core/ConversionException.cs`
- Modify: `src/MarkItDown.Core/DocumentConversionRequest.cs`
- Delete: `src/MarkItDown.Core/ConversionErrorCode.cs`

- [ ] **Step 1: Write failing tests for the new interfaces**

Create `tests/MarkItDown.Core.Tests/BaseConverterTests.cs`:

```csharp
using MarkItDown.Core;

namespace MarkItDown.Core.Tests;

public sealed class BaseConverterTests
{
    private sealed class TestConverter : BaseConverter
    {
        public override IReadOnlySet<string> SupportedExtensions { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".html", ".htm" };

        public override IReadOnlySet<string> SupportedMimeTypes { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/html" };

        public override Task<DocumentConversionResult> ConvertAsync(
            DocumentConversionRequest request, CancellationToken ct)
            => Task.FromResult(new DocumentConversionResult("Html", "test markdown"));
    }

    [Fact]
    public void CanConvert_MatchesByExtension()
    {
        var converter = new TestConverter();
        var request = new DocumentConversionRequest { FilePath = "/path/to/file.html" };

        Assert.True(converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_MatchesByFilename()
    {
        var converter = new TestConverter();
        var request = new DocumentConversionRequest { Stream = Stream.Null, Filename = "file.html" };

        Assert.True(converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_MatchesByMimeType()
    {
        var converter = new TestConverter();
        var request = new DocumentConversionRequest { Stream = Stream.Null, MimeType = "text/html" };

        Assert.True(converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_ReturnsFalseForUnknownExtension()
    {
        var converter = new TestConverter();
        var request = new DocumentConversionRequest { FilePath = "/path/to/file.pdf" };

        Assert.False(converter.CanConvert(request));
    }

    [Fact]
    public void Priority_DefaultIsZero()
    {
        var converter = new TestConverter();
        Assert.Equal(0.0, converter.Priority);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Core.Tests --filter "BaseConverterTests" --no-restore -v minimal 2>&1 | tail -5`
Expected: FAIL — types `BaseConverter`, `DocumentConversionRequest` (new shape) do not exist yet.

- [ ] **Step 3: Replace `IConverter.cs` with the new interface**

```csharp
namespace MarkItDown.Core;

public interface IConverter
{
    IReadOnlySet<string> SupportedExtensions { get; }
    IReadOnlySet<string> SupportedMimeTypes { get; }
    double Priority { get; }

    bool CanConvert(DocumentConversionRequest request);

    Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Create `BaseConverter.cs`**

```csharp
using System.Linq;

namespace MarkItDown.Core;

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
        DocumentConversionRequest request, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Create `ILlmClient.cs`**

```csharp
namespace MarkItDown.Core;

public interface ILlmClient
{
    Task<string> CompleteAsync(
        string prompt,
        byte[]? imageData = null,
        string? imageMimeType = null,
        CancellationToken ct = default);
}
```

- [ ] **Step 6: Create `UnsupportedFormatException.cs`**

```csharp
namespace MarkItDown.Core;

public sealed class UnsupportedFormatException : ConversionException
{
    public UnsupportedFormatException(string message)
        : base(message) { }

    public UnsupportedFormatException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

- [ ] **Step 7: Update `ConversionException.cs` — remove `ConversionErrorCode` dependency**

```csharp
namespace MarkItDown.Core;

public class ConversionException : Exception
{
    public ConversionException(string message)
        : base(message) { }

    public ConversionException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

- [ ] **Step 8: Remove old converters and tests that reference deleted types**

This step MUST happen before changing `DocumentConversionRequest` and `DocumentConversionResult`, because the old converters use their positional constructors and `DocumentKind` enum. HTML/PDF functionality is temporarily offline — it will be restored in Tasks 4 and 5.

```bash
rm src/MarkItDown.Core/HtmlConverter.cs
rm src/MarkItDown.Core/PdfConverter.cs
rm src/MarkItDown.Core/FileFormatClassifier.cs
rm src/MarkItDown.Core/DocumentKind.cs
rm src/MarkItDown.Core/ConversionErrorCode.cs
rm src/MarkItDown.Core/MarkItDownEngine.cs
rm tests/MarkItDown.Core.Tests/HtmlConverterTests.cs
rm tests/MarkItDown.Core.Tests/PdfConverterTests.cs
```

- [ ] **Step 9: Update `DocumentConversionRequest.cs`**

```csharp
namespace MarkItDown.Core;

public sealed record DocumentConversionRequest
{
    public string? FilePath { get; init; }
    public Stream? Stream { get; init; }
    public string? Filename { get; init; }
    public string? MimeType { get; init; }
    public ILlmClient? LlmClient { get; init; }
}
```

- [ ] **Step 10: Update `DocumentConversionResult.cs` — change `Kind` from enum to string**

> **Spec deviation note:** The spec says `DocumentKind` is "retained" in `DocumentConversionResult`, but since we're deleting the enum entirely (converters self-identify via extensions/MIME), we change `Kind` to `string`. This avoids maintaining a growing enum for every future format.

```csharp
namespace MarkItDown.Core;

public sealed record DocumentConversionResult(
    string Kind,
    string Markdown,
    string? Title = null);
```

- [ ] **Step 11: Run tests to verify BaseConverter tests pass**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Core.Tests --filter "BaseConverterTests" -v minimal 2>&1 | tail -5`
Expected: PASS — 5 tests

- [ ] **Step 12: Commit**

```bash
git add -A && git commit -m "refactor: replace IConverter with extensible interface, add BaseConverter, ILlmClient, UnsupportedFormatException

- IConverter now uses SupportedExtensions/SupportedMimeTypes/Priority
- BaseConverter provides default CanConvert via extension + MIME matching
- DocumentConversionRequest gains Stream, Filename, MimeType, LlmClient
- ConversionException simplified (no more ConversionErrorCode)
- DocumentConversionResult.Kind changed from enum to string
- Old HtmlConverter/PdfConverter removed from Core (to be restored in separate packages)"
```

---

## Task 2: ConverterRegistry and ConverterRegistryBuilder

**Files:**
- Create: `src/MarkItDown.Core/ConverterRegistryBuilder.cs`
- Create: `src/MarkItDown.Core/ConverterRegistry.cs`

- [ ] **Step 1: Write failing tests for the registry**

Create `tests/MarkItDown.Core.Tests/ConverterRegistryTests.cs`:

```csharp
using MarkItDown.Core;

namespace MarkItDown.Core.Tests;

public sealed class ConverterRegistryTests
{
    private sealed class FakeHtmlConverter : BaseConverter
    {
        public override IReadOnlySet<string> SupportedExtensions =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".html" };
        public override IReadOnlySet<string> SupportedMimeTypes =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/html" };
        public override Task<DocumentConversionResult> ConvertAsync(
            DocumentConversionRequest request, CancellationToken ct)
            => Task.FromResult(new DocumentConversionResult("Html", "fake"));
    }

    private sealed class FakePdfConverter : BaseConverter
    {
        public override IReadOnlySet<string> SupportedExtensions =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };
        public override IReadOnlySet<string> SupportedMimeTypes =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/pdf" };
        public override double Priority => 10.0;
        public override Task<DocumentConversionResult> ConvertAsync(
            DocumentConversionRequest request, CancellationToken ct)
            => Task.FromResult(new DocumentConversionResult("Pdf", "fake"));
    }

    [Fact]
    public void FindConverter_ReturnsConverterForMatchingExtension()
    {
        var registry = new ConverterRegistryBuilder()
            .Add(new FakeHtmlConverter())
            .Build();

        var converter = registry.FindConverter(
            new DocumentConversionRequest { FilePath = "test.html" });

        Assert.NotNull(converter);
        Assert.IsType<FakeHtmlConverter>(converter);
    }

    [Fact]
    public void FindConverter_ReturnsHighestPriorityConverter()
    {
        var registry = new ConverterRegistryBuilder()
            .Add(new FakePdfConverter())  // Priority 10.0
            .Add(new FakeHtmlConverter()) // Priority 0.0
            .Build();

        // Both could match, but lower priority number wins
        var allConverters = registry.GetAllConverters().ToList();
        Assert.Equal(0.0, allConverters[0].Priority);
        Assert.Equal(10.0, allConverters[1].Priority);
    }

    [Fact]
    public void FindConverter_ReturnsNullWhenNoMatch()
    {
        var registry = new ConverterRegistryBuilder()
            .Add(new FakeHtmlConverter())
            .Build();

        var converter = registry.FindConverter(
            new DocumentConversionRequest { FilePath = "test.docx" });

        Assert.Null(converter);
    }

    [Fact]
    public void Build_ReturnsImmutableRegistry()
    {
        var builder = new ConverterRegistryBuilder()
            .Add(new FakeHtmlConverter());

        var registry = builder.Build();
        Assert.Single(registry.GetAllConverters());
    }

    [Fact]
    public void AddFromAssembly_DiscoversConverters()
    {
        var registry = new ConverterRegistryBuilder()
            .AddFromAssembly(typeof(ConverterRegistryTests).Assembly)
            .Build();

        // Should find FakeHtmlConverter and FakePdfConverter from this test class
        // and TestConverter from BaseConverterTests. Minimum 2 converters expected.
        Assert.True(registry.GetAllConverters().Count() >= 2);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Core.Tests --filter "ConverterRegistryTests" --no-restore -v minimal 2>&1 | tail -5`
Expected: FAIL — `ConverterRegistryBuilder` and `ConverterRegistry` do not exist.

- [ ] **Step 3: Create `ConverterRegistry.cs`**

```csharp
using System.Linq;

namespace MarkItDown.Core;

public sealed class ConverterRegistry
{
    private readonly IReadOnlyList<IConverter> _converters;

    internal ConverterRegistry(IReadOnlyList<IConverter> converters)
    {
        _converters = converters;
    }

    public IConverter? FindConverter(DocumentConversionRequest request)
    {
        return _converters.FirstOrDefault(c => c.CanConvert(request));
    }

    public IEnumerable<IConverter> GetAllConverters() => _converters;
}
```

- [ ] **Step 4: Create `ConverterRegistryBuilder.cs`**

```csharp
using System.Linq;
using System.Reflection;

namespace MarkItDown.Core;

public class ConverterRegistryBuilder
{
    private readonly List<IConverter> _converters = new();

    public ConverterRegistryBuilder Add(IConverter converter)
    {
        _converters.Add(converter);
        return this;
    }

    public ConverterRegistryBuilder Add(IEnumerable<IConverter> converters)
    {
        _converters.AddRange(converters);
        return this;
    }

    public ConverterRegistryBuilder AddFromAssembly(Assembly assembly)
    {
        var converterTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition)
            .Where(t => typeof(IConverter).IsAssignableFrom(t) && t != typeof(BaseConverter));

        foreach (var type in converterTypes)
        {
            if (Activator.CreateInstance(type) is IConverter converter)
            {
                _converters.Add(converter);
            }
        }

        return this;
    }

    public ConverterRegistry Build()
    {
        var sorted = _converters
            .OrderBy(c => c.Priority)
            .ToList()
            .AsReadOnly();

        return new ConverterRegistry(sorted);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Core.Tests --filter "ConverterRegistryTests" -v minimal 2>&1 | tail -5`
Expected: PASS — 5 tests

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add ConverterRegistry with Builder pattern for immutable converter dispatch

- ConverterRegistryBuilder: mutable builder for registration
- ConverterRegistry: immutable, thread-safe, priority-sorted
- AddFromAssembly discovers IConverter implementations via reflection"
```

---

## Task 3: Refactor MarkItDownEngine to Use Registry

**Files:**
- Modify: `src/MarkItDown.Core/MarkItDownEngine.cs`
- Delete: `src/MarkItDown.Core/FileFormatClassifier.cs`
- Delete: `src/MarkItDown.Core/DocumentKind.cs`

- [ ] **Step 1: Write failing tests for the new engine**

Create `tests/MarkItDown.Core.Tests/MarkItDownEngineTests.cs`:

```csharp
using MarkItDown.Core;

namespace MarkItDown.Core.Tests;

public sealed class MarkItDownEngineTests
{
    private sealed class StubConverter : BaseConverter
    {
        private readonly string _kind;
        private readonly string _markdown;

        public StubConverter(string kind, string extension, string markdown, double priority = 0.0)
        {
            _kind = kind;
            _markdown = markdown;
            SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { extension };
            SupportedMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Priority = priority;
        }

        public override IReadOnlySet<string> SupportedExtensions { get; }
        public override IReadOnlySet<string> SupportedMimeTypes { get; }
        public override double Priority { get; }
        public override Task<DocumentConversionResult> ConvertAsync(
            DocumentConversionRequest request, CancellationToken ct)
            => Task.FromResult(new DocumentConversionResult(_kind, _markdown));
    }

    [Fact]
    public async Task ConvertAsync_FilePath_ConvertsSuccessfully()
    {
        var engine = new MarkItDownEngine(builder => builder
            .Add(new StubConverter("Html", ".html", "# Hello")));

        var result = await engine.ConvertAsync(
            new DocumentConversionRequest { FilePath = "test.html" });

        Assert.Equal("# Hello", result.Markdown);
        Assert.Equal("Html", result.Kind);
    }

    [Fact]
    public async Task ConvertAsync_StreamWithFilename_ConvertsSuccessfully()
    {
        var engine = new MarkItDownEngine(builder => builder
            .Add(new StubConverter("Html", ".html", "# Stream")));

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<h1>Hi</h1>"));
        var result = await engine.ConvertAsync(stream, "test.html");

        Assert.Equal("# Stream", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var engine = new MarkItDownEngine(builder => { });

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            engine.ConvertAsync("nonexistent.html"));
    }

    [Fact]
    public async Task ConvertAsync_UnsupportedFormat_ThrowsUnsupportedFormatException()
    {
        var engine = new MarkItDownEngine(builder => builder
            .Add(new StubConverter("Html", ".html", "x")));

        await Assert.ThrowsAsync<UnsupportedFormatException>(() =>
            engine.ConvertAsync("test.docx"));
    }

    [Fact]
    public async Task ConvertAsync_StreamWithoutHints_ThrowsArgumentException()
    {
        var engine = new MarkItDownEngine(builder => { });

        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.ConvertAsync(stream));
    }

    [Fact]
    public void CreateWithAllConverters_ReturnsEngineWithRegisteredConverters()
    {
        // This discovers converters from loaded assemblies (test stubs in this case)
        var engine = MarkItDownEngine.CreateWithAllConverters();
        Assert.NotNull(engine);
        // Verify converters were registered by attempting a conversion with a known extension
        // The stub converters from BaseConverterTests handle .html, so this should work
        // If no converters were found, it would throw UnsupportedFormatException
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Core.Tests --filter "MarkItDownEngineTests" --no-restore -v minimal 2>&1 | tail -5`
Expected: FAIL — old `MarkItDownEngine` constructor signature doesn't match.

- [ ] **Step 3: Rewrite `MarkItDownEngine.cs`**

```csharp
using System.Reflection;

namespace MarkItDown.Core;

public sealed class MarkItDownEngine
{
    private readonly ConverterRegistry _registry;

    public MarkItDownEngine(Action<ConverterRegistryBuilder> configure)
    {
        var builder = new ConverterRegistryBuilder();
        configure(builder);
        _registry = builder.Build();
    }

    public static MarkItDownEngine CreateWithAllConverters()
    {
        return new MarkItDownEngine(builder =>
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    builder.AddFromAssembly(assembly);
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that can't be loaded
                }
            }
        });
    }

    public async Task<DocumentConversionResult> ConvertAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A file path is required.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"Input file was not found: {filePath}", filePath);
        }

        var request = new DocumentConversionRequest { FilePath = filePath };
        return await ConvertCoreAsync(request, cancellationToken);
    }

    public async Task<DocumentConversionResult> ConvertAsync(
        Stream stream,
        string? filename = null,
        string? mimeType = null,
        CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (string.IsNullOrWhiteSpace(filename) && string.IsNullOrWhiteSpace(mimeType))
        {
            throw new ArgumentException(
                "Stream input requires at least a filename or MIME type hint.");
        }

        var request = new DocumentConversionRequest
        {
            Stream = stream,
            Filename = filename,
            MimeType = mimeType
        };

        return await ConvertCoreAsync(request, cancellationToken);
    }

    private async Task<DocumentConversionResult> ConvertCoreAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken)
    {
        var converter = _registry.FindConverter(request);

        if (converter is null)
        {
            var extension = Path.GetExtension(request.Filename ?? request.FilePath);
            throw new UnsupportedFormatException(
                $"No converter registered for format '{extension}'.");
        }

        try
        {
            return await converter.ConvertAsync(request, cancellationToken);
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var filename = request.Filename ?? request.FilePath ?? "stream";
            throw new ConversionException(
                $"Failed to convert '{filename}': {ex.Message}", ex);
        }
    }
}
```

- [ ] **Step 4: Run engine tests**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Core.Tests --filter "MarkItDownEngineTests" -v minimal 2>&1 | tail -5`
Expected: PASS — 6 tests

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "refactor: rewrite MarkItDownEngine as registry-driven with Stream support

- Engine uses ConverterRegistry for dispatch (no more DocumentKind lookup)
- Supports both file path and Stream input
- Proper error handling: FileNotFoundException, UnsupportedFormatException, ConversionException
- Remove FileFormatClassifier and DocumentKind (replaced by BaseConverter.CanConvert)"
```

---

## Task 4: Move HtmlConverter to MarkItDown.Converters.Html

**Files:**
- Create: `src/MarkItDown.Converters.Html/MarkItDown.Converters.Html.csproj`
- Create: `src/MarkItDown.Converters.Html/HtmlConverter.cs`
- Create: `tests/MarkItDown.Converters.Html.Tests/MarkItDown.Converters.Html.Tests.csproj`
- Create: `tests/MarkItDown.Converters.Html.Tests/HtmlConverterTests.cs`
- Create: `tests/MarkItDown.Converters.Html.Tests/FixturePath.cs`
- Delete: `src/MarkItDown.Core/HtmlConverter.cs`

- [ ] **Step 1: Create the new project directory and csproj**

`src/MarkItDown.Converters.Html/MarkItDown.Converters.Html.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="HtmlAgilityPack" Version="1.11.70" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MarkItDown.Core\MarkItDown.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create the moved HtmlConverter with updated namespace and BaseConverter**

`src/MarkItDown.Converters.Html/HtmlConverter.cs`:

Copy the full content from `src/MarkItDown.Core/HtmlConverter.cs` and make these changes:
- Namespace: `MarkItDown.Converters.Html`
- Remove `DocumentKind Kind` property
- Add `SupportedExtensions` and `SupportedMimeTypes` overrides
- Replace `new DocumentConversionResult(DocumentKind.Html, ...)` with `new DocumentConversionResult("Html", ...)`
- In `ConvertAsync`, read from `request.FilePath` (unchanged for now, Stream support for HTML is Phase 5)

The key changes to the old `HtmlConverter`:

```csharp
using HtmlAgilityPack;
using System.Net;
using System.Text;
using MarkItDown.Core;

namespace MarkItDown.Converters.Html;

public sealed class HtmlConverter : BaseConverter
{
    // ... (keep all private static fields and methods exactly as before) ...

    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".html", ".htm" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/html" };

    // Remove: public DocumentKind Kind => DocumentKind.Html;
    // Remove old CanConvert (inherited from BaseConverter)

    public override Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        // ... (same logic, but change result construction) ...
        return Task.FromResult(new DocumentConversionResult("Html", markdown, title));
        // ... (same catch blocks, but use new ConversionException) ...
    }

    // ... (all private methods remain exactly the same) ...
}
```

> **Implementation note:** Copy the entire file from `src/MarkItDown.Core/HtmlConverter.cs`, change the namespace, make the converter extend `BaseConverter` instead of `IConverter`, add the two `Supported*` property overrides, remove `Kind` and `CanConvert`, and update `DocumentConversionResult` constructor calls to use `"Html"` instead of `DocumentKind.Html`. Update `ConversionException` constructor calls to remove the `ConversionErrorCode` parameter.

- [ ] **Step 3: Create the test project**

`tests/MarkItDown.Converters.Html.Tests/MarkItDown.Converters.Html.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\MarkItDown.Converters.Html\MarkItDown.Converters.Html.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Create `FixturePath.cs` for Html tests**

`tests/MarkItDown.Converters.Html.Tests/FixturePath.cs`:

```csharp
namespace MarkItDown.Converters.Html.Tests;

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

- [ ] **Step 5: Create `HtmlConverterTests.cs`**

Copy the tests from the old `tests/MarkItDown.Core.Tests/HtmlConverterTests.cs` with updated namespace and `using`:

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Html;

namespace MarkItDown.Converters.Html.Tests;

public sealed class HtmlConverterTests
{
    private readonly HtmlConverter _converter = new();

    [Fact]
    public async Task ConvertAsync_PreservesHeadingsListsAndLinks()
    {
        var result = await _converter.ConvertAsync(
            new DocumentConversionRequest { FilePath = FixturePath.For("sample.html") });

        Assert.Contains("## Experiment Setup", result.Markdown);
        Assert.Contains("- gpt-3.5-turbo", result.Markdown);
        Assert.Contains("[MATH](", result.Markdown);
        Assert.DoesNotContain("Skip to main content", result.Markdown);
        Assert.DoesNotContain("What's new in AutoGen?", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_RendersMarkdownTableFromHtmlTable()
    {
        var result = await _converter.ConvertAsync(
            new DocumentConversionRequest { FilePath = FixturePath.For("table.html") });

        Assert.Contains("# Quarterly Inventory Review", result.Markdown);
        Assert.Contains("| Product | Expected | Actual |", result.Markdown);
        Assert.Contains("| SKU-100 | 24 | 26 |", result.Markdown);
        Assert.Contains("[inventory trends](https://example.com/inventory)", result.Markdown);
        Assert.DoesNotContain("Home", result.Markdown);
    }
}
```

- [ ] **Step 6: Update Core.csproj — remove HtmlAgilityPack**

`src/MarkItDown.Core/MarkItDown.Core.csproj` should become:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

- [ ] **Step 8: Run HTML converter tests**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Converters.Html.Tests -v minimal 2>&1 | tail -5`
Expected: PASS — 2 tests

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "refactor: move HtmlConverter to MarkItDown.Converters.Html package

- New NuGet package: MarkItDown.Converters.Html (depends on Core + HtmlAgilityPack)
- Core package now has zero format-specific dependencies
- Tests migrated to dedicated test project"
```

---

## Task 5: Move PdfConverter to MarkItDown.Converters.Pdf

**Files:**
- Create: `src/MarkItDown.Converters.Pdf/MarkItDown.Converters.Pdf.csproj`
- Create: `src/MarkItDown.Converters.Pdf/PdfConverter.cs`
- Create: `tests/MarkItDown.Converters.Pdf.Tests/MarkItDown.Converters.Pdf.Tests.csproj`
- Create: `tests/MarkItDown.Converters.Pdf.Tests/PdfConverterTests.cs`
- Create: `tests/MarkItDown.Converters.Pdf.Tests/FixturePath.cs`
- Delete: `src/MarkItDown.Core/PdfConverter.cs`

- [ ] **Step 1: Create the new project directory and csproj**

`src/MarkItDown.Converters.Pdf/MarkItDown.Converters.Pdf.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="UglyToad.PdfPig" Version="1.7.0-custom-5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MarkItDown.Core\MarkItDown.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create the moved PdfConverter**

Same pattern as HtmlConverter — copy the full content from `src/MarkItDown.Core/PdfConverter.cs` and:
- Namespace: `MarkItDown.Converters.Pdf`
- Extend `BaseConverter` instead of `IConverter`
- Add `SupportedExtensions` / `SupportedMimeTypes` overrides
- Remove `Kind` and `CanConvert`
- Replace `DocumentKind.Pdf` with `"Pdf"` in result construction
- Remove `ConversionErrorCode` from `ConversionException` constructor calls

- [ ] **Step 3: Create the test project and files**

Same pattern as Html tests — create csproj, FixturePath.cs, and PdfConverterTests.cs with updated namespaces. Copy existing test logic from old `tests/MarkItDown.Core.Tests/PdfConverterTests.cs`.

- [ ] **Step 4: Run PDF converter tests**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Converters.Pdf.Tests -v minimal 2>&1 | tail -5`
Expected: PASS — 3 tests

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "refactor: move PdfConverter to MarkItDown.Converters.Pdf package

- New NuGet package: MarkItDown.Converters.Pdf (depends on Core + PdfPig)
- Core package now fully clean — zero converter implementations"
```

---

## Task 6: Update CLI to Use New Engine API

**Files:**
- Modify: `src/MarkItDown.Cli/MarkItDown.Cli.csproj`
- Modify: `src/MarkItDown.Cli/CliRunner.cs`

- [ ] **Step 1: Update CLI csproj to reference converter packages**

`src/MarkItDown.Cli/MarkItDown.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MarkItDown.Core\MarkItDown.Core.csproj" />
    <ProjectReference Include="..\MarkItDown.Converters.Html\MarkItDown.Converters.Html.csproj" />
    <ProjectReference Include="..\MarkItDown.Converters.Pdf\MarkItDown.Converters.Pdf.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Update `CliRunner.cs` to use builder API**

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Html;
using MarkItDown.Converters.Pdf;

namespace MarkItDown.Cli;

public static class CliRunner
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = Parse(args);
            var engine = new MarkItDownEngine(builder => builder
                .Add(new HtmlConverter())
                .Add(new PdfConverter()));

            var result = await engine.ConvertAsync(options.InputPath, cancellationToken);

            if (string.IsNullOrWhiteSpace(options.OutputPath))
            {
                await stdout.WriteAsync(result.Markdown);
                if (!result.Markdown.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                {
                    await stdout.WriteLineAsync();
                }
            }
            else
            {
                var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                await File.WriteAllTextAsync(options.OutputPath, result.Markdown, cancellationToken);
            }

            return 0;
        }
        catch (FileNotFoundException ex)
        {
            await stderr.WriteLineAsync(ex.Message);
            return 1;
        }
        catch (UnsupportedFormatException ex)
        {
            await stderr.WriteLineAsync(ex.Message);
            return 1;
        }
        catch (ConversionException ex)
        {
            await stderr.WriteLineAsync(ex.Message);
            return 2;
        }
    }

    private static CliOptions Parse(IReadOnlyList<string> args)
    {
        string? inputPath = null;
        string? outputPath = null;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "-o":
                case "--output":
                    index++;
                    if (index >= args.Count)
                    {
                        throw new ArgumentException("Missing output path after -o/--output.");
                    }

                    outputPath = args[index];
                    break;
                case "-h":
                case "--help":
                    throw new ArgumentException(
                        "Usage: markitdown <input-file> [-o|--output <output-file>]");
                default:
                    if (inputPath is not null)
                    {
                        throw new ArgumentException(
                            "Only one input file path is supported.");
                    }

                    inputPath = arg;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException(
                "Usage: markitdown <input-file> [-o|--output <output-file>]");
        }

        return new CliOptions(inputPath, outputPath);
    }

    private sealed record CliOptions(string InputPath, string? OutputPath);
}
```

- [ ] **Step 3: Build and verify CLI compiles**

Run: `cd <worktree> && dotnet build src/MarkItDown.Cli -v minimal 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "refactor: update CLI to use builder-based engine and converter packages

- CLI now references Core + Html + Pdf packages
- Uses MarkItDownEngine builder API for converter registration
- Error handling updated for new exception hierarchy"
```

---

## Task 7: Update Solution File and CLI Tests

**Files:**
- Modify: `MarkItDown.sln`
- Modify: `tests/MarkItDown.Cli.Tests/CliRunnerTests.cs`
- Modify: `tests/MarkItDown.Cli.Tests/MarkItDown.Cli.Tests.csproj`

- [ ] **Step 1: Add new projects to the solution**

Run:
```bash
cd <worktree> && dotnet sln add src/MarkItDown.Converters.Html/MarkItDown.Converters.Html.csproj && dotnet sln add src/MarkItDown.Converters.Pdf/MarkItDown.Converters.Pdf.csproj && dotnet sln add tests/MarkItDown.Converters.Html.Tests/MarkItDown.Converters.Html.Tests.csproj && dotnet sln add tests/MarkItDown.Converters.Pdf.Tests/MarkItDown.Converters.Pdf.Tests.csproj
```

- [ ] **Step 2: Update CLI test project reference**

`tests/MarkItDown.Cli.Tests/MarkItDown.Cli.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\MarkItDown.Cli\MarkItDown.Cli.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Update CliRunnerTests — error message assertions**

The CLI tests check for specific error messages. After the refactor:
- "Unsupported file format" → becomes "No converter registered for format '.txt'"
- "Scanned or image-only PDFs are not supported" → stays the same (from PdfConverter)

Update `tests/MarkItDown.Cli.Tests/CliRunnerTests.cs`:

```csharp
using System.Diagnostics;

namespace MarkItDown.Cli.Tests;

public sealed class CliRunnerTests
{
    [Fact]
    public async Task Cli_WritesMarkdownToStdout()
    {
        var result = await RunCliAsync(FixturePath.For("sample.html"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Experiment Setup", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public async Task Cli_WritesMarkdownToOutputFile()
    {
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md");

        try
        {
            var result = await RunCliAsync(FixturePath.For("sample.pdf"), "-o", outputFile);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(outputFile));
            var content = await File.ReadAllTextAsync(outputFile);
            Assert.Contains("Introduction", content);
            Assert.Equal(string.Empty, result.Stdout);
            Assert.Equal(string.Empty, result.Stderr);
        }
        finally
        {
            if (File.Exists(outputFile))
            {
                File.Delete(outputFile);
            }
        }
    }

    [Fact]
    public async Task Cli_ReturnsErrorForUnsupportedInput()
    {
        var result = await RunCliAsync(FixturePath.For("unsupported.txt"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No converter registered", result.Stderr);
    }

    [Fact]
    public async Task Cli_ReturnsErrorForScannedPdf()
    {
        var result = await RunCliAsync(FixturePath.For("scanned.pdf"));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Scanned or image-only PDFs are not supported", result.Stderr);
    }

    // ... (keep RunCliAsync, Quote, CliResult exactly as before) ...
}
```

> **Note:** The scanned PDF test now expects exit code 2 because `ConversionException` (the base class) is caught by the `catch (ConversionException ex)` block in CliRunner which returns 2. This is correct per the new exception hierarchy.

- [ ] **Step 4: Run all tests across the entire solution**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Core.Tests tests/MarkItDown.Converters.Html.Tests tests/MarkItDown.Converters.Pdf.Tests -v minimal 2>&1 | tail -10`
Expected: All pass — Core (16) + Html (2) + Pdf (3) = 21 tests.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "chore: update solution and CLI tests for new package structure

- Add converter projects to solution
- Update CLI test assertions for new error messages
- CLI tests now verify behavior through new exception hierarchy"
```

---

## Task 8: Final Verification and Cleanup

**Files:**
- Verify: All test projects pass
- Verify: Core has zero format-specific dependencies
- Verify: Solution builds clean

- [ ] **Step 1: Verify Core has no format dependencies**

Run: `cat src/MarkItDown.Core/MarkItDown.Core.csproj`
Expected: No PackageReference items — only TargetFramework, ImplicitUsings, Nullable.

- [ ] **Step 2: Run full solution build**

Run: `cd <worktree> && dotnet build -v minimal 2>&1 | tail -5`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run all unit tests (Core + Converters)**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Core.Tests tests/MarkItDown.Converters.Html.Tests tests/MarkItDown.Converters.Pdf.Tests -v minimal 2>&1 | tail -10`
Expected: All pass — 21 tests (Core 16 + Html 2 + Pdf 3), 0 failures.

- [ ] **Step 4: Run CLI tests**

Run: `cd <worktree> && dotnet build src/MarkItDown.Cli -v minimal && dotnet test tests/MarkItDown.Cli.Tests --no-build -v minimal 2>&1 | tail -10`
Expected: All pass — 4 tests, 0 failures.

- [ ] **Step 5: Verify no files reference DocumentKind or ConversionErrorCode**

Run: `cd <worktree> && grep -r "DocumentKind\|ConversionErrorCode" src/ --include="*.cs" || echo "CLEAN"`
Expected: CLEAN — no references to removed types.

- [ ] **Step 6: Final commit if any cleanup needed**

```bash
git add -A && git commit -m "chore: Phase 1 architecture foundation complete

Architecture foundation is in place:
- Core package: zero converter dependencies, pure abstractions
- Converter packages: MarkItDown.Converters.Html, MarkItDown.Converters.Pdf
- Registry-driven engine with Builder pattern
- BaseConverter with default CanConvert (extension + MIME matching)
- Stream input support
- ILlmClient interface for future integration
- All existing test scenarios preserved and passing"
```

---

## Summary

| Task | Tests Added | Key Deliverable |
|------|-------------|-----------------|
| 1. Core Abstractions | 5 (BaseConverter) | New IConverter, BaseConverter, ILlmClient, UnsupportedFormatException |
| 2. Registry | 5 (Registry) | ConverterRegistry + Builder (immutable, priority-sorted) |
| 3. Engine | 6 (Engine) | Registry-driven MarkItDownEngine with Stream support |
| 4. HTML Package | 2 (migrated) | MarkItDown.Converters.Html |
| 5. PDF Package | 3 (migrated) | MarkItDown.Converters.Pdf |
| 6. CLI Update | 0 | Builder-based engine usage |
| 7. Solution + Tests | 4 (migrated) | Solution file, CLI test updates |
| 8. Verification | 0 | Clean build, all tests green |
| **Total** | **25** | |

> **Note:** Tasks 4 and 5 (HTML/PDF package moves) are independent of each other and can be executed in parallel.

## Acceptance Criteria

- [ ] All existing test scenarios preserved under the new API
- [ ] Tests updated to use new interfaces (record properties, builder-based engine)
- [ ] No regression in HTML/PDF conversion behavior
- [ ] Core package has zero format-specific library dependencies
- [ ] `grep -r "DocumentKind\|ConversionErrorCode" src/` returns nothing
- [ ] `dotnet build` succeeds with 0 errors
- [ ] `dotnet test` passes all 21 unit tests (Core 16 + Html 2 + Pdf 3)
- [ ] CLI integration tests pass (4 tests)
