using System.Text;
using System.Xml.Linq;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class RssConverter : BaseConverter
{
    private static readonly XNamespace AtomNs = "http://www.w3.org/2005/Atom";

    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".rss", ".atom" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/rss+xml", "application/atom+xml"
        };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("RSS converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var doc = XDocument.Parse(content);
                var root = doc.Root ?? throw new ConversionException("Empty RSS/Atom document.");

                var builder = new StringBuilder();

                if (root.Name.LocalName == "rss" || root.Name.LocalName == "Rss")
                {
                    RenderRss(root, builder);
                }
                else if (root.Name.LocalName == "feed")
                {
                    RenderAtom(root, builder);
                }
                else
                {
                    throw new ConversionException(
                        $"Unrecognized feed format: {root.Name.LocalName}");
                }

                var markdown = builder.ToString().TrimEnd();
                return new DocumentConversionResult("Rss", markdown);
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert RSS: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    private static void RenderRss(XElement root, StringBuilder builder)
    {
        var channel = root.Element("channel");
        if (channel is null) return;

        var title = channel.Element("title")?.Value ?? "Untitled Feed";
        var description = channel.Element("description")?.Value;

        builder.AppendLine($"# {title}");
        if (!string.IsNullOrWhiteSpace(description))
            builder.AppendLine(description);
        builder.AppendLine();

        foreach (var item in channel.Elements("item"))
        {
            var itemTitle = item.Element("title")?.Value;
            if (itemTitle is not null)
                builder.AppendLine($"## {itemTitle}");

            var date = item.Element("pubDate")?.Value;
            if (date is not null)
                builder.AppendLine($"Published: {date}");

            var desc = item.Element("description")?.Value;
            if (!string.IsNullOrWhiteSpace(desc))
                builder.AppendLine(desc);

            builder.AppendLine();
        }
    }

    private static void RenderAtom(XElement root, StringBuilder builder)
    {
        var title = root.Element(AtomNs + "title")?.Value
            ?? root.Element("title")?.Value
            ?? "Untitled Feed";
        var subtitle = root.Element(AtomNs + "subtitle")?.Value
            ?? root.Element("subtitle")?.Value;

        builder.AppendLine($"# {title}");
        if (!string.IsNullOrWhiteSpace(subtitle))
            builder.AppendLine(subtitle);
        builder.AppendLine();

        foreach (var entry in root.Elements(AtomNs + "entry").Concat(root.Elements("entry")))
        {
            var entryTitle = entry.Element(AtomNs + "title")?.Value
                ?? entry.Element("title")?.Value;
            if (entryTitle is not null)
                builder.AppendLine($"## {entryTitle}");

            var updated = entry.Element(AtomNs + "updated")?.Value
                ?? entry.Element("updated")?.Value;
            if (updated is not null)
                builder.AppendLine($"Updated: {updated}");

            var summary = entry.Element(AtomNs + "summary")?.Value
                ?? entry.Element("summary")?.Value;
            if (!string.IsNullOrWhiteSpace(summary))
                builder.AppendLine(summary);

            builder.AppendLine();
        }
    }
}
