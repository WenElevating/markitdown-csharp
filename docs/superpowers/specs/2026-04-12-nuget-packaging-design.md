# NuGet Packaging Architecture — Design Spec

**Date:** 2026-04-12
**Status:** Approved

## Goal

Package markitdown-csharp as NuGet packages so third parties and other projects can consume it. Third-party developers can depend on Core only to build custom converters; general users install the meta-package for all built-in converters.

## Package Structure

```
MarkItDown.Core          Core abstractions + engine + ILlmClient interface (zero third-party deps)
MarkItDown.Llm           LLM client implementation (depends on OpenAI SDK) — used only by CLI
MarkItDown.Pdf           PDF converter (depends on PdfPig)
MarkItDown.Html          HTML converter (depends on HtmlAgilityPack)
MarkItDown.Office        docx/pptx/xlsx/csv/msg converter
MarkItDown.Data          json/jsonl/xml/rss/ipynb/epub/zip/md converter
MarkItDown.Media         image/audio converter (depends on ImageSharp, TagLibSharp)
MarkItDown.Web           URL/Wikipedia converter (depends on HtmlAgilityPack)
MarkItDown               Meta-package: depends on Core + all converters
```

**Dependency graph (verified against actual .csproj files):**

```
MarkItDown (meta)
├── MarkItDown.Core
├── MarkItDown.Llm ───────→ Core + OpenAI SDK
├── MarkItDown.Pdf ────────→ Core + PdfPig (custom 1.7.0-custom-5)
├── MarkItDown.Html ───────→ Core + HtmlAgilityPack
├── MarkItDown.Office ─────→ Core + DocumentFormat.OpenXml
├── MarkItDown.Data ──────→ Core (no extra deps)
├── MarkItDown.Media ─────→ Core + SixLabors.ImageSharp + TagLibSharp
└── MarkItDown.Web ───────→ Core + HtmlAgilityPack
```

**Not packaged:**

| Project | Reason |
|---------|--------|
| `MarkItDown.Cli` | CLI tool, not a library. `IsPackable=false`. May be published as a .NET Tool later. |
| `MarkItDown.McpServer` | MCP server host, not a library. `IsPackable=false`. May be published as a .NET Tool later. |

**Consumer scenarios:**

1. **All-in-one** — `dotnet add package MarkItDown` → all converters available
2. **Selective** — `dotnet add package MarkItDown.Pdf` → only PDF support
3. **Custom converter dev** — `dotnet add package MarkItDown.Core` → interfaces only, register with `builder.Add()`

## Implementation

### 1. Directory.Build.props

Shared NuGet metadata across all packable projects:

```xml
<Project>
  <PropertyGroup>
    <Authors>WenElevating</Authors>
    <RepositoryUrl>https://github.com/WenElevating/markitdown-csharp</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/WenElevating/markitdown-csharp</PackageProjectUrl>
    <PackageTags>markdown;converter;pdf;html;docx</PackageTags>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
  </PropertyGroup>
</Project>
```

Version is NOT set here — it comes from CI (git tag) via `-p:Version`. During local dev, default `1.0.0`.

### 2. Per-project .csproj changes

Each packable project adds:

```xml
<PropertyGroup>
  <PackageId>MarkItDown.Core</PackageId>
  <Description>Core library for converting documents to Markdown</Description>
  <IsPackable>true</IsPackable>
</PropertyGroup>
```

Projects that are NOT packable — verify `IsPackable=false` is set (already present in test projects):
- `src/MarkItDown.Cli` — add `IsPackable=false`
- `src/MarkItDown.McpServer` — add `IsPackable=false`
- All test projects — already have `IsPackable=false` (verify only)

### 3. MarkItDown meta-package

New project `src/MarkItDown/MarkItDown.csproj` — an empty library with no source code. Uses plain `ProjectReference` (no `PrivateAssets`) so MSBuild automatically generates NuGet dependencies from them. `IncludeBuildOutput=false` prevents the empty assembly from being included:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PackageId>MarkItDown</PackageId>
    <Description>All-in-one Markdown conversion library with all built-in converters</Description>
    <IsPackable>true</IsPackable>
    <!-- No code in this project — it's a meta-package -->
    <IncludeBuildOutput>false</IncludeBuildOutput>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MarkItDown.Core\MarkItDown.Core.csproj" />
    <ProjectReference Include="..\MarkItDown.Llm\MarkItDown.Llm.csproj" />
    <ProjectReference Include="..\MarkItDown.Converters.Pdf\MarkItDown.Converters.Pdf.csproj" />
    <ProjectReference Include="..\MarkItDown.Converters.Html\MarkItDown.Converters.Html.csproj" />
    <ProjectReference Include="..\MarkItDown.Converters.Office\MarkItDown.Converters.Office.csproj" />
    <ProjectReference Include="..\MarkItDown.Converters.Data\MarkItDown.Converters.Data.csproj" />
    <ProjectReference Include="..\MarkItDown.Converters.Media\MarkItDown.Converters.Media.csproj" />
    <ProjectReference Include="..\MarkItDown.Converters.Web\MarkItDown.Converters.Web.csproj" />
  </ItemGroup>
