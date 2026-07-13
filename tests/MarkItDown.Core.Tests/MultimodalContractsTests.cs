using System.Text;
using MarkItDown.Core;

namespace MarkItDown.Core.Tests;

public sealed class MultimodalContractsTests
{
    [Fact]
    public void ConversionContext_UsesSafeDefaults()
    {
        var context = new ConversionContext();

        Assert.Equal(ConversionBackendMode.Auto, context.Backend);
        Assert.Equal(VisionMode.Auto, context.Vision);
        Assert.Equal(TimeSpan.FromMinutes(5), context.Timeout);
        Assert.Null(context.MaxPages);
        Assert.Null(context.MaxBytes);
        Assert.Equal(PrivacyMode.LocalOnly, context.Privacy);
    }

    [Fact]
    public async Task FileAssetStore_DeduplicatesBySha256AndRollsBackTransaction()
    {
        var root = Path.Combine(Path.GetTempPath(), "markitdown-assets-" + Guid.NewGuid());
        try
        {
            var store = new FileAssetStore(root);
            await using (var transaction = store.BeginTransaction())
            {
                var first = await transaction.PutAsync(
                    new MemoryStream(Encoding.UTF8.GetBytes("same")), "one.txt", "text/plain");
                var second = await transaction.PutAsync(
                    new MemoryStream(Encoding.UTF8.GetBytes("same")), "two.txt", "text/plain");

                Assert.Equal(first.Id, second.Id);
                Assert.Equal(first.Sha256, second.Sha256);
                await transaction.CommitAsync();
            }

            Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));

            await using (var transaction = store.BeginTransaction())
            {
                await transaction.PutAsync(
                    new MemoryStream(Encoding.UTF8.GetBytes("rollback")), "rollback.txt", "text/plain");
            }

            Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DoclingProtocol_RoundTripsVersionedJsonLine()
    {
        var request = new DoclingRequest("request-1", "1", "document.pdf", "application/pdf", "base64");

        var json = DoclingProtocol.Serialize(request);
        var parsed = DoclingProtocol.DeserializeRequest(json);

        Assert.Equal(request, parsed);
        Assert.Contains("\"protocolVersion\":\"1\"", json);
    }

    [Fact]
    public async Task Engine_ProvidesContextAndEnforcesByteQuota()
    {
        ConversionContext? received = null;
        var engine = new MarkItDownEngine(builder => builder.Add(new StubConverter(
            ".txt", convert: (request, _) =>
            {
                received = request.Context;
                return Task.FromResult(new DocumentConversionResult("Text", "ok"));
            })));

        var result = await engine.ConvertAsync(new DocumentConversionRequest
        {
            FilePath = "test.txt",
            Context = new ConversionContext { MaxBytes = 10 }
        });

        Assert.Equal("ok", result.Markdown);
        Assert.NotNull(received);
        Assert.Equal(10, received!.MaxBytes);
    }

    [Fact]
    public async Task Engine_RejectsMultimodalOnlyOptionsOnLegacyPipeline()
    {
        var engine = new MarkItDownEngine(builder => builder.Add(new StubConverter(
            ".txt", convert: (_, _) => Task.FromResult(new DocumentConversionResult("Text", "ok")))));

        await Assert.ThrowsAsync<ArgumentException>(() => engine.ConvertAsync(new DocumentConversionRequest
        {
            FilePath = "test.txt",
            Options = new ConversionOptions { VisionMode = VisionMode.Off }
        }));
    }

    [Fact]
    public async Task Engine_RejectsAssetStoreAndAssetBasePathTogether()
    {
        var engine = new MarkItDownEngine(builder => builder.Add(new StubConverter(
            ".txt", convert: (_, _) => Task.FromResult(new DocumentConversionResult("Text", "ok")))));

        await Assert.ThrowsAsync<ArgumentException>(() => engine.ConvertAsync(new DocumentConversionRequest
        {
            FilePath = "test.txt",
            AssetBasePath = "assets",
            AssetStore = new InMemoryAssetStore(),
            Options = new ConversionOptions { PipelineMode = PipelineMode.Multimodal }
        }));
    }

