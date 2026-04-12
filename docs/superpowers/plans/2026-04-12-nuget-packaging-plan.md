# NuGet Packaging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Package markitdown-csharp as NuGet packages with Core, individual converter packages, and a meta-package for all-in-one install.

**Architecture:** Add `Directory.Build.props` for shared metadata, add per-project NuGet properties, create a meta-package project, and add a GitHub Actions workflow for tag-triggered publishing.

**Tech Stack:** .NET 8 SDK, `dotnet pack`, GitHub Actions

**Spec:** `docs/superpowers/specs/2026-04-12-nuget-packaging-design.md`

---

## File Structure

### Modified files

| File | Change |
|------|--------|
| `Directory.Build.props` | New — shared NuGet metadata |
| `src/MarkItDown.Core/MarkItDown.Core.csproj` | Add PackageId, Description |
| `src/MarkItDown.Llm/MarkItDown.Llm.csproj` | Add PackageId, Description |
| `src/MarkItDown.Converters.Pdf/MarkItDown.Converters.Pdf.csproj` | Add PackageId, Description |
| `src/MarkItDown.Converters.Html/MarkItDown.Converters.Html.csproj` | Add PackageId, Description |
| `src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj` | Add PackageId, Description |
| `src/MarkItDown.Converters.Data/MarkItDown.Converters.Data.csproj` | Add PackageId, Description |
| `src/MarkItDown.Converters.Media/MarkItDown.Converters.Media.csproj` | Add PackageId, Description |
| `src/MarkItDown.Converters.Web/MarkItDown.Converters.Web.csproj` | Add PackageId, Description |
| `src/MarkItDown.Cli/MarkItDown.Cli.csproj` | Add IsPackable=false |
| `src/MarkItDown.McpServer/MarkItDown.McpServer.csproj` | Add IsPackable=false |
| `MarkItDown.sln` | Add meta-package project |
| `.gitignore` | Add artifacts/ |
| `.github/workflows/publish.yml` | New — CI/CD pipeline |

### Created files

| File | Purpose |
|------|---------|
| `Directory.Build.props` | Shared NuGet metadata |
| `src/MarkItDown/MarkItDown.csproj` | Meta-package (empty project) |
| `.github/workflows/publish.yml` | GitHub Actions auto-publish |

---

### Task 1: Create Directory.Build.props

**Files:**
- Create: `Directory.Build.props`
- Modify: `.gitignore`

- [ ] **Step 1: Create Directory.Build.props**

Create `Directory.Build.props` at repository root with shared NuGet metadata:

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

- [ ] **Step 2: Add artifacts/ to .gitignore**

Add to `.gitignore`:

```
## NuGet packaging output
artifacts/
```

- [ ] **Step 3: Build to verify no regressions**

Run: `dotnet build MarkItDown.sln`
Expected: Build succeeds. `GenerateDocumentationFile` may produce warnings for missing XML docs — these are non-blocking.

- [ ] **Step 4: Commit**

```bash
git add Directory.Build.props .gitignore
git commit -m "build: add shared NuGet metadata via Directory.Build.props"
```

---

### Task 2: Add NuGet package metadata to all packable projects

**Files:**
- Modify: `src/MarkItDown.Core/MarkItDown.Core.csproj`
- Modify: `src/MarkItDown.Llm/MarkItDown.Llm.csproj`
- Modify: `src/MarkItDown.Converters.Pdf/MarkItDown.Converters.Pdf.csproj`
- Modify: `src/MarkItDown.Converters.Html/MarkItDown.Converters.Html.csproj`
- Modify: `src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj`
- Modify: `src/MarkItDown.Converters.Data/MarkItDown.Converters.Data.csproj`
- Modify: `src/MarkItDown.Converters.Media/MarkItDown.Converters.Media.csproj`
- Modify: `src/MarkItDown.Converters.Web/MarkItDown.Converters.Web.csproj`

- [ ] **Step 1: Update MarkItDown.Core.csproj**

