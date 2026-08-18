using System.Text.Json;

namespace EgressGeo;

internal static class JsonLookupOutput
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    internal static CommandResult Render(LookupResult outcome)
    {
        var families = new[] { outcome.IPv4, outcome.IPv6 }
            .Select(CreateFamily)
            .OfType<JsonFamily>()
            .ToArray();
        var document = new JsonLookup(
            outcome.Status.Value,
            outcome.ObservedAt,
            outcome.IsCached,
            outcome.CacheAge is { } age ? (long)age.TotalSeconds : null,
            GetWarnings(outcome.HasCountryMismatch),
            families);
        var output = JsonSerializer.Serialize(document, SerializerOptions);

        return new CommandResult(
            outcome.ExitCode,
            output + "\n",
            string.Empty);
    }

    private static JsonFamily? CreateFamily(LookupOutcome outcome) =>
        outcome switch
        {
            LookupOutcome.Found found => new JsonFamily(
                found.PublicIp.Family.ToString(),
                found.PublicIp.Address.ToString(),
                found.City,
                found.Country.Value,
                PublicIpDiscoverySourceContract.Format(
                    found.PublicIp.Source)),
            LookupOutcome.LocationUnavailable unavailable => new JsonFamily(
                unavailable.PublicIp.Family.ToString(),
                unavailable.PublicIp.Address.ToString(),
                null,
                null,
                PublicIpDiscoverySourceContract.Format(
                    unavailable.PublicIp.Source)),
            LookupOutcome.DatabaseUnavailable
            {
                PublicIp: { } publicIp,
            } => new JsonFamily(
                    publicIp.Family.ToString(),
                    publicIp.Address.ToString(),
                    null,
                    null,
                    PublicIpDiscoverySourceContract.Format(publicIp.Source)),
            _ => null,
        };

    private static IReadOnlyList<string> GetWarnings(
        bool hasCountryMismatch) =>
        hasCountryMismatch
            ? ["possible-vpn-leak"]
            : [];

    private sealed record JsonLookup(
        string Status,
        DateTimeOffset ObservedAt,
        bool Cached,
        long? CacheAgeSeconds,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<JsonFamily> Families);

    private sealed record JsonFamily(
        string Family,
        string Address,
        string? ApproximateCity,
        string? CountryCode,
        string DiscoverySource);
}
