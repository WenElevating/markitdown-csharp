# MarkItDown C#

Minimal C# port of Microsoft's MarkItDown focused on local `PDF` and `HTML` to Markdown conversion.

## Scope

- Supports local `.pdf`, `.html`, `.htm`
- CLI-first delivery
- Preserves headings, lists, links, and HTML tables
- Rejects scanned/image-only PDFs instead of silently pretending OCR exists

## Non-goals

- OCR
- Scanned PDF understanding
- Office formats
- Audio/video
- ZIP recursion
- Plugin/MCP surfaces

## Requirements

- .NET SDK `8.0.401` or compatible `8.0.x`

## Usage

Write Markdown to stdout:

```powershell
dotnet run --project src/MarkItDown.Cli -- tests/Fixtures/sample.html
```

Write Markdown to a file:

```powershell
dotnet run --project src/MarkItDown.Cli -- tests/Fixtures/sample.pdf -o output.md
```

## Tests

Restore once with the local NuGet config:

```powershell
dotnet restore MarkItDown.sln --configfile NuGet.Config
```

Run tests:

```powershell
dotnet test tests/MarkItDown.Core.Tests/MarkItDown.Core.Tests.csproj --no-restore
dotnet test tests/MarkItDown.Cli.Tests/MarkItDown.Cli.Tests.csproj --no-restore
```

## Layout

- `src/MarkItDown.Core`: format detection and converters
- `src/MarkItDown.Cli`: CLI host
- `tests/`: fixtures and test projects

## Next Phase

- Add a stable SDK-facing API surface over the existing core engine
- Improve PDF layout heuristics with broader fixture coverage
