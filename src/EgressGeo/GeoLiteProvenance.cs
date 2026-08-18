namespace EgressGeo;

public sealed record GeoLiteProvenance
{
    private GeoLiteProvenance(
        GeoLiteReleaseIdentity identity,
        DateTimeOffset publishedAt,
        DateTimeOffset databaseBuildTime,
        DateTimeOffset activatedAt)
    {
        Identity = identity;
        PublishedAt = publishedAt;
        DatabaseBuildTime = databaseBuildTime;
        ActivatedAt = activatedAt;
    }

    private GeoLiteReleaseIdentity Identity { get; }

    public string Repository => Identity.Repository;

    public string ReleaseTag => Identity.ReleaseTag;

    public DateTimeOffset PublishedAt { get; }

    public Uri AssetUrl => Identity.AssetUrl;

    public string Digest => Identity.Digest;

    public DateTimeOffset DatabaseBuildTime { get; }

    public DateTimeOffset ActivatedAt { get; }

    internal static GeoLiteProvenance? Create(
        GeoLiteRelease release,
        DateTimeOffset databaseBuildTime,
        DateTimeOffset activatedAt) =>
        Create(
            release.Identity,
            release.PublishedAt,
            databaseBuildTime,
            activatedAt);

    internal static GeoLiteProvenance? Parse(
        string? repository,
        string? releaseTag,
        DateTimeOffset publishedAt,
        Uri? assetUrl,
        string? digest,
        DateTimeOffset databaseBuildTime,
        DateTimeOffset activatedAt)
    {
        var identity = GeoLiteReleaseIdentity.Create(
            repository,
            releaseTag,
            assetUrl,
            digest);
        return identity is null
            ? null
            : Create(
                identity,
                publishedAt,
                databaseBuildTime,
                activatedAt);
    }

    internal bool Matches(
        GeoLiteProvenance candidate,
        DateTimeOffset currentTime) =>
        Identity == candidate.Identity &&
        PublishedAt == candidate.PublishedAt &&
        DatabaseBuildTime == candidate.DatabaseBuildTime &&
        ActivatedAt <= currentTime;

    private static GeoLiteProvenance? Create(
        GeoLiteReleaseIdentity identity,
        DateTimeOffset publishedAt,
        DateTimeOffset databaseBuildTime,
        DateTimeOffset activatedAt) =>
        databaseBuildTime != default &&
        databaseBuildTime <= publishedAt &&
        publishedAt <= activatedAt
            ? new GeoLiteProvenance(
                identity,
                publishedAt,
                databaseBuildTime,
                activatedAt)
            : null;
}
