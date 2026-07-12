namespace MarkItDown.Core;

public sealed class LimitedAssetTransaction(IAssetTransaction inner, ConversionLimits limits) : IAssetTransaction
{
    private readonly Dictionary<string, AssetReference> _assets = new(StringComparer.OrdinalIgnoreCase);

    public async Task<AssetReference> PutAsync(Stream content, string fileName, string mimeType, string? source = null, CancellationToken cancellationToken = default)
    {
        var reference = await inner.PutAsync(content, fileName, mimeType, source, cancellationToken);
        if (_assets.ContainsKey(reference.Id)) return reference;
        if (_assets.Count >= limits.MaxAssets)
            throw new ConversionException("RESOURCE_LIMIT_EXCEEDED: maximum asset count reached.");
        if (reference.Size > limits.MaxAssetBytes)
            throw new ConversionException("RESOURCE_LIMIT_EXCEEDED: maximum single asset size reached.");
        if (_assets.Values.Sum(asset => asset.Size) + reference.Size > limits.MaxTotalAssetBytes)
            throw new ConversionException("RESOURCE_LIMIT_EXCEEDED: maximum total asset size reached.");
        _assets[reference.Id] = reference;
        return reference;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) => inner.CommitAsync(cancellationToken);
    public ValueTask RollbackAsync() => inner.RollbackAsync();
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
