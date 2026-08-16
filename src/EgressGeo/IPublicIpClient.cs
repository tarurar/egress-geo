namespace EgressGeo;

public interface IPublicIpClient
{
    ValueTask<PublicIpResponse> GetIpifyIPv4(
        CancellationToken cancellationToken);
}
