namespace MarkItDown.Core;

public sealed record DocumentConversionRequest
{
    public string? FilePath { get; init; }
    public Stream? Stream { get; init; }
    public string? Filename { get; init; }
    public string? MimeType { get; init; }
    public ILlmClient? LlmClient { get; init; }
    public string? AssetBasePath { get; init; }
    public int ContainerDepth { get; init; }
    public ConversionContext? Context { get; init; }
    public ConversionOptions? Options { get; init; }
    public IAssetStore? AssetStore { get; init; }
    public IVisionAnalyzer? VisionAnalyzer { get; init; }
}
