using MarkItDown.Core;

namespace MarkItDown.Core.Tests;

public sealed class ConverterRegistryTests
{
    private sealed class FakeHtmlConverter : BaseConverter
    {
        public override IReadOnlySet<string> SupportedExtensions =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".html" };
        public override IReadOnlySet<string> SupportedMimeTypes =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/html" };
        public override Task<DocumentConversionResult> ConvertAsync(
            DocumentConversionRequest request, CancellationToken ct)
            => Task.FromResult(new DocumentConversionResult("Html", "fake"));
    }

    private sealed class FakePdfConverter : BaseConverter
    {
        public override IReadOnlySet<string> SupportedExtensions =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };
        public override IReadOnlySet<string> SupportedMimeTypes =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/pdf" };
        public override double Priority => 10.0;
        public override Task<DocumentConversionResult> ConvertAsync(
            DocumentConversionRequest request, CancellationToken ct)
            => Task.FromResult(new DocumentConversionResult("Pdf", "fake"));
    }

    [Fact]
    public void FindConverter_ReturnsConverterForMatchingExtension()
    {
        var registry = new ConverterRegistryBuilder()
            .Add(new FakeHtmlConverter())
            .Build();

        var converter = registry.FindConverter(
            new DocumentConversionRequest { FilePath = "test.html" });

        Assert.NotNull(converter);
        Assert.IsType<FakeHtmlConverter>(converter);
    }

    [Fact]
    public void FindConverter_ReturnsHighestPriorityConverter()
    {
        var registry = new ConverterRegistryBuilder()
            .Add(new FakePdfConverter())  // Priority 10.0
            .Add(new FakeHtmlConverter()) // Priority 0.0
            .Build();

        var allConverters = registry.GetAllConverters().ToList();
        Assert.Equal(0.0, allConverters[0].Priority);
        Assert.Equal(10.0, allConverters[1].Priority);
    }

    [Fact]
    public void FindConverter_ReturnsNullWhenNoMatch()
    {
        var registry = new ConverterRegistryBuilder()
            .Add(new FakeHtmlConverter())
            .Build();

        var converter = registry.FindConverter(
            new DocumentConversionRequest { FilePath = "test.docx" });

        Assert.Null(converter);
    }

    [Fact]
    public void Build_ReturnsImmutableRegistry()
    {
        var builder = new ConverterRegistryBuilder()
            .Add(new FakeHtmlConverter());

        var registry = builder.Build();
        Assert.Single(registry.GetAllConverters());
    }

    [Fact]
    public void AddFromAssembly_DiscoversConverters()
    {
        var registry = new ConverterRegistryBuilder()
            .AddFromAssembly(typeof(ConverterRegistryTests).Assembly)
            .Build();

        Assert.True(registry.GetAllConverters().Count() >= 2);
    }
}
