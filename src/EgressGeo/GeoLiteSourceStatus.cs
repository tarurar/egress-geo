namespace EgressGeo;

public abstract record GeoLiteSourceStatus
{
    private GeoLiteSourceStatus()
    {
    }

    public sealed record Reachable(string ReleaseTag) : GeoLiteSourceStatus;

    public sealed record Invalid : GeoLiteSourceStatus;

    public sealed record Unavailable : GeoLiteSourceStatus;
}
