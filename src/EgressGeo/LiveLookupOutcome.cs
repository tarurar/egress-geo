namespace EgressGeo;

internal sealed record LiveLookupOutcome(
    DateTimeOffset ObservedAt,
    LookupOutcome IPv4,
    LookupOutcome IPv6)
{
    internal LiveLookupStatus Status =>
        HasCountryMismatch
            ? LiveLookupStatus.CountryMismatch
            : HasUsableLocation
                ? LiveLookupStatus.Healthy
                : LiveLookupStatus.Failed;

    internal int ExitCode => Status.ExitCode;

    internal bool HasCountryMismatch =>
        IPv4 is LookupOutcome.Found ipv4 &&
        IPv6 is LookupOutcome.Found ipv6 &&
        ipv4.Country != ipv6.Country;

    private bool HasUsableLocation =>
        IPv4 is LookupOutcome.Found || IPv6 is LookupOutcome.Found;
}