    [Fact]
    public void MarkdownRenderer_RendersTypedBlocksAndVisibleDiagnostics()
    {
        var model = new DocumentModel("Docx", new[]
        {
            new DocumentBlock("heading", "Title"),
            new DocumentBlock("paragraph", "Body"),
            new DocumentBlock("diagnostic", "Missing figure", Attributes: new Dictionary<string, string> { ["severity"] = "error" })
        });

        var markdown = MarkdownRenderer.Render(model);

        Assert.Contains("# Title", markdown);
        Assert.Contains("Body", markdown);
        Assert.Contains("> [!WARNING] Missing figure", markdown);
    }

    [Fact]
    public async Task Engine_MultimodalUnsupportedFormatIsNotEvaluatedWithDiagnostic()
    {
        var engine = new MarkItDownEngine(builder => builder.Add(new StubConverter(
            ".txt", convert: (_, _) => Task.FromResult(new DocumentConversionResult("Text", "slide")))));

        var result = await engine.ConvertAsync(new DocumentConversionRequest
        {
            FilePath = "test.txt",
            Options = new ConversionOptions { PipelineMode = PipelineMode.Multimodal }
        });

        Assert.Equal(FidelityStatus.NotEvaluated, result.FidelityStatus);
        Assert.Contains(result.Diagnostics!, d => d.Code == "MULTIMODAL_FORMAT_UNSUPPORTED");
    }

    [Fact]
    public async Task Engine_MultimodalOfficeResultRemainsEvaluated()
    {
        var engine = new MarkItDownEngine(builder => builder.Add(new StubConverter(
            ".pptx", convert: (_, _) => Task.FromResult(new DocumentConversionResult(
                "Pptx", "slide", Fidelity: FidelityStatus.Complete)))));

        var result = await engine.ConvertAsync(new DocumentConversionRequest
        {
            FilePath = "test.pptx",
            Options = new ConversionOptions { PipelineMode = PipelineMode.Multimodal }
        });

        Assert.Equal(FidelityStatus.Complete, result.FidelityStatus);
        Assert.DoesNotContain(result.Diagnostics ?? [], d => d.Code == "MULTIMODAL_FORMAT_UNSUPPORTED");
    }

