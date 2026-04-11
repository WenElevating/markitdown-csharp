using System.Net;
using System.Text;
using System.Web;
using HtmlAgilityPack;
using MarkItDown.Core;

namespace MarkItDown.Converters.Web;

public sealed class WikipediaConverter : BaseConverter
{
    private static readonly HttpClient HttpClient = new();

    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public override double Priority => -1.0;

    public override bool CanConvert(DocumentConversionRequest request)
    {
        var path = (request.FilePath ?? request.Filename ?? "").ToLowerInvariant();
        return path.Contains("wikipedia.org/wiki/");
    }

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = request.FilePath
                ?? throw new ConversionException("No URL provided for Wikipedia conversion.");

            var uri = new Uri(url);
            var title = ExtractArticleTitle(uri);
            var lang = ExtractLanguageCode(uri);

            var apiUrl = $"https://{lang}.wikipedia.org/api/rest_v1/page/html/{Uri.EscapeDataString(title.Replace(' ', '_'))}";
            var html = await HttpClient.GetStringAsync(apiUrl, cancellationToken);

            var document = new HtmlDocument();
            document.LoadHtml(html);

            RemoveNoise(document);

            var markdown = ExtractMarkdown(document, title);

            return new DocumentConversionResult("Wikipedia", markdown, title);
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConversionException("Failed to convert Wikipedia page to Markdown.", ex);
        }
    }

    private static string ExtractArticleTitle(Uri uri)
    {
        var segments = uri.Segments;
        // Segments: ["/", "wiki/", "Article_Title"]
        if (segments.Length < 3)
        {
            throw new ConversionException("Could not extract article title from Wikipedia URL.");
        }

        var encodedTitle = string.Join("/", segments[2..]);
        var title = Uri.UnescapeDataString(encodedTitle).TrimEnd('/');
        return title.Replace('_', ' ');
    }

    private static string ExtractLanguageCode(Uri uri)
    {
        var host = uri.Host;
        // host is e.g. "en.wikipedia.org" or "de.wikipedia.org"
        var dotIndex = host.IndexOf('.');
        return dotIndex > 0 ? host[..dotIndex] : "en";
    }

    private static void RemoveNoise(HtmlDocument document)
    {
        var noiseNodes = document.DocumentNode.SelectNodes(
            "//script|//style|//nav|//sup[contains(@class,'reference')]|//span[contains(@class,'mw-editsection')]");
        if (noiseNodes is not null)
        {
            foreach (var node in noiseNodes)
            {
                node.Remove();
            }
        }
    }

    private static string ExtractMarkdown(HtmlDocument document, string title)
    {
        var contentRoot = document.DocumentNode.SelectSingleNode("//body")
            ?? document.DocumentNode;

        var blocks = new List<string>();
        CollectBlocks(contentRoot, blocks);

        var markdown = new StringBuilder();
        markdown.Append("# ");
        markdown.AppendLine(title);
        markdown.AppendLine();

        markdown.Append(string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks));

        return markdown.ToString().Trim();
    }

    private static void CollectBlocks(HtmlNode node, List<string> blocks)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType is HtmlNodeType.Comment or HtmlNodeType.Text)
            {
                continue;
            }

            var name = child.Name.ToLowerInvariant();
            switch (name)
            {
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    var heading = RenderHeading(child, int.Parse(name[1..]));
                    if (!string.IsNullOrWhiteSpace(heading))
                    {
                        blocks.Add(heading);
                    }
                    break;
                case "p":
                    var paragraph = RenderInlineChildren(child);
                    if (!string.IsNullOrWhiteSpace(paragraph))
                    {
                        blocks.Add(paragraph);
                    }
                    break;
                case "ul":
                    var unordered = RenderList(child, ordered: false);
                    if (!string.IsNullOrWhiteSpace(unordered))
                    {
                        blocks.Add(unordered);
                    }
                    break;
                case "ol":
                    var ordered = RenderList(child, ordered: true);
                    if (!string.IsNullOrWhiteSpace(ordered))
                    {
                        blocks.Add(ordered);
                    }
                    break;
                default:
                    CollectBlocks(child, blocks);
                    break;
            }
        }
    }

    private static string RenderHeading(HtmlNode node, int level)
    {
        var text = RenderInlineChildren(node);
        return string.IsNullOrWhiteSpace(text) ? string.Empty : $"{new string('#', level)} {text}";
    }

    private static string RenderList(HtmlNode node, bool ordered)
    {
        var items = node.Elements("li").ToList();
        if (items.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < items.Count; index++)
        {
            var marker = ordered ? $"{index + 1}. " : "- ";
            var content = RenderInlineChildren(items[index]);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            builder.Append(marker);
            builder.AppendLine(content.Trim());
        }

        return builder.ToString().TrimEnd();
    }

    private static string RenderInlineChildren(HtmlNode node)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            return NormalizeInlineText(node.InnerText);
        }

        if (!node.HasChildNodes)
        {
            return NormalizeInlineText(node.InnerText);
        }

        var builder = new StringBuilder();
        foreach (var child in node.ChildNodes)
        {
            var rendered = RenderInlineNode(child);
            if (string.IsNullOrWhiteSpace(rendered))
            {
                continue;
            }

            if (builder.Length > 0 && NeedsInlineSpace(builder[^1], rendered[0]))
            {
                builder.Append(' ');
            }

            builder.Append(rendered);
        }

        return NormalizeInlineText(builder.ToString());
    }

    private static string RenderInlineNode(HtmlNode node)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            return NormalizeInlineText(node.InnerText);
        }

        var name = node.Name.ToLowerInvariant();
        return name switch
        {
            "a" => RenderLink(node),
            "strong" or "b" => WrapInline("**", node),
            "em" or "i" => WrapInline("*", node),
            "code" => $"`{NormalizeInlineText(node.InnerText)}`",
            "br" => "\n",
            _ => node.HasChildNodes ? RenderInlineChildren(node) : NormalizeInlineText(node.InnerText)
        };
    }

    private static string RenderLink(HtmlNode node)
    {
        var text = RenderInlineChildren(node);
        var href = node.GetAttributeValue("href", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(href))
        {
            return text;
        }

        return $"[{(string.IsNullOrWhiteSpace(text) ? href : text)}]({href})";
    }

    private static string WrapInline(string wrapper, HtmlNode node)
    {
        var content = RenderInlineChildren(node);
        return string.IsNullOrWhiteSpace(content) ? string.Empty : $"{wrapper}{content}{wrapper}";
    }

    private static bool NeedsInlineSpace(char previous, char next)
    {
        if (char.IsWhiteSpace(previous) || char.IsWhiteSpace(next))
        {
            return false;
        }

        return previous is not '(' && next is not ')' && next is not ',' && next is not '.';
    }

    private static string NormalizeInlineText(string value)
    {
        return WebUtility.HtmlDecode(value)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Aggregate(new StringBuilder(), (builder, part) =>
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(part);
                return builder;
            })
            .ToString();
    }
}
