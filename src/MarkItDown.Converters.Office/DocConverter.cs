using System.Collections.Frozen;
using MarkItDown.Core;
using NPOI.HWPF;

namespace MarkItDown.Converters.Office;

/// <summary>
/// Converts Word 97-2003 binary (.doc) files to Markdown using NPOI HWPF.
/// Also handles WPS Office files saved in OLE2/binary format with a .doc extension.
/// </summary>
public sealed class DocConverter : BaseConverter
{
    private static readonly FrozenSet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".doc" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> MimeTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/msword" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public override IReadOnlySet<string> SupportedExtensions => Extensions;

    public override IReadOnlySet<string> SupportedMimeTypes => MimeTypes;

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("DOC converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var doc = new HWPFDocument(fs);
                var markdown = DocRendering.RenderDocument(doc, cancellationToken);
                return new DocumentConversionResult("Doc", markdown);
            }
            catch (ConversionException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException(
                    $"Failed to convert DOC file '{filePath}': {ex.Message}", ex);
            }
        }, cancellationToken);
    }
}
