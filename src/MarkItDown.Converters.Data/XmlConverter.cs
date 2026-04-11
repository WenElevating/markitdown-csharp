using System.Text;
using System.Xml;
using System.Xml.Linq;
using MarkItDown.Core;

namespace MarkItDown.Converters.Data;

public sealed class XmlConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xml" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/xml", "text/xml"
        };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("XML converter requires a file path.");

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var doc = XDocument.Parse(content);

            var builder = new StringBuilder();
            builder.AppendLine("```xml");

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = doc.Declaration is null,
            };

            using (var writer = XmlWriter.Create(builder, settings))
            {
                doc.WriteTo(writer);
            }

            builder.AppendLine();
            builder.Append("```");

            return new DocumentConversionResult("Xml", builder.ToString());
        }
        catch (ConversionException) { throw; }
        catch (Exception ex)
        {
            throw new ConversionException($"Failed to convert XML: {ex.Message}", ex);
        }
    }
}
