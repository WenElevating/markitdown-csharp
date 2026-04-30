namespace MarkItDown.Core.Tests;

public sealed class FileSystemBoundaryTests
{
    [Fact]
    public void FindDuplicateOutput_ReturnsConflictingOutputPathForSameStems()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var inputs = new[]
        {
            Path.Combine("first", "same.md"),
            Path.Combine("second", "same.html")
        };

        var duplicateOutput = FileSystemBoundary.FindDuplicateOutput(inputs, outputDir);

        Assert.Equal(Path.GetFullPath(Path.Combine(outputDir, "same.md")), duplicateOutput);
    }

    [Fact]
    public void FindDuplicateOutput_ReturnsNullForUniqueStems()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var inputs = new[]
        {
            Path.Combine("first", "one.md"),
            Path.Combine("second", "two.html")
        };

        var duplicateOutput = FileSystemBoundary.FindDuplicateOutput(inputs, outputDir);

        Assert.Null(duplicateOutput);
    }

    [Fact]
    public void IsPathWithinRoot_AllowsRootItself()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.True(FileSystemBoundary.IsPathWithinRoot(root, root));
    }

    [Fact]
    public void IsPathWithinRoot_AllowsDescendantPath()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var child = Path.Combine(root, "nested", "file.md");

        Assert.True(FileSystemBoundary.IsPathWithinRoot(child, root));
    }

    [Fact]
    public void IsPathWithinRoot_RejectsSiblingWithSamePrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "allowed");
        var sibling = Path.Combine(Path.GetTempPath(), "allowed-but-not-really", "file.md");

        Assert.False(FileSystemBoundary.IsPathWithinRoot(sibling, root));
    }

    [Fact]
    public void IsPathWithinRoot_UsesPlatformCaseSensitivity()
    {
        var root = Path.Combine(Path.GetTempPath(), "allowed");
        var differentCase = Path.Combine(Path.GetTempPath(), "ALLOWED", "secret.md");

        var result = FileSystemBoundary.IsPathWithinRoot(differentCase, root);

        Assert.Equal(OperatingSystem.IsWindows(), result);
    }
}
