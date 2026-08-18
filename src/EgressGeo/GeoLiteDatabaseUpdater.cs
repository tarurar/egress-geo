using System.Security.Cryptography;

namespace EgressGeo;

internal sealed class GeoLiteDatabaseUpdater : IGeoLiteDatabaseUpdater
{
    private readonly GeoLiteInstallationStore installation;
    private readonly IGeoLiteReleaseSource source;
    private readonly IGeoLiteDatabaseInspector databaseInspector;
    private readonly TimeProvider timeProvider;

    internal GeoLiteDatabaseUpdater(
        GeoLiteUpdatePaths paths,
        IGeoLiteReleaseSource source,
        TimeProvider timeProvider)
        : this(
            paths,
            source,
            new MaxMindGeoLiteDatabaseInspector(),
            timeProvider)
    {
    }

    internal GeoLiteDatabaseUpdater(
        GeoLiteUpdatePaths paths,
        IGeoLiteReleaseSource source,
        IGeoLiteDatabaseInspector databaseInspector,
        TimeProvider timeProvider)
    {
        installation = new GeoLiteInstallationStore(
            paths ?? throw new ArgumentNullException(nameof(paths)));
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.databaseInspector = databaseInspector ??
            throw new ArgumentNullException(nameof(databaseInspector));
        this.timeProvider = timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<GeoLiteUpdateResult> Update(
        CancellationToken cancellationToken)
    {
        try
        {
            using var updateLock = installation.TryAcquireUpdateLock();
            if (updateLock is null)
            {
                return Failed("another GeoLite update is already running");
            }

            installation.RemoveInactiveDatabases(timeProvider.GetUtcNow());
            var resolution = await source.ResolveLatest(cancellationToken);
            return resolution is GeoLiteReleaseResolution.Found found
                ? await Acquire(found.Release, cancellationToken)
                : ResolutionFailure(resolution);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                CryptographicException)
        {
            return Failed("verified City database could not be activated");
        }
    }

    private async ValueTask<GeoLiteUpdateResult> Acquire(
        GeoLiteRelease release,
        CancellationToken cancellationToken)
    {
        var candidatePath = installation.CreateCandidatePath();
        try
        {
            var download = await Download(
                release,
                candidatePath,
                cancellationToken);
            if (download is GeoLiteAssetDownload.Failed failed)
            {
                return Failed(failed.Reason);
            }

            var candidateDigest = await GeoLiteDigest.Compute(
                candidatePath,
                cancellationToken);
            var candidateMetadata = databaseInspector.Read(candidatePath);
            var active = installation.ReadActive();
            var activeMetadata = active is null
                ? null
                : databaseInspector.Read(active.DatabasePath);
            var currentTime = timeProvider.GetUtcNow();
            var decision = GeoLiteCandidatePolicy.Evaluate(
                release,
                candidateDigest,
                candidateMetadata,
                activeMetadata,
                currentTime);
            return decision switch
            {
                GeoLiteCandidatePolicy.Decision.Rejected rejected =>
                    Failed(rejected.Reason),
                GeoLiteCandidatePolicy.Decision.Accepted accepted =>
                    await Activate(
                        candidatePath,
                        active,
                        accepted.Provenance,
                        currentTime,
                        cancellationToken),
                _ => throw new InvalidOperationException(
                    "Unknown GeoLite candidate decision."),
            };
        }
        finally
        {
            GeoLiteInstallationStore.DeleteIfPresent(candidatePath);
        }
    }

    private async ValueTask<GeoLiteUpdateResult> Activate(
        string candidatePath,
        GeoLiteInstallationStore.ActiveDatabase? active,
        GeoLiteProvenance provenance,
        DateTimeOffset currentTime,
        CancellationToken cancellationToken)
    {
        var activeDigest = active is null
            ? null
            : await GeoLiteDigest.Compute(
                active.DatabasePath,
                cancellationToken);
        var isNoChange = string.Equals(
            provenance.Digest,
            activeDigest,
            StringComparison.Ordinal);
        if (isNoChange &&
            active!.Provenance?.Matches(
                provenance,
                currentTime) == true)
        {
            installation.Secure(active);
            return new GeoLiteUpdateResult.NoChange(active.Provenance);
        }

        if (!isNoChange && active is not null)
        {
            installation.RetainForReaders(active, currentTime);
        }

        await installation.Activate(
            candidatePath,
            provenance,
            cancellationToken);
        return isNoChange
            ? new GeoLiteUpdateResult.NoChange(provenance)
            : new GeoLiteUpdateResult.Activated(provenance);
    }

    private async ValueTask<GeoLiteAssetDownload> Download(
        GeoLiteRelease release,
        string candidatePath,
        CancellationToken cancellationToken)
    {
        await using var candidate =
            GeoLiteInstallationStore.CreatePrivateCandidate(candidatePath);
        var result = await source.Download(
            release,
            candidate,
            cancellationToken);
        await candidate.FlushAsync(cancellationToken);
        candidate.Flush(flushToDisk: true);
        return result;
    }

    private static GeoLiteUpdateResult ResolutionFailure(
        GeoLiteReleaseResolution resolution) =>
        resolution switch
        {
            GeoLiteReleaseResolution.Invalid invalid =>
                Failed(invalid.Reason),
            GeoLiteReleaseResolution.Unavailable =>
                Failed("P3TERX release source is unavailable"),
            _ => throw new InvalidOperationException(
                "Unknown GeoLite release resolution."),
        };

    private static GeoLiteUpdateResult.Failed Failed(string reason) =>
        new(reason);
}
