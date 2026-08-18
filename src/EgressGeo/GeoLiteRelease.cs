namespace EgressGeo;

internal sealed record GeoLiteRelease(
    string Repository,
    string Tag,
    DateTimeOffset PublishedAt,
    Uri AssetUrl,
    string Digest);
