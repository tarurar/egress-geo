namespace EgressGeo;

internal abstract record GeoLiteReleaseResolution
{
    private GeoLiteReleaseResolution()
    {
    }

    internal sealed record Found(GeoLiteRelease Release) :
        GeoLiteReleaseResolution;

    internal sealed record Invalid(string Reason) :
        GeoLiteReleaseResolution;

    internal sealed record Unavailable : GeoLiteReleaseResolution;
}
