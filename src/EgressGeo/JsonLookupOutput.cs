using System.Text.Json;

namespace EgressGeo;

internal static class JsonLookupOutput
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    internal static CommandResult Render(LiveLookupOutcome outcome)
    {
        var families = new[] { outcome.IPv4, outcome.IPv6 }
            .Select(CreateFamily)
            .OfType<JsonFamily>()
            .ToArray();
        var document = new JsonLookup(
            GetStatus(outcome.Status),
            outcome.ObservedAt,
            false,
            null,
            GetWarnings(outcome.Status),
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
                GetSource(found.PublicIp.Provider)),
            LookupOutcome.LocationUnavailable unavailable => new JsonFamily(
                unavailable.PublicIp.Family.ToString(),
                unavailable.PublicIp.Address.ToString(),
                null,
                null,
                GetSource(unavailable.PublicIp.Provider)),
            _ => null,
        };

    private static string GetStatus(LiveLookupStatus status) =>
        status switch
        {
            LiveLookupStatus.Healthy => "healthy",
            LiveLookupStatus.CountryMismatch => "country-mismatch",
            LiveLookupStatus.Failed => "failed",
            _ => throw new InvalidOperationException(
                $"Unknown live lookup status: {status}"),
        };

    private static IReadOnlyList<string> GetWarnings(
        LiveLookupStatus status) =>
        status == LiveLookupStatus.CountryMismatch
            ? ["possible-vpn-leak"]
            : [];

    private static string GetSource(PublicIpProvider provider) =>
        provider switch
        {
            PublicIpProvider.Ipify => "ipify",
            PublicIpProvider.IdentMe => "ident.me",
            _ => throw new InvalidOperationException(
                $"Unknown public IP provider: {provider}"),
        };

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
