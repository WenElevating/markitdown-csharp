using System.Net.Http.Json;

namespace MarkItDown.Core;

public sealed class HttpDoclingTransport : IDoclingTransport
{
    private readonly HttpClient _client;
    private readonly Uri _endpoint;
    private readonly bool _disposeClient;

    public HttpDoclingTransport(Uri endpoint, HttpClient? client = null)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https"))
            throw new ArgumentException("Docling endpoint must be an absolute HTTP(S) URI.", nameof(endpoint));
        _client = client ?? new HttpClient();
        _disposeClient = client is null;
    }

    public async Task<DoclingResponse> SendAsync(DoclingRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _client.PostAsJsonAsync(_endpoint, request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ConversionException($"Docling HTTP endpoint returned {(int)response.StatusCode}: {body}");
        var result = DoclingProtocol.DeserializeResponse(body);
        if (!string.Equals(result.RequestId, request.RequestId, StringComparison.Ordinal))
            throw new InvalidDataException("Docling HTTP response requestId did not match.");
        return result;
    }

    public void Dispose()
    {
        if (_disposeClient) _client.Dispose();
    }
}
