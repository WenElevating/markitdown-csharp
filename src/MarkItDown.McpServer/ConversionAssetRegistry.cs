using System.Collections.Concurrent;
using MarkItDown.Core;

namespace MarkItDown.McpServer;

public sealed class ConversionAssetRegistry
{
    private sealed record Entry(InMemoryAssetStore Store, DateTimeOffset ExpiresAt, long PublishedBytes);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeSpan _lifetime;
    private readonly int _maxConversions;
    private readonly long _maxPublishedBytes;
    private readonly object _gate = new();
    private long _publishedBytes;

    public ConversionAssetRegistry(
        TimeSpan? lifetime = null,
        int maxConversions = 128,
        long maxPublishedBytes = 2L * 1024 * 1024 * 1024)
    {
        _lifetime = lifetime ?? TimeSpan.FromMinutes(15);
        if (maxConversions <= 0) throw new ArgumentOutOfRangeException(nameof(maxConversions));
        if (maxPublishedBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxPublishedBytes));
        _maxConversions = maxConversions;
        _maxPublishedBytes = maxPublishedBytes;
    }

    public int RegisteredConversionCount => _entries.Count;
    public long PublishedBytes => Interlocked.Read(ref _publishedBytes);

    public void Register(string conversionId, InMemoryAssetStore store)
    {
        if (!TryRegister(conversionId, store))
            throw new ConversionException("MCP conversion resource quota exceeded.");
    }

    public bool TryRegister(string conversionId, InMemoryAssetStore store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversionId);
        ArgumentNullException.ThrowIfNull(store);
        var bytes = store.Assets.Sum(asset => asset.Size);
        lock (_gate)
        {
            CleanupExpiredUnderLock(DateTimeOffset.UtcNow);
            if (_entries.ContainsKey(conversionId)
                || _entries.Count >= _maxConversions
                || bytes > _maxPublishedBytes - _publishedBytes)
                return false;
            var entry = new Entry(store, DateTimeOffset.UtcNow.Add(_lifetime), bytes);
            if (!_entries.TryAdd(conversionId, entry)) return false;
            _publishedBytes += bytes;
            return true;
        }
    }

    public bool TryRead(string conversionId, string assetId, out byte[] bytes)
    {
        Entry? entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(conversionId, out entry))
            {
                bytes = [];
                return false;
            }
            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                RemoveUnderLock(conversionId, entry);
                bytes = [];
                return false;
            }
        }
        return entry.Store.TryGetBytes(assetId, out bytes);
    }

    public int CleanupExpired()
    {
        lock (_gate)
        {
            return CleanupExpiredUnderLock(DateTimeOffset.UtcNow);
        }
    }

    private int CleanupExpiredUnderLock(DateTimeOffset now)
    {
        var removed = 0;
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                RemoveUnderLock(pair.Key, pair.Value);
                removed++;
            }
        }
        return removed;
    }

    private void RemoveUnderLock(string conversionId, Entry entry)
    {
        if (_entries.TryRemove(new KeyValuePair<string, Entry>(conversionId, entry)))
            Interlocked.Add(ref _publishedBytes, -entry.PublishedBytes);
    }
}
