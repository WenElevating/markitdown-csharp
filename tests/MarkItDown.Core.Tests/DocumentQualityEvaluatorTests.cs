namespace MarkItDown.Core.Tests;

public sealed class DocumentQualityEvaluatorTests
{
    [Fact]
    public void Evaluate_ReportsRecallHeadingAndTableCoverage()
    {
        var expected = new GoldenDocumentExpectation
        {
            RequiredText = ["Title", "Body", "Alice"],
            RequiredHeadings = ["Title"],
            RequiredTableCells = ["Alice", "Engineering"],
            MinimumAssets = 1,
            RequiredDiagnosticCodes = ["SOURCE_NOTE"]
        };
        var actual = new DocumentModel(
            "Xlsx",
            [
                new DocumentBlock("heading", "Title", Attributes: new Dictionary<string, string> { ["level"] = "2" }),
                new DocumentBlock("paragraph", "Body"),
                new DocumentBlock("table", "| Alice | Engineering |\n| --- | --- |"),
                new DocumentBlock("diagnostic", "note", Attributes: new Dictionary<string, string> { ["code"] = "SOURCE_NOTE" })
            ],
            Fidelity: FidelityStatus.Complete,
            Diagnostics: [new ConversionDiagnostic("SOURCE_NOTE", DiagnosticSeverity.Info, "source")]);

        var report = DocumentQualityEvaluator.Evaluate(actual, expected, assetCount: 1);

        Assert.True(report.Passed);
        Assert.Equal(1, report.TextRecall);
        Assert.Equal(1, report.HeadingAccuracy);
        Assert.Equal(1, report.TableCellRecall);
        Assert.Empty(report.MissingRequiredText);
        Assert.Empty(report.MissingRequiredDiagnosticCodes);
    }

    [Fact]
    public void Evaluate_ExposesUnexplainedMaterialLosses()
    {
        var report = DocumentQualityEvaluator.Evaluate(
            new DocumentModel("Pdf", [new DocumentBlock("paragraph", "kept")], Fidelity: FidelityStatus.Partial),
            new GoldenDocumentExpectation { RequiredText = ["kept", "lost"], MinimumAssets = 1 },
            assetCount: 0);

        Assert.False(report.Passed);
        Assert.Equal(0.5, report.TextRecall);
        Assert.Contains("lost", report.MissingRequiredText);
        Assert.Equal(1, report.MissingAssetCount);
        Assert.Equal(1, report.UnexplainedContentLossCount);
    }
}
