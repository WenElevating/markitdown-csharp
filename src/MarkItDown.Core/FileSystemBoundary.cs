namespace MarkItDown.Core;

public static class FileSystemBoundary
{
    public static string? FindDuplicateOutput(IEnumerable<string> inputPaths, string outputPath)
    {
        return inputPaths
            .Select(inputPath => BuildOutputFilePath(inputPath, outputPath))
            .GroupBy(path => Path.GetFullPath(path), PathComparer)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
    }

    public static string BuildOutputFilePath(string inputPath, string outputPath)
    {
        return Path.Combine(outputPath, Path.GetFileNameWithoutExtension(inputPath) + ".md");
    }

    public static bool IsPathWithinRoot(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = NormalizeRoot(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;

        return normalizedPath.Equals(normalizedRoot, PathComparison)
            || normalizedPath.StartsWith(rootWithSeparator, PathComparison);
    }

    public static string NormalizeRoot(string root)
    {
        return Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    internal static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
