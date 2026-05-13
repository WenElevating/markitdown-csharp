using System.Text;
using NPOI.HWPF;
using NPOI.HWPF.UserModel;

// Alias to avoid ambiguity with System.Range (introduced in .NET 8)
using HwpfRange = NPOI.HWPF.UserModel.Range;

namespace MarkItDown.Converters.Office;

/// <summary>
/// Shared rendering helpers for HWPF (.doc) documents used by both
/// <see cref="DocConverter"/> and the <see cref="DocxConverter"/> OLE2 fallback.
/// </summary>
internal static class DocRendering
{
    // OLE2 Compound Document magic bytes: D0 CF 11 E0 A1 B1 1A E1
    private static readonly byte[] Ole2Magic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>
    /// Returns true when the first 8 bytes of <paramref name="filePath"/> match the OLE2 magic signature.
    /// </summary>
    internal static bool IsOle2File(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[8];
            if (fs.Read(header, 0, 8) < 8) return false;
            return header.SequenceEqual(Ole2Magic);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Converts an <see cref="HWPFDocument"/> to Markdown text.
    /// </summary>
    internal static string RenderDocument(HWPFDocument doc, CancellationToken cancellationToken)
    {
        var range = doc.GetRange();
        var blocks = new List<string>();

        // Tracks the first paragraph index of each table that has already been rendered.
        var renderedTableStarts = new HashSet<int>();

        for (var i = 0; i < range.NumParagraphs; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var para = range.GetParagraph(i);

            if (para.IsInTable())
            {
                // Only render a table once, at the first paragraph of the table
                if (renderedTableStarts.Contains(i))
                    continue;

                try
                {
                    var table = range.GetTable(para);
                    // Mark all paragraphs in this table as processed
                    for (var j = i; j < i + CountTableParagraphs(table); j++)
                        renderedTableStarts.Add(j);

                    var tableMarkdown = RenderHwpfTable(table);
                    if (!string.IsNullOrEmpty(tableMarkdown))
                        blocks.Add(tableMarkdown);
                }
                catch
                {
                    // If table extraction fails, fall back to paragraph text
                    var rendered = RenderHwpfParagraph(para);
                    if (!string.IsNullOrEmpty(rendered))
                        blocks.Add(rendered);
                }
                continue;
            }

            if (renderedTableStarts.Contains(i))
                continue;

            var block = RenderHwpfParagraph(para);
            if (!string.IsNullOrEmpty(block))
                blocks.Add(block);
        }

        return MergeParagraphBlocks(blocks);
    }

    /// <summary>
    /// Renders a single HWPF paragraph to a Markdown string.
    /// </summary>
    internal static string RenderHwpfParagraph(Paragraph para)
    {
        // GetStyleIndex(): 0=Normal, 1=Heading1, ..., 9=Heading9
        var styleIndex = para.GetStyleIndex();
        var text = RenderCharacterRuns(para);

        // Heading styles: Word built-in heading styles are indices 1–9
        if (styleIndex >= 1 && styleIndex <= 9)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return $"{new string('#', styleIndex)} {text.Trim()}";
        }

        // List detection via ANLD (ilfo != 0 means paragraph is in a list)
        if (para.GetIlfo() != 0)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return $"- {text.Trim()}";
        }

        // Regular paragraph
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
    }

    /// <summary>
    /// Renders all character runs within a paragraph with bold/italic Markdown formatting.
    /// </summary>
    internal static string RenderCharacterRuns(Paragraph para)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < para.NumCharacterRuns; i++)
        {
            var run = para.GetCharacterRun(i);
            var text = run.Text;

            // Skip empty or null text
            if (string.IsNullOrEmpty(text)) continue;

            // Filter out non-printable control chars except normal whitespace
            text = FilterControlChars(text);
            if (string.IsNullOrEmpty(text)) continue;

            // IsBold() and IsItalic() are methods in NPOI.HWPFCore
            var isBold = run.IsBold();
            var isItalic = run.IsItalic();

            if (isBold && isItalic)
                sb.Append($"***{text}***");
            else if (isBold)
                sb.Append($"**{text}**");
            else if (isItalic)
                sb.Append($"*{text}*");
            else
                sb.Append(text);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders a table from HWPF table object to Markdown table syntax.
    /// </summary>
    internal static string RenderHwpfTable(Table table)
    {
        var rows = new List<List<string>>();

        for (var r = 0; r < table.NumRows; r++)
        {
            var row = table.GetRow(r);
            var cells = new List<string>();
            for (var c = 0; c < row.NumCells(); c++)
            {
                var cell = row.GetCell(c);
                var cellText = new StringBuilder();
                for (var p = 0; p < cell.NumParagraphs; p++)
                {
                    if (p > 0) cellText.Append(' ');
                    cellText.Append(RenderHwpfParagraph(cell.GetParagraph(p)));
                }
                cells.Add(EscapePipe(cellText.ToString().Trim()));
            }
            rows.Add(cells);
        }

        if (rows.Count == 0) return string.Empty;

        var columnCount = rows.Max(r => r.Count);
        if (columnCount == 0) return string.Empty;

        foreach (var row in rows)
            while (row.Count < columnCount)
                row.Add(string.Empty);

        var sb = new StringBuilder();
        sb.AppendLine($"| {string.Join(" | ", rows[0])} |");
        sb.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", columnCount))} |");
        foreach (var row in rows.Skip(1))
            sb.AppendLine($"| {string.Join(" | ", row)} |");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Counts total paragraphs belonging to a table (for tracking processed indices).
    /// </summary>
    private static int CountTableParagraphs(Table table)
    {
        var count = 0;
        for (var r = 0; r < table.NumRows; r++)
        {
            var row = table.GetRow(r);
            for (var c = 0; c < row.NumCells(); c++)
                count += row.GetCell(c).NumParagraphs;
        }
        return count;
    }

    /// <summary>
    /// Merges paragraph blocks into final Markdown, collapsing repeated blank lines.
    /// </summary>
    private static string MergeParagraphBlocks(List<string> blocks)
    {
        return string.Join(Environment.NewLine + Environment.NewLine,
            blocks.Where(b => !string.IsNullOrWhiteSpace(b))).Trim();
    }

    private static string FilterControlChars(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            // Keep normal chars, spaces, tabs, line feeds
            if (ch >= 0x20 || ch == '\t' || ch == '\n' || ch == '\r')
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string EscapePipe(string value) =>
        value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
