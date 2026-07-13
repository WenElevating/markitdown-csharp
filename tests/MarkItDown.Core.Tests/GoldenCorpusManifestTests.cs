using System.Text.Json;
using System.IO.Compression;

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
        var realCorpus = document.RootElement.GetProperty("realCorpus");
        var noticePath = Path.Combine(Path.GetDirectoryName(manifestPath)!, realCorpus.GetProperty("licenseNotice").GetString()!);
        Assert.True(File.Exists(noticePath));
        var totalRealFiles = 0;
        foreach (var set in realCorpus.GetProperty("sets").EnumerateArray())
        {
            var directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, set.GetProperty("path").GetString()!));
            Assert.True(Directory.Exists(directory), directory);
            foreach (var count in set.GetProperty("counts").EnumerateObject())
            {
                var files = Directory.EnumerateFiles(directory, $"*.{count.Name}", SearchOption.TopDirectoryOnly).ToArray();
                Assert.Equal(count.Value.GetInt32(), files.Length);
                totalRealFiles += files.Length;
            }
        }
        Assert.Equal(40, totalRealFiles);
        foreach (var check in realCorpus.GetProperty("packageChecks").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, check.GetProperty("path").GetString()!));
            using var archive = ZipFile.OpenRead(path);
            var archiveEntries = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var requiredEntry in check.GetProperty("requiredEntries").EnumerateArray())
                Assert.Contains(requiredEntry.GetString()!, archiveEntries);
            Assert.NotEmpty(check.GetProperty("features").EnumerateArray());
        }
        foreach (var labeledCase in realCorpus.GetProperty("labeledCases").EnumerateArray())
        {
            var inputPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, labeledCase.GetProperty("path").GetString()!));
            var labelPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, labeledCase.GetProperty("label").GetString()!));
            Assert.True(File.Exists(inputPath), inputPath);
            Assert.True(File.Exists(labelPath), labelPath);
            Assert.Equal("pdf", labeledCase.GetProperty("format").GetString());
            Assert.NotEmpty(labeledCase.GetProperty("requiredText").EnumerateArray());
            Assert.NotEmpty(labeledCase.GetProperty("orderedText").EnumerateArray());
        }
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
