using System.Net;
using System.Net.Sockets;

namespace EgressGeo;

internal sealed record DiscoveredPublicIp
{
    private DiscoveredPublicIp(
        IpFamily family,
        IPAddress address,
        PublicIpProvider provider)
    {
        Family = family;
        Address = address;
        Provider = provider;
    }

    internal IpFamily Family { get; }

    internal IPAddress Address { get; }

    internal PublicIpProvider Provider { get; }

    internal static DiscoveredPublicIp? Parse(
        string content,
        IpFamily family,
        PublicIpProvider provider)
    {
        var candidate = content.Trim();
        if (!IPAddress.TryParse(candidate, out var address) ||
            address.AddressFamily != GetAddressFamily(family) ||
            (family == IpFamily.IPv4 &&
                !string.Equals(
                    candidate,
                    address.ToString(),
                    StringComparison.Ordinal)))
        {
            return null;
        }

        return new DiscoveredPublicIp(family, address, provider);
    }

    private static AddressFamily GetAddressFamily(IpFamily family) =>
        family switch
        {
            IpFamily.IPv4 => AddressFamily.InterNetwork,
            IpFamily.IPv6 => AddressFamily.InterNetworkV6,
            _ => throw new InvalidOperationException(
                $"Unknown IP family: {family}"),
        };
}
