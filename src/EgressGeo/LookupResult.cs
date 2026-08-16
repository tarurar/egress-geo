namespace EgressGeo;

internal sealed record LookupResult(
    DateTimeOffset ObservedAt,
    LookupOutcome IPv4,
    LookupOutcome IPv6,
    CacheUsage CacheUsage)
{
    internal LookupStatus Status =>
        CacheUsage is CacheUsage.CompleteSnapshot
            ? LookupStatus.Cached
            : HasCountryMismatch
                ? LookupStatus.CountryMismatch
                : HasUsableLocation
                    ? LookupStatus.Healthy
                    : LookupStatus.Failed;

    internal int ExitCode => Status.ExitCode;

    internal bool HasCountryMismatch =>
        IPv4 is LookupOutcome.Found ipv4 &&
        IPv6 is LookupOutcome.Found ipv6 &&
        ipv4.Country != ipv6.Country;

    internal bool IsCached => CacheUsage is not CacheUsage.None;

    internal TimeSpan? CacheAge =>
        CacheUsage switch
        {
            CacheUsage.ExactAddressMatch exactAddress => exactAddress.Age,
            CacheUsage.CompleteSnapshot completeSnapshot =>
                completeSnapshot.Age,
            _ => null,
        };

    private bool HasUsableLocation =>
        IPv4 is LookupOutcome.Found || IPv6 is LookupOutcome.Found;
}
