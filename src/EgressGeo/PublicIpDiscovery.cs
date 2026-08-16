namespace EgressGeo;

internal abstract record PublicIpDiscovery
{
    private PublicIpDiscovery()
    {
    }

    internal sealed record Found(DiscoveredPublicIp PublicIp) :
        PublicIpDiscovery;

    internal sealed record Unavailable(IpFamily Family) : PublicIpDiscovery;
}
