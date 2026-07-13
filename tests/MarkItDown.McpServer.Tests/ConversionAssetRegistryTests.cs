using System.Text;
using MarkItDown.Core;
using MarkItDown.McpServer;

namespace MarkItDown.McpServer.Tests;

public sealed class ConversionAssetRegistryTests
{
    [Fact]
    public async Task Registry_ReadsOnlyRegisteredAssetsAndExpiresEntries()
    {
        var store = new InMemoryAssetStore();
        await using (var transaction = store.BeginTransaction())
        {
            await transaction.PutAsync(new MemoryStream(Encoding.UTF8.GetBytes("asset")), "x.txt", "text/plain");
            await transaction.CommitAsync();
        }
        var asset = store.Assets.Single();
        var registry = new ConversionAssetRegistry(TimeSpan.FromMilliseconds(1));
        registry.Register(new string('a', 64), store);

        Assert.True(registry.TryRead(new string('a', 64), asset.Id, out var bytes));
        Assert.Equal("asset", Encoding.UTF8.GetString(bytes));
        await Task.Delay(10);
        Assert.False(registry.TryRead(new string('a', 64), asset.Id, out _));
        Assert.False(registry.TryRead(new string('b', 64), asset.Id, out _));
    }

    [Fact]
    public async Task Registry_EnforcesPublishedConversionAndByteQuotas()
    {
        var smallStore = await CreateStoreAsync("123");
        var largeStore = await CreateStoreAsync("1234");
        var registry = new ConversionAssetRegistry(
            TimeSpan.FromMinutes(15), maxConversions: 1, maxPublishedBytes: 3);

        Assert.True(registry.TryRegister(new string('a', 64), smallStore));
        Assert.False(registry.TryRegister(new string('b', 64), smallStore));
        Assert.False(registry.TryRegister(new string('c', 64), largeStore));
        Assert.Equal(1, registry.RegisteredConversionCount);
        Assert.Equal(3, registry.PublishedBytes);
    }

    private static async Task<InMemoryAssetStore> CreateStoreAsync(string content)
    {
        var store = new InMemoryAssetStore();
        await using var transaction = store.BeginTransaction();
        await transaction.PutAsync(new MemoryStream(Encoding.UTF8.GetBytes(content)), "x.txt", "text/plain");
        await transaction.CommitAsync();
        return store;
    }
}
