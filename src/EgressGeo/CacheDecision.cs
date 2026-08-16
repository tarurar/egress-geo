namespace EgressGeo;

internal sealed record CacheDecision(
    LookupResult Outcome,
    CachedEgressSnapshot? SnapshotToWrite)
{
    internal static readonly TimeSpan MaximumCacheAge =
        TimeSpan.FromHours(24);

    internal static CacheDecision Decide(
        LookupResult liveOutcome,
        CachedEgressSnapshot? cachedSnapshot)
    {
        var outcome = Restore(liveOutcome, cachedSnapshot) ?? liveOutcome;
        var snapshotToWrite = CachedEgressSnapshot.FromOutcome(
            liveOutcome.ObservedAt,
            liveOutcome.IPv4,
            liveOutcome.IPv6);
        return new CacheDecision(outcome, snapshotToWrite);
    }

    private static LookupResult? Restore(
        LookupResult liveOutcome,
        CachedEgressSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var age = liveOutcome.ObservedAt - snapshot.ObservedAt;
        if (age < TimeSpan.Zero || MaximumCacheAge < age)
        {
            return null;
        }

        if (AddressDiscoveryFailed(liveOutcome.IPv4) &&
            AddressDiscoveryFailed(liveOutcome.IPv6))
        {
            return new LookupResult(
                snapshot.ObservedAt,
                snapshot.RestoreIPv4(),
                snapshot.RestoreIPv6(),
                new CacheUsage.CompleteSnapshot(age));
        }

        var ipv4 = RestoreExactAddress(liveOutcome.IPv4, snapshot);
        var ipv6 = RestoreExactAddress(liveOutcome.IPv6, snapshot);
        return ipv4 is null && ipv6 is null
            ? null
            : new LookupResult(
                liveOutcome.ObservedAt,
                ipv4 ?? liveOutcome.IPv4,
                ipv6 ?? liveOutcome.IPv6,
                new CacheUsage.ExactAddressMatch(age));
    }

    private static bool AddressDiscoveryFailed(LookupOutcome outcome) =>
        outcome is LookupOutcome.PublicAddressUnavailable or
            LookupOutcome.DatabaseUnavailable { PublicIp: null };

    private static LookupOutcome.Found? RestoreExactAddress(
        LookupOutcome live,
        CachedEgressSnapshot snapshot)
    {
        var publicIp = live switch
        {
            LookupOutcome.LocationUnavailable unavailable =>
                unavailable.PublicIp,
            LookupOutcome.DatabaseUnavailable unavailable =>
                unavailable.PublicIp,
            _ => null,
        };
        if (publicIp is null)
        {
            return null;
        }

        return snapshot.RestoreExactAddress(publicIp);
    }
}