</Project>
```

### 4. LLM package

`MarkItDown.Llm` provides `OpenAILlmClient` implementing `ILlmClient` (interface in Core). It's only referenced by `MarkItDown.Cli`, not by any converter. The meta-package includes it for users who want LLM features, but individual converter packages don't pull it in.

### 5. PdfPig custom version

`MarkItDown.Pdf` uses `UglyToad.PdfPig` version `1.7.0-custom-5` — a custom build not available on nuget.org. Before CI publishing, one of these must happen:

1. **Publish custom PdfPig to a private feed** and add the feed to `NuGet.Config`
2. **Switch to official PdfPig version** (preferred if the custom changes can be upstreamed or dropped)

This is a blocking issue for CI. If unresolved, `dotnet restore` will fail on the GitHub Actions runner.

### 6. Public API

Current API is already consumer-friendly — no changes needed:

```csharp
// All-in-one usage
using MarkItDown.Core;
using MarkItDown.Converters.Pdf;

var engine = new MarkItDownEngine(builder => builder.Add(new PdfConverter()));
var result = await engine.ConvertAsync("document.pdf");

// Custom converter
using MarkItDown.Core;

public class MyConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions => new HashSet<string> { ".my" };
    public override Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken ct = default)
    {
        // custom logic
    }
}
```

### 7. Converter package naming

Converter packages use a shortened name (dropping the `Converters.` prefix):

| Project | Package ID |
|---------|-----------|
| `MarkItDown.Core` | `MarkItDown.Core` |
| `MarkItDown.Llm` | `MarkItDown.Llm` |
| `MarkItDown.Converters.Pdf` | `MarkItDown.Pdf` |
| `MarkItDown.Converters.Html` | `MarkItDown.Html` |
| `MarkItDown.Converters.Office` | `MarkItDown.Office` |
| `MarkItDown.Converters.Data` | `MarkItDown.Data` |
| `MarkItDown.Converters.Media` | `MarkItDown.Media` |
| `MarkItDown.Converters.Web` | `MarkItDown.Web` |
| `MarkItDown` (new) | `MarkItDown` |

## CI/CD — GitHub Actions

Create directory `.github/workflows/` (does not exist yet).

```yaml
name: publish
on:
  push:
    tags: ['v*']

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Extract version
        run: echo "VERSION=${GITHUB_REF#refs/tags/v}" >> $GITHUB_ENV
      - run: dotnet restore MarkItDown.sln --configfile NuGet.Config
      - run: dotnet build --no-restore -c Release -p:Version=${{ env.VERSION }}
      - run: dotnet test --no-build
      - run: dotnet pack --no-build -c Release -p:Version=${{ env.VERSION }} -o artifacts/
      - run: dotnet nuget push artifacts/*.nupkg -s https://api.nuget.org/v3/index.json -k ${{ secrets.NUGET_API_KEY }} --skip-duplicate
```

**Prerequisites:**
- Add `NUGET_API_KEY` secret to GitHub repo settings
- Push a `v1.0.0` tag to trigger the workflow
- Resolve PdfPig custom version issue (Section 5)

## Versioning

- Semantic versioning (SemVer): `MAJOR.MINOR.PATCH`
- Version set via `-p:Version` from git tag during CI
- Local dev builds use default `1.0.0`

## Files Changed

| File | Change |
|------|--------|
| `Directory.Build.props` | New — shared NuGet metadata |
| `src/MarkItDown/MarkItDown.csproj` | New — meta-package project |
| `src/MarkItDown.Core/*.csproj` | Add PackageId, Description, IsPackable |
| `src/MarkItDown.Llm/*.csproj` | Add PackageId, Description, IsPackable |
| `src/MarkItDown.Converters.*/.csproj` | Add PackageId, Description, IsPackable |
| `src/MarkItDown.Cli/*.csproj` | Add IsPackable=false |
| `src/MarkItDown.McpServer/*.csproj` | Add IsPackable=false |
| `MarkItDown.sln` | Add MarkItDown meta-package project |
| `.github/workflows/publish.yml` | New — CI/CD pipeline |
| `.gitignore` | Add `artifacts/` directory |

## Test Strategy

- `dotnet pack` succeeds for all packable projects
- Meta-package `.nupkg` contains correct dependency list
- No public API changes — existing tests continue to pass
- Manual smoke test: install from local feed, verify converter registration works
