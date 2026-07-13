using System.IO.Compression;
using MarkItDown.Core;

namespace MarkItDown.Converters.Office.Tests;

public sealed class OfficePackageGuardTests
{
    [Fact]
    public void Validate_RejectsZipPathTraversal()
    {
        var path = CreateZip((archive, _) => archive.CreateEntry("../escape.txt"));
        try
        {
            var exception = Assert.Throws<ConversionException>(() => OfficePackageGuard.Validate(path, new ConversionLimits()));
            Assert.Contains("path", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Validate_RejectsExternalRelationshipsByDefault()
    {
        var path = CreateZip((archive, _) =>
        {
            var entry = archive.CreateEntry("word/_rels/document.xml.rels");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<Relationships><Relationship TargetMode=\"External\" Target=\"https://example.test\" /></Relationships>");
        });
        try
        {
            var exception = Assert.Throws<ConversionException>(() => OfficePackageGuard.Validate(path, new ConversionLimits()));
            Assert.Contains("external", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Validate_RejectsTooManyEntries()
    {
        var path = CreateZip((archive, _) =>
        {
            for (var index = 0; index < 3; index++) archive.CreateEntry($"part{index}.xml");
        });
        try
        {
            Assert.Throws<ConversionException>(() => OfficePackageGuard.Validate(
                path, new ConversionLimits { MaxPackageEntries = 2 }));
        }
        finally { File.Delete(path); }
    }

    private static string CreateZip(Action<ZipArchive, string> populate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"office-guard-{Guid.NewGuid():N}.zip");
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        populate(archive, path);
        return path;
    }
}
