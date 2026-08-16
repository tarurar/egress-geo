namespace EgressGeo;

public sealed record CachedEgressFamily
{
    private CachedEgressFamily(
        IpFamily family,
        DiscoveredPublicIp? publicIp,
        string? approximateCity,
        CountryCode? country)
    {
        FamilyValue = family;
        PublicIp = publicIp;
        Country = country;
        Family = IpFamilyContract.Format(family);
        Address = publicIp?.Address.ToString();
        ApproximateCity = approximateCity;
        CountryCode = country?.Value;
        DiscoverySource = publicIp is null
            ? null
            : PublicIpProviderContract.Format(publicIp.Provider);
    }

    public string Family { get; }

    public string? Address { get; }

    public string? ApproximateCity { get; }

    public string? CountryCode { get; }

    public string? DiscoverySource { get; }

    internal IpFamily FamilyValue { get; }

    internal DiscoveredPublicIp? PublicIp { get; }

    internal CountryCode? Country { get; }

    internal bool HasLocation => PublicIp is not null && Country is not null;

    public static CachedEgressFamily? Create(
        string? family,
        string? address,
        string? approximateCity,
        string? countryCode,
        string? discoverySource)
    {
        var parsedFamily = IpFamilyContract.Parse(family);
        if (parsedFamily is null)
        {
            return null;
        }

        return address is null
            ? CreateAddressUnavailable(
                parsedFamily.Value,
                approximateCity,
                countryCode,
                discoverySource)
            : CreateDiscovered(
                parsedFamily.Value,
                address,
                approximateCity,
                countryCode,
                discoverySource);
    }

    private static CachedEgressFamily? CreateAddressUnavailable(
        IpFamily family,
        string? approximateCity,
        string? countryCode,
        string? discoverySource) =>
        approximateCity is null &&
        countryCode is null &&
        discoverySource is null
            ? new CachedEgressFamily(family, null, null, null)
            : null;

    private static CachedEgressFamily? CreateDiscovered(
        IpFamily family,
        string address,
        string? approximateCity,
        string? countryCode,
        string? discoverySource)
    {
        var publicIp = ParsePublicIp(family, address, discoverySource);
        if (publicIp is null || !string.Equals(
                address,
                publicIp.Address.ToString(),
                StringComparison.Ordinal))
        {
            return null;
        }

        if (countryCode is null)
        {
            return approximateCity is null
                ? new CachedEgressFamily(
                    family,
                    publicIp,
                    null,
                    null)
                : null;
        }

        var country = EgressGeo.CountryCode.Parse(countryCode);
        return country is null ||
            !string.Equals(
                country.Value,
                countryCode,
                StringComparison.Ordinal) ||
            approximateCity is not null &&
            string.IsNullOrWhiteSpace(approximateCity)
                ? null
                : new CachedEgressFamily(
                    family,
                    publicIp,
                    approximateCity,
                    country);
    }

    private static DiscoveredPublicIp? ParsePublicIp(
        IpFamily family,
        string address,
        string? discoverySource) =>
        PublicIpProviderContract.Parse(discoverySource) is { } provider
            ? DiscoveredPublicIp.Parse(address, family, provider)
            : null;

    internal static CachedEgressFamily FromOutcome(
        IpFamily family,
        LookupOutcome outcome) =>
        outcome switch
        {
            LookupOutcome.Found found
                when found.PublicIp.Family == family =>
                new CachedEgressFamily(
                    family,
                    found.PublicIp,
                    found.City,
                    found.Country),
            LookupOutcome.LocationUnavailable unavailable
                when unavailable.PublicIp.Family == family =>
                new CachedEgressFamily(
                    family,
                    unavailable.PublicIp,
                    null,
                    null),
            LookupOutcome.DatabaseUnavailable
            {
                PublicIp: { } publicIp,
            } when publicIp.Family == family =>
                new CachedEgressFamily(
                    family,
                    publicIp,
                    null,
                    null),
            LookupOutcome.PublicAddressUnavailable unavailable
                when unavailable.Family == family =>
                new CachedEgressFamily(family, null, null, null),
            LookupOutcome.DatabaseUnavailable { PublicIp: null } =>
                new CachedEgressFamily(family, null, null, null),
            _ => throw new InvalidOperationException(
                $"Cannot cache {family} outcome {outcome.GetType().Name}."),
        };

    internal LookupOutcome Restore() =>
        (PublicIp, Country) switch
        {
            (null, _) => new LookupOutcome.PublicAddressUnavailable(
                FamilyValue),
            ({ } publicIp, null) =>
                new LookupOutcome.LocationUnavailable(publicIp),
            ({ } publicIp, { } country) => new LookupOutcome.Found(
                publicIp,
                ApproximateCity,
                country),
        };
}
