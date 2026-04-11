# Phase 4: Media + LLM Integration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add image metadata extraction with optional LLM captioning, audio metadata extraction, and the `MarkItDown.Llm` package with OpenAI implementation.

**Architecture:** Two new packages: `MarkItDown.Llm` (OpenAI `ILlmClient` implementation) and `MarkItDown.Converters.Media` (Image + Audio converters). ImageConverter extracts EXIF via SixLabors.ImageSharp and optionally captions via `ILlmClient` from the request. AudioConverter extracts metadata via TagLib#. LLM is always optional — converters work without it.

**Tech Stack:** .NET 8, xUnit, SixLabors.ImageSharp 3.1.5, TagLib# 2.1.0, OpenAI 2.1.0

**Spec:** `docs/superpowers/specs/2026-04-11-markitdown-csharp-full-alignment-design.md` (Phase 4)

---

## File Structure

### Files to Create

| File | Responsibility |
|------|---------------|
| `src/MarkItDown.Llm/MarkItDown.Llm.csproj` | LLM package |
| `src/MarkItDown.Llm/OpenAILlmClient.cs` | OpenAI ILlmClient implementation |
| `src/MarkItDown.Llm/LlmClientOptions.cs` | Options for OpenAI client |
| `src/MarkItDown.Converters.Media/MarkItDown.Converters.Media.csproj` | Media converter package |
| `src/MarkItDown.Converters.Media/ImageConverter.cs` | Image → Markdown |
| `src/MarkItDown.Converters.Media/AudioConverter.cs` | Audio → Markdown |
| `tests/MarkItDown.Llm.Tests/MarkItDown.Llm.Tests.csproj` | LLM test project |
| `tests/MarkItDown.Llm.Tests/OpenAILlmClientTests.cs` | OpenAI client tests |
| `tests/MarkItDown.Converters.Media.Tests/MarkItDown.Converters.Media.Tests.csproj` | Media test project |
| `tests/MarkItDown.Converters.Media.Tests/ImageConverterTests.cs` | Image converter tests |
| `tests/MarkItDown.Converters.Media.Tests/AudioConverterTests.cs` | Audio converter tests |
| `tests/MarkItDown.Converters.Media.Tests/FixturePath.cs` | Fixture helper |
| `tests/Fixtures/media/` | Test fixture files |

### Files to Modify

| File | Change |
|------|--------|
| `src/MarkItDown.Cli/MarkItDown.Cli.csproj` | Add Media + Llm references |
| `src/MarkItDown.Cli/CliRunner.cs` | Register Media converters, add LLM options |
| `MarkItDown.sln` | Add new projects |

---

## Task 1: Create LLM Package with OpenAI Implementation

**Files:**
- Create: `src/MarkItDown.Llm/MarkItDown.Llm.csproj`
- Create: `src/MarkItDown.Llm/OpenAILlmClient.cs`
- Create: `src/MarkItDown.Llm/LlmClientOptions.cs`
- Create: `tests/MarkItDown.Llm.Tests/MarkItDown.Llm.Tests.csproj`
- Create: `tests/MarkItDown.Llm.Tests/OpenAILlmClientTests.cs`

- [ ] **Step 1: Create LLM package csproj**

`src/MarkItDown.Llm/MarkItDown.Llm.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="OpenAI" Version="2.1.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MarkItDown.Core\MarkItDown.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create LlmClientOptions**

`src/MarkItDown.Llm/LlmClientOptions.cs`:

```csharp
namespace MarkItDown.Llm;

public sealed class LlmClientOptions
{
    public required string ApiKey { get; init; }
    public string Model { get; init; } = "gpt-4o";
    public string? Endpoint { get; init; }
}
```

- [ ] **Step 3: Create OpenAILlmClient**

`src/MarkItDown.Llm/OpenAILlmClient.cs`:

```csharp
using System.ClientModel;
using MarkItDown.Core;
using OpenAI;

namespace MarkItDown.Llm;

public sealed class OpenAILlmClient : ILlmClient
{
    private readonly OpenAIClient _client;
    private readonly string _model;

