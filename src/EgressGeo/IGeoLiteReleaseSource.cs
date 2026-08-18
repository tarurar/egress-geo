namespace EgressGeo;

internal interface IGeoLiteReleaseSource
{
    ValueTask<GeoLiteReleaseResolution> ResolveLatest(
        CancellationToken cancellationToken);

    ValueTask<GeoLiteAssetDownload> Download(
        GeoLiteRelease release,
        Stream destination,
        CancellationToken cancellationToken);
}
