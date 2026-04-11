using HtmlAgilityPack;
using MarkItDown.Core;
using System.Net;
using System.Text;

namespace MarkItDown.Converters.Web;

public sealed class WebConverter : BaseConverter
{
    private static readonly HttpClient HttpClient = new();

    private static readonly HashSet<string> ContainerTags =
    [
        "article", "body", "div", "header", "main", "section"
    ];

    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".url" };

    public override IReadOnlySet<string> SupportedMimeTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/html" };

    public override double Priority => 10.0;

    public override bool CanConvert(DocumentConversionRequest request)
    {
        if (!string.IsNullOrEmpty(request.FilePath) &&
            (request.FilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             request.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return base.CanConvert(request);
    }

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = request.FilePath ?? throw new ConversionException("No URL provided for web conversion.");
            var html = await HttpClient.GetStringAsync(url, cancellationToken);

            var document = new HtmlDocument();
            document.LoadHtml(html);

            RemoveNoise(document);

            var title = NormalizeInlineText(
                document.DocumentNode.SelectSingleNode("//title")?.InnerText ?? string.Empty);
            var contentRoot = SelectContentRoot(document);
            var blocks = RenderBlockChildren(contentRoot)
                .Where(block => !string.IsNullOrWhiteSpace(block))
                .ToList();

            if (!string.IsNullOrWhiteSpace(title))
            {
                blocks.Insert(0, $"# {title}");
            }

            var markdown = string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks).Trim();
            return new DocumentConversionResult("Web", markdown, title);
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConversionException("Failed to convert web page to Markdown.", ex);
        }
    }

    private static void RemoveNoise(HtmlDocument document)
    {
        var noiseNodes = document.DocumentNode.SelectNodes("//script|//style|//nav|//footer|//header");
        if (noiseNodes is not null)
        {
            foreach (var node in noiseNodes)
            {
                node.Remove();
            }
        }
    }

    private static HtmlNode SelectContentRoot(HtmlDocument document)
    {
        return document.DocumentNode.SelectSingleNode("//main")
            ?? document.DocumentNode.SelectSingleNode("//article")
            ?? document.DocumentNode.SelectSingleNode("//body")
            ?? document.DocumentNode;
    }

    private static IEnumerable<string> RenderBlockChildren(HtmlNode node)
    {
        foreach (var child in node.ChildNodes)
        {
            foreach (var block in RenderNode(child))
            {
                if (!string.IsNullOrWhiteSpace(block))
                {
                    yield return block.Trim();
                }
            }
        }
    }

    private static IEnumerable<string> RenderNode(HtmlNode node)
    {
        if (node.NodeType is HtmlNodeType.Comment or HtmlNodeType.Text)
        {
            yield break;
        }

        var name = node.Name.ToLowerInvariant();
        switch (name)
        {
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                var heading = RenderHeading(node, int.Parse(name[1..]));
                if (!string.IsNullOrWhiteSpace(heading))
                {
                    yield return heading;
                }

                yield break;
            case "p":
                var paragraph = RenderInlineChildren(node);
                if (!string.IsNullOrWhiteSpace(paragraph))
                {
                    yield return paragraph;
                }

                yield break;
            case "blockquote":
                var quote = RenderBlockquote(node);
                if (!string.IsNullOrWhiteSpace(quote))
                {
                    yield return quote;
                }

                yield break;
            case "pre":
                var code = RenderCodeBlock(node);
                if (!string.IsNullOrWhiteSpace(code))
                {
                    yield return code;
                }

                yield break;
            case "ul":
                var unordered = RenderList(node, false);
                if (!string.IsNullOrWhiteSpace(unordered))
                {
                    yield return unordered;
                }

                yield break;
            case "ol":
                var ordered = RenderList(node, true);
                if (!string.IsNullOrWhiteSpace(ordered))
                {
                    yield return ordered;
                }

                yield break;
            case "table":
                var table = RenderTable(node);
                if (!string.IsNullOrWhiteSpace(table))
                {
                    yield return table;
                }

                yield break;
            case "hr":
                yield return "---";
                yield break;
            default:
                if (ContainerTags.Contains(name))
                {
                    foreach (var block in RenderBlockChildren(node))
                    {
                        yield return block;
                    }

                    yield break;
                }

                var inline = RenderInlineChildren(node);
                if (!string.IsNullOrWhiteSpace(inline))
                {
                    yield return inline;
                }

                yield break;
        }
    }

    private static string RenderHeading(HtmlNode node, int level)
    {
        var text = RenderInlineChildren(node);
        return string.IsNullOrWhiteSpace(text) ? string.Empty : $"{new string('#', level)} {text}";
    }

    private static string RenderBlockquote(HtmlNode node)
    {
        var innerBlocks = RenderBlockChildren(node).ToList();
        if (innerBlocks.Count == 0)
        {
            var text = RenderInlineChildren(node);
            return string.IsNullOrWhiteSpace(text) ? string.Empty : $"> {text}";
        }

        return string.Join(Environment.NewLine, innerBlocks.SelectMany(block =>
            block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => $"> {line}")));
    }

    private static string RenderCodeBlock(HtmlNode node)
    {
        var code = WebUtility.HtmlDecode(node.InnerText).Trim();
        return string.IsNullOrWhiteSpace(code) ? string.Empty : $"```\n{code}\n```";
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
            var content = RenderListItem(items[index]);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var lines = content.Split('\n', StringSplitOptions.None);
            builder.Append(marker);
            builder.AppendLine(lines[0].Trim());

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                builder.Append("  ");
                builder.AppendLine(line.Trim());
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string RenderListItem(HtmlNode item)
    {
        var inlineParts = item.ChildNodes
            .Where(child => child.Name is not "ul" and not "ol")
            .Select(RenderInlineChildren)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        var nestedParts = item.ChildNodes
            .Where(child => child.Name is "ul" or "ol")
            .Select(child => RenderList(child, child.Name.Equals("ol", StringComparison.OrdinalIgnoreCase)))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        return string.Join(Environment.NewLine, inlineParts.Concat(nestedParts)).Trim();
    }

    private static string RenderTable(HtmlNode tableNode)
    {
        var rows = tableNode.SelectNodes(".//tr");
        if (rows is null || rows.Count == 0)
        {
            return string.Empty;
        }

        var data = rows
            .Select(row => row.Elements("th").Concat(row.Elements("td"))
                .Select(cell => EscapePipes(RenderInlineChildren(cell)))
                .Where(cell => !string.IsNullOrWhiteSpace(cell))
                .ToList())
            .Where(cells => cells.Count > 0)
            .ToList();

        if (data.Count == 0)
        {
            return string.Empty;
        }

        var columnCount = data.Max(row => row.Count);
        foreach (var row in data)
        {
            while (row.Count < columnCount)
            {
                row.Add(string.Empty);
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine($"| {string.Join(" | ", data[0])} |");
        builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", columnCount))} |");

        foreach (var row in data.Skip(1))
        {
            builder.AppendLine($"| {string.Join(" | ", row)} |");
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
            return RenderInlineNode(node);
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
            "img" => RenderImage(node),
            "br" => "\n",
            _ => node.HasChildNodes ? RenderInlineChildren(node) : NormalizeInlineText(node.InnerText)
        };
    }

    private static string RenderLink(HtmlNode node)
    {
        var text = RenderInlineChildren(node);
        var href = node.GetAttributeValue("href", string.Empty).Trim();
        if (href.StartsWith('#') && string.IsNullOrWhiteSpace(text.Replace("\u200B", string.Empty, StringComparison.Ordinal)))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(href))
        {
            return text;
        }

        return $"[{(string.IsNullOrWhiteSpace(text) ? href : text)}]({href})";
    }

    private static string RenderImage(HtmlNode node)
    {
        var alt = NormalizeInlineText(node.GetAttributeValue("alt", string.Empty));
        var src = node.GetAttributeValue("src", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(src))
        {
            return alt;
        }

        return string.IsNullOrWhiteSpace(alt) ? $"![]({src})" : $"![{alt}]({src})";
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

    private static string EscapePipes(string value) => value.Replace("|", "\\|");
}
