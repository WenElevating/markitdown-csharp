using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using MarkItDown.Core;

namespace MarkItDown.Converters.Office;

public sealed class PptxConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pptx" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/vnd.openxmlformats-officedocument.presentationml.presentation"
        };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            try
            {
                using var input = DocumentInputMaterializer.Materialize(request, cancellationToken);
                OfficePackageGuard.Validate(input.FilePath,
                    request.Context?.Options.Limits ?? new ConversionLimits(),
                    request.Context?.Options.Privacy.AllowExternalRelationships == true);
                using var doc = PresentationDocument.Open(input.FilePath, false);
                var presentationPart = doc.PresentationPart
                    ?? throw new ConversionException("Invalid PPTX file.");

                var slideParts = presentationPart.SlideParts.ToList();
                if (slideParts.Count == 0)
                {
                    var emptyFidelity = request.Context?.Pipeline == PipelineMode.Multimodal
                        ? FidelityStatus.Complete : FidelityStatus.NotEvaluated;
                    var emptyDocument = new DocumentModel("Pptx", [], Fidelity: emptyFidelity);
                    return await OfficeDoclingEnhancer.EnhanceAsync(
                        "Pptx", request, input.FilePath, emptyDocument, cancellationToken);
                }

                var nativeBlocks = new List<DocumentBlock>();

                for (var slideIndex = 0; slideIndex < slideParts.Count; slideIndex++)
                {
                    var slidePart = slideParts[slideIndex];
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = new SourceLocation(Slide: slideIndex + 1);

                    foreach (var shape in slidePart.Slide?.Descendants<Shape>() ?? [])
                    {
                        var text = ExtractShapeText(shape);
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        var placeholderType = shape.NonVisualShapeProperties?
                            .ApplicationNonVisualDrawingProperties?
                            .PlaceholderShape?.Type?.Value;

                        if (placeholderType == PlaceholderValues.Title ||
                            placeholderType == PlaceholderValues.CenteredTitle)
                        {
                            nativeBlocks.Add(new DocumentBlock(
                                "heading", text.Trim(), source,
                                new Dictionary<string, string> { ["level"] = "2" }));
                        }
                        else
                        {
                            // Split multi-line text into bullet points
                            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                var trimmed = line.Trim();
                                if (!string.IsNullOrWhiteSpace(trimmed))
                                    nativeBlocks.Add(new DocumentBlock("list", $"- {trimmed}", source));
                            }
                        }
                    }

                    // Notes
                    var notesText = slidePart.NotesSlidePart?
                        .NotesSlide?.Descendants<A.Text>()
                        .Select(t => t.Text)
                        .Where(t => !string.IsNullOrWhiteSpace(t));

                    if (notesText is not null && notesText.Any())
                    {
                        nativeBlocks.Add(new DocumentBlock("note", $"> {string.Join(" ", notesText)}", source));
                    }

                    if (slideIndex < slideParts.Count - 1 && nativeBlocks.Count > 0)
                        nativeBlocks.Add(new DocumentBlock("pagebreak", "---", source));
                }

                var images = OfficeAssetExtractor.Extract(
                    slideParts.SelectMany(slide => slide.ImageParts), request);
                if (!string.IsNullOrWhiteSpace(images))
                    nativeBlocks.Add(new DocumentBlock("figure", images));
                var fidelity = request.Context?.Pipeline == PipelineMode.Multimodal
                    ? FidelityStatus.Complete : FidelityStatus.NotEvaluated;
                var document = new DocumentModel("Pptx", nativeBlocks, Fidelity: fidelity);
                return await OfficeDoclingEnhancer.EnhanceAsync(
                    "Pptx", request, input.FilePath, document, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert PPTX: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    private static string ExtractShapeText(Shape shape)
    {
        var texts = shape.Descendants<A.Text>().Select(t => t.Text);
        return string.Join("", texts);
    }
}
