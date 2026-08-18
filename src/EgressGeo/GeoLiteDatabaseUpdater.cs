using System.Security.Cryptography;

namespace EgressGeo;

internal sealed class GeoLiteDatabaseUpdater : IGeoLiteDatabaseUpdater
{
    private readonly GeoLiteUpdatePaths paths;
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
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.databaseInspector = databaseInspector ??
            throw new ArgumentNullException(nameof(databaseInspector));
        this.timeProvider = timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<GeoLiteUpdateResult> Update(
        CancellationToken cancellationToken)
    {
        var resolution = await source.ResolveLatest(cancellationToken);
        if (resolution is not GeoLiteReleaseResolution.Found found)
        {
            return ResolutionFailure(resolution);
        }

        var currentTime = timeProvider.GetUtcNow();
        if (currentTime < found.Release.PublishedAt)
        {
            return Failed("P3TERX release publication time is in the future");
        }

        var directory = Path.GetDirectoryName(paths.DatabasePath) ??
            throw new InvalidOperationException(
                "The GeoLite database path must have a parent directory.");
        var candidatePath = TemporaryPath(directory);
        try
        {
            CreatePrivateDirectory(directory);
            var download = await Download(
                found.Release,
                candidatePath,
                cancellationToken);
            if (download is GeoLiteAssetDownload.Failed downloadFailure)
            {
                return Failed(downloadFailure.Reason);
            }

            var candidateDigest = await GeoLiteDigest.Compute(
                candidatePath,
                cancellationToken);
            if (!string.Equals(
                    candidateDigest,
                    found.Release.Digest,
                    StringComparison.Ordinal))
            {
                return Failed("City database digest does not match the release");
            }

            var candidateMetadata = databaseInspector.Read(candidatePath);
            if (candidateMetadata is null)
            {
                return Failed("downloaded asset is not a readable City database");
            }

            if (!GeoLiteDatabasePolicy.IsFresh(
                    candidateMetadata.BuildTime,
                    currentTime) ||
                found.Release.PublishedAt < candidateMetadata.BuildTime)
            {
                return Failed("downloaded City database build time is not fresh");
            }

            var activeMetadata = databaseInspector.Read(
                paths.DatabasePath);
            if (activeMetadata is not null &&
                GeoLiteDatabasePolicy.IsFresh(
                    activeMetadata.BuildTime,
                    currentTime) &&
                candidateMetadata.BuildTime < activeMetadata.BuildTime)
            {
                return Failed("downloaded City database would be a rollback");
            }

            var provenance = new GeoLiteProvenance(
                found.Release.Repository,
                found.Release.Tag,
                found.Release.PublishedAt,
                found.Release.AssetUrl,
                found.Release.Digest,
                candidateMetadata.BuildTime,
                currentTime);
            if (await IsCurrentRelease(
                    found.Release.Digest,
                    cancellationToken))
            {
                var existingProvenance = await ReadExistingProvenance(
                    cancellationToken);
                GeoLiteProvenanceFile.SetPrivateFileMode(paths.DatabasePath);
                if (MatchesCurrentRelease(
                        existingProvenance,
                        found.Release,
                        candidateMetadata.BuildTime,
                        currentTime))
                {
                    GeoLiteProvenanceFile.SetPrivateFileMode(
                        paths.ProvenancePath);
                    return new GeoLiteUpdateResult.NoChange(
                        existingProvenance!);
                }

                await ReplaceProvenance(provenance, cancellationToken);
                return new GeoLiteUpdateResult.NoChange(provenance);
            }

            await Activate(candidatePath, provenance, cancellationToken);
            return new GeoLiteUpdateResult.Activated(provenance);
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
        finally
        {
            DeleteIfPresent(candidatePath);
        }
    }

    private async ValueTask<GeoLiteAssetDownload> Download(
        GeoLiteRelease release,
        string candidatePath,
        CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous |
                FileOptions.SequentialScan,
        };
        if (OperatingSystem.IsLinux())
        {
            options.UnixCreateMode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var candidate = new FileStream(candidatePath, options);
        var result = await source.Download(
            release,
            candidate,
            cancellationToken);
        await candidate.FlushAsync(cancellationToken);
        return result;
    }

    private async ValueTask Activate(
        string candidatePath,
        GeoLiteProvenance provenance,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(paths.ProvenancePath) ??
            throw new InvalidOperationException(
                "The provenance path must have a parent directory.");
        CreatePrivateDirectory(directory);
        var provenanceTemporary = TemporaryPath(directory);
        var databaseBackup = TemporaryPath(directory);
        var provenanceBackup = TemporaryPath(directory);
        var hadDatabase = File.Exists(paths.DatabasePath);
        var hadProvenance = File.Exists(paths.ProvenancePath);
        try
        {
            await GeoLiteProvenanceFile.Write(
                provenanceTemporary,
                provenance,
                cancellationToken);
            BackupIfPresent(
                paths.DatabasePath,
                databaseBackup,
                hadDatabase);
            BackupIfPresent(
                paths.ProvenancePath,
                provenanceBackup,
                hadProvenance);
            File.Move(candidatePath, paths.DatabasePath, overwrite: true);
            try
            {
                File.Move(
                    provenanceTemporary,
                    paths.ProvenancePath,
                    overwrite: true);
            }
            catch
            {
                Restore(
                    paths.DatabasePath,
                    databaseBackup,
                    hadDatabase);
                Restore(
                    paths.ProvenancePath,
                    provenanceBackup,
                    hadProvenance);
                throw;
            }
        }
        finally
        {
            DeleteIfPresent(provenanceTemporary);
            DeleteIfPresent(databaseBackup);
            DeleteIfPresent(provenanceBackup);
        }
    }

    private async ValueTask ReplaceProvenance(
        GeoLiteProvenance provenance,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(paths.ProvenancePath) ??
            throw new InvalidOperationException(
                "The provenance path must have a parent directory.");
        CreatePrivateDirectory(directory);
        var temporaryPath = TemporaryPath(directory);
        try
        {
            await GeoLiteProvenanceFile.Write(
                temporaryPath,
                provenance,
                cancellationToken);
            File.Move(temporaryPath, paths.ProvenancePath, overwrite: true);
        }
        finally
        {
            DeleteIfPresent(temporaryPath);
        }
    }

    private async ValueTask<bool> IsCurrentRelease(
        string releaseDigest,
        CancellationToken cancellationToken) =>
        File.Exists(paths.DatabasePath) &&
        string.Equals(
            await GeoLiteDigest.Compute(
                paths.DatabasePath,
                cancellationToken),
            releaseDigest,
            StringComparison.Ordinal);

    private async ValueTask<GeoLiteProvenance?> ReadExistingProvenance(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.ProvenancePath) ||
            File.GetAttributes(paths.ProvenancePath)
                .HasFlag(FileAttributes.ReparsePoint))
        {
            return null;
        }

