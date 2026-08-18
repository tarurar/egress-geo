namespace EgressGeo;

internal static class GeoLiteSetupOutput
{
    internal static CommandResult Render(
        GeoLiteUpdateResult result,
        bool isScheduled) =>
        isScheduled ? RenderScheduled(result) : RenderInteractive(result);

    private static CommandResult RenderInteractive(
        GeoLiteUpdateResult result) =>
        result switch
        {
            GeoLiteUpdateResult.Activated activated => Success(
                "activated from",
                activated.Provenance),
            GeoLiteUpdateResult.NoChange noChange => Success(
                "is current at",
                noChange.Provenance),
            GeoLiteUpdateResult.Failed failed => new CommandResult(
                1,
                string.Empty,
                $"geo setup: {failed.Reason}; previous database preserved.\n"),
            _ => throw new InvalidOperationException(
                "Unknown GeoLite update result."),
        };

    private static CommandResult RenderScheduled(
        GeoLiteUpdateResult result) =>
        result switch
        {
            GeoLiteUpdateResult.Activated => new CommandResult(
                0,
                "geo update: database updated and verified.\n",
                string.Empty),
            GeoLiteUpdateResult.NoChange => new CommandResult(
                0,
                "geo update: no update available; current database " +
                "verified.\n",
                string.Empty),
            GeoLiteUpdateResult.Failed => new CommandResult(
                1,
                string.Empty,
                "geo update: failed; previous database preserved.\n"),
            _ => throw new InvalidOperationException(
                "Unknown GeoLite update result."),
        };

    private static CommandResult Success(
        string action,
        GeoLiteProvenance provenance) =>
        new(
            0,
            $"GeoLite2 City {action} P3TERX release " +
            $"{provenance.ReleaseTag}.\n" +
            "P3TERX is a third-party source, not an official MaxMind " +
            "service.\n" +
            $"Verified digest: {provenance.Digest}\n" +
            "This product includes GeoLite Data created by MaxMind, " +
            "available from https://www.maxmind.com.\n" +
            "IP geolocation is approximate.\n" +
            "Run: geo\n",
            string.Empty);
}
