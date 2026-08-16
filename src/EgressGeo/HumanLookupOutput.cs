namespace EgressGeo;

internal static class HumanLookupOutput
{
    internal static CommandResult Render(LiveLookupOutcome outcome)
    {
        var locations = new[] { outcome.IPv4, outcome.IPv6 }
            .OfType<LookupOutcome.Found>()
            .ToArray();
        if (locations.Length > 0)
        {
            return locations.Length == 2 && !SameLocation(locations)
                ? RenderSeparate(
                    locations,
                    outcome.HasCountryMismatch)
                : RenderCompact(locations);
        }

        var unavailable = new[] { outcome.IPv4, outcome.IPv6 };
        if (unavailable.Any(
                candidate =>
                    candidate is LookupOutcome.DatabaseUnavailable))
        {
            return MissingDatabase();
        }

        var locationUnavailable = unavailable
            .OfType<LookupOutcome.LocationUnavailable>()
            .FirstOrDefault();
        if (locationUnavailable is not null)
        {
            return RenderUnavailable(locationUnavailable)!;
        }

        if (unavailable.All(
                candidate =>
                    candidate is LookupOutcome.PublicAddressUnavailable))
        {
            return PublicAddressesUnavailable();
        }

        return unavailable
            .Select(RenderUnavailable)
            .First(result => result is not null)!;
    }

    private static CommandResult? RenderUnavailable(LookupOutcome outcome) =>
        outcome switch
        {
            LookupOutcome.LocationUnavailable unavailable => new CommandResult(
                1,
                string.Empty,
                $"Approximate location unavailable for " +
                $"{unavailable.PublicIp.Family} " +
                $"{unavailable.PublicIp.Address}: GeoLite2 City has no country " +
                "for this address.\n"),
            LookupOutcome.DatabaseUnavailable => MissingDatabase(),
            LookupOutcome.PublicAddressUnavailable unavailable =>
                PublicAddressUnavailable(unavailable.Family),
            _ => null,
        };

    internal static CommandResult MissingDatabase() =>
        new(
            1,
            string.Empty,
            "GeoLite2 City database is missing or unreadable.\n" +
            "Run: geo setup\n");

    private static CommandResult PublicAddressUnavailable(IpFamily family) =>
        new(
            1,
            string.Empty,
            $"Public {family} address is unavailable.\n");

    private static CommandResult PublicAddressesUnavailable() =>
        new(
            1,
            string.Empty,
            "Public IPv4 and IPv6 addresses are unavailable.\n");

    private static CommandResult RenderCompact(
        LookupOutcome.Found[] locations)
    {
        var found = locations[0];
        var output =
            $"Approximate location: {RenderLocation(found)}\n" +
            string.Concat(locations.Select(RenderAddress));

        return new CommandResult(0, output, string.Empty);
    }

    private static CommandResult RenderSeparate(
        LookupOutcome.Found[] locations,
        bool hasCountryMismatch) =>
        new(
            hasCountryMismatch ? 2 : 0,
            RenderMismatchWarning(hasCountryMismatch) +
            string.Concat(locations.Select(RenderLocationRow)),
            string.Empty);

    private static string RenderMismatchWarning(bool hasCountryMismatch) =>
        hasCountryMismatch
            ? "WARNING: Possible VPN leak: IPv4 and IPv6 egress resolve " +
                "to different countries.\n"
            : string.Empty;

    private static string RenderLocationRow(LookupOutcome.Found found) =>
        $"Approximate location ({found.PublicIp.Family}): " +
        $"{RenderLocation(found)}\n" +
        RenderAddress(found);

    private static bool SameLocation(LookupOutcome.Found[] locations) =>
        locations[0].Country == locations[1].Country &&
        string.Equals(
            locations[0].City,
            locations[1].City,
            StringComparison.Ordinal);

    private static string RenderLocation(LookupOutcome.Found found)
    {
        var city = found.City is null ? string.Empty : $"{found.City}, ";
        return $"{GetFlag(found.Country)} {city}{found.Country}";
    }

    private static string RenderAddress(LookupOutcome.Found found) =>
        $"Public address ({found.PublicIp.Family}): " +
        $"{found.PublicIp.Address}\n";

    private static string GetFlag(CountryCode country) =>
        string.Concat(
            country.Value.Select(
                letter => char.ConvertFromUtf32(0x1F1E6 + letter - 'A')));
}
