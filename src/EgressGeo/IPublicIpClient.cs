namespace EgressGeo;

public interface IPublicIpClient
{
    ValueTask<string> GetIpifyIPv4(CancellationToken cancellationToken);
}
