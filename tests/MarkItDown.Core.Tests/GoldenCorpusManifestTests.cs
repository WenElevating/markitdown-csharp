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
        foreach (var entry in entries)
        {
            var relative = entry.GetProperty("path").GetString();
            Assert.False(string.IsNullOrWhiteSpace(relative));
            Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, relative!))), relative);
            Assert.NotEmpty(entry.GetProperty("features").EnumerateArray());
        }
    }
}
