namespace MarkItDown.Converters.Html.Tests;

internal static class FixturePath
{
    public static string For(string fileName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var root = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(root, "tests", "Fixtures", fileName);
    }
}
