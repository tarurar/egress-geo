using System.Net;
using System.Net.Sockets;

namespace EgressGeo;

internal sealed record DiscoveredPublicIp
{
    private DiscoveredPublicIp(
        IpFamily family,
        IPAddress address,
        PublicIpDiscoverySource source)
    {
        Family = family;
        Address = address;
        Source = source;
    }

    internal IpFamily Family { get; }

    internal IPAddress Address { get; }

    internal PublicIpDiscoverySource Source { get; }

    internal static DiscoveredPublicIp? Parse(
        string content,
        IpFamily family,
        PublicIpDiscoverySource source)
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

        return new DiscoveredPublicIp(family, address, source);
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
