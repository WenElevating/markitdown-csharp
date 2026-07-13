using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;

namespace MarkItDown.Converters.Office;

public static class OfficeChartExtractor
{
    public static string Extract(OpenXmlElement chartSpace) =>
        Extract(chartSpace?.OuterXml ?? string.Empty);

    public static string Extract(string chartXml)
    {
        if (string.IsNullOrWhiteSpace(chartXml)) return string.Empty;
        var document = XDocument.Parse(chartXml, LoadOptions.PreserveWhitespace);
        var rows = new List<(string Series, string Category, string Value)>();
        foreach (var series in document.Descendants().Where(element => element.Name.LocalName == "ser"))
        {
            var seriesName = ValuesUnder(series, "tx").FirstOrDefault() ?? "Series";
            var categories = ValuesUnder(series, "cat");
            var values = ValuesUnder(series, "val");
            var count = Math.Max(categories.Count, values.Count);
            for (var index = 0; index < count; index++)
            {
                rows.Add((seriesName,
                    index < categories.Count ? categories[index] : string.Empty,
                    index < values.Count ? values[index] : string.Empty));
            }
        }

        if (rows.Count == 0) return string.Empty;
        var output = new StringBuilder();
        output.AppendLine("| Series | Category | Value |");
        output.AppendLine("| --- | --- | --- |");
        foreach (var row in rows)
            output.Append("| ").Append(Escape(row.Series)).Append(" | ")
                .Append(Escape(row.Category)).Append(" | ")
                .Append(Escape(row.Value)).AppendLine(" |");
        return output.ToString().TrimEnd();
    }

    private static List<string> ValuesUnder(XElement root, string parentName) =>
        root.Descendants().Where(element => element.Name.LocalName == parentName)
            .SelectMany(parent => parent.Descendants().Where(element => element.Name.LocalName == "v"))
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .ToList();

    private static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
