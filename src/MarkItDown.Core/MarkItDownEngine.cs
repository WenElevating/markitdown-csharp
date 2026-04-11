using System.Reflection;

namespace MarkItDown.Core;

public sealed class MarkItDownEngine
{
    private readonly ConverterRegistry _registry;

    public MarkItDownEngine(Action<ConverterRegistryBuilder> configure)
    {
        var builder = new ConverterRegistryBuilder();
        configure(builder);
        _registry = builder.Build();
    }

    public static MarkItDownEngine CreateWithAllConverters()
    {
        return new MarkItDownEngine(builder =>
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    builder.AddFromAssembly(assembly);
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that can't be loaded
                }
                catch (MissingMethodException)
                {
                    // Skip types without parameterless constructors
                }
            }
        });
    }

    public async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return await ConvertCoreAsync(request, cancellationToken);
    }

    public async Task<DocumentConversionResult> ConvertAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A file path is required.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"Input file was not found: {filePath}", filePath);
        }

        var request = new DocumentConversionRequest { FilePath = filePath };
        return await ConvertCoreAsync(request, cancellationToken);
    }

    public async Task<DocumentConversionResult> ConvertAsync(
        Stream stream,
        string? filename = null,
        string? mimeType = null,
        CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (string.IsNullOrWhiteSpace(filename) && string.IsNullOrWhiteSpace(mimeType))
        {
            throw new ArgumentException(
                "Stream input requires at least a filename or MIME type hint.");
        }

        var request = new DocumentConversionRequest
        {
            Stream = stream,
            Filename = filename,
            MimeType = mimeType
        };

        return await ConvertCoreAsync(request, cancellationToken);
    }

    private async Task<DocumentConversionResult> ConvertCoreAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken)
    {
        var converter = _registry.FindConverter(request);

        if (converter is null)
        {
            var extension = Path.GetExtension(request.Filename ?? request.FilePath);
            throw new UnsupportedFormatException(
                $"No converter registered for format '{extension}'.");
        }

        try
        {
            return await converter.ConvertAsync(request, cancellationToken);
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var filename = request.Filename ?? request.FilePath ?? "stream";
            throw new ConversionException(
                $"Failed to convert '{filename}': {ex.Message}", ex);
        }
    }
}
