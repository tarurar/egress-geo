namespace EgressGeo;

public sealed class PublicIpHttpClient(HttpClient client) : IPublicIpClient
{
    private static readonly Uri IpifyIPv4Endpoint = new(
        "https://api.ipify.org");

    public async ValueTask<PublicIpResponse> GetIpifyIPv4(
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(
                IpifyIPv4Endpoint,
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
