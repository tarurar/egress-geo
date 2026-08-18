namespace EgressGeo;

internal sealed record GeoLiteUpdatePaths(string DataDirectory)
{
    internal string LegacyDatabasePath =>
        Path.Combine(DataDirectory, GeoLiteReleaseContract.AssetName);

    internal string ProvenancePath =>
        Path.Combine(DataDirectory, "provenance.json");

    internal string DatabaseDirectory =>
        Path.Combine(DataDirectory, "databases");

    internal string LockPath =>
        Path.Combine(DataDirectory, ".update.lock");

    internal string ManagedDatabasePath(string digest)
    {
        if (!GeoLiteReleaseContract.TryNormalizeDigest(
                digest,
                out var normalized) ||
            !string.Equals(digest, normalized, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The database digest must be normalized SHA-256.",
                nameof(digest));
        }

        return Path.Combine(
            DatabaseDirectory,
            digest["sha256:".Length..] + ".mmdb");
    }
}
