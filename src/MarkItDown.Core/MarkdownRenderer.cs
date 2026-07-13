using System.Text;

namespace MarkItDown.Core;

public static class MarkdownRenderer
{
    public static string Render(DocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var output = new StringBuilder();
        foreach (var block in model.Blocks)
        {
            if (output.Length > 0) output.AppendLine().AppendLine();
            RenderBlock(output, block);
        }

        foreach (var supplement in model.Supplements ?? [])
        {
            if (supplement.Blocks.Count == 0) continue;
            if (output.Length > 0) output.AppendLine().AppendLine();
            output.Append("## ").Append(supplement.Kind.Trim()).AppendLine();
            foreach (var block in supplement.Blocks)
            {
                output.AppendLine();
                RenderBlock(output, block);
            }
        }
        return output.ToString().Trim();
    }

    private static void RenderBlock(StringBuilder output, DocumentBlock block)
    {
        var kind = block.Kind.ToLowerInvariant();
        switch (kind)
        {
            case "heading":
                var level = block.Attributes is not null && block.Attributes.TryGetValue("level", out var levelText) && int.TryParse(levelText, out var parsedLevel)
                    ? Math.Clamp(parsedLevel, 1, 6) : 1;
                output.Append(new string('#', level)).Append(' ').Append(block.Text.Trim());
                break;
            case "diagnostic":
                output.Append("> [!WARNING] ").Append(block.Text.Trim());
                break;
            case "pagebreak":
                output.Append("---");
                break;
            default:
                output.Append(block.Text.Trim());
                break;
        }
    }
}
