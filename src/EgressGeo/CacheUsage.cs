namespace EgressGeo;

internal abstract record CacheUsage
{
    private CacheUsage()
    {
    }

    internal sealed record None : CacheUsage;

    internal sealed record ExactAddressMatch(TimeSpan Age) : CacheUsage;

    internal sealed record CompleteSnapshot(TimeSpan Age) : CacheUsage;
}
