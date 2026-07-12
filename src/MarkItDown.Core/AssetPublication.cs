using System.Security.Cryptography;
using System.Text;

namespace MarkItDown.Core;

public sealed class AssetPublicationLease : IDisposable
{
    private readonly FileStream _lock;
    private bool _disposed;
    private bool _completed;

    internal AssetPublicationLease(FileStream @lock, string ownerId, string publicationId, string assetDirectory)
    {
        _lock = @lock;
        OwnerId = ownerId;
        PublicationId = publicationId;
        AssetDirectory = assetDirectory;
    }

    public string OwnerId { get; }
    public string PublicationId { get; }
    public string AssetDirectory { get; }

    public void Complete() => _completed = true;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_completed && Directory.Exists(AssetDirectory))
        {
            try { Directory.Delete(AssetDirectory, recursive: true); } catch { }
        }
        _lock.Dispose();
    }
}

public static class AssetPublication
{
    public static AssetPublicationLease Acquire(string assetRoot, string outputIdentity, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(assetRoot);
        Directory.CreateDirectory(root);
        var ownerId = ComputeOwnerId(outputIdentity);
        var locks = Path.Combine(root, ".locks");
        Directory.CreateDirectory(locks);
        var lockPath = Path.Combine(locks, ownerId + ".lock");
        FileStream? handle = null;
        while (handle is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                handle = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                Thread.Sleep(25);
            }
        }
        var publicationId = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(root, ownerId, publicationId);
        Directory.CreateDirectory(directory);
        return new AssetPublicationLease(handle, ownerId, publicationId, directory);
    }

    public static string ComputeOwnerId(string outputIdentity)
    {
        var normalized = Path.GetFullPath(outputIdentity);
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant()[..32];
    }

    public static void CleanupOldPublications(AssetPublicationLease lease)
    {
        var ownerDirectory = Directory.GetParent(lease.AssetDirectory)?.FullName;
        if (ownerDirectory is null || !Directory.Exists(ownerDirectory)) return;
        foreach (var directory in Directory.GetDirectories(ownerDirectory))
        {
            if (string.Equals(directory, lease.AssetDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }
}
