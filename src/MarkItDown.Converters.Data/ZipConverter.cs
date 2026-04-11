using System.IO.Compression;
using System.Text;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class ZipConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/zip" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("ZIP converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                using var archive = ZipFile.OpenRead(filePath);
                var engine = MarkItDownEngine.CreateWithAllConverters();

                var sections = new List<string>();
                sections.Add($"Content from the zip file `{Path.GetFileName(filePath)}`:");

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var tempPath = Path.Combine(Path.GetTempPath(),
                        $"{Guid.NewGuid():N}{Path.GetExtension(entry.Name)}");

                    try
                    {
                        entry.ExtractToFile(tempPath, overwrite: true);

                        try
                        {
                            var result = engine.ConvertAsync(tempPath, cancellationToken)
                                .GetAwaiter().GetResult();

                            sections.Add($"## File: {entry.FullName}");
                            sections.Add(result.Markdown);
                        }
                        catch (UnsupportedFormatException)
                        {
                            sections.Add($"## File: {entry.FullName}");
                            sections.Add($"*(Unsupported format: {Path.GetExtension(entry.Name)})*");
                        }
                    }
                    finally
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                }

                var markdown = string.Join(Environment.NewLine + Environment.NewLine, sections);
                return new DocumentConversionResult("Zip", markdown);
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert ZIP: {ex.Message}", ex);
            }
        }, cancellationToken);
    }
}
