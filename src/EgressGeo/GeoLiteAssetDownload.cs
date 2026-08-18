namespace EgressGeo;

internal abstract record GeoLiteAssetDownload
{
    private GeoLiteAssetDownload()
    {
    }

    internal sealed record Downloaded : GeoLiteAssetDownload;

    internal sealed record Failed(string Reason) : GeoLiteAssetDownload;
}
