using MarkItDown.Core;

namespace MarkItDown.Converters.Office.Tests;

public sealed class RealDoclingOfficeIntegrationTests
{
    [Fact]
    public async Task RealDoclingWorker_ProcessesAllOfficeFormatsThroughOneTransport()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MARKITDOWN_REAL_DOCLING"), "1", StringComparison.Ordinal))
            throw Xunit.Sdk.SkipException.ForSkip("Set MARKITDOWN_REAL_DOCLING=1 with MARKITDOWN_DOCLING_PYTHON to run the real provider gate.");

        var python = Environment.GetEnvironmentVariable("MARKITDOWN_DOCLING_PYTHON");
        var worker = Environment.GetEnvironmentVariable("MARKITDOWN_DOCLING_WORKER")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "docling_worker.py"));
        if (string.IsNullOrWhiteSpace(python) || !File.Exists(python) || !File.Exists(worker))
            throw Xunit.Sdk.SkipException.ForSkip("The configured Docling Python or worker script is unavailable.");

        await using var transport = new ProcessDoclingTransport(python!, worker, TimeSpan.FromMinutes(4));
        foreach (var format in new[] { "docx", "pptx", "xlsx" })
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
                var result = await converter.ConvertAsync(new DocumentConversionRequest
                {
                    FilePath = path,
                    Context = new ConversionContext
                    {
                        Backend = ConversionBackendMode.Auto,
                        Pipeline = PipelineMode.Multimodal,
                        Vision = VisionMode.Auto,
                        Options = new ConversionOptions
                        {
                            PipelineMode = PipelineMode.Multimodal,
                            VisionMode = VisionMode.Auto,
                            DoclingTransport = transport
                        }
                    }
                });

                Assert.Equal(format switch { "docx" => "Docx", "pptx" => "Pptx", _ => "Xlsx" }, result.Kind);
                Assert.NotNull(result.Document);
                Assert.NotEqual(FidelityStatus.Failed, result.FidelityStatus);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
