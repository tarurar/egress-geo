namespace EgressGeo;

public interface IPublicIpClient
{
    ValueTask<PublicIpResponse> GetIpifyIPv4(
        CancellationToken cancellationToken);

    ValueTask<PublicIpResponse> GetIdentMeIPv4(
        CancellationToken cancellationToken);

    ValueTask<PublicIpResponse> GetIpifyIPv6(
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<PublicIpResponse>(
            new PublicIpResponse.Unavailable());

    ValueTask<PublicIpResponse> GetIdentMeIPv6(
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<PublicIpResponse>(
            new PublicIpResponse.Unavailable());
}
