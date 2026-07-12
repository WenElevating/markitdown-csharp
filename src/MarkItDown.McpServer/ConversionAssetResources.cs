using System.ComponentModel;
using MarkItDown.Core;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MarkItDown.McpServer;

[McpServerResourceType]
public static class ConversionAssetResources
{
    [McpServerResource(
        UriTemplate = "markitdown://conversion/{conversionId}/assets/{assetId}",
        Name = "Temporary conversion asset",
        MimeType = "application/octet-stream")]
    [Description("Reads an asset published by a detailed conversion. The URI is opaque and expires with the conversion.")]
    public static ResourceContents Read(string conversionId, string assetId)
    {
        if (!MarkItDownTools.TryReadAsset(conversionId, assetId, out var bytes))
            throw new NotSupportedException("CONVERSION_NOT_FOUND");
        return new BlobResourceContents
        {
            Uri = $"markitdown://conversion/{conversionId}/assets/{assetId}",
            MimeType = "application/octet-stream",
            Blob = bytes
        };
    }
}
