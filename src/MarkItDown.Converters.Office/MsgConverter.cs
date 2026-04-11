using System.Text;
using System.Text.RegularExpressions;
using MarkItDown.Core;
using MsgReader.Outlook;

namespace MarkItDown.Converters.Office;

public sealed class MsgConverter : BaseConverter
{
    public override IReadOnlySet<string> SupportedExtensions =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".msg" };

    public override IReadOnlySet<string> SupportedMimeTypes =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/vnd.ms-outlook",
            "application/outlook",
            "application/x-msg"
        };

    public override async Task<DocumentConversionResult> ConvertAsync(
        DocumentConversionRequest request, CancellationToken cancellationToken = default)
    {
        var filePath = request.FilePath
            ?? throw new ConversionException("MSG converter requires a file path.");

        return await Task.Run(() =>
        {
            try
            {
                using var msg = new Storage.Message(filePath);

                var builder = new StringBuilder();

                // Subject as title
                var subject = msg.Subject ?? "(No Subject)";
                builder.AppendLine($"# {subject}");

                // Metadata
                builder.AppendLine();

                if (msg.Sender is not null)
                {
                    var senderEmail = msg.Sender.Email ?? msg.Sender.DisplayName;
                    if (!string.IsNullOrWhiteSpace(senderEmail))
                        builder.AppendLine($"**From:** {senderEmail}");
                }

                var toRecipients = msg.GetEmailRecipients(RecipientType.To, false, false);
                if (toRecipients is not null && toRecipients.Length > 0)
                {
                    builder.AppendLine($"**To:** {string.Join(", ", toRecipients)}");
                }

                if (msg.SentOn.HasValue)
                {
                    builder.AppendLine($"**Date:** {msg.SentOn.Value:yyyy-MM-dd HH:mm}");
                }

                builder.AppendLine();
                builder.AppendLine("---");
                builder.AppendLine();

                // Body: prefer HTML (extract text), fallback to plain text
                var body = msg.BodyHtml;
                if (!string.IsNullOrWhiteSpace(body))
                {
                    body = Regex.Replace(body, "<br\\s*/?>", Environment.NewLine, RegexOptions.IgnoreCase);
                    body = Regex.Replace(body, "</p>", Environment.NewLine + Environment.NewLine, RegexOptions.IgnoreCase);
                    body = Regex.Replace(body, "<[^>]+>", "");
                    body = System.Net.WebUtility.HtmlDecode(body);
                    builder.Append(body.Trim());
                }
                else
                {
                    var textBody = msg.BodyText;
                    if (!string.IsNullOrWhiteSpace(textBody))
                        builder.Append(textBody.Trim());
                }

                return new DocumentConversionResult("Msg", builder.ToString().Trim());
            }
            catch (ConversionException) { throw; }
            catch (Exception ex)
            {
                throw new ConversionException($"Failed to convert MSG: {ex.Message}", ex);
            }
        }, cancellationToken);
    }
}
