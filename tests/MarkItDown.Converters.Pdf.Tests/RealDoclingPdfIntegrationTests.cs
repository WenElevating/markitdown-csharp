using MarkItDown.Core;

namespace MarkItDown.Converters.Pdf.Tests;

public sealed class RealDoclingPdfIntegrationTests
{
    [Fact]
    public async Task RealDoclingWorker_ProcessesScannedPdf()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MARKITDOWN_REAL_DOCLING"), "1", StringComparison.Ordinal))
            throw Xunit.Sdk.SkipException.ForSkip("Set MARKITDOWN_REAL_DOCLING=1 with MARKITDOWN_DOCLING_PYTHON to run the real provider gate.");

        var python = Environment.GetEnvironmentVariable("MARKITDOWN_DOCLING_PYTHON");
        var worker = Environment.GetEnvironmentVariable("MARKITDOWN_DOCLING_WORKER")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "docling_worker.py"));
        if (string.IsNullOrWhiteSpace(python) || !File.Exists(python) || !File.Exists(worker))
            throw Xunit.Sdk.SkipException.ForSkip("The configured Docling Python or worker script is unavailable.");

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine(root, "tests", "Fixtures", "real", "markitdown-ocr", "pdf_scanned_minimal.pdf");
        await using var transport = new ProcessDoclingTransport(python!, worker, TimeSpan.FromMinutes(4));
        var options = new ConversionOptions
        {
            PipelineMode = PipelineMode.Multimodal,
            VisionMode = VisionMode.Auto,
            DoclingTransport = transport
        };
        var result = await new PdfConverter().ConvertAsync(new DocumentConversionRequest
        {
            FilePath = path,
            Options = options,
            Context = new ConversionContext
            {
                Backend = ConversionBackendMode.Auto,
                Pipeline = PipelineMode.Multimodal,
                Vision = VisionMode.Auto,
                Options = options
            }
        });

        Assert.Equal("Pdf", result.Kind);
        Assert.NotNull(result.Document);
        Assert.NotEqual(FidelityStatus.Failed, result.FidelityStatus);
        Assert.False(string.IsNullOrWhiteSpace(result.Markdown));
    }
}
