using MarkItDown.Core;
using MarkItDown.Converters.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MarkItDown.Converters.Media.Tests;

public sealed class ImageConverterTests
{
    private readonly ImageConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsJpgExtension()
    {
        var request = new DocumentConversionRequest { FilePath = "test.jpg" };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_AcceptsJpegExtension()
    {
        var request = new DocumentConversionRequest { FilePath = "test.jpeg" };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_AcceptsPngExtension()
    {
        var request = new DocumentConversionRequest { FilePath = "test.png" };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public async Task ConvertAsync_ExtractsImageSize()
    {
        var path = CreateTestPng(100, 50);
        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Equal("Image", result.Kind);
            Assert.Contains("ImageSize: 100x50", result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_WorksForJpeg()
    {
        var path = CreateTestJpeg(200, 100);
        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Equal("Image", result.Kind);
            Assert.Contains("ImageSize: 200x100", result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTestPng(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        using var image = new Image<Rgba32>(width, height);
        image.SaveAsPng(path);
        return path;
    }

    private static string CreateTestJpeg(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jpg");
        using var image = new Image<Rgba32>(width, height);
        image.SaveAsJpeg(path);
        return path;
    }
}
