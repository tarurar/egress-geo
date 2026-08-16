using System.Net;

namespace EgressGeo;

internal static class LookupDecision
{
    internal static LookupOutcome Decide(
        IPAddress address,
        GeolocationLookup lookup) =>
        lookup switch
        {
            GeolocationLookup.DatabaseUnavailable =>
                new LookupOutcome.DatabaseUnavailable(),
            GeolocationLookup.Found found
                when NormalizeCountryCode(found.CountryCode) is { } country =>
                new LookupOutcome.Found(
                    address,
                    NormalizeCity(found.City),
                    country),
            _ => new LookupOutcome.LocationUnavailable(address),
        };

    private static string? NormalizeCountryCode(string? countryCode)
    {
        var normalized = countryCode?.ToUpperInvariant();
        return normalized is [>= 'A' and <= 'Z', >= 'A' and <= 'Z']
            ? normalized
            : null;
    }

    private static string? NormalizeCity(string? city) =>
        string.IsNullOrWhiteSpace(city) ? null : city;
}
