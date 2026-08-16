namespace EgressGeo;

internal static class PublicIpProviderContract
{
    internal static string Format(PublicIpProvider provider) =>
        provider switch
        {
            PublicIpProvider.Ipify => "ipify",
            PublicIpProvider.IdentMe => "ident.me",
            _ => throw new InvalidOperationException(
                $"Unknown public IP provider: {provider}"),
        };

    internal static PublicIpProvider? Parse(string? value) =>
        value switch
        {
            "ipify" => PublicIpProvider.Ipify,
            "ident.me" => PublicIpProvider.IdentMe,
            _ => null,
        };
}
