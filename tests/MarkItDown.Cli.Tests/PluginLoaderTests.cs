using MarkItDown.Cli;

namespace MarkItDown.Cli.Tests;

public sealed class PluginLoaderTests
{
    [Fact]
    public void LoadFromDirectory_ReportsInvalidManifestWithoutThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"markitdown-plugin-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "broken"));
            File.WriteAllText(Path.Combine(root, "broken", "plugin.json"), "{ not-json }");

            var catalog = PluginLoader.Load([root]);

            var plugin = Assert.Single(catalog.Plugins);
            Assert.False(plugin.IsLoaded);
            Assert.Contains("manifest", plugin.Status, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadFromDirectory_SkipsMissingDirectory()
    {
        var catalog = PluginLoader.Load([Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))]);

        Assert.Empty(catalog.Plugins);
    }

    [Fact]
    public void LoadFromDirectory_LoadsDeclaredEntryAndRegistersProvider()
    {
        var root = Path.Combine(Path.GetTempPath(), $"markitdown-plugin-test-{Guid.NewGuid():N}");
        var plugin = Path.Combine(root, "sample");
        Directory.CreateDirectory(plugin);
        var assemblyName = Path.GetFileName(typeof(PluginLoaderTests).Assembly.Location);
        File.Copy(typeof(PluginLoaderTests).Assembly.Location, Path.Combine(plugin, assemblyName));
        File.WriteAllText(Path.Combine(plugin, "plugin.json"), $$"""
            {
              "id": "sample-plugin",
              "version": "1.0.0",
              "entryAssembly": "{{assemblyName}}",
              "entryType": "MarkItDown.Cli.Tests.SamplePlugin",
              "capabilities": ["ocr"]
            }
            """);
        try
        {
            var catalog = PluginLoader.Load([root]);

            var loaded = Assert.Single(catalog.Plugins);
            Assert.True(loaded.IsLoaded, loaded.Status);
            Assert.Single(loaded.OcrProviders);
            Assert.Equal("sample-ocr", loaded.OcrProviders[0].Id);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DefaultDirectories_AreStableAndIncludeLocalAndUserLocations()
    {
        var directories = PluginLoader.GetDefaultDirectories();

        Assert.Contains(Path.Combine(AppContext.BaseDirectory, "plugins"), directories);
        Assert.Contains(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarkItDown", "plugins"), directories);
    }
}

public sealed class SamplePlugin : MarkItDown.Core.IMarkItDownPlugin
{
    public string Id => "sample-plugin";
    public string Version => "1.0.0";
    public IReadOnlyCollection<string> Capabilities => ["ocr"];

    public void Register(MarkItDown.Core.MarkItDownPluginContext context) =>
        context.RegisterOcrProvider(new SampleOcrProvider());

    private sealed class SampleOcrProvider : MarkItDown.Core.IOcrProvider
    {
        public string Id => "sample-ocr";
        public bool IsAvailable(MarkItDown.Core.ConversionContext context) => true;
        public Task<MarkItDown.Core.OcrDocumentResult> RecognizeAsync(
            MarkItDown.Core.OcrRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
