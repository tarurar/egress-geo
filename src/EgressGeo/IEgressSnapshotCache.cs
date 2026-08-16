namespace EgressGeo;

public interface IEgressSnapshotCache
{
    ValueTask<CachedEgressSnapshot?> Read(
        CancellationToken cancellationToken);

    ValueTask Write(
        CachedEgressSnapshot snapshot,
        CancellationToken cancellationToken);
}
