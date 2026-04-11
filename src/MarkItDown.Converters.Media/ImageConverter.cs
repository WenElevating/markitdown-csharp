using System.Text;
using MarkItDown.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace MarkItDown.Converters.Media;

public sealed class ImageConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    public override IReadOnlySet<string> SupportedMimeTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = request.FilePath
                ?? throw new ConversionException("FilePath is required for image conversion.");

            using var image = Image.Load(filePath);
            var markdown = new StringBuilder();

            markdown.AppendLine($"ImageSize: {image.Width}x{image.Height}");

            AppendExifMetadata(image, markdown);

            if (request.LlmClient is not null)
            {
                var mimeType = GetMimeType(filePath);
                var imageData = await File.ReadAllBytesAsync(filePath, cancellationToken);
                var caption = await request.LlmClient.CompleteAsync(
                    "Write a detailed caption for this image.",
                    imageData,
                    mimeType,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(caption))
                {
                    markdown.AppendLine();
                    markdown.AppendLine("# Description:");
                    markdown.AppendLine(caption.Trim());
                }
            }

            return new DocumentConversionResult("Image", markdown.ToString().Trim());
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConversionException("Failed to convert image to Markdown.", ex);
        }
    }

    private static void AppendExifMetadata(Image image, StringBuilder markdown)
    {
        var exif = image.Metadata.ExifProfile;
        if (exif is null)
        {
            return;
        }

        var title = GetExifValue(exif, ExifTag.ImageDescription);
        var artist = GetExifValue(exif, ExifTag.Artist);
        var dateTaken = GetExifValue(exif, ExifTag.DateTimeOriginal);
        var copyright = GetExifValue(exif, ExifTag.Copyright);

        var hasMetadata = !string.IsNullOrEmpty(title)
                          || !string.IsNullOrEmpty(artist)
                          || !string.IsNullOrEmpty(dateTaken)
                          || !string.IsNullOrEmpty(copyright);

        if (!hasMetadata)
        {
            return;
        }

        markdown.AppendLine();

        if (!string.IsNullOrEmpty(title))
        {
            markdown.AppendLine($"Title: {title}");
        }

        if (!string.IsNullOrEmpty(artist))
        {
            markdown.AppendLine($"Artist: {artist}");
        }

        if (!string.IsNullOrEmpty(dateTaken))
        {
            markdown.AppendLine($"DateTaken: {dateTaken}");
        }

        if (!string.IsNullOrEmpty(copyright))
        {
            markdown.AppendLine($"Copyright: {copyright}");
        }
    }

    private static string? GetExifValue(ExifProfile exif, ExifTag<string> tag)
    {
        return exif.TryGetValue(tag, out var value) ? value.Value : null;
    }

    private static string GetMimeType(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";
    }
}
