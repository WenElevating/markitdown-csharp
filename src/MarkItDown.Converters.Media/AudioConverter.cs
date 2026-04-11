using System.Text;
using MarkItDown.Core;

namespace MarkItDown.Converters.Media;

public sealed class AudioConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".m4a" };

    public override IReadOnlySet<string> SupportedMimeTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "audio/mpeg", "audio/x-wav", "audio/mp4", "audio/x-m4a"
        };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = request.FilePath
                ?? throw new ConversionException("FilePath is required for audio conversion.");

            cancellationToken.ThrowIfCancellationRequested();

            using var file = TagLib.File.Create(filePath);
            var tag = file.Tag;
            var properties = file.Properties;

            var markdown = new StringBuilder();

            if (properties is not null)
            {
                markdown.AppendLine($"Duration: {properties.Duration}");
                markdown.AppendLine($"MediaTypes: {properties.MediaTypes}");
                markdown.AppendLine($"AudioBitrate: {properties.AudioBitrate}");
                markdown.AppendLine($"AudioSampleRate: {properties.AudioSampleRate}");
                markdown.AppendLine($"AudioChannels: {properties.AudioChannels}");
            }

            if (tag is not null)
            {
                if (!string.IsNullOrEmpty(tag.Title))
                {
                    markdown.AppendLine($"Title: {tag.Title}");
                }

                if (tag.Performers is { Length: > 0 })
                {
                    markdown.AppendLine($"Artist: {string.Join(", ", tag.Performers)}");
                }

                if (!string.IsNullOrEmpty(tag.Album))
                {
                    markdown.AppendLine($"Album: {tag.Album}");
                }

                if (tag.Genres is { Length: > 0 })
                {
                    markdown.AppendLine($"Genre: {string.Join(", ", tag.Genres)}");
                }

                if (tag.Track > 0)
                {
                    markdown.AppendLine($"Track: {tag.Track}");
                }

                if (tag.Year > 0)
                {
                    markdown.AppendLine($"Year: {tag.Year}");
                }
            }

            return new DocumentConversionResult("Audio", markdown.ToString().Trim());
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConversionException("Failed to convert audio to Markdown.", ex);
        }
    }
}
