using System.Text.Json;
using MarkItDown.Core;

namespace MarkItDown.Converters.Pdf.Tests;

public sealed class GoldenCorpusLabeledQualityTests
{
    [Fact]
    public async Task LabeledRealPdfCases_PassTextRecallAndReadingOrder()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var manifestPath = Path.Combine(root, "tests", "GoldenCorpus", "manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var cases = manifest.RootElement.GetProperty("realCorpus").GetProperty("labeledCases").EnumerateArray().ToArray();
        Assert.Equal(5, cases.Length);

        foreach (var labeledCase in cases)
        {
            var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, labeledCase.GetProperty("path").GetString()!));
            var store = new InMemoryAssetStore();
            var options = new ConversionOptions { PipelineMode = PipelineMode.Multimodal, VisionMode = VisionMode.Off };
            var result = await new MarkItDownEngine(builder => builder.Add(new PdfConverter())).ConvertAsync(new DocumentConversionRequest
            {
                FilePath = path,
                AssetStore = store,
                Options = options,
                Context = new ConversionContext { Pipeline = PipelineMode.Multimodal, Vision = VisionMode.Off, Assets = store, Options = options }
            });

            var expected = new GoldenDocumentExpectation
            {
                RequiredText = labeledCase.GetProperty("requiredText").EnumerateArray().Select(value => value.GetString()!).ToArray(),
                ExpectedOrderedText = labeledCase.GetProperty("orderedText").EnumerateArray().Select(value => value.GetString()!).ToArray()
            };
            var report = DocumentQualityEvaluator.Evaluate(result.Document!, expected, result.Assets.Count);
            Assert.True(report.Passed, $"{path}: {JsonSerializer.Serialize(report)}");
            Assert.Equal(1, report.TextRecall);
            Assert.Equal(1, report.ReadingOrderAccuracy);
        }
    }
}
