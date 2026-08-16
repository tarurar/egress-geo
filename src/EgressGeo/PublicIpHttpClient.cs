namespace EgressGeo;

public sealed class PublicIpHttpClient(HttpClient client) : IPublicIpClient
{
    private static readonly Uri IpifyIPv4Endpoint = new(
        "https://api.ipify.org");
    private static readonly Uri IdentMeIPv4Endpoint = new(
        "https://4.ident.me");

    public ValueTask<PublicIpResponse> GetIpifyIPv4(
        CancellationToken cancellationToken) =>
        Get(IpifyIPv4Endpoint, cancellationToken);

    public ValueTask<PublicIpResponse> GetIdentMeIPv4(
        CancellationToken cancellationToken) =>
        Get(IdentMeIPv4Endpoint, cancellationToken);

    private async ValueTask<PublicIpResponse> Get(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(
                endpoint,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new PublicIpResponse.Unavailable();
            }

            var content = await response.Content.ReadAsStringAsync(
                cancellationToken);
            return new PublicIpResponse.Received(content);
        }
        catch (HttpRequestException)
        {
            return new PublicIpResponse.Unavailable();
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return new PublicIpResponse.Unavailable();
        }
    }
}
