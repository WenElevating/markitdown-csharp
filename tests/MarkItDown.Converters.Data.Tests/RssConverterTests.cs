using MarkItDown.Core;
using MarkItDown.Converters.Data;

namespace MarkItDown.Converters.Data.Tests;

public sealed class RssConverterTests
{
    private readonly RssConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsRssExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "feed.rss" }));
    }

    [Fact]
    public void CanConvert_AcceptsAtomExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "feed.atom" }));
    }

    [Fact]
    public async Task ConvertAsync_ExtractsRssFeedItems()
    {
        var path = CreateRssFeed();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("# Tech News", result.Markdown);
            Assert.Contains("## First Article", result.Markdown);
            Assert.Contains("## Second Article", result.Markdown);
            Assert.Equal("Rss", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_ExtractsAtomFeedItems()
    {
        var path = CreateAtomFeed();

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Contains("# Atom Blog", result.Markdown);
            Assert.Contains("## Atom Entry", result.Markdown);
            Assert.Equal("Rss", result.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateRssFeed()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
  <channel>
    <title>Tech News</title>
    <description>Latest tech news</description>
    <item>
      <title>First Article</title>
      <description>Description of the first article</description>
      <pubDate>Mon, 01 Jan 2024 00:00:00 GMT</pubDate>
    </item>
    <item>
      <title>Second Article</title>
      <description>Description of the second article</description>
      <pubDate>Tue, 02 Jan 2024 00:00:00 GMT</pubDate>
    </item>
  </channel>
</rss>";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.rss");
        File.WriteAllText(path, xml);
        return path;
    }

    private static string CreateAtomFeed()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<feed xmlns=""http://www.w3.org/2005/Atom"">
  <title>Atom Blog</title>
  <subtitle>An atom feed</subtitle>
  <entry>
    <title>Atom Entry</title>
    <summary>Summary of the entry</summary>
    <updated>2024-01-01T00:00:00Z</updated>
  </entry>
</feed>";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.atom");
        File.WriteAllText(path, xml);
        return path;
    }
}
