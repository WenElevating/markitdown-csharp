using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics;

namespace MarkItDown.Converters.Pdf;

internal static class PdfTextClassifier
{
    internal static List<PdfTextBlock> ClassifyTextBlocks(Page page)
    {
        var letters = page.Letters;
        if (letters.Count == 0)
        {
            return [];
        }

        var rows = GroupWordsIntoRows(page);
        var blocks = new List<PdfTextBlock>();

        foreach (var row in rows)
        {
            var text = BuildRowText(row);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var rowFontSize = row.Average(w => w.Letters.Average(l => l.FontSize));
            var top = row.Max(w => w.BoundingBox.Top);
            var bottom = row.Min(w => w.BoundingBox.Bottom);
            var y = (top + bottom) / 2.0;

            blocks.Add(new PdfTextBlock(
                Y: y,
                Top: top,
                Bottom: bottom,
                Text: text,
                FontSize: rowFontSize));
        }

        return blocks;
    }

    internal static double ComputeBodyFontSize(IReadOnlyList<Letter> letters)
    {
        return letters
            .Select(l => Math.Round(l.FontSize, 1))
            .GroupBy(size => size)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;
    }

    internal static List<List<Word>> GroupWordsIntoRows(Page page)
    {
        return page.GetWords()
            .GroupBy(word => Math.Round(word.BoundingBox.Bottom, 1))
            .OrderByDescending(group => group.Key)
            .Select(group => group.OrderBy(word => word.BoundingBox.Left).ToList())
            .ToList();
    }

    internal static string BuildRowText(List<Word> row)
    {
        var builder = new StringBuilder();
        Word? previous = null;

        foreach (var word in row)
        {
            if (previous is not null)
            {
                var gap = word.BoundingBox.Left - previous.BoundingBox.Right;
                var previousWidth = previous.BoundingBox.Width;
                var approximateChars = Math.Max(previous.Text.Length, 1);
                var averageCharWidth = Math.Max(previousWidth / approximateChars, 1);
                var spaces = gap <= averageCharWidth * 3
                    ? 1
                    : Math.Max(2, (int)Math.Round(gap / (averageCharWidth * 2)));

                builder.Append(' ', spaces);
            }

            builder.Append(word.Text);
            previous = word;
        }

        return builder.ToString().Trim();
    }

    internal static string ClassifyRole(double fontSize, double bodyFontSize, string text)
    {
        if (bodyFontSize <= 0) return "body";

        var ratio = fontSize / bodyFontSize;

        if (ratio >= 1.5)
        {
            return text.Length < 40 ? "heading" : "body";
        }

        if (ratio < 0.7)
        {
            return "caption";
        }

        return "body";
    }
}
