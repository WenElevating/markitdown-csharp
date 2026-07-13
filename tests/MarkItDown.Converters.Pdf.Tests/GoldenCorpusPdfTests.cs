using MarkItDown.Core;

namespace MarkItDown.Converters.Pdf.Tests;

public sealed class GoldenCorpusPdfTests
{
    [Fact]
    public async Task RealPdfCorpus_ConvertsWithoutSilentFailure()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var corpusRoot = Path.Combine(root, "tests", "Fixtures", "real");
        var files = Directory.EnumerateFiles(corpusRoot, "*.pdf", SearchOption.AllDirectories).OrderBy(path => path).ToArray();
        Assert.Equal(18, files.Length);

        foreach (var path in files)
        {
            var store = new InMemoryAssetStore();
            var converter = new PdfConverter();
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

            Assert.NotNull(result.Document);
            Assert.False(string.IsNullOrWhiteSpace(result.Markdown), path);
            Assert.DoesNotContain(result.Diagnostics ?? [], diagnostic => diagnostic.Code == "CONVERSION_FAILED");
        }
    }
}
