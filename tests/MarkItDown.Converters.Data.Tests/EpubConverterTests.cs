using System.IO.Compression;
using MarkItDown.Core;
using MarkItDown.Converters.Data;

namespace MarkItDown.Converters.Data.Tests;

public sealed class EpubConverterTests
{
    private readonly EpubConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsEpubExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "book.epub" }));
    }

    [Fact]
    public async Task ConvertAsync_ExtractsMetadataAndChapters()
    {
        var path = CreateTestEpub();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("**Title:** Test Book", result.Markdown);
            Assert.Contains("**Author:** John Doe", result.Markdown);
            Assert.Contains("## Chapter 1", result.Markdown);
            Assert.Contains("Hello world", result.Markdown);
            Assert.Equal("Epub", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTestEpub()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.epub");

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var mimetypeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(mimetypeEntry.Open()))
                writer.Write("application/epub+zip");

            var containerXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"">
  <rootfiles>
    <rootfile full-path=""OEBPS/content.opf"" media-type=""application/oebps-package+xml""/>
  </rootfiles>
</container>";
            var containerEntry = zip.CreateEntry("META-INF/container.xml");
            using (var writer = new StreamWriter(containerEntry.Open()))
                writer.Write(containerXml);

            var opfXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<package xmlns=""http://www.idpf.org/2007/opf"" version=""3.0"">
  <metadata xmlns:dc=""http://purl.org/dc/elements/1.1/"">
    <dc:title>Test Book</dc:title>
    <dc:creator>John Doe</dc:creator>
    <dc:language>en</dc:language>
  </metadata>
  <manifest>
    <item id=""chapter1"" href=""chapter1.xhtml"" media-type=""application/xhtml+xml""/>
    <item id=""nav"" href=""nav.xhtml"" media-type=""application/xhtml+xml""/>
  </manifest>
  <spine>
    <itemref idref=""chapter1""/>
  </spine>
</package>";
            var opfEntry = zip.CreateEntry("OEBPS/content.opf");
            using (var writer = new StreamWriter(opfEntry.Open()))
                writer.Write(opfXml);

            var chapter1 = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<head><title>Chapter 1</title></head>
<body>
  <h2>Chapter 1</h2>
  <p>Hello world from the test EPUB.</p>
</body>
</html>";
            var chapterEntry = zip.CreateEntry("OEBPS/chapter1.xhtml");
            using (var writer = new StreamWriter(chapterEntry.Open()))
                writer.Write(chapter1);
        }

        return path;
    }
}
