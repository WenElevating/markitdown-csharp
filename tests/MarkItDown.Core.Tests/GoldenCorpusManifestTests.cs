using System.Text.Json;

namespace MarkItDown.Core.Tests;

public sealed class GoldenCorpusManifestTests
{
    [Fact]
    public void GoldenCorpusManifest_IsValidAndReferencesExistingFixtures()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var manifestPath = Path.Combine(root, "tests", "GoldenCorpus", "manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.Equal("1", document.RootElement.GetProperty("schemaVersion").GetString());
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.NotEmpty(entries);
        var thresholds = document.RootElement.GetProperty("qualityThresholds");
        Assert.Equal(1, thresholds.GetProperty("minTextRecall").GetDouble());
        Assert.Equal(0, thresholds.GetProperty("maxUnexplainedContentLoss").GetInt32());
        foreach (var entry in entries)
        {
            if (entry.TryGetProperty("path", out var pathElement))
            {
                var relative = pathElement.GetString();
                Assert.False(string.IsNullOrWhiteSpace(relative));
                Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, relative!))), relative);
            }
            else
            {
                Assert.Equal("generated-office-smoke", entry.GetProperty("factory").GetString());
            }
            Assert.NotEmpty(entry.GetProperty("features").EnumerateArray());
            var expected = entry.GetProperty("expected");
            Assert.True(expected.TryGetProperty("requiredText", out _));
            Assert.True(expected.TryGetProperty("requiredHeadings", out _));
            Assert.True(expected.TryGetProperty("requiredTableCells", out _));
        }
    }
}
