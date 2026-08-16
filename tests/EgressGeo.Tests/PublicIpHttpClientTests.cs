using System.Net;

namespace EgressGeo.Tests;

[TestClass]
public sealed class PublicIpHttpClientTests
{
    [TestMethod]
    public Task Ipify_request_uses_IPv4_only_endpoint() =>
        AssertEndpoint(
            "https://api.ipify.org/",
            "203.0.113.7",
            static (client, token) => client.GetIpifyIPv4(token));

    [TestMethod]
    public Task Ident_me_request_uses_IPv4_only_endpoint() =>
        AssertEndpoint(
            "https://4.ident.me/",
            "203.0.113.7",
            static (client, token) => client.GetIdentMeIPv4(token));

    [TestMethod]
    public Task Ipify_request_uses_IPv6_only_endpoint() =>
        AssertEndpoint(
            "https://api6.ipify.org/",
            "2001:db8::7",
            static (client, token) => client.GetIpifyIPv6(token));

    [TestMethod]
    public Task Ident_me_request_uses_IPv6_only_endpoint() =>
        AssertEndpoint(
            "https://6.ident.me/",
            "2001:db8::7",
            static (client, token) => client.GetIdentMeIPv6(token));

    private static async Task AssertEndpoint(
        string expectedEndpoint,
        string responseContent,
        Func<PublicIpHttpClient,
            CancellationToken,
            ValueTask<PublicIpResponse>> request)
    {
        Uri? requestedUri = null;
        using var http = new HttpClient(
            new FakeHttpMessageHandler(
                (request, _) =>
                {
                    requestedUri = request.RequestUri;
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(responseContent),
                        });
                }));
        var client = new PublicIpHttpClient(http);

        await request(client, CancellationToken.None);

        Assert.AreEqual(new Uri(expectedEndpoint), requestedUri);
    }

    [TestMethod]
    public async Task Successful_response_contains_provider_content()
    {
        using var http = new HttpClient(
            new FakeHttpMessageHandler(
                (_, _) => Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("203.0.113.7"),
                    })));
        var client = new PublicIpHttpClient(http);

        var result = await client.GetIpifyIPv4(CancellationToken.None);

        Assert.AreEqual(
            new PublicIpResponse.Received("203.0.113.7"),
            result);
    }

    [TestMethod]
    public async Task Non_success_response_is_unavailable()
    {
        using var http = new HttpClient(
            new FakeHttpMessageHandler(
                (_, _) => Task.FromResult(
                    new HttpResponseMessage(
                        HttpStatusCode.ServiceUnavailable))));
        var client = new PublicIpHttpClient(http);

        var result = await client.GetIpifyIPv4(CancellationToken.None);

        Assert.IsInstanceOfType<PublicIpResponse.Unavailable>(result);
    }

    [TestMethod]
    public async Task Transport_failure_is_unavailable()
    {
        using var http = new HttpClient(
            new FakeHttpMessageHandler(
                (_, _) => Task.FromException<HttpResponseMessage>(
                    new HttpRequestException("Synthetic failure."))));
        var client = new PublicIpHttpClient(http);

        var result = await client.GetIpifyIPv4(CancellationToken.None);

        Assert.IsInstanceOfType<PublicIpResponse.Unavailable>(result);
    }

    [TestMethod]
    public async Task Timeout_is_unavailable()
    {
        using var http = new HttpClient(
            new FakeHttpMessageHandler(
                (_, _) => Task.FromException<HttpResponseMessage>(
                    new TaskCanceledException("Synthetic timeout."))));
        var client = new PublicIpHttpClient(http);

        var result = await client.GetIpifyIPv4(CancellationToken.None);

        Assert.IsInstanceOfType<PublicIpResponse.Unavailable>(result);
    }

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
