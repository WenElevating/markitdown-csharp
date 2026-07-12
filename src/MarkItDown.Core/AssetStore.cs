using System.Security.Cryptography;

namespace MarkItDown.Core;

public sealed record AssetReference(
    string Id,
    string FileName,
    string MimeType,
    string Sha256,
    long Size,
    string? Source = null,
    string? Path = null,
    bool Normalized = false)
{
    public string RenderUri => $"asset://{Id}";
}

public interface IAssetStore
{
    IAssetTransaction BeginTransaction();
    IAssetTransaction Begin() => BeginTransaction();
    IReadOnlyCollection<AssetReference> Assets => Array.Empty<AssetReference>();
}

public interface IAssetTransaction : IAsyncDisposable
{
    Task<AssetReference> PutAsync(Stream content, string fileName, string mimeType, string? source = null, CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
    ValueTask RollbackAsync() => DisposeAsync();
}

public sealed class InMemoryAssetStore : IAssetStore
{
    private readonly Dictionary<string, (AssetReference Reference, byte[] Data)> _assets = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<AssetReference> Assets => _assets.Values.Select(x => x.Reference).ToArray();
    public bool TryGetBytes(string assetId, out byte[] data)
    {
        if (_assets.TryGetValue(assetId, out var asset))
        {
            data = asset.Data.ToArray();
            return true;
        }
        data = [];
        return false;
    }
    public IAssetTransaction BeginTransaction() => new Transaction(this);

    private sealed class Transaction(InMemoryAssetStore owner) : IAssetTransaction
    {
        private readonly List<(AssetReference Reference, byte[] Data)> _pending = [];

        public async Task<AssetReference> PutAsync(Stream content, string fileName, string mimeType, string? source = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(content);
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var data = buffer.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            if (owner._assets.TryGetValue(hash, out var existing)) return existing.Reference;
            var pending = _pending.FirstOrDefault(x => x.Reference.Sha256 == hash);
            if (pending.Reference is not null) return pending.Reference;
            var safeName = Path.GetFileName(fileName);
            var reference = new AssetReference(hash, safeName, mimeType, hash, data.LongLength, source);
            _pending.Add((reference, data));
            return reference;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in _pending) owner._assets[item.Reference.Sha256] = item;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _pending.Clear();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class FileAssetStore(string rootDirectory) : IAssetStore
{
    private readonly List<AssetReference> _assets = [];
    public string RootDirectory { get; } = Path.GetFullPath(rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory)));
    public IReadOnlyCollection<AssetReference> Assets => _assets;
    public IAssetTransaction BeginTransaction() => new Transaction(this);

    private sealed class Transaction(FileAssetStore owner) : IAssetTransaction
    {
        private readonly List<string> _created = [];
        private readonly List<AssetReference> _pending = [];
        private bool _committed;

        public async Task<AssetReference> PutAsync(Stream content, string fileName, string mimeType, string? source = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(content);
            Directory.CreateDirectory(owner.RootDirectory);
            var temp = Path.Combine(owner.RootDirectory, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using (var output = File.Create(temp)) await content.CopyToAsync(output, cancellationToken);
                string hash;
                await using (var input = File.OpenRead(temp))
                {
                    hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).ToLowerInvariant();
                }
                var extension = Path.GetExtension(Path.GetFileName(fileName));
                var finalName = hash + extension;
                var finalPath = Path.Combine(owner.RootDirectory, finalName);
                if (File.Exists(finalPath))
                {
                    File.Delete(temp);
                }
                else
                {
                    File.Move(temp, finalPath);
                    _created.Add(finalPath);
                }
                var size = new FileInfo(finalPath).Length;
                var reference = new AssetReference(hash, Path.GetFileName(fileName), mimeType, hash, size, source, finalPath);
                if (_created.Contains(finalPath)) _pending.Add(reference);
                return reference;
            }
            catch
            {
                if (File.Exists(temp)) File.Delete(temp);
                throw;
            }
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _committed = true;
            owner._assets.AddRange(_pending.Where(reference => owner._assets.All(existing => existing.Id != reference.Id)));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                foreach (var path in _created)
                    if (File.Exists(path)) File.Delete(path);
            }
            _created.Clear();
            _pending.Clear();
            return ValueTask.CompletedTask;
        }
    }
}
