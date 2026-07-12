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
        return await Task.Run(() =>
        {
            try
            {
                using var input = DocumentInputMaterializer.Materialize(request, cancellationToken);
                using var doc = PresentationDocument.Open(input.FilePath, false);
                var presentationPart = doc.PresentationPart
                    ?? throw new ConversionException("Invalid PPTX file.");

                var slideParts = presentationPart.SlideParts.ToList();
                if (slideParts.Count == 0)
                    return new DocumentConversionResult("Pptx", string.Empty);

                var sections = new List<string>();

                foreach (var slidePart in slideParts)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var blocks = new List<string>();

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
                            blocks.Add($"## {text.Trim()}");
                        }
                        else
                        {
                            // Split multi-line text into bullet points
                            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                var trimmed = line.Trim();
                                if (!string.IsNullOrWhiteSpace(trimmed))
                                    blocks.Add($"- {trimmed}");
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
                        blocks.Add($"> {string.Join(" ", notesText)}");
                    }

                    if (blocks.Count > 0)
                        sections.Add(string.Join(Environment.NewLine + Environment.NewLine, blocks));
                }

                var markdown = string.Join(
                    Environment.NewLine + Environment.NewLine + "---" + Environment.NewLine + Environment.NewLine,
                    sections);
                var images = OfficeAssetExtractor.Extract(
                    slideParts.SelectMany(slide => slide.ImageParts), request);
                if (!string.IsNullOrWhiteSpace(images))
                    markdown = string.IsNullOrWhiteSpace(markdown) ? images : markdown + Environment.NewLine + Environment.NewLine + images;
                var fidelity = request.Context?.Pipeline == PipelineMode.Multimodal
                    ? FidelityStatus.NotEvaluated : FidelityStatus.NotEvaluated;
                var document = DocumentModelBuilder.FromMarkdown("Pptx", markdown, fidelity);
                markdown = MarkdownRenderer.Render(document);
                return new DocumentConversionResult("Pptx", markdown, Document: document, Fidelity: fidelity);
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
