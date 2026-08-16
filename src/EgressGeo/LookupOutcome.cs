namespace EgressGeo;

internal abstract record LookupOutcome
{
    private LookupOutcome()
    {
    }

    internal sealed record Found(
        DiscoveredPublicIp PublicIp,
        string? City,
        CountryCode Country) : LookupOutcome;

    internal sealed record LocationUnavailable(DiscoveredPublicIp PublicIp) :
        LookupOutcome;

    internal sealed record PublicAddressUnavailable(IpFamily Family) :
        LookupOutcome;

    internal sealed record DatabaseUnavailable : LookupOutcome;
}
