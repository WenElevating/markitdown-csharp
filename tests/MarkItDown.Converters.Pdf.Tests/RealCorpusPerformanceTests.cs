using System.Diagnostics;
using System.Text.Json;
using MarkItDown.Core;
using Xunit.Abstractions;

namespace MarkItDown.Converters.Pdf.Tests;

public sealed class RealCorpusPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RealPdfCorpus_ReportsLatencyDistribution()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var files = Directory.EnumerateFiles(Path.Combine(root, "tests", "Fixtures", "real"), "*.pdf", SearchOption.AllDirectories)
            .OrderBy(path => path)
            .ToArray();
        Assert.Equal(18, files.Length);

        var durations = new List<double>();
        foreach (var path in files)
        {
            var store = new InMemoryAssetStore();
            var options = new ConversionOptions { PipelineMode = PipelineMode.Multimodal, VisionMode = VisionMode.Off };
            var request = new DocumentConversionRequest
            {
                FilePath = path,
                AssetStore = store,
                Options = options,
                Context = new ConversionContext { Pipeline = PipelineMode.Multimodal, Vision = VisionMode.Off, Assets = store, Options = options }
            };
            var stopwatch = Stopwatch.StartNew();
            var result = await new PdfConverter().ConvertAsync(request);
            stopwatch.Stop();
            Assert.NotEqual(FidelityStatus.Failed, result.FidelityStatus);
            durations.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        durations.Sort();
        static double Percentile(List<double> sorted, double percentile) => sorted[Math.Min(sorted.Count - 1, (int)Math.Floor((sorted.Count - 1) * percentile))];
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Count = durations.Count,
            P50Ms = Math.Round(Percentile(durations, 0.50), 2),
            P95Ms = Math.Round(Percentile(durations, 0.95), 2),
            MaxMs = Math.Round(durations[^1], 2)
        }));
    }
}
