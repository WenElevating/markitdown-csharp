using System.Security.Cryptography;
using UglyToad.PdfPig.Content;

namespace MarkItDown.Converters.Pdf;

internal static class PdfImageExtractor
{
    private const int MinImageDimension = 20;
    private const double MinAreaRatio = 0.01;

    internal static List<PdfImageBlock> ExtractImages(
        Page page,
        int pageNumber,
        string assetBasePath,
        double pageArea,
        Dictionary<string, string> seenHashes)
    {
        var images = page.GetImages().ToList();
        if (images.Count == 0)
        {
            return [];
        }

        Directory.CreateDirectory(assetBasePath);
        var blocks = new List<PdfImageBlock>();

        for (var i = 0; i < images.Count; i++)
        {
            var pdfImage = images[i];

            if (!MeetsSizeThreshold(pdfImage, pageArea))
            {
                continue;
            }

            var saved = SaveImage(pdfImage, pageNumber, i, assetBasePath, seenHashes);
            if (saved is null)
            {
                continue;
            }

            var bounds = pdfImage.Bounds;
            var y = (bounds.Top + bounds.Bottom) / 2.0;

            blocks.Add(new PdfImageBlock(
                Y: y,
                Top: bounds.Top,
                Bottom: bounds.Bottom,
                PageNumber: pageNumber,
                ImageIndex: i,
                FileName: saved));
        }

        return blocks;
    }

    private static bool MeetsSizeThreshold(IPdfImage image, double pageArea)
    {
        if (image.WidthInSamples < MinImageDimension || image.HeightInSamples < MinImageDimension)
        {
            return false;
        }

        var imageArea = image.Bounds.Width * image.Bounds.Height;
        if (pageArea > 0 && imageArea / pageArea < MinAreaRatio)
        {
            return false;
        }

        return true;
    }

    private static string? SaveImage(
        IPdfImage image,
        int pageNumber,
        int imageIndex,
        string assetBasePath,
        Dictionary<string, string> seenHashes)
    {
        byte[]? imageBytes = null;
        string extension;

        if (image.TryGetPng(out var pngBytes))
        {
            imageBytes = pngBytes;
            extension = ".png";
        }
        else if (TryGetJpegBytes(image, out var jpegBytes))
        {
            imageBytes = jpegBytes;
            extension = ".jpg";
        }
        else
        {
            return null;
        }

        var hash = Convert.ToHexString(SHA256.HashData(imageBytes));
        if (seenHashes.TryGetValue(hash, out var existingFileName))
        {
            return existingFileName;
        }

        var fileName = $"page{pageNumber}_img{imageIndex}{extension}";
        var fullPath = Path.Combine(assetBasePath, fileName);
        File.WriteAllBytes(fullPath, imageBytes);

        seenHashes[hash] = fileName;
        return fileName;
    }

    private static bool TryGetJpegBytes(IPdfImage image, out byte[] bytes)
    {
        // RawBytes is IReadOnlyList<byte> — use .Count and indexing
        var raw = image.RawBytes;
        if (raw is not null && raw.Count >= 2 && raw[0] == 0xFF && raw[1] == 0xD8)
        {
            bytes = raw.ToArray();
            return true;
        }

        // TryGetBytes is on IPdfImage interface directly
        if (image.TryGetBytes(out var decoded))
        {
            if (decoded.Count >= 2 && decoded[0] == 0xFF && decoded[1] == 0xD8)
            {
                bytes = decoded.ToArray();
                return true;
            }
        }

        bytes = [];
        return false;
    }
}