    public OpenAILlmClient(LlmClientOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _model = options.Model;

        var clientOptions = new OpenAIClientOptions();
        if (options.Endpoint is not null)
            clientOptions.Endpoint = new Uri(options.Endpoint);

        _client = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey), clientOptions);
    }

    public async Task<string> CompleteAsync(
        string prompt,
        byte[]? imageData = null,
        string? imageMimeType = null,
        CancellationToken ct = default)
    {
        var chatClient = _client.GetChatClient(_model);

        if (imageData is not null && imageMimeType is not null)
        {
            // Multimodal request with image
            var imageParts = new List<ChatMessageContentPart>
            {
                ChatMessageContentPart.CreateTextPart(prompt),
                ChatMessageContentPart.CreateImagePart(
                    BinaryData.FromBytes(imageData), imageMimeType)
            };

            var response = await chatClient.CompleteChatAsync(
                new UserChatMessage(imageParts), cancellationToken: ct);
            return response.Value.Content[0].Text;
        }

        // Text-only request
        var textResponse = await chatClient.CompleteChatAsync(
            new UserChatMessage(prompt), cancellationToken: ct);
        return textResponse.Value.Content[0].Text;
    }
}
```

- [ ] **Step 4: Create test project**

`tests/MarkItDown.Llm.Tests/MarkItDown.Llm.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\MarkItDown.Llm\MarkItDown.Llm.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Write LLM tests**

`tests/MarkItDown.Llm.Tests/OpenAILlmClientTests.cs`:

```csharp
using MarkItDown.Llm;

namespace MarkItDown.Llm.Tests;

public sealed class OpenAILlmClientTests
{
    [Fact]
    public void Constructor_RequiresApiKey()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OpenAILlmClient(new LlmClientOptions { ApiKey = null! }));
    }

    [Fact]
    public void Constructor_AcceptsValidOptions()
    {
        var client = new OpenAILlmClient(
            new LlmClientOptions { ApiKey = "test-key" });
        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_UsesDefaultModel()
    {
        var options = new LlmClientOptions { ApiKey = "test-key" };
        Assert.Equal("gpt-4o", options.Model);
    }

    [Fact]
    public void Constructor_AcceptsCustomEndpoint()
    {
        var client = new OpenAILlmClient(
            new LlmClientOptions
            {
                ApiKey = "test-key",
                Endpoint = "https://custom.openai.azure.com"
            });
        Assert.NotNull(client);
    }
}
```

- [ ] **Step 6: Run tests — verify 4 pass**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Llm.Tests -v minimal`

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: add MarkItDown.Llm package with OpenAI ILlmClient implementation

- OpenAILlmClient: text + multimodal (vision) completion
- LlmClientOptions: API key, model, custom endpoint
- 4 tests passing"
```

---

## Task 2: Create Media Package with ImageConverter

**Files:**
- Create: `src/MarkItDown.Converters.Media/MarkItDown.Converters.Media.csproj`
- Create: `src/MarkItDown.Converters.Media/ImageConverter.cs`
- Create: `tests/MarkItDown.Converters.Media.Tests/MarkItDown.Converters.Media.Tests.csproj`
- Create: `tests/MarkItDown.Converters.Media.Tests/ImageConverterTests.cs`
- Create: `tests/MarkItDown.Converters.Media.Tests/FixturePath.cs`

- [ ] **Step 1: Create Media package csproj**

`src/MarkItDown.Converters.Media/MarkItDown.Converters.Media.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SixLabors.ImageSharp" Version="3.1.5" />
    <PackageReference Include="TagLibSharp" Version="2.1.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MarkItDown.Core\MarkItDown.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create ImageConverter**

`src/MarkItDown.Converters.Media/ImageConverter.cs`:

```csharp
using System.Text;
using MarkItDown.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace MarkItDown.Converters.Media;