Add to the existing `<PropertyGroup>`:

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <PackageId>MarkItDown.Core</PackageId>
  <Description>Core library for converting documents to Markdown with plugin-based converter architecture</Description>
</PropertyGroup>
```

- [ ] **Step 2: Update MarkItDown.Llm.csproj**

Add to the existing `<PropertyGroup>`:

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <PackageId>MarkItDown.Llm</PackageId>
  <Description>LLM integration for MarkItDown — OpenAI-compatible image and audio captioning</Description>
</PropertyGroup>
```

- [ ] **Step 3: Update MarkItDown.Converters.Pdf.csproj**

Add to the existing `<PropertyGroup>`:

```xml
<PackageId>MarkItDown.Pdf</PackageId>
<Description>PDF to Markdown converter for MarkItDown — supports text extraction, images, tables, and layout analysis</Description>
```

- [ ] **Step 4: Update MarkItDown.Converters.Html.csproj**

Add to the existing `<PropertyGroup>`:

```xml
<PackageId>MarkItDown.Html</PackageId>
<Description>HTML to Markdown converter for MarkItDown — noise removal, structure preservation</Description>
```

- [ ] **Step 5: Update MarkItDown.Converters.Office.csproj**

Add to the existing `<PropertyGroup>`:

```xml
<PackageId>MarkItDown.Office</PackageId>
<Description>Office document converter for MarkItDown — docx, pptx, xlsx, csv, msg</Description>
```

- [ ] **Step 6: Update MarkItDown.Converters.Data.csproj**

Add to the existing `<PropertyGroup>`:

```xml
<PackageId>MarkItDown.Data</PackageId>
<Description>Data format converter for MarkItDown — json, jsonl, xml, rss, atom, ipynb, epub, zip, md</Description>
```

- [ ] **Step 7: Update MarkItDown.Converters.Media.csproj**

Add to the existing `<PropertyGroup>`:

```xml
<PackageId>MarkItDown.Media</PackageId>
<Description>Media converter for MarkItDown — images and audio with optional LLM captioning</Description>
```

- [ ] **Step 8: Update MarkItDown.Converters.Web.csproj**

Add to the existing `<PropertyGroup>`:

```xml
<PackageId>MarkItDown.Web</PackageId>
<Description>Web converter for MarkItDown — URLs and Wikipedia articles</Description>
```

- [ ] **Step 9: Build and run tests**

Run: `dotnet test MarkItDown.sln --no-restore`
Expected: All 119 tests pass.

- [ ] **Step 10: Commit**

```bash
git add src/MarkItDown.Core/MarkItDown.Core.csproj src/MarkItDown.Llm/MarkItDown.Llm.csproj src/MarkItDown.Converters.Pdf/MarkItDown.Converters.Pdf.csproj src/MarkItDown.Converters.Html/MarkItDown.Converters.Html.csproj src/MarkItDown.Converters.Office/MarkItDown.Converters.Office.csproj src/MarkItDown.Converters.Data/MarkItDown.Converters.Data.csproj src/MarkItDown.Converters.Media/MarkItDown.Converters.Media.csproj src/MarkItDown.Converters.Web/MarkItDown.Converters.Web.csproj
git commit -m "build: add NuGet package metadata to all packable projects"
```

---

### Task 3: Mark CLI and McpServer as non-packable

**Files:**
- Modify: `src/MarkItDown.Cli/MarkItDown.Cli.csproj`
- Modify: `src/MarkItDown.McpServer/MarkItDown.McpServer.csproj`

- [ ] **Step 1: Update MarkItDown.Cli.csproj**

