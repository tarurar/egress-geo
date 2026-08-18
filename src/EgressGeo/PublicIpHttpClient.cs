namespace EgressGeo;

public sealed class PublicIpHttpClient(HttpClient client) : IPublicIpClient
{
    private static readonly Uri DeSecIPv4Endpoint = new(
        "https://checkipv4.dedyn.io/");
    private static readonly Uri JokerIPv4Endpoint = new(
        "https://ipv4.svc.joker.com/nic/myip");
    private static readonly Uri DeSecIPv6Endpoint = new(
        "https://checkipv6.dedyn.io/");
    private static readonly Uri JokerIPv6Endpoint = new(
        "https://ipv6.svc.joker.com/nic/myip");

    public ValueTask<PublicIpResponse> GetDeSecIPv4(
        CancellationToken cancellationToken) =>
        Get(DeSecIPv4Endpoint, cancellationToken);

    public ValueTask<PublicIpResponse> GetJokerIPv4(
        CancellationToken cancellationToken) =>
        Get(JokerIPv4Endpoint, cancellationToken);

    public ValueTask<PublicIpResponse> GetDeSecIPv6(
        CancellationToken cancellationToken) =>
        Get(DeSecIPv6Endpoint, cancellationToken);

    public ValueTask<PublicIpResponse> GetJokerIPv6(
        CancellationToken cancellationToken) =>
        Get(JokerIPv6Endpoint, cancellationToken);

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
