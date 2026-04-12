using UglyToad.PdfPig;
using MarkItDown.Core;

namespace MarkItDown.Converters.Pdf;

public sealed class PdfConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    public override IReadOnlySet<string> SupportedMimeTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/pdf" };

    public override Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var document = PdfDocument.Open(request.FilePath);

            var pages = document.GetPages().ToList();
            var totalLetters = pages.Sum(p => p.Letters.Count);
            var hasImages = pages.Any(p => p.GetImages().Any());

            if (totalLetters < 20 && !hasImages)
            {
                throw new ConversionException(
                    "Scanned or image-only PDFs are not supported in this MVP.");
            }

            var assetBasePath = request.AssetBasePath;
            var assetDirName = assetBasePath is not null
                ? Path.GetFileName(assetBasePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : null;

            // --- Pass 1: Extract text blocks from all pages for header/footer detection ---
            var allPageTextBlocks = new List<List<PdfContentBlock>>();
            double? bodyFontSize = null;

            foreach (var page in pages)
            {
                if (bodyFontSize is null && page.Letters.Count > 0)
                {
                    bodyFontSize = PdfTextClassifier.ComputeBodyFontSize(page.Letters);
                }

                var textBlocks = PdfTextClassifier.ClassifyTextBlocks(page)
                    .Cast<PdfContentBlock>().ToList();
                allPageTextBlocks.Add(textBlocks);
            }

            var fontSize = bodyFontSize ?? 12.0;

            // Detect headers/footers across pages
            var avgPageHeight = pages.Average(p => p.Height);
            PdfLayoutAnalyzer.DetectHeadersFooters(allPageTextBlocks, avgPageHeight);

            // --- Pass 2: Per-page processing with filtered blocks ---
            var seenHashes = new Dictionary<string, string>();
            var pageMarkdowns = new List<string>();

            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = pages[pageIndex];
                var pageNumber = pageIndex + 1;
                var pageArea = page.Width * page.Height;

                // Get text blocks with header/footer flags from pass 1
                var textBlocks = allPageTextBlocks[pageIndex];

                // Extract images
                var imageBlocks = new List<PdfImageBlock>();
                if (assetBasePath is not null)
                {
                    imageBlocks = PdfImageExtractor.ExtractImages(
                        page, pageNumber, assetBasePath, pageArea, seenHashes);
                }

                var allBlocks = textBlocks
                    .Concat(imageBlocks.Cast<PdfContentBlock>())
                    .ToList();

                var pageMarkdown = PdfContentGrouper.RenderPage(allBlocks, fontSize, assetDirName);

                if (!string.IsNullOrWhiteSpace(pageMarkdown))
                {
                    pageMarkdowns.Add(pageMarkdown);
                }
            }

            var markdown = string.Join(
                $"{Environment.NewLine}{Environment.NewLine}---{Environment.NewLine}{Environment.NewLine}",
                pageMarkdowns).Trim();

            if (string.IsNullOrWhiteSpace(markdown))
            {
                throw new ConversionException(
                    "The PDF did not contain extractable text or images.");
            }

            var assetDir = assetBasePath is not null && Directory.Exists(assetBasePath)
                ? assetBasePath : null;

            return Task.FromResult(new DocumentConversionResult(
                "Pdf", markdown, null, assetDir));
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConversionException("Failed to convert PDF to Markdown.", ex);
        }
    }
}
