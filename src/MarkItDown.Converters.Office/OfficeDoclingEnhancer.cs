using MarkItDown.Core;

namespace MarkItDown.Converters.Office;

internal static class OfficeDoclingEnhancer
{
    public static async Task<DocumentConversionResult> EnhanceAsync(
        string kind,
        DocumentConversionRequest request,
        string materializedPath,
        DocumentModel nativeDocument,
        CancellationToken cancellationToken)
    {
        var context = request.Context;
        var nativeResult = new DocumentConversionResult(
            kind,
            MarkdownRenderer.Render(nativeDocument),
            Document: nativeDocument,
            Fidelity: nativeDocument.Fidelity,
            Diagnostics: nativeDocument.Diagnostics);

        if (context?.Pipeline != PipelineMode.Multimodal
            || context.Backend == ConversionBackendMode.Native
            || context.Vision == VisionMode.Off)
            return nativeResult;

        var transport = context.Options.DoclingTransport;
        if (transport is null)
        {
            if (context.Vision == VisionMode.Required)
                throw RequiredFailure(kind, "No Docling transport is configured.", context.OperationId);
            return nativeResult;
        }

        try
        {
            var response = await DoclingDocumentConverter.ConvertFileAsync(
                kind,
                request with
                {
                    FilePath = materializedPath,
                    Filename = Path.GetFileName(materializedPath),
                    MimeType = request.MimeType ?? MimeTypeFor(kind)
                },
                transport,
                cancellationToken);

            var visualDocument = response.Document
                ?? throw new ConversionException("Docling returned no structured document.");
            var additions = visualDocument.Blocks
                .Where(block => !IsAlreadyRepresented(block, nativeDocument.Blocks))
                .ToArray();

            var supplements = nativeDocument.Supplements?.ToList() ?? [];
            if (additions.Length > 0)
                supplements.Add(new DocumentSupplement("Docling visual supplement", additions));

            var merged = nativeDocument with
            {
                Fidelity = FidelityStatus.Complete,
                Supplements = supplements
            };
            return nativeResult with
            {
                Markdown = MarkdownRenderer.Render(merged),
                Document = merged,
                Fidelity = FidelityStatus.Complete,
                Usage = response.Usage
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (context.Vision == VisionMode.Required)
                throw RequiredFailure(kind, $"Docling enhancement failed: {ex.Message}", context.OperationId, ex);

            var diagnostic = new ConversionDiagnostic(
                "DOCLING_FAILED_FALLBACK_NATIVE",
                DiagnosticSeverity.Warning,
                $"Docling enhancement failed; native {kind} output was retained: {ex.Message}",
                AffectsSubstantiveContent: true,
                Backend: "Docling",
                FallbackReason: "native-office",
                RequiresReview: true);
            var diagnostics = (nativeDocument.Diagnostics ?? []).Append(diagnostic).ToArray();
            var partial = nativeDocument with { Fidelity = FidelityStatus.Partial, Diagnostics = diagnostics };
            return nativeResult with
            {
                Markdown = MarkdownRenderer.Render(partial),
                Document = partial,
                Fidelity = FidelityStatus.Partial,
                Diagnostics = diagnostics
            };
        }
    }

    private static bool IsAlreadyRepresented(DocumentBlock visualBlock, IReadOnlyList<DocumentBlock> nativeBlocks)
    {
        var candidate = Normalize(visualBlock.Text);
        return candidate.Length == 0 || nativeBlocks.Any(native =>
            Normalize(native.Text).Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string MimeTypeFor(string kind) => kind switch
    {
        "Docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "Pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "Xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream"
    };

    private static ConversionException RequiredFailure(string kind, string message, string operationId, Exception? inner = null)
    {
        var report = ConversionFailureReport.Create(
            "DOCLING_REQUIRED_FAILED", message, operationId, kind);
        return inner is null
            ? new ConversionException(message) { FailureReport = report }
            : new ConversionException(message, inner) { FailureReport = report };
    }
}
