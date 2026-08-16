using System.Net;

namespace EgressGeo;

internal abstract record LookupOutcome
{
    private LookupOutcome()
    {
    }

    internal sealed record Found(
        IPAddress Address,
        string? City,
        CountryCode Country) : LookupOutcome;

    internal sealed record LocationUnavailable(IPAddress Address) :
        LookupOutcome;

    internal sealed record DatabaseUnavailable : LookupOutcome;
}
