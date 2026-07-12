using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using MarkItDown.Core;

namespace MarkItDown.Converters.Office;

internal static class OfficeAssetExtractor
{
    public static string Extract(IEnumerable<ImagePart> imageParts, DocumentConversionRequest request)
    {
        var parts = imageParts.ToList();
        if (parts.Count == 0) return string.Empty;
        var lines = new List<string>();
        foreach (var part in parts)
        {
            using var input = part.GetStream();
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var extension = MimeToExtension(part.ContentType);
            var fileName = hash + extension;
            string reference;
            if (!string.IsNullOrWhiteSpace(request.AssetBasePath))
            {
                Directory.CreateDirectory(request.AssetBasePath);
                var path = Path.Combine(request.AssetBasePath, fileName);
                if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
                var prefix = request.Context?.Properties.TryGetValue("assetPathPrefix", out var configuredPrefix) == true && configuredPrefix is string prefixText
                    ? prefixText
                    : Path.GetFileName(request.AssetBasePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                reference = Path.Combine(prefix, fileName).Replace('\\', '/');
            }
            else if (request.Context?.AssetTransaction is not null)
            {
                var asset = request.Context.AssetTransaction.PutAsync(
                    new MemoryStream(bytes), fileName, part.ContentType, cancellationToken: CancellationToken.None)
                    .GetAwaiter().GetResult();
                reference = $"asset://{asset.Id}";
            }
            else
            {
                reference = $"asset://{hash}";
            }
            lines.Add($"![Embedded image]({reference})");
        }
        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private static string MimeToExtension(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/tiff" => ".tif",
        _ => ".bin"
    };
}
