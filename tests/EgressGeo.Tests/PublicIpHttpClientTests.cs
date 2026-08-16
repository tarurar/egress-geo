using System.Net;

namespace EgressGeo.Tests;

[TestClass]
public sealed class PublicIpHttpClientTests
{
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
