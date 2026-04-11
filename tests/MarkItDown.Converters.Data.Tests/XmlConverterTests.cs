using MarkItDown.Core;
using MarkItDown.Converters.Data;

namespace MarkItDown.Converters.Data.Tests;

public sealed class XmlConverterTests
{
    private readonly XmlConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsXmlExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "data.xml" }));
    }

    [Fact]
    public async Task ConvertAsync_WrapsInCodeFence()
    {
        var xml = "<root><item key=\"a\">Value A</item><item key=\"b\">Value B</item></root>";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(path, xml);

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("```xml", result.Markdown);
            Assert.Contains("<root>", result.Markdown);
            Assert.Contains("Value A", result.Markdown);
            Assert.Equal("Xml", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_FormatsWithIndentation()
    {
        var xml = "<root><child>text</child></root>";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(path, xml);

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains(Environment.NewLine, result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
