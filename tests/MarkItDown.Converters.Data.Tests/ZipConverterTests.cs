using System.IO.Compression;
using MarkItDown.Core;
using MarkItDown.Converters.Data;

namespace MarkItDown.Converters.Data.Tests;

public sealed class ZipConverterTests
{
    private readonly ZipConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsZipExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "archive.zip" }));
    }

    [Fact]
    public async Task ConvertAsync_RecursivelyConvertsFiles()
    {
        var path = CreateTestZip();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains(".zip", result.Markdown);
            Assert.Contains("## File: readme.md", result.Markdown);
            Assert.Contains("Hello from ZIP", result.Markdown);
            Assert.Equal("Zip", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_SkipsDirectories()
    {
        var path = CreateTestZip();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.DoesNotContain("## File: subdir/", result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTestZip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var readmeEntry = zip.CreateEntry("readme.md");
            using (var writer = new StreamWriter(readmeEntry.Open()))
                writer.Write("# Hello from ZIP\n\nThis was inside a zip file.");

            var dataEntry = zip.CreateEntry("data.json");
            using (var writer = new StreamWriter(dataEntry.Open()))
                writer.Write("{\"key\": \"value\"}");

            var dirEntry = zip.CreateEntry("subdir/");
        }

        return path;
    }
}
