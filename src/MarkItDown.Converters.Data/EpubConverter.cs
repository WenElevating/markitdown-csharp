using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class EpubConverter : BaseConverter
{
    private static readonly XNamespace OpfNs = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace ContainerNs = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";

    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".epub" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/epub+zip" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("EPUB converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                using var archive = ZipFile.OpenRead(filePath);

                var opfPath = FindOpfPath(archive);

                var opfEntry = archive.GetEntry(opfPath)
                    ?? throw new ConversionException($"OPF file not found: {opfPath}");
                var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? "";

                XDocument opfDoc;
                using (var opfStream = opfEntry.Open())
                    opfDoc = XDocument.Load(opfStream);

                var builder = new StringBuilder();
                RenderMetadata(opfDoc, builder);
                builder.AppendLine();

                var manifest = GetManifest(opfDoc);
                var spineIds = GetSpineIds(opfDoc);

                foreach (var id in spineIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!manifest.TryGetValue(id, out var href)) continue;

                    var entryPath = string.IsNullOrEmpty(opfDir)
                        ? href : $"{opfDir}/{href}";

                    var entry = archive.GetEntry(entryPath);
                    if (entry is null) continue;

                    using var stream = entry.Open();
                    var chapterText = ExtractHtmlText(stream);
                    if (!string.IsNullOrWhiteSpace(chapterText))
                    {
                        builder.AppendLine(chapterText);
                        builder.AppendLine();
                    }
                }

                var markdown = builder.ToString().TrimEnd();
                return new DocumentConversionResult("Epub", markdown);
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert EPUB: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    private static string FindOpfPath(ZipArchive archive)
    {
        var containerEntry = archive.GetEntry("META-INF/container.xml")
            ?? throw new ConversionException("Not a valid EPUB: missing META-INF/container.xml.");

        using var stream = containerEntry.Open();
        var doc = XDocument.Load(stream);

        var rootFile = doc.Descendants(ContainerNs + "rootfile").FirstOrDefault()
            ?? doc.Descendants("rootfile").FirstOrDefault()
            ?? throw new ConversionException("No rootfile found in container.xml.");

        return rootFile.Attribute("full-path")?.Value
            ?? throw new ConversionException("No full-path in rootfile element.");
    }

    private static void RenderMetadata(XDocument opfDoc, StringBuilder builder)
    {
        var title = opfDoc.Descendants(DcNs + "title").FirstOrDefault()?.Value
            ?? opfDoc.Descendants("title").FirstOrDefault()?.Value;

        var author = opfDoc.Descendants(DcNs + "creator").FirstOrDefault()?.Value
            ?? opfDoc.Descendants("creator").FirstOrDefault()?.Value;

        var language = opfDoc.Descendants(DcNs + "language").FirstOrDefault()?.Value
            ?? opfDoc.Descendants("language").FirstOrDefault()?.Value;

        if (title is not null)
            builder.AppendLine($"**Title:** {title}");
        if (author is not null)
            builder.AppendLine($"**Author:** {author}");
        if (language is not null)
            builder.AppendLine($"**Language:** {language}");
    }

    private static Dictionary<string, string> GetManifest(XDocument opfDoc)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var items = opfDoc.Descendants(OpfNs + "item")
            .Concat(opfDoc.Descendants("item"));

        foreach (var item in items)
        {
            var id = item.Attribute("id")?.Value;
            var href = item.Attribute("href")?.Value;
            if (id is not null && href is not null)
                result[id] = href;
        }

        return result;
    }

    private static List<string> GetSpineIds(XDocument opfDoc)
    {
        return opfDoc.Descendants(OpfNs + "itemref")
            .Concat(opfDoc.Descendants("itemref"))
            .Select(e => e.Attribute("idref")?.Value)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();
    }

    private static string ExtractHtmlText(Stream stream)
    {
        try
        {
            var doc = XDocument.Load(stream);
            var body = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "body");

            if (body is null) return "";

            var builder = new StringBuilder();
            RenderHtmlElement(body, builder);
            return builder.ToString().TrimEnd();
        }
        catch
        {
            return "";
        }
    }

    private static void RenderHtmlElement(XElement element, StringBuilder builder)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                builder.Append(text.Value);
            }
            else if (node is XElement child)
            {
                var tag = child.Name.LocalName.ToLowerInvariant();
                switch (tag)
                {
                    case "h1":
                        builder.AppendLine();
                        builder.AppendLine($"# {child.Value.Trim()}");
                        break;
                    case "h2":
                        builder.AppendLine();
                        builder.AppendLine($"## {child.Value.Trim()}");
                        break;
                    case "h3":
                        builder.AppendLine();
                        builder.AppendLine($"### {child.Value.Trim()}");
                        break;
                    case "p":
                        builder.AppendLine();
                        builder.Append(child.Value.Trim());
                        builder.AppendLine();
                        break;
                    case "li":
                        builder.AppendLine($"- {child.Value.Trim()}");
                        break;
                    case "br":
                        builder.AppendLine();
                        break;
                    default:
                        RenderHtmlElement(child, builder);
                        break;
                }
            }
        }
    }
}
