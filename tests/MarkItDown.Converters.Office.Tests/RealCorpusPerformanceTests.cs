using System.Diagnostics;
using System.Text.Json;
using MarkItDown.Core;
using Xunit.Abstractions;

namespace MarkItDown.Converters.Office.Tests;

public sealed class RealCorpusPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RealOfficeCorpus_ReportsLatencyDistribution()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var files = Directory.EnumerateFiles(Path.Combine(root, "tests", "Fixtures", "real"), "*.*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".docx" or ".pptx" or ".xlsx")
            .OrderBy(path => path)
            .ToArray();
        Assert.Equal(22, files.Length);

        var samples = new List<LatencySample>();
        foreach (var path in files)
        {
            var extension = Path.GetExtension(path);
            var converter = extension switch
            {
                ".docx" => (IConverter)new DocxConverter(),
                ".pptx" => new PptxConverter(),
                ".xlsx" => new XlsxConverter(),
                _ => throw new ArgumentOutOfRangeException(nameof(path))
            };
            var store = new InMemoryAssetStore();
            var options = new ConversionOptions
            {
                PipelineMode = PipelineMode.Multimodal,
                VisionMode = VisionMode.Off,
                Privacy = new PrivacyOptions { AllowExternalRelationships = true }
            };
            var request = new DocumentConversionRequest
            {
                FilePath = path,
                AssetStore = store,
                Options = options,
                Context = new ConversionContext { Pipeline = PipelineMode.Multimodal, Vision = VisionMode.Off, Assets = store, Options = options }
            };
            var stopwatch = Stopwatch.StartNew();
            var result = await converter.ConvertAsync(request);
            stopwatch.Stop();
            Assert.NotEqual(FidelityStatus.Failed, result.FidelityStatus);
            samples.Add(new LatencySample(extension[1..], stopwatch.Elapsed.TotalMilliseconds));
        }

        var report = samples.GroupBy(sample => sample.Format, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => LatencyReport.Create(group.Select(sample => sample.Milliseconds).ToArray()));
        output.WriteLine(JsonSerializer.Serialize(report));
    }

    private sealed record LatencySample(string Format, double Milliseconds);

    private sealed record LatencyReport(int Count, double P50Ms, double P95Ms, double MaxMs)
    {
        public static LatencyReport Create(double[] values)
        {
            Array.Sort(values);
            static double Percentile(double[] sorted, double percentile) => sorted[Math.Min(sorted.Length - 1, (int)Math.Floor((sorted.Length - 1) * percentile))];
            return new LatencyReport(values.Length, Math.Round(Percentile(values, 0.50), 2), Math.Round(Percentile(values, 0.95), 2), Math.Round(values[^1], 2));
        }
    }
}
