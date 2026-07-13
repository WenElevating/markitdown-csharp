using System.Globalization;
using System.Text.Json;

namespace MarkItDown.Core;

public static class DoclingDocumentAdapter
{
    public static DocumentModel Adapt(string json, string kind = "Docling")
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var entries = new Dictionary<string, (string Kind, JsonElement Value)>(StringComparer.Ordinal);
        AddEntries(root, "texts", "paragraph", entries);
        AddEntries(root, "tables", "table", entries);
        AddEntries(root, "pictures", "figure", entries);

        var blocks = new List<DocumentBlock>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object)
            EmitChildren(body, root, entries, emitted, blocks);

        foreach (var entry in entries)
        {
            if (emitted.Add(entry.Key))
                AddBlock(entry.Key, entry.Value.Kind, entry.Value.Value, root, blocks);
        }

        return new DocumentModel(kind, blocks, Fidelity: FidelityStatus.Partial);
    }

    private static void AddEntries(
        JsonElement root,
        string collectionName,
        string defaultKind,
        IDictionary<string, (string Kind, JsonElement Value)> entries)
    {
        if (!root.TryGetProperty(collectionName, out var collection) || collection.ValueKind != JsonValueKind.Array)
            return;

        var index = 0;
        foreach (var item in collection.EnumerateArray())
        {
            var reference = item.TryGetProperty("self_ref", out var selfRef) && selfRef.ValueKind == JsonValueKind.String
                ? selfRef.GetString()!
                : $"#/{collectionName}/{index}";
            entries[reference] = (defaultKind, item);
            index++;
        }
    }

    private static void EmitChildren(
        JsonElement parent,
        JsonElement root,
        IReadOnlyDictionary<string, (string Kind, JsonElement Value)> entries,
        ISet<string> emitted,
        ICollection<DocumentBlock> blocks)
    {
        if (!parent.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
            return;

        foreach (var child in children.EnumerateArray())
        {
            if (!child.TryGetProperty("$ref", out var reference) || reference.ValueKind != JsonValueKind.String)
                continue;
            var referenceValue = reference.GetString()!;
            if (!entries.TryGetValue(referenceValue, out var entry))
            {
                if (TryResolve(root, referenceValue, out var nested))
                    EmitChildren(nested, root, entries, emitted, blocks);
                continue;
            }

            if (emitted.Add(referenceValue))
                AddBlock(referenceValue, entry.Kind, entry.Value, root, blocks);
            EmitChildren(entry.Value, root, entries, emitted, blocks);
        }
    }

    private static void AddBlock(
        string reference,
        string defaultKind,
        JsonElement item,
        JsonElement root,
        ICollection<DocumentBlock> blocks)
    {
        var text = ReadText(item, defaultKind);
        if (string.IsNullOrWhiteSpace(text)) return;

        var label = ReadString(item, "label");
        var kind = KindFor(label, defaultKind);
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (kind == "heading")
        {
            var level = ReadInt(item, "level") ?? 1;
            attributes["level"] = Math.Clamp(level, 1, 6).ToString(CultureInfo.InvariantCulture);
        }

        blocks.Add(new DocumentBlock(
            kind,
            text,
            ReadSource(item, root),
            attributes.Count == 0 ? null : attributes));
    }

    private static string ReadText(JsonElement item, string defaultKind)
    {
        foreach (var property in defaultKind == "table"
                     ? new[] { "text", "caption_text", "caption", "data" }
                     : defaultKind == "figure"
                         ? new[] { "caption_text", "caption", "text", "orig" }
                         : new[] { "text", "orig" })
        {
            if (!item.TryGetProperty(property, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? string.Empty;
            if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                return value.GetRawText();
        }
        return string.Empty;
    }

    private static string KindFor(string? label, string defaultKind) => label?.ToLowerInvariant() switch
    {
        "section_header" or "title" => "heading",
        "list_item" or "list" => "list",
        "footnote" or "page_footer" or "page_header" => "note",
        "picture" or "figure" => "figure",
        "table" => "table",
        _ => defaultKind
    };

    private static SourceLocation? ReadSource(JsonElement item, JsonElement root)
    {
        if (!item.TryGetProperty("prov", out var provenance) || provenance.ValueKind != JsonValueKind.Array)
            return null;
        var first = provenance.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.Object) return null;

        var page = ReadInt(first, "page_no");
        var bbox = first.TryGetProperty("bbox", out var bboxElement) && bboxElement.ValueKind == JsonValueKind.Object
            ? bboxElement : default;
        var dimensions = page is not null && root.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Object
            ? ReadPageDimensions(pages, page.Value) : (null, null);

        return new SourceLocation(
            Page: page,
            Part: ReadString(first, "component_type"),
            PageWidth: dimensions.Width,
            PageHeight: dimensions.Height,
            Left: ReadDouble(bbox, "l"),
            Top: ReadDouble(bbox, "t"),
            Right: ReadDouble(bbox, "r"),
            Bottom: ReadDouble(bbox, "b"));
    }

    private static (double? Width, double? Height) ReadPageDimensions(JsonElement pages, int page)
    {
        if (!pages.TryGetProperty(page.ToString(CultureInfo.InvariantCulture), out var pageElement))
            return (null, null);
        if (!pageElement.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Object)
            return (null, null);
        return (ReadDouble(size, "width"), ReadDouble(size, "height"));
    }

    private static bool TryResolve(JsonElement root, string reference, out JsonElement value)
    {
        value = default;
        if (!reference.StartsWith("#/", StringComparison.Ordinal)) return false;
        var current = root;
        foreach (var segment in reference[2..].Split('/'))
        {
            if (!current.TryGetProperty(segment, out current)) return false;
        }
        value = current;
        return true;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static int? ReadInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var integer)
            ? integer : int.TryParse(value.ToString(), out integer) ? integer : null;
    }

    private static double? ReadDouble(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number : double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : null;
    }
}
