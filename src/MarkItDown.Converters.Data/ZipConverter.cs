using System.IO.Compression;
using System.Text;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class ZipConverter : BaseConverter
{
    private const int MaxEntries = 1_000;
    private const long MaxTotalUncompressedBytes = 100L * 1024 * 1024;
    private const int MaxContainerDepth = 3;

    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/zip" };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("ZIP converter requires a file path.");

        if (request.ContainerDepth >= MaxContainerDepth)
        {
            throw new ConversionException("Maximum nested archive depth exceeded.");
        }

        return await Task.Run(() =>
        {
            try
            {
                using var archive = ZipFile.OpenRead(filePath);
                var engine = MarkItDownEngine.CreateWithAllConverters();

                if (archive.Entries.Count > MaxEntries)
                {
                    throw new ConversionException($"ZIP contains too many entries. Maximum supported entries: {MaxEntries}.");
                }

                var sections = new List<string>();
                sections.Add($"Content from the zip file `{Path.GetFileName(filePath)}`:");

                long totalUncompressedBytes = 0;

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    totalUncompressedBytes = checked(totalUncompressedBytes + entry.Length);
                    if (totalUncompressedBytes > MaxTotalUncompressedBytes)
                    {
                        throw new ConversionException($"ZIP uncompressed content exceeds {MaxTotalUncompressedBytes / 1024 / 1024} MB.");
                    }

                    var tempPath = Path.Combine(Path.GetTempPath(),
                        $"{Guid.NewGuid():N}{Path.GetExtension(entry.Name)}");

                    try
                    {
                        entry.ExtractToFile(tempPath, overwrite: true);

                        try
                        {
                            var result = engine.ConvertAsync(new DocumentConversionRequest
                                {
                                    FilePath = tempPath,
                                    ContainerDepth = request.ContainerDepth + 1
                                }, cancellationToken)
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
