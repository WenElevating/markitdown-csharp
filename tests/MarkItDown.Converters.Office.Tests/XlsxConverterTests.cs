using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MarkItDown.Core;
using MarkItDown.Converters.Office;

namespace MarkItDown.Converters.Office.Tests;

public sealed class XlsxConverterTests
{
    private readonly XlsxConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsXlsxExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "data.xlsx" }));
    }

    [Fact]
    public async Task ConvertAsync_ConvertsXlsxToMarkdown()
    {
        var xlsxPath = CreateTestXlsx();
        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = xlsxPath });

            Assert.Contains("## Employees", result.Markdown);
            Assert.Contains("| Name | Department | Salary |", result.Markdown);
            Assert.Contains("| Alice | Engineering | 95000 |", result.Markdown);
            Assert.Contains("| Bob | Marketing | 72000 |", result.Markdown);
            Assert.Equal("Xlsx", result.Kind);
            Assert.Equal("Employees", result.Document!.Blocks.First(block => block.Kind == "heading").Source!.Sheet);
        }
        finally
        {
            File.Delete(xlsxPath);
        }
    }

    [Fact]
    public async Task ConvertAsync_HandlesEmptySheet()
    {
        var xlsxPath = CreateEmptyXlsx();
        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = xlsxPath });
            // Should not crash on empty workbook
            Assert.NotNull(result.Markdown);
        }
        finally
        {
            File.Delete(xlsxPath);
        }
    }

    [Fact]
    public async Task ConvertAsync_UsesHtmlTableForMergedCells()
    {
        var xlsxPath = CreateMergedXlsx();
        try
        {
            var result = await _converter.ConvertAsync(new DocumentConversionRequest { FilePath = xlsxPath });

            Assert.Contains("<table>", result.Markdown);
            Assert.Contains("Merged cells: A1:B1", result.Markdown);
            Assert.DoesNotContain("| Name | Department |", result.Markdown);
        }
        finally
        {
            File.Delete(xlsxPath);
        }
    }

    [Fact]
    public async Task ConvertAsync_MultimodalAddsDoclingSupplementAndCompletes()
    {
        var xlsxPath = CreateTestXlsx();
        try
        {
            var result = await _converter.ConvertAsync(new DocumentConversionRequest
            {
                FilePath = xlsxPath,
                Context = new ConversionContext
                {
                    Pipeline = PipelineMode.Multimodal,
                    Vision = VisionMode.Auto,
                    Options = new ConversionOptions { PipelineMode = PipelineMode.Multimodal, VisionMode = VisionMode.Auto, DoclingTransport = new FakeDoclingTransport() }
                }
            });

            Assert.Equal(FidelityStatus.Complete, result.FidelityStatus);
            Assert.Contains("| Alice | Engineering | 95000 |", result.Markdown);
            Assert.Contains("Visual table insight", result.Markdown);
            Assert.DoesNotContain(result.Diagnostics ?? [], d => d.Code == "MULTIMODAL_FORMAT_UNSUPPORTED");
            Assert.NotEmpty(result.Document?.Supplements ?? []);
        }
        finally
        {
            File.Delete(xlsxPath);
        }
    }

    [Fact]
    public async Task ConvertAsync_RequiredVisionFailsWhenDoclingIsUnavailable()
    {
        var xlsxPath = CreateTestXlsx();
        try
        {
            var exception = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync(new DocumentConversionRequest
            {
                FilePath = xlsxPath,
                Context = new ConversionContext
                {
                    Pipeline = PipelineMode.Multimodal,
                    Vision = VisionMode.Required,
                    Options = new ConversionOptions { PipelineMode = PipelineMode.Multimodal, VisionMode = VisionMode.Required }
                }
            }));

            Assert.Equal("DOCLING_REQUIRED_FAILED", exception.FailureReport!.Diagnostics[0].Code);
        }
        finally
        {
            File.Delete(xlsxPath);
        }
    }

    [Fact]
    public async Task ConvertAsync_NativeBackendDoesNotInvokeDocling()
    {
        var xlsxPath = CreateTestXlsx();
        try
        {
            var result = await _converter.ConvertAsync(new DocumentConversionRequest
            {
                FilePath = xlsxPath,
                Context = new ConversionContext
                {
                    Backend = ConversionBackendMode.Native,
                    Pipeline = PipelineMode.Multimodal,
                    Vision = VisionMode.Required,
                    Options = new ConversionOptions { PipelineMode = PipelineMode.Multimodal, VisionMode = VisionMode.Required, DoclingTransport = new FailingDoclingTransport() }
                }
            });

            Assert.Equal(FidelityStatus.Complete, result.FidelityStatus);
            Assert.Contains("| Alice | Engineering | 95000 |", result.Markdown);
        }
        finally
        {
            File.Delete(xlsxPath);
        }
    }

    private static string CreateTestXlsx()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);

        var workbookPart = doc.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new Worksheet(new SheetData());

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Employees"
        });

        var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>()!;

        // Header row
        var headerRow = new Row();
        headerRow.Append(
            CreateCell("A", "Name", CellValues.String),
            CreateCell("B", "Department", CellValues.String),
            CreateCell("C", "Salary", CellValues.String));
        sheetData.Append(headerRow);

        // Data rows
        var row1 = new Row();
        row1.Append(
            CreateCell("A", "Alice", CellValues.String),
            CreateCell("B", "Engineering", CellValues.String),
            CreateCell("C", "95000", CellValues.String));
        sheetData.Append(row1);

        var row2 = new Row();
        row2.Append(
            CreateCell("A", "Bob", CellValues.String),
            CreateCell("B", "Marketing", CellValues.String),
            CreateCell("C", "72000", CellValues.String));
        sheetData.Append(row2);

        workbookPart.Workbook.Save();
        return path;
    }

    private sealed class FakeDoclingTransport : IDoclingTransport
    {
        public Task<DoclingResponse> SendAsync(DoclingRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DoclingResponse(request.RequestId, "1", true,
                "{\"texts\":[{\"text\":\"Visual table insight\"}]}", null, null));
    }

    private sealed class FailingDoclingTransport : IDoclingTransport
    {
        public Task<DoclingResponse> SendAsync(DoclingRequest request, CancellationToken cancellationToken = default) =>
            throw new IOException("must not be called");
    }

    private static string CreateEmptyXlsx()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);

        var workbookPart = doc.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new Worksheet(new SheetData());

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Empty"
        });

        workbookPart.Workbook.Save();
        return path;
    }

    private static string CreateMergedXlsx()
    {
        var path = CreateTestXlsx();
        using var doc = SpreadsheetDocument.Open(path, true);
        var worksheet = doc.WorkbookPart!.WorksheetParts.First().Worksheet!;
        worksheet.AppendChild(new MergeCells(new MergeCell { Reference = "A1:B1" }));
        worksheet.Save();
        return path;
    }

    private static Cell CreateCell(string column, string value, CellValues dataType)
    {
        return new Cell
        {
            CellReference = column,
            CellValue = new CellValue(value),
            DataType = new EnumValue<CellValues>(dataType)
        };
    }
}
