namespace EgressGeo;

internal static class GeoLiteDatabasePolicy
{
    internal static readonly TimeSpan MaximumAge = TimeSpan.FromDays(30);

    internal static bool IsFresh(
        DateTimeOffset buildTime,
        DateTimeOffset currentTime)
    {
        var age = currentTime - buildTime;
        return TimeSpan.Zero <= age && age <= MaximumAge;
    }
}
