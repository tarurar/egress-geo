namespace EgressGeo;

internal static class IpFamilyContract
{
    internal static string Format(IpFamily family) =>
        family switch
        {
            IpFamily.IPv4 => "IPv4",
            IpFamily.IPv6 => "IPv6",
            _ => throw new InvalidOperationException(
                $"Unknown IP family: {family}"),
        };

    internal static IpFamily? Parse(string? value) =>
        value switch
        {
            "IPv4" => IpFamily.IPv4,
            "IPv6" => IpFamily.IPv6,
            _ => null,
        };
}
