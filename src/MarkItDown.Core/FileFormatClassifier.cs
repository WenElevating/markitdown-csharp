namespace MarkItDown.Core;

public sealed class FileFormatClassifier
{
    public DocumentKind? Classify(string filePath)
    {
        var extension = Path.GetExtension(filePath)?.ToLowerInvariant();

        return extension switch
        {
            ".html" or ".htm" => DocumentKind.Html,
            ".pdf" => DocumentKind.Pdf,
            _ => null
        };
    }
}
