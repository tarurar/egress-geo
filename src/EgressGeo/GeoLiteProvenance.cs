namespace EgressGeo;

public sealed record GeoLiteProvenance(
    string Repository,
    string ReleaseTag,
    DateTimeOffset PublishedAt,
    Uri AssetUrl,
    string Digest,
    DateTimeOffset DatabaseBuildTime,
    DateTimeOffset ActivatedAt);
