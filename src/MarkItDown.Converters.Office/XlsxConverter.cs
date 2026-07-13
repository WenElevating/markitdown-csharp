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
        return await Task.Run(async () =>
        {
            try
            {
                using var input = DocumentInputMaterializer.Materialize(request, cancellationToken);
                OfficePackageGuard.Validate(input.FilePath,
                    request.Context?.Options.Limits ?? new ConversionLimits(),
                    request.Context?.Options.Privacy.AllowExternalRelationships == true);
                using var doc = SpreadsheetDocument.Open(input.FilePath, false);
                var workbookPart = doc.WorkbookPart
                    ?? throw new ConversionException("Invalid XLSX file.");

                var workbook = workbookPart.Workbook ?? throw new ConversionException("Invalid XLSX file.");
                var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
                var sheets = workbook.Sheets;
                var sheetElements = sheets is not null
                    ? sheets.Elements<Sheet>().ToList()
                    : new List<Sheet>();

                var nativeBlocks = new List<DocumentBlock>();

                foreach (var (worksheetPart, sheetIndex) in workbookPart.WorksheetParts.Select((part, index) => (part, index)))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var sheetId = workbookPart.GetIdOfPart(worksheetPart);
                    var sheetName = sheetElements.FirstOrDefault(s => s.Id == sheetId)?.Name?.Value ?? "Sheet";

                    var sheetData = worksheetPart.Worksheet?.Elements<SheetData>().FirstOrDefault();
                    if (sheetData is null) continue;

                    var rows = sheetData.Elements<Row>().ToList();
                    if (rows.Count == 0) continue;

                    var source = new SourceLocation(Sheet: sheetName, Index: sheetIndex);
                    nativeBlocks.Add(new DocumentBlock(
                        "heading", sheetName, source,
                        new Dictionary<string, string> { ["level"] = "2" }));
                    var data = new List<List<string>>();

                    var hiddenRows = rows.Where(row => row.Hidden?.Value == true).Select(row => row.RowIndex?.Value).Where(index => index is not null).ToArray();
                    if (hiddenRows.Length > 0)
                        nativeBlocks.Add(new DocumentBlock("paragraph", $"<!-- Hidden rows: {string.Join(", ", hiddenRows)} -->", source));
                    var hiddenColumns = (worksheetPart.Worksheet?.Elements<Columns>() ?? [])
                        .SelectMany(columns => columns.Elements<Column>())
                        .Where(column => column.Hidden?.Value == true)
                        .Select(column => $"{column.Min?.Value}-{column.Max?.Value}").ToArray();
                    if (hiddenColumns.Length > 0)
                        nativeBlocks.Add(new DocumentBlock("paragraph", $"<!-- Hidden columns: {string.Join(", ", hiddenColumns)} -->", source));
                    var mergedRanges = (worksheetPart.Worksheet?.Elements<MergeCells>() ?? [])
                        .SelectMany(merges => merges.Elements<MergeCell>())
                        .Select(cell => cell.Reference?.Value).Where(reference => !string.IsNullOrWhiteSpace(reference)).ToArray();
                    if (mergedRanges.Length > 0)
                        nativeBlocks.Add(new DocumentBlock("paragraph", $"<!-- Merged cells: {string.Join(", ", mergedRanges)} -->", source));

                    // First row = header
                    var headerCells = rows[0].Elements<Cell>().ToList();
                    var header = headerCells.Select(c => GetCellValue(c, sharedStrings)).ToList();
                    var colCount = header.Count;
                    if (colCount == 0) continue;
                    data.Add(header);

                    // Data rows
                    foreach (var row in rows.Skip(1))
                    {
                        var cells = row.Elements<Cell>().ToList();
                        var fields = new List<string>();

                        for (var i = 0; i < colCount; i++)
                        {
                            var cell = cells.FirstOrDefault(c =>
                                GetColumnIndex(c.CellReference?.Value) == i + 1);
                            fields.Add(cell is not null
                                ? GetCellValue(cell, sharedStrings)
                                : string.Empty);
                        }
                        data.Add(fields);
                    }

                    var table = mergedRanges.Length > 0
                        ? RenderHtmlTable(data)
                        : RenderMarkdownTable(data);
                    nativeBlocks.Add(new DocumentBlock("table", table, source));

                    foreach (var chartPart in worksheetPart.DrawingsPart?.ChartParts ?? [])
                    {
                        var chart = chartPart.ChartSpace is null ? string.Empty : OfficeChartExtractor.Extract(chartPart.ChartSpace);
                        if (!string.IsNullOrWhiteSpace(chart))
                            nativeBlocks.Add(new DocumentBlock("table", chart, source));
                    }
                }

                var images = OfficeAssetExtractor.Extract(
                    workbookPart.WorksheetParts.SelectMany(sheet => sheet.ImageParts), request);
                if (!string.IsNullOrWhiteSpace(images))
                    nativeBlocks.Add(new DocumentBlock("figure", images));
                var fidelity = request.Context?.Pipeline == PipelineMode.Multimodal
                    ? FidelityStatus.Complete : FidelityStatus.NotEvaluated;
                var document = new DocumentModel("Xlsx", nativeBlocks, Fidelity: fidelity);
                return await OfficeDoclingEnhancer.EnhanceAsync(
                    "Xlsx", request, input.FilePath, document, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
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

        if (cell.DataType?.Value == CellValues.Error)
            value = $"#ERROR! {value}";
        if (cell.CellFormula is not null && !string.IsNullOrWhiteSpace(cell.CellFormula.Text))
            value = $"={cell.CellFormula.Text} => {value}";

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

    private static string RenderMarkdownTable(IReadOnlyList<List<string>> rows)
    {
        var columnCount = rows[0].Count;
        var builder = new StringBuilder();
        builder.AppendLine($"| {string.Join(" | ", rows[0].Select(EscapePipe))} |");
        builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", columnCount))} |");
        foreach (var row in rows.Skip(1))
            builder.AppendLine($"| {string.Join(" | ", row.Select(EscapePipe))} |");
        return builder.ToString().TrimEnd();
    }

    private static string RenderHtmlTable(IReadOnlyList<List<string>> rows)
    {
        var builder = new StringBuilder("<table>\n  <thead><tr>");
        foreach (var cell in rows[0])
            builder.Append("<th>").Append(System.Net.WebUtility.HtmlEncode(cell)).Append("</th>");
        builder.AppendLine("</tr></thead>\n  <tbody>");
        foreach (var row in rows.Skip(1))
        {
            builder.Append("    <tr>");
            foreach (var cell in row)
                builder.Append("<td>").Append(System.Net.WebUtility.HtmlEncode(cell)).Append("</td>");
            builder.AppendLine("</tr>");
        }
        return builder.Append("  </tbody>\n</table>").ToString();
    }
}
