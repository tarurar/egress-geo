namespace EgressGeo.Tests;

internal static class GeoLiteTestData
{
    internal static GeoLiteRelease Release(
        string tag,
        DateTimeOffset publishedAt,
        Uri assetUrl,
        string digest) =>
        GeoLiteRelease.Create(
            GeoLiteReleaseContract.Repository,
            tag,
            publishedAt,
            assetUrl,
            digest) ?? throw new AssertFailedException(
            "The test release must satisfy the release contract.");

    internal static GeoLiteProvenance Provenance(
        string releaseTag,
        DateTimeOffset publishedAt,
        Uri assetUrl,
        string digest,
        DateTimeOffset databaseBuildTime,
        DateTimeOffset activatedAt) =>
        GeoLiteProvenance.Parse(
            GeoLiteReleaseContract.Repository,
            releaseTag,
            publishedAt,
            assetUrl,
            digest,
            databaseBuildTime,
            activatedAt) ?? throw new AssertFailedException(
            "The test provenance must satisfy the provenance contract.");
}
