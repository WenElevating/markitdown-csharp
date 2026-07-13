namespace MarkItDown.Core;

public enum ConversionBackendMode { Native, Auto, Docling }
public enum VisionMode { Off, Auto, Required }
public enum PrivacyMode { LocalOnly, AllowConfiguredServices }
public enum PipelineMode { Legacy, Multimodal }

public sealed record ConversionLimits
{
    public long MaxInputBytes { get; init; } = 256L * 1024 * 1024;
    public int MaxPages { get; init; } = 500;
    public int MaxAssets { get; init; } = 1_000;
    public long MaxAssetBytes { get; init; } = 32L * 1024 * 1024;
    public long MaxTotalAssetBytes { get; init; } = 512L * 1024 * 1024;
    public int MaxVisionTasks { get; init; } = 100;
    public int MaxPackageEntries { get; init; } = 10_000;
    public long MaxPackageUncompressedBytes { get; init; } = 1L * 1024 * 1024 * 1024;
    public TimeSpan OverallTimeout { get; init; } = TimeSpan.FromMinutes(15);
}

public sealed record PrivacyOptions
{
    public bool AllowDocumentUpload { get; init; }
    public bool AllowExternalRelationships { get; init; }
}

public sealed record DiagnosticRenderOptions
{
    public bool RenderWarningsInMarkdown { get; init; } = true;
}

public sealed record ConversionOptions
{
    public PipelineMode PipelineMode { get; init; } = PipelineMode.Legacy;
    public VisionMode? VisionMode { get; init; }
    public ConversionLimits Limits { get; init; } = new();
    public PrivacyOptions Privacy { get; init; } = new();
    public DiagnosticRenderOptions Diagnostics { get; init; } = new();
    public IDoclingTransport? DoclingTransport { get; init; }
}

public enum VisionTaskType { OcrPage, DescribeFigure, AnalyzeChart, RecoverTable }
public interface IVisionAnalyzer
{
    Task<VisionAnalysisResult> AnalyzeAsync(VisionTaskType task, Stream input, CancellationToken cancellationToken = default);
}

public sealed record VisionAnalysisResult(
    string Text,
    double Confidence,
    string Provider,
    string Model,
    string SchemaVersion);

public sealed record ConversionUsage
{
    public int VisionTasks { get; init; }
    public long InputBytes { get; init; }
    public long AssetBytes { get; init; }
}

public sealed record ConversionContext
{
    public DocumentInput? Input { get; init; }
    public ConversionBackendMode Backend { get; init; } = ConversionBackendMode.Auto;
    public VisionMode Vision { get; init; } = VisionMode.Auto;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
    public int? MaxPages { get; init; }
    public long? MaxBytes { get; init; }
    public IAssetStore? Assets { get; init; }
    public PrivacyMode Privacy { get; init; } = PrivacyMode.LocalOnly;
    public PipelineMode Pipeline { get; init; } = PipelineMode.Legacy;
    public ConversionOptions Options { get; init; } = new();
    public string OperationId { get; init; } = Guid.NewGuid().ToString("N");
    public IVisionAnalyzer? VisionAnalyzer { get; init; }
    public IOcrProvider? OcrProvider { get; init; }
    public ILlmClient? LlmClient { get; init; }
    public string? AssetBasePath { get; init; }
    public int ContainerDepth { get; init; }
    public IAssetTransaction? AssetTransaction { get; init; }
    public IDictionary<string, object?> Properties { get; init; } = new Dictionary<string, object?>();
}

public sealed record DocumentInput(string? FilePath, string? Filename, string? MimeType, long? Length, Stream? Stream = null);

public enum FidelityStatus { Complete, Partial, Failed, NotEvaluated }
public enum DiagnosticSeverity { Info, Warning, Error }

public sealed record SourceLocation(
    int? Page = null,
    int? Slide = null,
    string? Sheet = null,
    string? Part = null,
    int? Index = null,
    double? PageWidth = null,
    double? PageHeight = null,
    double? Left = null,
    double? Top = null,
    double? Right = null,
    double? Bottom = null,
    string? RelationshipId = null,
    string? PartUri = null);

public sealed record ConversionDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    SourceLocation? Location = null,
    bool AffectsSubstantiveContent = false,
    string? Backend = null,
    string? FallbackReason = null,
    bool RequiresReview = false);

public sealed record DocumentBlock(
    string Kind,
    string Text,
    SourceLocation? Source = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    public string Id { get; init; } = CreateId(Kind, Text, Source);

    private static string CreateId(string kind, string text, SourceLocation? source)
    {
        var input = $"{kind}|{text}|{source?.Page}|{source?.Slide}|{source?.Sheet}|{source?.Index}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input)))[..16].ToLowerInvariant();
    }
}

public sealed record DocumentSupplement(
    string Kind,
    IReadOnlyList<DocumentBlock> Blocks,
    string? Reference = null);

public sealed record DocumentModel(
    string Kind,
    IReadOnlyList<DocumentBlock> Blocks,
    IReadOnlyDictionary<string, string>? Metadata = null,
    IReadOnlyList<ConversionDiagnostic>? Diagnostics = null,
    FidelityStatus Fidelity = FidelityStatus.NotEvaluated,
    IReadOnlyList<DocumentSupplement>? Supplements = null);
