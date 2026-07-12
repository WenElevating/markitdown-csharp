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
            using var input = DocumentInputMaterializer.Materialize(request, cancellationToken);
            using var document = PdfDocument.Open(input.FilePath);

            var pages = document.GetPages().ToList();
            var maxPages = request.Context?.MaxPages ?? request.Context?.Options.Limits.MaxPages;
            if (maxPages is { } pageLimit && pages.Count > pageLimit)
                throw new ConversionException($"PDF contains {pages.Count} pages, exceeding the configured limit of {pageLimit}.");
            var totalLetters = pages.Sum(p => p.Letters.Count);
            var hasImages = pages.Any(p => p.GetImages().Any());

            if (totalLetters < 20 && !hasImages)
            {
                if (request.Context?.Pipeline == PipelineMode.Multimodal && request.Context.Options.DoclingTransport is not null)
                    return ConvertWithDoclingFallbackAsync(request with { FilePath = input.FilePath }, request.Context.Options.DoclingTransport, cancellationToken);
                if (request.Context?.Pipeline == PipelineMode.Multimodal)
                {
                    var diagnostic = new ConversionDiagnostic(
                        "VISION_PROVIDER_UNAVAILABLE",
                        request.Context.Vision == VisionMode.Required ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                        "The PDF has insufficient native text and no configured vision backend was available.",
                        AffectsSubstantiveContent: true,
                        Backend: "Pdf",
                        RequiresReview: true);
                    if (request.Context.Vision == VisionMode.Required)
                    {
                        throw new ConversionException(diagnostic.Message)
                        {
                            FailureReport = ConversionFailureReport.Create(
                                diagnostic.Code, diagnostic.Message, request.Context.OperationId, "Pdf")
                        };
                    }
                    var warning = "> [!WARNING] " + diagnostic.Message;
                    var partialModel = DocumentModelBuilder.FromMarkdown("Pdf", warning, FidelityStatus.Partial, [diagnostic]);
                    return Task.FromResult(new DocumentConversionResult(
                        "Pdf", MarkdownRenderer.Render(partialModel), Document: partialModel,
                        Fidelity: FidelityStatus.Partial, Diagnostics: [diagnostic]));
                }
                throw new ConversionException(
                    "The PDF did not contain extractable text or images. Scanned or image-only PDFs require the multimodal pipeline or a vision backend.");
            }

            var assetBasePath = request.AssetBasePath;
            var assetDirName = request.Context?.Properties.TryGetValue("assetPathPrefix", out var prefix) == true && prefix is string prefixText
                ? prefixText
                : assetBasePath is not null
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
                if (assetBasePath is not null || request.Context?.AssetTransaction is not null)
                {
                    imageBlocks = PdfImageExtractor.ExtractImages(
                        page, pageNumber, assetBasePath, pageArea, seenHashes, request.Context?.AssetTransaction);
                }

                var allBlocks = textBlocks
                    .Concat(imageBlocks.Cast<PdfContentBlock>())
                    .ToList();

                // Skip TOC pages (unless it's the only content page).
                if (pages.Count > 1 && PdfLayoutAnalyzer.IsTocPage(allBlocks))
                {
                    continue;
                }

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
                if (request.Context?.Pipeline == PipelineMode.Multimodal)
                {
                    var required = request.Context.Vision == VisionMode.Required;
                    var diagnostic = new ConversionDiagnostic(
                        "VISION_PROVIDER_UNAVAILABLE", required ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                        "The PDF contained visual content that could not be decoded without a vision backend.",
                        AffectsSubstantiveContent: true, Backend: "Pdf", RequiresReview: true);
                    if (required)
                    {
                        throw new ConversionException(diagnostic.Message)
                        {
                            FailureReport = ConversionFailureReport.Create(diagnostic.Code, diagnostic.Message, request.Context.OperationId, "Pdf")
                        };
                    }
                    var partialModel = DocumentModelBuilder.FromMarkdown("Pdf", "> [!WARNING] " + diagnostic.Message, FidelityStatus.Partial, [diagnostic]);
                    return Task.FromResult(new DocumentConversionResult(
                        "Pdf", MarkdownRenderer.Render(partialModel), Document: partialModel,
                        Fidelity: FidelityStatus.Partial, Diagnostics: [diagnostic]));
                }
                throw new ConversionException(
                    "The PDF did not contain extractable text or images.");
            }

            var assetDir = assetBasePath is not null && Directory.Exists(assetBasePath)
                ? assetBasePath : null;

            var fidelity = request.Context?.Pipeline == PipelineMode.Multimodal
                ? FidelityStatus.Complete
                : FidelityStatus.NotEvaluated;
            var model = DocumentModelBuilder.FromMarkdown("Pdf", markdown, fidelity);
            markdown = MarkdownRenderer.Render(model);
            return Task.FromResult(new DocumentConversionResult(
                "Pdf", markdown, null, assetDir, model, fidelity));
        }
        catch (OperationCanceledException)
        {
            throw;
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

    private static async Task<DocumentConversionResult> ConvertWithDoclingFallbackAsync(
        DocumentConversionRequest request,
        IDoclingTransport transport,
        CancellationToken cancellationToken)
    {
        try
        {
            return await DoclingDocumentConverter.ConvertFileAsync("Pdf", request, transport, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            if (request.Context?.Vision == VisionMode.Required)
            {
                throw new ConversionException("Docling enhancement failed and is required.", ex)
                {
                    FailureReport = ConversionFailureReport.Create(
                        "DOCLING_FAILED", ex.Message, request.Context.OperationId, "Pdf")
                };
            }
            var diagnostic = new ConversionDiagnostic(
                "DOCLING_FAILED_FALLBACK_NATIVE", DiagnosticSeverity.Warning,
                $"Docling enhancement failed; native PDF output was retained: {ex.Message}",
                AffectsSubstantiveContent: true, Backend: "Docling", FallbackReason: "native-pdf", RequiresReview: true);
            var model = DocumentModelBuilder.FromMarkdown(
                "Pdf", "> [!WARNING] " + diagnostic.Message, FidelityStatus.Partial, [diagnostic]);
            return new DocumentConversionResult(
                "Pdf", MarkdownRenderer.Render(model), Document: model,
                Fidelity: FidelityStatus.Partial, Diagnostics: [diagnostic]);
        }
    }
}
