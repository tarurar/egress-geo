namespace EgressGeo;

internal static class LookupDecision
{
    internal static LookupOutcome Decide(
        DiscoveredPublicIp publicIp,
        GeolocationLookup lookup) =>
        lookup switch
        {
            GeolocationLookup.DatabaseUnavailable =>
                new LookupOutcome.DatabaseUnavailable(publicIp),
            GeolocationLookup.Found found
                when CountryCode.Parse(found.CountryCode) is { } country =>
                new LookupOutcome.Found(
                    publicIp,
                    NormalizeCity(found.City),
                    country),
            _ => new LookupOutcome.LocationUnavailable(publicIp),
        };

    private static string? NormalizeCity(string? city) =>
        string.IsNullOrWhiteSpace(city) ? null : city;
}
