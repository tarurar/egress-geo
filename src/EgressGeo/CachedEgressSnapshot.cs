using System.Collections.ObjectModel;

namespace EgressGeo;

public sealed class CachedEgressSnapshot
{
    private CachedEgressSnapshot(
        DateTimeOffset observedAt,
        CachedEgressFamily ipv4,
        CachedEgressFamily ipv6)
    {
        ObservedAt = observedAt;
        Families = new ReadOnlyCollection<CachedEgressFamily>([ipv4, ipv6]);
    }

    public DateTimeOffset ObservedAt { get; }

    public IReadOnlyList<CachedEgressFamily> Families { get; }

    public static CachedEgressSnapshot? Create(
        DateTimeOffset observedAt,
        IReadOnlyCollection<CachedEgressFamily?>? families)
    {
        if (observedAt == default || families is not { Count: 2 })
        {
            return null;
        }

        var ipv4Families = families
            .Where(family => family?.FamilyValue == IpFamily.IPv4)
            .ToArray();
        var ipv6Families = families
            .Where(family => family?.FamilyValue == IpFamily.IPv6)
            .ToArray();
        return ipv4Families is not [{ } ipv4] ||
            ipv6Families is not [{ } ipv6] ||
            !families.All(family => family?.HasLocation == true)
                ? null
                : new CachedEgressSnapshot(observedAt, ipv4, ipv6);
    }

    internal static CachedEgressSnapshot? FromOutcome(
        DateTimeOffset observedAt,
        LookupOutcome ipv4,
        LookupOutcome ipv6) =>
        Create(
            observedAt,
            [
                CachedEgressFamily.FromOutcome(IpFamily.IPv4, ipv4),
                CachedEgressFamily.FromOutcome(IpFamily.IPv6, ipv6),
            ]);

    internal LookupOutcome RestoreIPv4() => Families[0].Restore();

    internal LookupOutcome RestoreIPv6() => Families[1].Restore();

    internal LookupOutcome.Found? RestoreExactAddress(
        DiscoveredPublicIp publicIp)
    {
        var cached = Families.FirstOrDefault(
            family => family.PublicIp?.Address.Equals(publicIp.Address) == true);
        return cached?.Restore() is LookupOutcome.Found found
            ? found with { PublicIp = publicIp }
            : null;
    }
}
