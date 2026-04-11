using MarkItDown.Core;
using MarkItDown.Converters.Web;

namespace MarkItDown.Converters.Web.Tests;

public sealed class WebConverterTests
{
    private readonly WebConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsHttpUrl()
    {
        var request = new DocumentConversionRequest { FilePath = "http://example.com/page" };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_AcceptsHttpsUrl()
    {
        var request = new DocumentConversionRequest { FilePath = "https://example.com/page" };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_AcceptsUrlExtension()
    {
        var request = new DocumentConversionRequest { FilePath = "/path/to/bookmark.url" };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_RejectsNonWebPaths()
    {
        var request = new DocumentConversionRequest { FilePath = "document.pdf" };
        Assert.False(_converter.CanConvert(request));
    }
}
