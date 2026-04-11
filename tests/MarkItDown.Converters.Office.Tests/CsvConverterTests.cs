using MarkItDown.Core;
using MarkItDown.Converters.Office;

namespace MarkItDown.Converters.Office.Tests;

public sealed class CsvConverterTests
{
    private readonly CsvConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsCsvExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "data.csv" }));
    }

    [Fact]
    public async Task ConvertAsync_ConvertsCsvToMarkdownTable()
    {
        var result = await _converter.ConvertAsync(
            new DocumentConversionRequest { FilePath = FixturePath.For("office/sample.csv") });

        Assert.Contains("| Name | Department | Salary |", result.Markdown);
        Assert.Contains("| Alice | Engineering | 95000 |", result.Markdown);
        Assert.Equal("Csv", result.Kind);
    }

    [Fact]
    public async Task ConvertAsync_HandlesEmptyCsv()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(csvPath, "");

        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = csvPath });
            Assert.Equal(string.Empty, result.Markdown.Trim());
        }
        finally
        {
            File.Delete(csvPath);
        }
    }
}
