namespace EgressGeo;

internal static class PublicIpDiscoverySourceContract
{
    internal static string Format(PublicIpDiscoverySource source) =>
        source switch
        {
            PublicIpDiscoverySource.DeSec => "deSEC",
            PublicIpDiscoverySource.Joker => "Joker",
            PublicIpDiscoverySource.LegacyIpify => "ipify",
            PublicIpDiscoverySource.LegacyIdentMe => "ident.me",
            _ => throw new InvalidOperationException(
                $"Unknown public IP discovery source: {source}"),
        };

    internal static PublicIpDiscoverySource? Parse(string? value) =>
        value switch
        {
            "deSEC" => PublicIpDiscoverySource.DeSec,
            "Joker" => PublicIpDiscoverySource.Joker,
            "ipify" => PublicIpDiscoverySource.LegacyIpify,
            "ident.me" => PublicIpDiscoverySource.LegacyIdentMe,
            _ => null,
        };
}
