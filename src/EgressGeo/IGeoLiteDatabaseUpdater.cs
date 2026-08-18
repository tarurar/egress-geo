namespace EgressGeo;

public interface IGeoLiteDatabaseUpdater
{
    ValueTask<GeoLiteUpdateResult> Update(
        CancellationToken cancellationToken);
}
