namespace MarkItDown.Core;

public static class DocumentModelBuilder
{
    public static DocumentModel FromMarkdown(string kind, string markdown, FidelityStatus fidelity, IReadOnlyList<ConversionDiagnostic>? diagnostics = null)
    {
        var blocks = new List<DocumentBlock>();
        foreach (var section in markdown.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var text = section.Trim();
            if (text.Length == 0) continue;
            var headingLength = text.TakeWhile(c => c == '#').Count();
            var blockKind = headingLength is >= 1 and <= 6 && text.Length > headingLength && char.IsWhiteSpace(text[headingLength]) ? "heading"
                : text.StartsWith("![", StringComparison.Ordinal) ? "figure"
                : text.StartsWith("| ", StringComparison.Ordinal) ? "table"
                : text.StartsWith("- ", StringComparison.Ordinal) ? "list"
                : text == "---" ? "pagebreak" : "paragraph";
            blocks.Add(new DocumentBlock(blockKind, blockKind == "heading" ? text[headingLength..].Trim() : text,
                Attributes: blockKind == "heading" ? new Dictionary<string, string> { ["level"] = headingLength.ToString() } : null));
        }
        if (diagnostics is not null)
            blocks.AddRange(diagnostics.Select(d => new DocumentBlock("diagnostic", d.Message,
                Attributes: new Dictionary<string, string> { ["code"] = d.Code, ["severity"] = d.Severity.ToString() })));
        return new DocumentModel(kind, blocks, Diagnostics: diagnostics, Fidelity: fidelity);
    }
}
