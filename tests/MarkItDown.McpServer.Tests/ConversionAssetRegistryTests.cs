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
}
