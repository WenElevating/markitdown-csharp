using System.Net;
using MarkItDown.Core;
using MarkItDown.Converters.Web;

namespace MarkItDown.Converters.Web.Tests;

public sealed class WebRequestGuardTests
{
    [Theory]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://100.64.0.1/")]
    [InlineData("http://192.0.2.1/")]
    [InlineData("http://198.51.100.1/")]
    [InlineData("http://203.0.113.1/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[::ffff:192.168.0.1]/")]
    [InlineData("http://[fc00::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[2001:db8::1]/")]
    public async Task ValidatePublicHttpUrlAsync_RejectsSpecialUseAddresses(string url)
    {
        var exception = await Assert.ThrowsAsync<ConversionException>(() =>
            WebRequestGuard.ValidatePublicHttpUrlAsync(url, CancellationToken.None));

        Assert.Contains("non-public", exception.Message);
    }

    [Fact]
    public async Task FetchStringAsync_FollowsSafeRedirects()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == "http://93.184.216.34/start")
            {
                return Redirect(HttpStatusCode.MovedPermanently, "/final");
            }

            Assert.Equal("http://93.184.216.34/final", request.RequestUri?.AbsoluteUri);
            return Text(HttpStatusCode.OK, "redirected content");
        }));

        var result = await WebRequestGuard.FetchStringAsync(
            "http://93.184.216.34/start",
            httpClient,
            CancellationToken.None);

        Assert.Equal("redirected content", result);
    }

    [Fact]
    public async Task FetchStringAsync_RejectsRedirectToPrivateAddress()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            Redirect(HttpStatusCode.Redirect, "http://127.0.0.1/final")));

        var exception = await Assert.ThrowsAsync<ConversionException>(() =>
            WebRequestGuard.FetchStringAsync(
                "http://93.184.216.34/start",
                httpClient,
                CancellationToken.None));

        Assert.Contains("non-public", exception.Message);
    }

    private static HttpResponseMessage Redirect(HttpStatusCode statusCode, string location)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static HttpResponseMessage Text(HttpStatusCode statusCode, string content)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content)
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handle)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handle(request));
        }
    }
}
