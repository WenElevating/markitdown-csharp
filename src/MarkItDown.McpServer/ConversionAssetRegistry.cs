using System.Collections.Concurrent;
using MarkItDown.Core;

namespace MarkItDown.McpServer;

public sealed class ConversionAssetRegistry
{
    private sealed record Entry(InMemoryAssetStore Store, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeSpan _lifetime;

    public ConversionAssetRegistry(TimeSpan? lifetime = null)
    {
        _lifetime = lifetime ?? TimeSpan.FromMinutes(15);
    }

    public void Register(string conversionId, InMemoryAssetStore store)
    {
        CleanupExpired();
        _entries[conversionId] = new Entry(store, DateTimeOffset.UtcNow.Add(_lifetime));
    }

    public bool TryRead(string conversionId, string assetId, out byte[] bytes)
    {
        if (!_entries.TryGetValue(conversionId, out var entry))
        {
            bytes = [];
            return false;
        }
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _entries.TryRemove(conversionId, out _);
            bytes = [];
            return false;
        }
        return entry.Store.TryGetBytes(assetId, out bytes);
    }

    public int CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var removed = 0;
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now && _entries.TryRemove(pair.Key, out _)) removed++;
        }
        return removed;
    }
}