public sealed class ImageConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/png"
        };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("Image converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                var builder = new StringBuilder();

                using var image = Image.Load(filePath);

                // Basic properties
                builder.AppendLine($"ImageSize: {image.Width}x{image.Height}");

                // EXIF metadata
                var exif = image.Metadata.ExifProfile;
                if (exif is not null)
                {
                    AppendExifValue(builder, "Title", exif, ExifTag.ImageDescription);
                    AppendExifValue(builder, "Artist", exif, ExifTag.Artist);
                    AppendExifValue(builder, "DateTimeOriginal", exif, ExifTag.DateTimeOriginal);
                    AppendExifValue(builder, "Copyright", exif, ExifTag.Copyright);
                }

                // LLM captioning (optional)
                if (request.LlmClient is not null)
                {
                    try
                    {
                        var imageData = File.ReadAllBytes(filePath);
                        var mimeType = Path.GetExtension(filePath).Equals(".png", StringComparison.OrdinalIgnoreCase)
                            ? "image/png" : "image/jpeg";

                        var caption = request.LlmClient.CompleteAsync(
                            "Write a detailed caption for this image.",
                            imageData, mimeType, cancellationToken).GetAwaiter().GetResult();

                        builder.AppendLine();
                        builder.AppendLine("# Description:");
                        builder.Append(caption);
                    }
                    catch (Exception ex)
                    {
                        builder.AppendLine();
                        builder.AppendLine($"# Description: (LLM error: {ex.Message})");
                    }
                }

                return new DocumentConversionResult("Image", builder.ToString().TrimEnd());
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert image: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    private static void AppendExifValue(
        StringBuilder builder, string label, ExifProfile exif, ExifTag<string> tag)
    {
        var value = exif.GetValue(tag)?.Value;
        if (!string.IsNullOrWhiteSpace(value))
            builder.AppendLine($"{label}: {value}");
    }
}
```

- [ ] **Step 3: Create test project and FixturePath**

`tests/MarkItDown.Converters.Media.Tests/MarkItDown.Converters.Media.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\MarkItDown.Converters.Media\MarkItDown.Converters.Media.csproj" />
  </ItemGroup>
</Project>
```

`tests/MarkItDown.Converters.Media.Tests/FixturePath.cs`:

```csharp
namespace MarkItDown.Converters.Media.Tests;

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

- [ ] **Step 4: Write image tests**

Tests generate test images programmatically using ImageSharp.

`tests/MarkItDown.Converters.Media.Tests/ImageConverterTests.cs`:

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MarkItDown.Converters.Media.Tests;

