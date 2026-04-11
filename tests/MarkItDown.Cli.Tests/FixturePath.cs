namespace MarkItDown.Cli.Tests;

internal static class FixturePath
{
    public static string RepositoryRoot
    {
        get
        {
            var baseDirectory = AppContext.BaseDirectory;
            return Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", ".."));
        }
    }

    public static string For(string fileName)
    {
        return Path.Combine(RepositoryRoot, "tests", "Fixtures", fileName);
    }
}
