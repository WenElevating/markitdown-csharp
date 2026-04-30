using System.IO;

namespace MarkItDown.Core.Tests;

public sealed class BaseConverterTests
{
    private sealed class TestConverter : BaseConverter
    {
        public override IReadOnlySet<string> SupportedExtensions { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".html", ".htm" };

        public override IReadOnlySet<string> SupportedMimeTypes { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/html" };

        public override Task<DocumentConversionResult> ConvertAsync(
            DocumentConversionRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DocumentConversionResult("Html", "test markdown"));
        }
    }

    private readonly TestConverter _converter = new();

    [Fact]
    public void CanConvert_MatchesByExtension()
    {
        var request = new DocumentConversionRequest
        {
            FilePath = "/some/path/document.html",
            Stream = Stream.Null
        };

        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_MatchesByFilename()
    {
        var request = new DocumentConversionRequest
        {
            Filename = "page.htm",
            Stream = Stream.Null
        };

        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_MatchesByMimeType()
    {
        var request = new DocumentConversionRequest
        {
            MimeType = "text/html; charset=utf-8",
            Stream = Stream.Null
        };

        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_DoesNotMatchMimeTypePrefixOnly()
    {
        var request = new DocumentConversionRequest
        {
            MimeType = "text/html-malicious",
            Stream = Stream.Null
        };

        Assert.False(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_ReturnsFalseForUnknownExtension()
    {
        var request = new DocumentConversionRequest
        {
            FilePath = "/some/path/document.pdf",
            Stream = Stream.Null
        };

        Assert.False(_converter.CanConvert(request));
    }

    [Fact]
    public void Priority_DefaultIsZero()
    {
        Assert.Equal(0.0, _converter.Priority);
    }
}
