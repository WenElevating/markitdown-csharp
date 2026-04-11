using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MarkItDown.Core;

namespace MarkItDown.Converters.Office;

public sealed class XlsxConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xlsx" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("XLSX converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                using var doc = SpreadsheetDocument.Open(filePath, false);
                var workbookPart = doc.WorkbookPart
                    ?? throw new ConversionException("Invalid XLSX file.");

                var workbook = workbookPart.Workbook ?? throw new ConversionException("Invalid XLSX file.");
                var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
                var sheets = workbook.Sheets;
                var sheetElements = sheets is not null
                    ? sheets.Elements<Sheet>().ToList()
                    : new List<Sheet>();

                var sections = new List<string>();

                foreach (var worksheetPart in workbookPart.WorksheetParts)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var sheetId = workbookPart.GetIdOfPart(worksheetPart);
                    var sheetName = sheetElements.FirstOrDefault(s => s.Id == sheetId)?.Name?.Value ?? "Sheet";

                    var sheetData = worksheetPart.Worksheet?.Elements<SheetData>().FirstOrDefault();
                    if (sheetData is null) continue;

                    var rows = sheetData.Elements<Row>().ToList();
                    if (rows.Count == 0) continue;

                    var builder = new StringBuilder();
                    builder.AppendLine($"## {sheetName}");

                    // First row = header
                    var headerCells = rows[0].Elements<Cell>().ToList();
                    var header = headerCells.Select(c => EscapePipe(GetCellValue(c, sharedStrings))).ToList();
                    var colCount = header.Count;
                    if (colCount == 0) continue;

                    builder.AppendLine($"| {string.Join(" | ", header)} |");
                    builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", colCount))} |");

                    // Data rows
                    foreach (var row in rows.Skip(1))
                    {
                        var cells = row.Elements<Cell>().ToList();
                        var fields = new List<string>();

                        for (var i = 0; i < colCount; i++)
                        {
                            var cell = cells.FirstOrDefault(c =>
                                GetColumnIndex(c.CellReference?.Value) == i + 1);
                            fields.Add(EscapePipe(cell is not null
                                ? GetCellValue(cell, sharedStrings)
                                : string.Empty));
                        }

                        builder.AppendLine($"| {string.Join(" | ", fields)} |");
                    }

                    sections.Add(builder.ToString().TrimEnd());
                }

                var markdown = string.Join(Environment.NewLine + Environment.NewLine, sections);
                return new DocumentConversionResult("Xlsx", markdown);
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert XLSX: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    internal static string GetCellValue(Cell cell, SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.Text ?? string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString && sharedStrings is not null)
        {
            if (int.TryParse(value, out var index))
            {
                var element = sharedStrings.ElementAtOrDefault(index);
                if (element is not null)
                    value = element.InnerText;
            }
        }

        return value;
    }

    internal static int GetColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return 0;
        var col = 0;
        foreach (var c in cellRef)
        {
            if (char.IsLetter(c))
                col = col * 26 + (c - 'A' + 1);
            else
                break;
        }
        return col;
    }

    internal static string ColumnIndexToLetter(int index)
    {
        var letter = string.Empty;
        while (index > 0)
        {
            var mod = (index - 1) % 26;
            letter = Convert.ToChar('A' + mod) + letter;
            index = (index - 1) / 26;
        }
        return letter;
    }

    private static string EscapePipe(string value) =>
        value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
