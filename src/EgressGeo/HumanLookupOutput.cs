namespace EgressGeo;

internal static class HumanLookupOutput
{
    internal static CommandResult Render(LookupOutcome outcome) =>
        outcome switch
        {
            LookupOutcome.Found found => Render(found),
            LookupOutcome.LocationUnavailable unavailable => new CommandResult(
                1,
                string.Empty,
                $"Approximate location unavailable for IPv4 " +
                $"{unavailable.Address}: GeoLite2 City has no country " +
                "for this address.\n"),
            LookupOutcome.DatabaseUnavailable => MissingDatabase(),
            _ => throw new InvalidOperationException(
                $"Unknown lookup outcome: {outcome.GetType().Name}"),
        };

    internal static CommandResult MissingDatabase() =>
        new(
            1,
            string.Empty,
            "GeoLite2 City database is missing or unreadable.\n" +
            "Run: geo setup\n");

    internal static CommandResult PublicAddressUnavailable() =>
        new(
            1,
            string.Empty,
            "Public IPv4 address is unavailable.\n");

    private static CommandResult Render(LookupOutcome.Found found)
    {
        var city = found.City is null ? string.Empty : $"{found.City}, ";
        var output =
            $"Approximate location: {GetFlag(found.Country)} " +
            $"{city}{found.Country}\n" +
            $"Public address (IPv4): {found.Address}\n";

        return new CommandResult(0, output, string.Empty);
    }

    private static string GetFlag(CountryCode country) =>
        string.Concat(
            country.Value.Select(
                letter => char.ConvertFromUtf32(0x1F1E6 + letter - 'A')));
}
