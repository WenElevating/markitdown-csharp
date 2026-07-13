using System.Text.Json;
using MarkItDown.Core;

namespace MarkItDown.Converters.Office.Tests;

public sealed class GoldenCorpusOfficeTests
{
    [Fact]
    public async Task GeneratedDocxFloatingImage_IsPreservedAsAsset()
    {
        var path = GoldenOfficeFixtureFactory.Create("docx-floating");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.OpenRead(path))
            {
                using var reader = new StreamReader((archive.GetEntry("word/document.xml") ?? throw new InvalidOperationException()).Open());
                Assert.Contains("<wp:anchor", reader.ReadToEnd(), StringComparison.Ordinal);
            }

            var store = new InMemoryAssetStore();
            var options = new ConversionOptions { PipelineMode = PipelineMode.Multimodal, VisionMode = VisionMode.Off };
            var result = await new MarkItDownEngine(builder => builder.Add(new DocxConverter())).ConvertAsync(new DocumentConversionRequest
            {
                FilePath = path,
                AssetStore = store,
                Options = options,
                Context = new ConversionContext { Pipeline = PipelineMode.Multimodal, Vision = VisionMode.Off, Assets = store, Options = options }
            });

            Assert.NotNull(result.Document);
            Assert.NotEmpty(store.Assets);
            Assert.Contains("asset://", result.Markdown, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("pptx")]
    [InlineData("xlsx")]
    public async Task RealOfficeCorpus_ConvertsWithoutSilentFailure(string format)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var corpusRoot = Path.Combine(root, "tests", "Fixtures", "real");
        var files = Directory.EnumerateFiles(corpusRoot, $"*.{format}", SearchOption.AllDirectories).OrderBy(path => path).ToArray();
        Assert.NotEmpty(files);

        foreach (var path in files)
        {
            var converter = format switch
            {
                "docx" => (IConverter)new DocxConverter(),
                "pptx" => new PptxConverter(),
                "xlsx" => new XlsxConverter(),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            var store = new InMemoryAssetStore();
            var engine = new MarkItDownEngine(builder => builder.Add(converter));
            var result = await engine.ConvertAsync(new DocumentConversionRequest
            {
                FilePath = path,
                AssetStore = store,
                Context = new ConversionContext
                {
                    Pipeline = PipelineMode.Multimodal,
                    Vision = VisionMode.Off,
                    Assets = store,
                    Options = new ConversionOptions
                    {
                        PipelineMode = PipelineMode.Multimodal,
                        VisionMode = VisionMode.Off,
                        Privacy = new PrivacyOptions { AllowExternalRelationships = true }
                    }
                }
            });

            Assert.NotNull(result.Document);
            Assert.False(string.IsNullOrWhiteSpace(result.Markdown), path);
            Assert.DoesNotContain(result.Diagnostics ?? [], diagnostic =>
                diagnostic.Code == "CONVERSION_FAILED" || diagnostic.Code == "OFFICE_PACKAGE_INVALID");
        }
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("pptx")]
    [InlineData("xlsx")]
    public async Task GeneratedOfficeCorpus_PassesStructuralQualityGate(string format)
    {
        var path = GoldenOfficeFixtureFactory.Create(format);
        try
        {
            var converter = format switch
            {
                "docx" => (IConverter)new DocxConverter(),
                "pptx" => new PptxConverter(),
                "xlsx" => new XlsxConverter(),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            var store = new InMemoryAssetStore();
            var engine = new MarkItDownEngine(builder => builder.Add(converter));
            var result = await engine.ConvertAsync(new DocumentConversionRequest
            {
                FilePath = path,
                AssetStore = store,
                Context = new ConversionContext
                {
                    Pipeline = PipelineMode.Multimodal,
                    Vision = VisionMode.Off,
                    Assets = store,
                    Options = new ConversionOptions { PipelineMode = PipelineMode.Multimodal, VisionMode = VisionMode.Off }
                }
            });

            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "GoldenCorpus", "manifest.json")));
            var entry = manifest.RootElement.GetProperty("entries").EnumerateArray()
                .Single(item => item.GetProperty("format").GetString() == format);
            var expectedJson = entry.GetProperty("expected");
            var expected = new GoldenDocumentExpectation
            {
                RequiredText = ReadArray(expectedJson, "requiredText"),
                RequiredHeadings = ReadArray(expectedJson, "requiredHeadings"),
                RequiredTableCells = ReadArray(expectedJson, "requiredTableCells"),
                MinimumAssets = expectedJson.TryGetProperty("minimumAssets", out var minimumAssets) ? minimumAssets.GetInt32() : 0
            };
            var report = DocumentQualityEvaluator.Evaluate(result.Document!, expected, result.Assets.Count);
            var thresholds = manifest.RootElement.GetProperty("qualityThresholds");

            Assert.Equal(FidelityStatus.Complete, result.FidelityStatus);
            Assert.True(report.Passed, JsonSerializer.Serialize(report));
            Assert.Contains("asset://", result.Markdown, StringComparison.Ordinal);
            foreach (var asset in store.Assets)
                Assert.True(store.TryGetBytes(asset.Id, out var bytes) && bytes.Length == asset.Size);
            Assert.True(report.TextRecall >= thresholds.GetProperty("minTextRecall").GetDouble());
            Assert.True(report.HeadingAccuracy >= thresholds.GetProperty("minHeadingAccuracy").GetDouble());
            Assert.True(report.TableCellRecall >= thresholds.GetProperty("minTableCellRecall").GetDouble());
            Assert.True(report.UnexplainedContentLossCount <= thresholds.GetProperty("maxUnexplainedContentLoss").GetInt32());
            Assert.Equal(0, report.UnexplainedContentLossCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IReadOnlyList<string> ReadArray(JsonElement objectElement, string property) =>
        objectElement.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
}
