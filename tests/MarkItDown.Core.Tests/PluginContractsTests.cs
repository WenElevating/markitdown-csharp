using MarkItDown.Core;

namespace MarkItDown.Core.Tests;

public sealed class PluginContractsTests
{
    [Fact]
    public void PluginContext_RegistersOcrProvidersAndRejectsDuplicateIds()
    {
        var context = new MarkItDownPluginContext();
        context.RegisterOcrProvider(new TestOcrProvider("sample"));

        Assert.Single(context.OcrProviders);
        Assert.Equal("sample", context.OcrProviders[0].Id);
        Assert.Throws<InvalidOperationException>(() =>
            context.RegisterOcrProvider(new TestOcrProvider("SAMPLE")));
    }

    private sealed class TestOcrProvider(string id) : IOcrProvider
    {
        public string Id => id;
        public bool IsAvailable(ConversionContext context) => true;
        public Task<OcrDocumentResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
