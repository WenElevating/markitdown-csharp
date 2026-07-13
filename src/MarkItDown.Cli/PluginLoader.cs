using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using MarkItDown.Core;

namespace MarkItDown.Cli;

public sealed record PluginManifest(
    string Id,
    string Version,
    string EntryAssembly,
    string EntryType,
    IReadOnlyList<string>? Capabilities = null,
    IReadOnlyList<string>? Platforms = null);

public sealed class LoadedPlugin
{
    internal LoadedPlugin(
        string directory,
        PluginManifest? manifest,
        bool isLoaded,
        string status,
        IMarkItDownPlugin? instance = null,
        IReadOnlyList<IOcrProvider>? ocrProviders = null)
    {
        Directory = directory;
        Manifest = manifest;
        IsLoaded = isLoaded;
        Status = status;
        Instance = instance;
        OcrProviders = ocrProviders ?? [];
    }

    public string Directory { get; }
    public PluginManifest? Manifest { get; }
    public bool IsLoaded { get; }
    public string Status { get; }
    public IMarkItDownPlugin? Instance { get; }
    public IReadOnlyList<IOcrProvider> OcrProviders { get; }
}

public sealed class PluginCatalog
{
    internal PluginCatalog(IReadOnlyList<LoadedPlugin> plugins, IReadOnlyList<AssemblyLoadContext> loadContexts)
    {
        Plugins = plugins;
        _loadContexts = loadContexts;
    }

    private readonly IReadOnlyList<AssemblyLoadContext> _loadContexts;
    public IReadOnlyList<LoadedPlugin> Plugins { get; }

    public IReadOnlyList<IOcrProvider> GetOcrProviders() =>
        Plugins.Where(p => p.IsLoaded).SelectMany(p => p.OcrProviders).ToArray();

    public IOcrProvider? SelectOcrProvider(string mode, ConversionContext context, out string? reason)
    {
        reason = null;
        if (string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase))
            return null;

        var providers = GetOcrProviders();
        if (string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var provider = providers.FirstOrDefault(p => IsAvailable(p, context));
            if (provider is null)
                reason = "OCR_PROVIDER_UNAVAILABLE: no installed OCR provider is available for this environment.";
            return provider;
        }

        var selected = providers.FirstOrDefault(p => string.Equals(p.Id, mode, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            reason = $"OCR_PROVIDER_UNAVAILABLE: OCR provider '{mode}' is not installed.";
            return null;
        }

        if (!IsAvailable(selected, context))
        {
            reason = $"OCR_PROVIDER_UNAVAILABLE: OCR provider '{mode}' is not available for this environment.";
            return null;
        }

        return selected;
    }

    private static bool IsAvailable(IOcrProvider provider, ConversionContext context)
    {
        try { return provider.IsAvailable(context); }
        catch { return false; }
    }
}

public static class PluginLoader
{
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<string> GetDefaultDirectories() =>
    [
        Path.Combine(AppContext.BaseDirectory, "plugins"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarkItDown", "plugins")
    ];

    public static PluginCatalog Load(IEnumerable<string>? directories = null)
    {
        var plugins = new List<LoadedPlugin>();
        var contexts = new List<AssemblyLoadContext>();
        var roots = (directories ?? GetDefaultDirectories())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (!System.IO.Directory.Exists(root)) continue;
            foreach (var directory in DiscoverPluginDirectories(root))
                plugins.Add(LoadOne(directory, contexts));
        }

        return new PluginCatalog(plugins, contexts);
    }

    private static IEnumerable<string> DiscoverPluginDirectories(string root)
    {
        if (File.Exists(Path.Combine(root, "plugin.json")))
            yield return root;

        foreach (var directory in System.IO.Directory.EnumerateDirectories(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(directory, "plugin.json")))
                yield return directory;
        }
    }

    private static LoadedPlugin LoadOne(string directory, ICollection<AssemblyLoadContext> contexts)
    {
        var manifestPath = Path.Combine(directory, "plugin.json");
        PluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonOptions);
            ValidateManifest(manifest);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new LoadedPlugin(directory, null, false, $"Invalid manifest: {ex.Message}");
        }

        if (!IsCompatiblePlatform(manifest!.Platforms))
            return new LoadedPlugin(directory, manifest, false, "Incompatible platform.");

        var entryPath = Path.GetFullPath(Path.Combine(directory, manifest.EntryAssembly));
        if (!IsWithinDirectory(entryPath, directory) || !File.Exists(entryPath))
            return new LoadedPlugin(directory, manifest, false, "Entry assembly was not found in the plugin directory.");

        try
        {
            var loadContext = new PluginLoadContext(entryPath);
            contexts.Add(loadContext);
            using var entryStream = File.OpenRead(entryPath);
            var assembly = loadContext.LoadFromStream(entryStream);
            var type = assembly.GetType(manifest.EntryType, throwOnError: false, ignoreCase: false)
                ?? throw new InvalidDataException($"Entry type '{manifest.EntryType}' was not found.");
            if (!typeof(IMarkItDownPlugin).IsAssignableFrom(type))
                throw new InvalidDataException($"Entry type '{manifest.EntryType}' does not implement IMarkItDownPlugin.");
            if (Activator.CreateInstance(type) is not IMarkItDownPlugin plugin)
                throw new InvalidDataException("Plugin entry type could not be instantiated.");
            if (!string.Equals(plugin.Id, manifest.Id, StringComparison.Ordinal))
                throw new InvalidDataException("Plugin id does not match plugin.json.");

            var registration = new MarkItDownPluginContext();
            plugin.Register(registration);
            return new LoadedPlugin(directory, manifest, true, "Loaded.", plugin, registration.OcrProviders);
        }
        catch (Exception ex)
        {
            return new LoadedPlugin(directory, manifest, false, $"Load failed: {ex.Message}");
        }
    }

    private static void ValidateManifest(PluginManifest? manifest)
    {
        if (manifest is null) throw new InvalidDataException("Manifest is empty.");
        if (!IdPattern.IsMatch(manifest.Id)) throw new InvalidDataException("Plugin id is invalid.");
        if (string.IsNullOrWhiteSpace(manifest.Version)) throw new InvalidDataException("Plugin version is required.");
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) || Path.GetFileName(manifest.EntryAssembly) != manifest.EntryAssembly)
            throw new InvalidDataException("Entry assembly must be a file name in the plugin directory.");
        if (string.IsNullOrWhiteSpace(manifest.EntryType)) throw new InvalidDataException("Entry type is required.");
        if (string.Equals(Path.GetFileNameWithoutExtension(manifest.EntryAssembly), "MarkItDown.Core", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileNameWithoutExtension(manifest.EntryAssembly), "MarkItDown.Cli", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A plugin cannot replace MarkItDown.Core or MarkItDown.Cli.");
    }

    private static bool IsCompatiblePlatform(IReadOnlyList<string>? platforms)
    {
        if (platforms is null || platforms.Count == 0) return true;
        var current = $"{(OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "osx" : "unknown")}-{(RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : RuntimeInformation.OSArchitecture == Architecture.X64 ? "x64" : RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant())}";
        return platforms.Any(platform => string.Equals(platform, current, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWithinDirectory(string file, string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return file.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PluginLoadContext(string mainAssemblyPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, typeof(IMarkItDownPlugin).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
                return typeof(IMarkItDownPlugin).Assembly;
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
