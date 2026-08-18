namespace EgressGeo;

internal static class GeoLiteCandidatePolicy
{
    internal static Decision Evaluate(
        GeoLiteRelease release,
        string candidateDigest,
        GeoLiteDatabaseMetadata? candidate,
        GeoLiteDatabaseMetadata? active,
        DateTimeOffset currentTime)
    {
        if (currentTime < release.PublishedAt)
        {
            return Rejected(
                "P3TERX release publication time is in the future");
        }

        if (!string.Equals(
                candidateDigest,
                release.Digest,
                StringComparison.Ordinal))
        {
            return Rejected(
                "City database digest does not match the release");
        }

        if (candidate is null)
        {
            return Rejected(
                "downloaded asset is not a readable City database");
        }

        if (!GeoLiteDatabasePolicy.IsFresh(
                candidate.BuildTime,
                currentTime) ||
            release.PublishedAt < candidate.BuildTime)
        {
            return Rejected(
                "downloaded City database build time is not fresh");
        }

        if (active is not null &&
            GeoLiteDatabasePolicy.IsFresh(active.BuildTime, currentTime) &&
            candidate.BuildTime < active.BuildTime)
        {
            return Rejected(
                "downloaded City database would be a rollback");
        }

        var provenance = GeoLiteProvenance.Create(
            release,
            candidate.BuildTime,
            currentTime);
        return provenance is null
            ? Rejected("downloaded City database build time is not credible")
            : new Decision.Accepted(provenance);
    }

    private static Decision.Rejected Rejected(string reason) => new(reason);

    internal abstract record Decision
    {
        private Decision()
        {
        }

        internal sealed record Accepted(GeoLiteProvenance Provenance) :
            Decision;

        internal sealed record Rejected(string Reason) : Decision;
    }
}
