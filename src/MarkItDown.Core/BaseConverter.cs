namespace MarkItDown.Core;

public abstract class BaseConverter : IConverter
{
    public abstract IReadOnlySet<string> SupportedExtensions { get; }
    public abstract IReadOnlySet<string> SupportedMimeTypes { get; }
    public virtual double Priority => 0.0;

    public virtual bool CanConvert(DocumentConversionRequest request)
    {
        var extension = GetExtension(request);
        if (extension is not null && SupportedExtensions.Contains(extension))
        {
            return true;
        }

        var mimeType = request.MimeType;
        if (mimeType is not null)
        {
            foreach (var supported in SupportedMimeTypes)
            {
                if (mimeType.StartsWith(supported, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public abstract Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken = default);

    private static string? GetExtension(DocumentConversionRequest request)
    {
        if (!string.IsNullOrEmpty(request.FilePath))
        {
            return Path.GetExtension(request.FilePath);
        }

        if (!string.IsNullOrEmpty(request.Filename))
        {
            return Path.GetExtension(request.Filename);
        }

        return null;
    }
}
