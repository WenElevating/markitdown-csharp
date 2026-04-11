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
