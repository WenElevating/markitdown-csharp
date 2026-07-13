using MarkItDown.Converters.Office;

namespace MarkItDown.Converters.Office.Tests;

public sealed class OfficeChartExtractorTests
{
    [Fact]
    public void Extract_IgnoresVendorExtensionAndKeepsChartSeries()
    {
        const string xml = """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:c15="http://schemas.microsoft.com/office/drawing/2012/chart">
              <c:extLst><c:ext uri="{vendor-chart-extension}"><c15:style val="modern" /></c:ext></c:extLst>
              <c:barChart><c:ser>
                <c:tx><c:v>Revenue</c:v></c:tx>
                <c:cat><c:strRef><c:strCache><c:pt><c:v>Q1</c:v></c:pt></c:strCache></c:strRef></c:cat>
                <c:val><c:numRef><c:numCache><c:pt><c:v>10</c:v></c:pt></c:numCache></c:numRef></c:val>
              </c:ser></c:barChart>
            </c:chartSpace>
            """;

        var markdown = OfficeChartExtractor.Extract(xml);

        Assert.Contains("| Revenue | Q1 | 10 |", markdown);
    }

    [Fact]
    public void Extract_EmitsDeterministicSeriesCategoryValueTable()
    {
        const string xml = """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:barChart><c:ser>
                <c:tx><c:v>Revenue</c:v></c:tx>
                <c:cat><c:strRef><c:strCache><c:pt><c:v>Q1</c:v></c:pt><c:pt><c:v>Q2</c:v></c:pt></c:strCache></c:strRef></c:cat>
                <c:val><c:numRef><c:numCache><c:pt><c:v>10</c:v></c:pt><c:pt><c:v>20</c:v></c:pt></c:numCache></c:numRef></c:val>
              </c:ser></c:barChart>
            </c:chartSpace>
            """;

        var markdown = OfficeChartExtractor.Extract(xml);

        Assert.Contains("| Series | Category | Value |", markdown);
        Assert.Contains("| Revenue | Q1 | 10 |", markdown);
        Assert.Contains("| Revenue | Q2 | 20 |", markdown);
    }
}
