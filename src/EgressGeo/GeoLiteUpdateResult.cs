namespace EgressGeo;

public abstract record GeoLiteUpdateResult
{
    private GeoLiteUpdateResult()
    {
    }

    public sealed record Activated(GeoLiteProvenance Provenance) :
        GeoLiteUpdateResult;

    public sealed record NoChange(GeoLiteProvenance Provenance) :
        GeoLiteUpdateResult;

    public sealed record Failed(string Reason) : GeoLiteUpdateResult;
}
