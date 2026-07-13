namespace MarkItDown.Core;

public sealed record GoldenDocumentExpectation
{
    public IReadOnlyList<string> RequiredText { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RequiredHeadings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RequiredTableCells { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RequiredDiagnosticCodes { get; init; } = Array.Empty<string>();
    public int MinimumAssets { get; init; }
}

public sealed record DocumentQualityReport(
    double TextRecall,
    double HeadingAccuracy,
    double TableCellRecall,
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

        var allText = string.Join("\n", actual.Blocks.Select(block => block.Text));
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
        var diagnosticCodes = (actual.Diagnostics ?? [])
            .Select(diagnostic => diagnostic.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingDiagnostics = expected.RequiredDiagnosticCodes
            .Where(code => !diagnosticCodes.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingAssets = Math.Max(0, expected.MinimumAssets - assetCount);
        var textRecall = Recall(expected.RequiredText.Count, missingText.Count);
        var headingAccuracy = Recall(expected.RequiredHeadings.Count, missingHeadings.Count);
        var tableCellRecall = Recall(expected.RequiredTableCells.Count, missingTableCells.Count);
        var unexplainedLosses = missingText.Count;
        var passed = actual.Fidelity != FidelityStatus.Failed
            && missingText.Count == 0
            && missingHeadings.Count == 0
            && missingTableCells.Count == 0
            && missingDiagnostics.Length == 0
            && missingAssets == 0;

        return new DocumentQualityReport(
            textRecall,
            headingAccuracy,
            tableCellRecall,
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
}