public sealed class ImageConverterTests
{
    private readonly ImageConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsJpgExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "photo.jpg" }));
    }

    [Fact]
    public void CanConvert_AcceptsJpegExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "photo.jpeg" }));
    }

    [Fact]
    public void CanConvert_AcceptsPngExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "image.png" }));
    }

    [Fact]
    public async Task ConvertAsync_ExtractsImageSize()
    {
        var path = CreateTestPng(100, 50);

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("ImageSize: 100x50", result.Markdown);
            Assert.Equal("Image", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_WorksForJpeg()
    {
        var path = CreateTestJpeg(200, 100);

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("ImageSize: 200x100", result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTestPng(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        using var image = new Image<Rgba32>(width, height);
        image.SaveAsPng(path);
        return path;
    }

    private static string CreateTestJpeg(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jpg");
        using var image = new Image<Rgba32>(width, height);
        image.SaveAsJpeg(path);
        return path;
    }
}
```

- [ ] **Step 5: Run tests — verify 5 pass**

Run: `cd <worktree> && dotnet test tests/MarkItDown.Converters.Media.Tests --filter "ImageConverterTests" -v minimal`

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add ImageConverter with EXIF metadata + optional LLM captioning

- SixLabors.ImageSharp for image loading and EXIF
- Metadata: ImageSize, Title, Artist, DateTimeOriginal, Copyright
- Optional LLM captioning via ILlmClient on request
- 5 tests passing"
```

---

## Task 3: AudioConverter

**Files:**
- Create: `src/MarkItDown.Converters.Media/AudioConverter.cs`
- Create: `tests/MarkItDown.Converters.Media.Tests/AudioConverterTests.cs`

- [ ] **Step 1: Write failing tests**

`tests/MarkItDown.Converters.Media.Tests/AudioConverterTests.cs`:

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Media;
using TagLib;

namespace MarkItDown.Converters.Media.Tests;

public sealed class AudioConverterTests
{
    private readonly AudioConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsMp3Extension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "song.mp3" }));
    }

    [Fact]
    public void CanConvert_AcceptsWavExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "audio.wav" }));
    }

    [Fact]
    public void CanConvert_AcceptsM4aExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "audio.m4a" }));
    }

    [Fact]
    public async Task ConvertAsync_ExtractsMetadata()
    {
        var path = CreateTestMp3();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("Duration:", result.Markdown);
            Assert.Contains("MediaTypes: Audio", result.Markdown);
            Assert.Equal("Audio", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTestMp3()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp3");

        // Create a minimal valid MP3 file with a single MPEG frame
        // MPEG Audio Layer 3/2, Layer III, 128kbps, 44100Hz, Joint Stereo
        var header = new byte[] { 0xFF, 0xFB, 0x90, 0x00 };
        var frame = new byte[417]; // Frame size for 128kbps, 44100Hz
        header.CopyTo(frame, 0);
        File.WriteAllBytes(path, frame);

        return path;
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

- [ ] **Step 3: Create AudioConverter**

`src/MarkItDown.Converters.Media/AudioConverter.cs`:

```csharp
using System.Text;
using MarkItDown.Core;
using TagLib;

namespace MarkItDown.Converters.Media;

public sealed class AudioConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".m4a" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "audio/mpeg", "audio/x-wav", "audio/mp4", "audio/x-m4a"
        };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("Audio converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                var builder = new StringBuilder();

                using var file = TagLib.File.Create(filePath);
                var tag = file.Tag;
                var properties = file.Properties;

                // Duration
                if (properties?.Duration is TimeSpan duration)
                    builder.AppendLine($"Duration: {duration:hh\\:mm\\:ss}");

                // Media types
                if (properties?.MediaTypes is not null)
                    builder.AppendLine($"MediaTypes: {properties.MediaTypes}");

                // Title
                if (!string.IsNullOrWhiteSpace(tag.Title))
                    builder.AppendLine($"Title: {tag.Title}");

                // Artists
                if (tag.Performers is { Length: > 0 })
                    builder.AppendLine($"Artist: {string.Join(", ", tag.Performers)}");

                // Album
                if (!string.IsNullOrWhiteSpace(tag.Album))
                    builder.AppendLine($"Album: {tag.Album}");

                // Genre
                if (tag.Genres is { Length: > 0 })
                    builder.AppendLine($"Genre: {string.Join(", ", tag.Genres)}");

                // Track
                if (tag.Track > 0)
                    builder.AppendLine($"Track: {tag.Track}");

                // Year
                if (tag.Year > 0)
                    builder.AppendLine($"Year: {tag.Year}");

                // Audio codec
                if (properties?.AudioBitrate is > 0)
                    builder.AppendLine($"Bitrate: {properties.AudioBitrate} kbps");

                if (properties?.AudioSampleRate is > 0)
                    builder.AppendLine($"SampleRate: {properties.AudioSampleRate} Hz");

                if (properties?.AudioChannels is > 0)
                    builder.AppendLine($"Channels: {properties.AudioChannels}");

                return new DocumentConversionResult("Audio", builder.ToString().TrimEnd());
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert audio: {ex.Message}", ex);
            }
        }, cancellationToken);
    }
}
```

- [ ] **Step 4: Run tests, commit**

```bash
cd <worktree> && dotnet test tests/MarkItDown.Converters.Media.Tests --filter "AudioConverterTests" -v minimal
git add -A && git commit -m "feat: add AudioConverter with TagLib# metadata extraction"
```

---

## Task 4: Update CLI and Solution, Final Verification

- [ ] **Step 1: Update CLI csproj**

Add to `src/MarkItDown.Cli/MarkItDown.Cli.csproj`:
```xml
<ProjectReference Include="..\MarkItDown.Converters.Media\MarkItDown.Converters.Media.csproj" />
```

- [ ] **Step 2: Update CliRunner.cs**

Add using and register converters:
```csharp
using MarkItDown.Converters.Media;
// In engine construction:
.Add(new ImageConverter())
.Add(new AudioConverter())
```

- [ ] **Step 3: Add projects to solution**

```bash
dotnet sln add src/MarkItDown.Llm/MarkItDown.Llm.csproj
dotnet sln add tests/MarkItDown.Llm.Tests/MarkItDown.Llm.Tests.csproj
dotnet sln add src/MarkItDown.Converters.Media/MarkItDown.Converters.Media.csproj
dotnet sln add tests/MarkItDown.Converters.Media.Tests/MarkItDown.Llm.Tests.csproj
```

- [ ] **Step 4: Run all tests**

Run: `cd <worktree> && dotnet test -v minimal`

- [ ] **Step 5: Verify build clean**

Run: `cd <worktree> && dotnet build -v minimal`

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: integrate Media converters and LLM into CLI, add to solution

Phase 4 complete: Image (EXIF + LLM), Audio (metadata), LLM (OpenAI) all supported"
```

---

## Acceptance Criteria

- [ ] Images extract metadata (ImageSize, EXIF fields when present)
- [ ] Images support optional LLM captioning via ILlmClient
- [ ] Audio files extract metadata (Duration, Title, Artist, Album, Genre, Bitrate, etc.)
- [ ] OpenAILlmClient implements ILlmClient with text + vision support
- [ ] LLM is optional — converters work without it
- [ ] All converters registered in CLI
- [ ] All previous tests still pass (no regression)
- [ ] `dotnet build` succeeds with 0 errors