Add `<IsPackable>false</IsPackable>` to the existing `<PropertyGroup>`:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net8.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <IsPackable>false</IsPackable>
</PropertyGroup>
```

- [ ] **Step 2: Update MarkItDown.McpServer.csproj**

Add `<IsPackable>false</IsPackable>` to the existing `<PropertyGroup>`:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net8.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <IsPackable>false</IsPackable>
</PropertyGroup>
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build MarkItDown.sln`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/MarkItDown.Cli/MarkItDown.Cli.csproj src/MarkItDown.McpServer/MarkItDown.McpServer.csproj
git commit -m "build: mark CLI and McpServer as non-packable"
```

---

### Task 4: Create MarkItDown meta-package project

**Files:**
- Create: `src/MarkItDown/MarkItDown.csproj`
- Modify: `MarkItDown.sln`

- [ ] **Step 1: Create project directory and csproj**

```bash
mkdir -p src/MarkItDown
```

Create `src/MarkItDown/MarkItDown.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PackageId>MarkItDown</PackageId>
    <Description>All-in-one Markdown conversion library with all built-in converters</Description>
    <IsPackable>true</IsPackable>
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

- [ ] **Step 2: Add project to solution**

```bash
dotnet sln add src/MarkItDown/MarkItDown.csproj
```

- [ ] **Step 3: Build and run all tests**

Run: `dotnet test MarkItDown.sln --no-restore`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/MarkItDown/MarkItDown.csproj MarkItDown.sln
git commit -m "build: add MarkItDown meta-package project"
```

---

### Task 5: Create GitHub Actions publish workflow

**Files:**
- Create: `.github/workflows/publish.yml`

- [ ] **Step 1: Create workflow directory**

```bash
mkdir -p .github/workflows
```

- [ ] **Step 2: Create publish.yml**

Create `.github/workflows/publish.yml`:

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

      - name: Extract version from tag
        run: echo "VERSION=${GITHUB_REF#refs/tags/v}" >> $GITHUB_ENV

      - run: dotnet restore MarkItDown.sln --configfile NuGet.Config

      - run: dotnet build --no-restore -c Release -p:Version=${{ env.VERSION }}

      - run: dotnet test --no-build

      - run: dotnet pack --no-build -c Release -p:Version=${{ env.VERSION }} -o artifacts/

      - run: dotnet nuget push artifacts/*.nupkg -s https://api.nuget.org/v3/index.json -k ${{ secrets.NUGET_API_KEY }} --skip-duplicate
```

- [ ] **Step 3: Verify YAML is valid**

```bash
cat .github/workflows/publish.yml
```

Verify the file contents are correct.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/publish.yml
git commit -m "ci: add GitHub Actions workflow for NuGet publishing on tag push"
```

---

### Task 6: Verify packaging works locally

**Files:** None (verification only)

- [ ] **Step 1: Restore and build**

```bash
dotnet restore MarkItDown.sln --configfile NuGet.Config
dotnet build MarkItDown.sln -c Release
```

Expected: Build succeeds.

- [ ] **Step 2: Pack all projects**

```bash
dotnet pack MarkItDown.sln -c Release -o artifacts/ --no-build
```

Expected: All 9 `.nupkg` files generated:
- `MarkItDown.Core.1.0.0.nupkg`
- `MarkItDown.Llm.1.0.0.nupkg`
- `MarkItDown.Pdf.1.0.0.nupkg`
- `MarkItDown.Html.1.0.0.nupkg`
- `MarkItDown.Office.1.0.0.nupkg`
- `MarkItDown.Data.1.0.0.nupkg`
- `MarkItDown.Media.1.0.0.nupkg`
- `MarkItDown.Web.1.0.0.nupkg`
- `MarkItDown.1.0.0.nupkg`

Plus matching `.snupkg` symbol packages.

- [ ] **Step 3: Verify meta-package dependencies**

```bash
dotnet nuget locals all --clear
unzip -p artifacts/MarkItDown.1.0.0.nupkg MarkItDown.nuspec | grep -A1 "dependency"
```

Verify the meta-package lists all 8 dependencies (Core + Llm + 6 converters).

- [ ] **Step 4: Run all tests**

```bash
dotnet test MarkItDown.sln --no-restore
```

Expected: All 119 tests pass.

- [ ] **Step 5: Clean up artifacts**

```bash
rm -rf artifacts/
```

- [ ] **Step 6: Final commit if any fixes needed**

```bash
git add -A
git commit -m "fix: address packaging issues found during verification"
```
