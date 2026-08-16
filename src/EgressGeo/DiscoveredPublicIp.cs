using System.Net;

namespace EgressGeo;

internal sealed record DiscoveredPublicIp(
    IpFamily Family,
    IPAddress Address,
    PublicIpProvider Provider);
