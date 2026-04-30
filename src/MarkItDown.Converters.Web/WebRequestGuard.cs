using System.Net;
using System.Net.Sockets;
using MarkItDown.Core;

namespace MarkItDown.Converters.Web;

internal static class WebRequestGuard
{
    private const int MaxRedirects = 5;

    private static readonly HttpClientHandler Handler = new()
    {
        AllowAutoRedirect = false
    };

    internal static readonly HttpClient HttpClient = new(Handler)
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    internal static Task<string> FetchStringAsync(string url, CancellationToken cancellationToken)
    {
        return FetchStringAsync(url, HttpClient, cancellationToken);
    }

    internal static async Task<string> FetchStringAsync(
        string url,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var currentUri = await ValidatePublicHttpUrlAsync(url, cancellationToken);

        for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
        {
            using var response = await httpClient.GetAsync(
                currentUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount == MaxRedirects)
                {
                    throw new ConversionException($"Too many redirects. Maximum supported redirects: {MaxRedirects}.");
                }

                var location = response.Headers.Location
                    ?? throw new ConversionException("Redirect response did not include a Location header.");
                var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                currentUri = await ValidatePublicHttpUrlAsync(nextUri.ToString(), cancellationToken);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        throw new ConversionException($"Too many redirects. Maximum supported redirects: {MaxRedirects}.");
    }

    internal static async Task<Uri> ValidatePublicHttpUrlAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new ConversionException("Only absolute http and https URLs are supported.");
        }

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            ValidatePublicAddress(IPAddress.Parse(uri.Host));
            return uri;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            throw new ConversionException($"Could not resolve URL host: {uri.Host}", ex);
        }

        foreach (var address in addresses)
        {
            ValidatePublicAddress(address);
        }

        return uri;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect
            or HttpStatusCode.MultipleChoices;
    }

    private static void ValidatePublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || !IsGlobalUnicast(address))
        {
            throw new ConversionException("URL resolves to a private, loopback, link-local, or otherwise non-public address.");
        }
    }

    private static bool IsGlobalUnicast(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return !(bytes[0] == 0
                || bytes[0] == 10
                || bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127
                || bytes[0] == 127
                || bytes[0] == 169 && bytes[1] == 254
                || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31
                || bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0
                || bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2
                || bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99
                || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19)
                || bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100
                || bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113
                || bytes[0] >= 224);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return !(address.Equals(IPAddress.IPv6None)
                || address.Equals(IPAddress.IPv6Loopback)
                || HasPrefix(bytes, [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0], 96)
                || HasPrefix(bytes, [0x00, 0x64, 0xff, 0x9b, 0x00, 0x01], 48)
                || HasPrefix(bytes, [0x01, 0x00, 0, 0, 0, 0, 0, 0], 64)
                || HasPrefix(bytes, [0x20, 0x01], 23)
                || HasPrefix(bytes, [0x20, 0x01, 0x0d, 0xb8], 32)
                || HasPrefix(bytes, [0x20, 0x02], 16)
                || HasPrefix(bytes, [0xfc], 7)
                || HasPrefix(bytes, [0xfe, 0x80], 10)
                || HasPrefix(bytes, [0xff], 8));
        }

        return false;
    }

    private static bool HasPrefix(byte[] bytes, byte[] prefix, int prefixLength)
    {
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var index = 0; index < fullBytes; index++)
        {
            var expected = index < prefix.Length ? prefix[index] : (byte)0;
            if (bytes[index] != expected)
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xff << (8 - remainingBits));
        var expectedPartial = fullBytes < prefix.Length ? prefix[fullBytes] : (byte)0;
        return (bytes[fullBytes] & mask) == (expectedPartial & mask);
    }
}
