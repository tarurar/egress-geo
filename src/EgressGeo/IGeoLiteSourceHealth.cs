namespace EgressGeo;

public interface IGeoLiteSourceHealth
{
    ValueTask<GeoLiteSourceStatus> Check(
        CancellationToken cancellationToken);
}