    [Fact]
    public void DocumentInputMaterializer_CopiesStreamAndDeletesTemporaryFile()
    {
        using var materialized = DocumentInputMaterializer.Materialize(new DocumentConversionRequest
        {
            Stream = new MemoryStream(Encoding.UTF8.GetBytes("content")),
            Filename = "input.txt",
            Context = new ConversionContext { Options = new ConversionOptions { Limits = new ConversionLimits { MaxInputBytes = 100 } } }
        });

        Assert.True(File.Exists(materialized.FilePath));
        Assert.Equal("content", File.ReadAllText(materialized.FilePath));
        var path = materialized.FilePath;
        materialized.Dispose();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Engine_MultimodalFailureIncludesStableFailureReport()
    {
        var engine = new MarkItDownEngine(builder => builder.Add(new StubConverter(
            ".pdf", convert: (_, _) => throw new ConversionException("broken"))));

        var exception = await Assert.ThrowsAsync<ConversionException>(() => engine.ConvertAsync(new DocumentConversionRequest
        {
            FilePath = "test.pdf",
            Options = new ConversionOptions { PipelineMode = PipelineMode.Multimodal }
        }));

        Assert.NotNull(exception.FailureReport);
        Assert.Equal(FidelityStatus.Failed, exception.FailureReport!.Status);
        Assert.NotEmpty(exception.FailureReport.Diagnostics);
    }

    [Fact]
    public async Task Engine_RejectsDocumentUploadWhenVisionIsOff()
    {
        var engine = new MarkItDownEngine(builder => builder.Add(new StubConverter(
            ".pdf", convert: (_, _) => Task.FromResult(new DocumentConversionResult("Pdf", "ok")))));

        await Assert.ThrowsAsync<ArgumentException>(() => engine.ConvertAsync(new DocumentConversionRequest
        {
            FilePath = "test.pdf",
            Options = new ConversionOptions
            {
                PipelineMode = PipelineMode.Multimodal,
                VisionMode = VisionMode.Off,
                Privacy = new PrivacyOptions { AllowDocumentUpload = true }
            }
        }));
    }

    [Fact]
    public async Task AssetPublication_UsesOwnerScopedImmutableDirectoryAndCrossProcessLock()
    {
        var root = Path.Combine(Path.GetTempPath(), "markitdown-publication-" + Guid.NewGuid());
        try
        {
            using var first = AssetPublication.Acquire(root, Path.Combine(root, "out.md"));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                using var ignored = AssetPublication.Acquire(root, Path.Combine(root, "out.md"), cts.Token);
                await Task.CompletedTask;
            });
            Assert.StartsWith(Path.Combine(root, first.OwnerId), first.AssetDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(first.PublicationId, Guid.Empty.ToString("N"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LimitedAssetTransaction_RollsBackWhenAssetExceedsQuota()
    {
        var store = new InMemoryAssetStore();
        await using var transaction = new LimitedAssetTransaction(
            store.BeginTransaction(), new ConversionLimits { MaxAssetBytes = 2 });

        await Assert.ThrowsAsync<ConversionException>(() => transaction.PutAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("too large")), "x.txt", "text/plain"));
        Assert.Empty(store.Assets);
    }

    [Fact]
    public async Task DoclingDocumentConverter_AdaptsStructuredResponseWithoutUsingMarkdownPassthrough()
    {
        var path = Path.Combine(Path.GetTempPath(), $"docling-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        try
        {
            var transport = new FakeDoclingTransport();
            var result = await DoclingDocumentConverter.ConvertFileAsync(
                "Pdf",
                new DocumentConversionRequest { FilePath = path, MimeType = "application/pdf" },
                transport);

            Assert.Contains("Recovered text", result.Markdown);
            Assert.NotNull(result.Document);
            Assert.Equal("paragraph", result.Document!.Blocks[0].Kind);
            Assert.Equal(3, result.Usage.InputBytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DoclingDocumentAdapter_PreservesBodyOrderHeadingKindAndSourceLocation()
    {
        const string json = """
            {
              "body": { "children": [{ "$ref": "#/texts/1" }, { "$ref": "#/texts/0" }] },
              "texts": [
                { "self_ref": "#/texts/0", "label": "text", "text": "body", "prov": [{ "page_no": 2, "bbox": { "l": 1, "t": 2, "r": 3, "b": 4 } }] },
                { "self_ref": "#/texts/1", "label": "section_header", "text": "Title", "level": 2, "prov": [{ "page_no": 1 }] }
              ],
              "pages": { "1": { "size": { "width": 600, "height": 800 } } }
            }
            """;

        var model = DoclingDocumentAdapter.Adapt(json, "Pptx");

        Assert.Equal(["heading", "paragraph"], model.Blocks.Select(block => block.Kind));
        Assert.Equal("Title", model.Blocks[0].Text);
        Assert.Equal("2", model.Blocks[0].Attributes!["level"]);
        Assert.Equal(1, model.Blocks[0].Source!.Page);
        Assert.Equal(2, model.Blocks[1].Source!.Page);
    }

    private sealed class FakeDoclingTransport : IDoclingTransport
    {
        public Task<DoclingResponse> SendAsync(DoclingRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DoclingResponse(request.RequestId, "1", true,
                "{\"texts\":[{\"text\":\"Recovered text\"}]}"));
    }

    private sealed class StubConverter(
        string extension,
        Func<DocumentConversionRequest, CancellationToken, Task<DocumentConversionResult>> convert)
        : BaseConverter
    {
        public override IReadOnlySet<string> SupportedExtensions { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { extension };
        public override IReadOnlySet<string> SupportedMimeTypes { get; } = new HashSet<string>();
        public override Task<DocumentConversionResult> ConvertAsync(DocumentConversionRequest request, CancellationToken cancellationToken = default) => convert(request, cancellationToken);
    }
}