        return await GeoLiteProvenanceFile.Read(
            paths.ProvenancePath,
            cancellationToken);
    }

    private static bool MatchesCurrentRelease(
        GeoLiteProvenance? provenance,
        GeoLiteRelease release,
        DateTimeOffset buildTime,
        DateTimeOffset currentTime) =>
        provenance is not null &&
        string.Equals(
            provenance.Repository,
            release.Repository,
            StringComparison.Ordinal) &&
        string.Equals(
            provenance.ReleaseTag,
            release.Tag,
            StringComparison.Ordinal) &&
        provenance.PublishedAt == release.PublishedAt &&
        provenance.AssetUrl == release.AssetUrl &&
        string.Equals(
            provenance.Digest,
            release.Digest,
            StringComparison.Ordinal) &&
        provenance.DatabaseBuildTime == buildTime &&
        provenance.ActivatedAt <= currentTime;

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

    private static string TemporaryPath(string directory) =>
        Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
        else
        {
            Directory.CreateDirectory(path);
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void BackupIfPresent(
        string source,
        string backup,
        bool isPresent)
    {
        if (isPresent)
        {
            File.Copy(source, backup);
            GeoLiteProvenanceFile.SetPrivateFileMode(backup);
        }
    }

    private static void Restore(
        string destination,
        string backup,
        bool hadOriginal)
    {
        if (hadOriginal)
        {
            File.Move(backup, destination, overwrite: true);
        }
        else
        {
            DeleteIfPresent(destination);
        }
    }
}
