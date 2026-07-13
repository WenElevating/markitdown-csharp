using MarkItDown.Core;

namespace MarkItDown.Core.Tests;

public sealed class MarkItDownEngineTests
{
    private sealed class StubConverter : BaseConverter
    {
        private readonly string _kind;
        private readonly string _markdown;
        private readonly Func<DocumentConversionRequest, CancellationToken, Task<DocumentConversionResult>>? _convert;

        public StubConverter(
            string kind,
            string extension,
            string markdown,
            double priority = 0.0,
            Func<DocumentConversionRequest, CancellationToken, Task<DocumentConversionResult>>? convert = null)
        {
            _kind = kind;
            _markdown = markdown;
            _convert = convert;
            SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { extension };
            SupportedMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Priority = priority;
        }

        public override IReadOnlySet<string> SupportedExtensions { get; }
        public override IReadOnlySet<string> SupportedMimeTypes { get; }
        public override double Priority { get; }
        public override Task<DocumentConversionResult> ConvertAsync(
            DocumentConversionRequest request, CancellationToken ct)
            => _convert?.Invoke(request, ct)
                ?? Task.FromResult(new DocumentConversionResult(_kind, _markdown));
    }

    [Fact]
    public async Task ConvertAsync_FilePath_ConvertsSuccessfully()
    {
        var engine = new MarkItDownEngine(builder => builder
            .Add(new StubConverter("Html", ".html", "# Hello")));

        var result = await engine.ConvertAsync(
            new DocumentConversionRequest { FilePath = "test.html" });

        Assert.Equal("# Hello", result.Markdown);
        Assert.Equal("Html", result.Kind);
    }

    [Fact]
    public async Task ConvertAsync_StreamWithFilename_ConvertsSuccessfully()
    {
        var engine = new MarkItDownEngine(builder => builder
            .Add(new StubConverter("Html", ".html", "# Stream")));

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<h1>Hi</h1>"));
        var result = await engine.ConvertAsync(stream, "test.html");

        Assert.Equal("# Stream", result.Markdown);
    }

    [Fact]
    public async Task ConvertAsync_ReportsUnavailableOcrProviderAsPartialDiagnostic()
    {
        var engine = new MarkItDownEngine(builder => builder
            .Add(new StubConverter("Html", ".html", "# Content")));
        var context = new ConversionContext
        {
            Properties = new Dictionary<string, object?>
            {
                ["ocrProviderStatus"] = "OCR_PROVIDER_UNAVAILABLE: no installed OCR provider is available."
            }
        };

        var result = await engine.ConvertAsync(new DocumentConversionRequest
        {
            FilePath = "test.html",
            Context = context
        });

        Assert.Equal(FidelityStatus.Partial, result.FidelityStatus);
        Assert.Contains(result.Diagnostics!, diagnostic => diagnostic.Code == "OCR_PROVIDER_UNAVAILABLE");
    }

    [Fact]
    public async Task ConvertAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var engine = new MarkItDownEngine(builder => { });

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            engine.ConvertAsync("nonexistent.html"));
    }

    [Fact]
    public async Task ConvertAsync_UnsupportedFormat_ThrowsUnsupportedFormatException()
    {
        var engine = new MarkItDownEngine(builder => builder
            .Add(new StubConverter("Html", ".html", "x")));

        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<UnsupportedFormatException>(() =>
            engine.ConvertAsync(stream, "test.docx"));
    }

    [Fact]
    public async Task ConvertAsync_StreamWithoutHints_ThrowsArgumentException()
    {
        var engine = new MarkItDownEngine(builder => { });

        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.ConvertAsync(stream));
    }

    [Fact]
    public async Task ConvertAsync_WhenConverterCancels_PreservesOperationCanceledException()
    {
        var engine = new MarkItDownEngine(builder => builder
            .Add(new StubConverter("Html", ".html", "x", convert: (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Expected cancellation.");
            })));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            engine.ConvertAsync(
                new DocumentConversionRequest { FilePath = "test.html" },
                cts.Token));
    }

    [Fact]
    public void CreateWithAllConverters_ReturnsEngineWithRegisteredConverters()
    {
        var engine = MarkItDownEngine.CreateWithAllConverters();
        Assert.NotNull(engine);
    }
}
