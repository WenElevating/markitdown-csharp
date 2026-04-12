# MarkItDown C#

[简体中文](./README.zh-CN.md)

A C# library and CLI tool for converting files and URLs to Markdown. A port of [microsoft/markitdown](https://github.com/microsoft/markitdown) powered by .NET 8.

## Features

- **CLI-first** — convert files from the terminal with a single command
- **20+ formats** — documents, data, media, and web content
- **Preserves structure** — headings, lists, links, tables, code blocks
- **Noise removal** — strips navigation, sidebars, and boilerplate from HTML
- **LLM captioning** — optional image/audio description via OpenAI-compatible APIs
- **Extensible** — plugin-based converter architecture, easy to add new formats

## Supported Formats

| Category | Extensions / Types |
|----------|-------------------|
| Documents | `.docx` `.pptx` `.xlsx` `.csv` `.msg` `.pdf` `.html` `.htm` |
| Data | `.json` `.jsonl` `.xml` `.rss` `.atom` `.ipynb` `.epub` `.zip` `.md` |
| Media | `.jpg` `.jpeg` `.png` `.mp3` `.wav` `.m4a` |
| Web | URLs (`http://`, `https://`), Wikipedia articles |

## Requirements

- .NET SDK 8.0 (`8.0.401` or compatible `8.0.x`)

## Installation

```bash
git clone https://github.com/WenElevating/markitdown-csharp.git
cd markitdown-csharp
dotnet restore MarkItDown.sln --configfile NuGet.Config
```

## Usage

### CLI

Convert a file to stdout:

```bash
dotnet run --project src/MarkItDown.Cli -- document.pdf
```

Write output to a file:

```bash
dotnet run --project src/MarkItDown.Cli -- document.docx -o output.md
```

Convert multiple files to a directory:

```bash
dotnet run --project src/MarkItDown.Cli -- *.html -o markdown/
```

Convert a web page:

```bash
dotnet run --project src/MarkItDown.Cli -- https://example.com
```

Enable LLM captioning for images and audio:

```bash
dotnet run --project src/MarkItDown.Cli -- photo.jpg --llm-key sk-... --llm-model gpt-4o
```

List all supported formats:

```bash
dotnet run --project src/MarkItDown.Cli -- --list-formats
```

### Library

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Pdf;

var engine = new MarkItDownEngine(builder => builder.Add(new PdfConverter()));
var result = await engine.ConvertAsync("document.pdf");

Console.WriteLine(result.Markdown);
Console.WriteLine(result.Title);    // extracted title (may be null)
Console.WriteLine(result.Kind);     // format identifier, e.g. "Pdf"
```

## Options

```
markitdown <path> [<path>...] [options]

Arguments:
  <paths>          Input file paths or URLs to convert

Options:
  -o, --output       Output file or directory (default: stdout)
  --list-formats     List all supported formats
  --llm-key          OpenAI API key (enables LLM captioning)
  --llm-model        LLM model name (default: gpt-4o)
  --llm-endpoint     Custom API endpoint (e.g. Azure OpenAI)
  -h, --help         Show help
  -V, --version      Show version
```

## Project Layout

```
src/
  MarkItDown.Core/               Engine, converter interfaces, request/result models
  MarkItDown.Cli/                CLI entry point
  MarkItDown.Converters.Html/    HTML to Markdown (HtmlAgilityPack)
  MarkItDown.Converters.Pdf/     PDF to Markdown (iText7)
  MarkItDown.Converters.Office/  docx, pptx, xlsx, csv, msg
  MarkItDown.Converters.Data/    json, jsonl, xml, rss, ipynb, epub, zip, md
  MarkItDown.Converters.Media/   images, audio
  MarkItDown.Converters.Web/     URLs, Wikipedia
  MarkItDown.Llm/                OpenAI-compatible API client
  MarkItDown.McpServer/          MCP server implementation

tests/                           Test projects (xUnit) + shared fixtures
```

## Tests

```bash
dotnet restore MarkItDown.sln --configfile NuGet.Config

# Run all tests
dotnet test MarkItDown.sln --no-restore

# Run a specific test project
dotnet test tests/MarkItDown.Converters.Html.Tests --no-restore
```

## Architecture

The converter system is built on a plugin architecture:

```
IConverter → BaseConverter → concrete converters (HtmlConverter, PdfConverter, ...)
                                    ↓
ConverterRegistry → MarkItDownEngine → CLI / library consumer
```

Each converter declares its supported file extensions and MIME types. The engine selects the first matching converter and delegates conversion. New formats can be added by implementing `BaseConverter` and registering it with the builder.

## Acknowledgements

This project is a C# port of [microsoft/markitdown](https://github.com/microsoft/markitdown). Special thanks to the original authors for designing an excellent conversion architecture.

## License

MIT
