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
    private readonly HashSet<string> _activeConversions = new(StringComparer.Ordinal);
    private long _reservedBytes;
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
    public long ReservedBytes => Interlocked.Read(ref _reservedBytes);
    public int ActiveConversionCount { get { lock (_gate) return _activeConversions.Count; } }

    public bool TryAcquireConversion(out IDisposable lease) =>
        TryAcquireConversion(Guid.NewGuid().ToString("N"), out lease);

    public bool TryAcquireConversion(string conversionId, out IDisposable lease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversionId);
        lock (_gate)
        {
            CleanupExpiredUnderLock(DateTimeOffset.UtcNow);
            if (_entries.Count + _activeConversions.Count >= _maxConversions
                || !_activeConversions.Add(conversionId))
            {
                lease = DisposableLease.Empty;
                return false;
            }
            lease = new DisposableLease(() => ReleaseActive(conversionId));
            return true;
        }
    }

    public bool TryReserveBytes(long bytes, out IDisposable lease)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        lock (_gate)
        {
            CleanupExpiredUnderLock(DateTimeOffset.UtcNow);
            if (bytes > _maxPublishedBytes - _publishedBytes - _reservedBytes)
            {
                lease = DisposableLease.Empty;
                return false;
            }
            _reservedBytes += bytes;
            lease = new DisposableLease(() => ReleaseReservedBytes(bytes));
            return true;
        }
    }

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
            var transferred = _activeConversions.Remove(conversionId);
            if (_entries.ContainsKey(conversionId)
                || _entries.Count + _activeConversions.Count >= _maxConversions
                || bytes > _maxPublishedBytes - _publishedBytes)
            {
                if (transferred) _activeConversions.Add(conversionId);
                return false;
            }
            var entry = new Entry(store, DateTimeOffset.UtcNow.Add(_lifetime), bytes);
            if (!_entries.TryAdd(conversionId, entry))
            {
                if (transferred) _activeConversions.Add(conversionId);
                return false;
            }
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

    private void ReleaseActive(string conversionId)
    {
        lock (_gate) _activeConversions.Remove(conversionId);
    }

    private void ReleaseReservedBytes(long bytes)
    {
        lock (_gate) _reservedBytes -= bytes;
    }

    private sealed class DisposableLease(Action release) : IDisposable
    {
        public static readonly DisposableLease Empty = new(() => { });
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) release();
        }
    }
}
