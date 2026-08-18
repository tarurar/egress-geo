namespace EgressGeo;

public interface IPublicIpClient
{
    ValueTask<PublicIpResponse> GetDeSecIPv4(
        CancellationToken cancellationToken);

    ValueTask<PublicIpResponse> GetJokerIPv4(
        CancellationToken cancellationToken);

    ValueTask<PublicIpResponse> GetDeSecIPv6(
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<PublicIpResponse>(
            new PublicIpResponse.Unavailable());

    ValueTask<PublicIpResponse> GetJokerIPv6(
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<PublicIpResponse>(
            new PublicIpResponse.Unavailable());
}
