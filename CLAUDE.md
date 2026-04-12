# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
# Restore (required once, uses local NuGet.Config)
dotnet restore MarkItDown.sln --configfile NuGet.Config

# Build
dotnet build MarkItDown.sln

# Run all tests
dotnet test MarkItDown.sln --no-restore

# Run a single test project
dotnet test tests/MarkItDown.Core.Tests --no-restore
dotnet test tests/MarkItDown.Converters.Html.Tests --no-restore

# Run a specific test by name
dotnet test tests/MarkItDown.Core.Tests --no-restore --filter "FullyQualifiedName~ConvertAsync_PreservesHeadings"

# Run the CLI
dotnet run --project src/MarkItDown.Cli -- tests/Fixtures/sample.html
dotnet run --project src/MarkItDown.Cli -- tests/Fixtures/sample.pdf -o output.md
```

Requires .NET SDK 8.0.401 (configured in `global.json` with rollForward).

## Architecture

**Plugin-based converter system** — each file format has an isolated converter in its own project.

### Core Flow

```
CLI (CliRunner) → MarkItDownEngine → ConverterRegistry.FindConverter() → IConverter.ConvertAsync()
```

- `MarkItDownEngine` is the entry point. It accepts a builder delegate to register converters, then delegates to `ConverterRegistry` for format dispatch.
- `IConverter` defines the contract: `SupportedExtensions`, `SupportedMimeTypes`, `Priority`, `CanConvert()`, `ConvertAsync()`.
- `BaseConverter` is the abstract base that most converters inherit from.
- `ConverterRegistryBuilder` supports `Add(converter)` and `AddFromAssembly(assembly)` for auto-discovery.

### Project Layout

| Project | Role |
|---------|------|
| `MarkItDown.Core` | `IConverter`, `BaseConverter`, `ConverterRegistry`, `MarkItDownEngine`, request/result models |
| `MarkItDown.Cli` | CLI host using System.CommandLine |
| `MarkItDown.Converters.Html` | HTML→Markdown via HtmlAgilityPack |
| `MarkItDown.Converters.Pdf` | PDF→Markdown via iText7 |
| `MarkItDown.Converters.Office` | docx, pptx, xlsx, csv, msg |
| `MarkItDown.Converters.Data` | json, jsonl, xml, rss, ipynb, epub, zip, md |
| `MarkItDown.Converters.Media` | images, audio |
| `MarkItDown.Converters.Web` | URLs, Wikipedia |
| `MarkItDown.Llm` | OpenAI-compatible API client for image captioning |
| `MarkItDown.McpServer` | MCP server implementation |

Each converter project in `src/` has a matching test project in `tests/`.

### Adding a New Converter

1. Create `src/MarkItDown.Converters.<Format>/<Format>Converter.cs` inheriting `BaseConverter`
2. Implement `SupportedExtensions`, `SupportedMimeTypes`, and `ConvertAsync()`
3. Register via `ConverterRegistryBuilder.Add()` or assembly scanning
4. Add test project with fixture files in `tests/Fixtures/`

## Key Conventions

- Test fixtures live in `tests/Fixtures/`, accessed via `FixturePath.For("filename")` helper
- CLI tests run the tool as a subprocess (`dotnet run --project src/MarkItDown.Cli --`)
- Custom exceptions: `ConversionException` (wraps converter failures), `UnsupportedFormatException` (no registered converter)
- Nullable reference types enabled across all projects
- File encoding is UTF-8; Windows CRLF line endings are used

## HTML Converter Specifics

The HTML converter (`HtmlConverter.cs`) has a noise removal pipeline that strips navigation, sidebars, and boilerplate before converting content to Markdown. It uses compiled `Regex` patterns (not XPath `contains()`) for CSS class/id matching to handle case-insensitive and hyphenated class names correctly (e.g., `VPSidebar`, `sidebar-nav`, `has-sidebar`).

Content root selection follows a priority chain: `#__blog-post-container` → `//main` → `//article` → `//body` → document root.
