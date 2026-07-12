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
            var kind = block.Kind.ToLowerInvariant();
            switch (kind)
            {
                case "heading":
                    var level = block.Attributes is not null && block.Attributes.TryGetValue("level", out var levelText) && int.TryParse(levelText, out var parsedLevel)
                        ? Math.Clamp(parsedLevel, 1, 6) : 1;
                    output.Append(new string('#', level)).Append(' ').Append(block.Text.Trim());
                    break;
                case "paragraph":
                case "list":
                case "table":
                case "equation":
                case "note":
                case "figure":
                    output.Append(block.Text.Trim());
                    break;
                case "pagebreak":
                    output.Append("---");
                    break;
                case "diagnostic":
                    output.Append("> [!WARNING] ").Append(block.Text.Trim());
                    break;
                default:
                    output.Append(block.Text.Trim());
                    break;
            }
        }
        return output.ToString().Trim();
    }
}
