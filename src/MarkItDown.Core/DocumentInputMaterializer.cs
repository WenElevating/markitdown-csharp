namespace MarkItDown.Core;

public sealed class MaterializedDocument : IDisposable
{
    private readonly bool _deleteOnDispose;
    public string FilePath { get; }

    internal MaterializedDocument(string filePath, bool deleteOnDispose)
    {
        FilePath = filePath;
        _deleteOnDispose = deleteOnDispose;
    }

    public void Dispose()
    {
        if (_deleteOnDispose && File.Exists(FilePath)) File.Delete(FilePath);
    }
}

public static class DocumentInputMaterializer
{
    public static MaterializedDocument Materialize(DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.FilePath))
            return new MaterializedDocument(request.FilePath, deleteOnDispose: false);
        if (request.Stream is null)
            throw new ConversionException("A file path or stream is required.");

        var extension = Path.GetExtension(request.Filename ?? string.Empty);
        var path = Path.Combine(Path.GetTempPath(), "markitdown-" + Guid.NewGuid().ToString("N") + extension);
        var maxBytes = request.Context?.Options.Limits.MaxInputBytes ?? 256L * 1024 * 1024;
        long total = 0;
        try
        {
            using var output = File.Create(path);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = request.Stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += read;
                if (total > maxBytes) throw new ConversionException($"Input exceeds the configured byte limit of {maxBytes}.");
                output.Write(buffer, 0, read);
            }
            return new MaterializedDocument(path, deleteOnDispose: true);
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }
}
