namespace MarkItDown.Core;

public sealed record GoldenDocumentExpectation
{
    public IReadOnlyList<string> RequiredText { get; init; } = Array.Empty<string>();
    public string? ExpectedText { get; init; }
    public IReadOnlyList<string> ExpectedOrderedText { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RequiredHeadings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RequiredTableCells { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RequiredDiagnosticCodes { get; init; } = Array.Empty<string>();
    public int MinimumAssets { get; init; }
    public int? ExpectedSourceCount { get; init; }
}

public sealed record DocumentQualityReport(
    double TextRecall,
    double TextErrorRate,
    double ReadingOrderAccuracy,
    double HeadingAccuracy,
    double TableCellRecall,
    double TableCellPrecision,
    double TableCellF1,
    double SourceCoverage,
    IReadOnlyList<string> MissingRequiredText,
    IReadOnlyList<string> MissingHeadings,
    IReadOnlyList<string> MissingTableCells,
    IReadOnlyList<string> MissingRequiredDiagnosticCodes,
    int MissingAssetCount,
    int UnexplainedContentLossCount,
    bool Passed);

public static class DocumentQualityEvaluator
{
    public static DocumentQualityReport Evaluate(
        DocumentModel actual,
        GoldenDocumentExpectation expected,
        int assetCount = 0)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);

        var semanticBlocks = actual.Blocks.Where(block =>
            !block.Kind.Equals("table", StringComparison.OrdinalIgnoreCase)
            && !block.Kind.Equals("diagnostic", StringComparison.OrdinalIgnoreCase)).ToArray();
        var allText = string.Join("\n", actual.Blocks.Select(block => block.Text));
        var semanticText = string.Join("\n", semanticBlocks.Select(block => block.Text));
        var missingText = Missing(expected.RequiredText, allText);
        var actualHeadings = actual.Blocks
            .Where(block => block.Kind.Equals("heading", StringComparison.OrdinalIgnoreCase))
            .Select(block => block.Text)
            .ToArray();
        var missingHeadings = Missing(expected.RequiredHeadings, string.Join("\n", actualHeadings));
        var tableText = string.Join("\n", actual.Blocks
            .Where(block => block.Kind.Equals("table", StringComparison.OrdinalIgnoreCase))
            .Select(block => block.Text));
        var missingTableCells = Missing(expected.RequiredTableCells, tableText);
        var actualTableCells = ExtractTableCells(actual);
        var expectedTableCells = expected.RequiredTableCells
            .Where(cell => !string.IsNullOrWhiteSpace(cell))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var truePositiveTableCells = actualTableCells.Count(cell => expectedTableCells.Contains(cell));
        var tablePrecision = actualTableCells.Count == 0
            ? expectedTableCells.Count == 0 ? 1 : 0
            : (double)truePositiveTableCells / actualTableCells.Count;
        var tableRecall = expectedTableCells.Count == 0
            ? 1
            : (double)truePositiveTableCells / expectedTableCells.Count;
        var tableF1 = tablePrecision + tableRecall == 0
            ? 0
            : 2 * tablePrecision * tableRecall / (tablePrecision + tableRecall);
        var diagnosticCodes = (actual.Diagnostics ?? [])
            .Select(diagnostic => diagnostic.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingDiagnostics = expected.RequiredDiagnosticCodes
            .Where(code => !diagnosticCodes.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingAssets = Math.Max(0, expected.MinimumAssets - assetCount);
        var textRecall = Recall(expected.RequiredText.Count, missingText.Count);
        var textErrorRate = expected.ExpectedText is null
            ? 0
            : (double)Levenshtein(Normalize(expected.ExpectedText), Normalize(semanticText))
                / Math.Max(1, Normalize(expected.ExpectedText).Length);
        var readingOrderAccuracy = OrderedAccuracy(expected.ExpectedOrderedText, semanticBlocks);
        var headingAccuracy = Recall(expected.RequiredHeadings.Count, missingHeadings.Count);
        var tableCellRecall = Recall(expected.RequiredTableCells.Count, missingTableCells.Count);
        var sourceCoverage = expected.ExpectedSourceCount is null
            ? 1
            : Recall(expected.ExpectedSourceCount.Value, Math.Max(0, expected.ExpectedSourceCount.Value - actual.Blocks.Count(block => block.Source is not null)));
        var unexplainedLosses = missingText.Count;
        var passed = actual.Fidelity != FidelityStatus.Failed
            && missingText.Count == 0
            && missingHeadings.Count == 0
            && missingTableCells.Count == 0
            && missingDiagnostics.Length == 0
            && missingAssets == 0
            && textErrorRate == 0
            && readingOrderAccuracy == 1
            && tableF1 == 1
            && sourceCoverage == 1;

        return new DocumentQualityReport(
            textRecall,
            textErrorRate,
            readingOrderAccuracy,
            headingAccuracy,
            tableCellRecall,
            tablePrecision,
            tableF1,
            sourceCoverage,
            missingText,
            missingHeadings,
            missingTableCells,
            missingDiagnostics,
            missingAssets,
            unexplainedLosses,
            passed);
    }

    private static IReadOnlyList<string> Missing(IEnumerable<string> expected, string actual)
    {
        return expected
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Where(value => !actual.Contains(value, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static double Recall(int total, int missing) =>
        total == 0 ? 1 : (double)(total - missing) / total;

    private static IReadOnlyList<string> ExtractTableCells(DocumentModel model) => model.Blocks
        .Where(block => block.Kind.Equals("table", StringComparison.OrdinalIgnoreCase))
        .SelectMany(block => block.Text.Split('\n'))
        .Where(line => line.TrimStart().StartsWith('|'))
        .SelectMany(line => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()))
        .Where(cell => cell.Length > 0 && cell.Any(character => character != '-'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static double OrderedAccuracy(IReadOnlyList<string> expected, IReadOnlyList<DocumentBlock> actual)
    {
        if (expected.Count == 0) return 1;
        var cursor = 0;
        var found = 0;
        foreach (var value in expected)
        {
            for (; cursor < actual.Count; cursor++)
            {
                if (!actual[cursor].Text.Contains(value, StringComparison.OrdinalIgnoreCase)) continue;
                found++;
                cursor++;
                break;
            }
        }
        return (double)found / expected.Count;
    }

    private static int Levenshtein(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[right.Length];
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
