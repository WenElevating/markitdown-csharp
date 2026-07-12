namespace MarkItDown.Core;

public class ConversionException : Exception
{
    public ConversionFailureReport? FailureReport { get; init; }
    public ConversionException(string message) : base(message) { }
    public ConversionException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed record ConversionFailureReport(
    FidelityStatus Status,
    string? Kind,
    IReadOnlyList<ConversionDiagnostic> Diagnostics,
    ConversionUsage Usage,
    string OperationId)
{
    public static ConversionFailureReport Create(string code, string message, string operationId, string? kind = null) =>
        new(FidelityStatus.Failed, kind,
            [new ConversionDiagnostic(code, DiagnosticSeverity.Error, message, AffectsSubstantiveContent: true, RequiresReview: true)],
            new(), operationId);
}
