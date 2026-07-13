using System.IO.Compression;
using System.Text;
using MarkItDown.Core;

namespace MarkItDown.Converters.Office;

public static class OfficePackageGuard
{
    public static void Validate(
        string path,
        ConversionLimits limits,
        bool allowExternalRelationships = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(limits);

        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count > limits.MaxPackageEntries)
                throw new ConversionException($"Office package contains too many entries ({archive.Entries.Count}).");

            long totalLength = 0;
            foreach (var entry in archive.Entries)
            {
                ValidateEntryName(entry.FullName);
                totalLength = checked(totalLength + entry.Length);
                if (totalLength > limits.MaxPackageUncompressedBytes)
                    throw new ConversionException("Office package exceeds the configured uncompressed size limit.");

                if (!allowExternalRelationships && entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                    RejectExternalRelationship(entry);
            }
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            throw new ConversionException($"Invalid Office package: {ex.Message}", ex);
        }
        catch (OverflowException ex)
        {
            throw new ConversionException("Office package size exceeds the configured limit.", ex);
        }
    }

    private static void ValidateEntryName(string name)
    {
        var normalized = name.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
            throw new ConversionException($"Office package contains an unsafe entry path: '{name}'.");
    }

    private static void RejectExternalRelationship(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);
        var content = reader.ReadToEnd();
        if (content.Contains("TargetMode=\"External\"", StringComparison.OrdinalIgnoreCase)
            || content.Contains("TargetMode='External'", StringComparison.OrdinalIgnoreCase))
            throw new ConversionException($"External Office relationships are disabled: '{entry.FullName}'.");
    }
}
