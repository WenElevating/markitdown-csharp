using MarkItDown.Core;
using MarkItDown.Converters.Web;

namespace MarkItDown.Converters.Web.Tests;

public sealed class WikipediaConverterTests
{
    private readonly WikipediaConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsWikipediaUrl()
    {
        var request = new DocumentConversionRequest
        {
            FilePath = "https://en.wikipedia.org/wiki/C_Sharp_(programming_language)"
        };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_RejectsNonWikipediaUrl()
    {
        var request = new DocumentConversionRequest
        {
            FilePath = "https://example.com/page"
        };
        Assert.False(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_RejectsFilePath()
    {
        var request = new DocumentConversionRequest
        {
            FilePath = "document.pdf"
        };
        Assert.False(_converter.CanConvert(request));
    }
}
