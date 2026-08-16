namespace EgressGeo;

public sealed class PublicIpHttpClient(HttpClient client) : IPublicIpClient
{
    private static readonly Uri IpifyIPv4Endpoint = new(
        "https://api.ipify.org");

    public async ValueTask<string> GetIpifyIPv4(
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            IpifyIPv4Endpoint,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
