namespace EgressGeo;

internal static class PublicIpProviderContract
{
    internal static PublicIpDiscoverySource ToDiscoverySource(
        PublicIpProvider provider) =>
        provider switch
        {
            PublicIpProvider.DeSec => PublicIpDiscoverySource.DeSec,
            PublicIpProvider.Joker => PublicIpDiscoverySource.Joker,
            _ => throw new InvalidOperationException(
                $"Unknown public IP provider: {provider}"),
        };
}
