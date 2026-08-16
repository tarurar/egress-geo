namespace EgressGeo;

public abstract record GeolocationLookup
{
    private GeolocationLookup()
    {
    }

    public sealed record Found(string? City, string? CountryCode) :
        GeolocationLookup;

    public sealed record LocationUnavailable : GeolocationLookup;

    public sealed record DatabaseUnavailable : GeolocationLookup;
}
