namespace EgressGeo;

internal sealed record GeoLiteReleaseIdentity
{
    private GeoLiteReleaseIdentity(
        string repository,
        string releaseTag,
        Uri assetUrl,
        string digest)
    {
        Repository = repository;
        ReleaseTag = releaseTag;
        AssetUrl = assetUrl;
        Digest = digest;
    }

    internal string Repository { get; }

    internal string ReleaseTag { get; }

    internal Uri AssetUrl { get; }

    internal string Digest { get; }

    internal static GeoLiteReleaseIdentity? Create(
        string? repository,
        string? releaseTag,
        Uri? assetUrl,
        string? digest) =>
        GeoLiteReleaseContract.IsValidIdentity(
            repository,
            releaseTag,
            assetUrl,
            digest)
            ? new GeoLiteReleaseIdentity(
                repository!,
                releaseTag!,
                assetUrl!,
                digest!)
            : null;
}
