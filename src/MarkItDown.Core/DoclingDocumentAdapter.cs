using System.Text.Json;

namespace MarkItDown.Core;

public static class DoclingDocumentAdapter
{
    public static DocumentModel Adapt(string json, string kind = "Docling")
    {
        using var document = JsonDocument.Parse(json);
        var blocks = new List<DocumentBlock>();
        AddTextBlocks(document.RootElement, "texts", "text", "paragraph", blocks);
        AddTextBlocks(document.RootElement, "tables", "data", "table", blocks);
        AddTextBlocks(document.RootElement, "pictures", "caption", "image", blocks);
        return new DocumentModel(kind, blocks, Fidelity: FidelityStatus.Partial);
    }

    private static void AddTextBlocks(JsonElement root, string collectionName, string valueName, string kind, ICollection<DocumentBlock> blocks)
    {
        if (!root.TryGetProperty(collectionName, out var collection) || collection.ValueKind != JsonValueKind.Array) return;
        foreach (var item in collection.EnumerateArray())
        {
            if (!item.TryGetProperty(valueName, out var value)) continue;
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            if (!string.IsNullOrWhiteSpace(text)) blocks.Add(new DocumentBlock(kind, text));
        }
    }
}
