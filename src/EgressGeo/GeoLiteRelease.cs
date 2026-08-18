namespace EgressGeo;

internal sealed record GeoLiteRelease
{
    private GeoLiteRelease(
        GeoLiteReleaseIdentity identity,
        DateTimeOffset publishedAt)
    {
        Identity = identity;
        PublishedAt = publishedAt;
    }

    internal GeoLiteReleaseIdentity Identity { get; }

    internal string Repository => Identity.Repository;

    internal string Tag => Identity.ReleaseTag;

    internal DateTimeOffset PublishedAt { get; }

    internal Uri AssetUrl => Identity.AssetUrl;

    internal string Digest => Identity.Digest;

    internal static GeoLiteRelease? Create(
        string? repository,
        string? tag,
        DateTimeOffset publishedAt,
        Uri? assetUrl,
        string? digest)
    {
        var identity = GeoLiteReleaseIdentity.Create(
            repository,
            tag,
            assetUrl,
            digest);
        return identity is not null && publishedAt != default
            ? new GeoLiteRelease(identity, publishedAt)
            : null;
    }
}
