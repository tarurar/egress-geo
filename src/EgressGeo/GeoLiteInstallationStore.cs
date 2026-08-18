namespace EgressGeo;

internal sealed class GeoLiteInstallationStore
{
    private static readonly TimeSpan ReaderRetention = TimeSpan.FromHours(1);
    private readonly GeoLiteUpdatePaths paths;

    internal GeoLiteInstallationStore(GeoLiteUpdatePaths paths)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    internal FileStream? TryAcquireUpdateLock()
    {
        CreatePrivateDirectory(paths.DataDirectory);
        try
        {
            var options = PrivateFileOptions(
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite);
            var stream = new FileStream(paths.LockPath, options);
            GeoLiteProvenanceFile.SetPrivateFileMode(paths.LockPath);
            return stream;
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal string CreateCandidatePath() =>
        TemporaryPath(paths.DataDirectory, "candidate");

    internal ActiveDatabase? ReadActive()
    {
        var provenance = ReadProvenance();
        if (provenance is not null)
        {
            var databasePath = paths.ManagedDatabasePath(provenance.Digest);
            return File.Exists(databasePath)
                ? new ActiveDatabase(databasePath, provenance)
                : null;
        }

        return File.Exists(paths.LegacyDatabasePath)
            ? new ActiveDatabase(paths.LegacyDatabasePath, null)
            : null;
    }

    internal string ResolveDatabasePath()
    {
        var provenance = ReadProvenance();
        return provenance is null
            ? paths.LegacyDatabasePath
            : paths.ManagedDatabasePath(provenance.Digest);
    }

    internal async ValueTask Activate(
        string candidatePath,
        GeoLiteProvenance provenance,
        CancellationToken cancellationToken)
    {
        using var activation = await PrepareActivation(
            candidatePath,
            provenance,
            cancellationToken);
        activation.Commit();
    }

    internal async ValueTask<PreparedActivation> PrepareActivation(
        string candidatePath,
        GeoLiteProvenance provenance,
        CancellationToken cancellationToken)
    {
        CreatePrivateDirectory(paths.DatabaseDirectory);
        var databasePath = paths.ManagedDatabasePath(provenance.Digest);
        if (await HasDigest(
                databasePath,
                provenance.Digest,
                cancellationToken))
        {
            File.Delete(candidatePath);
        }
        else
        {
            File.Move(candidatePath, databasePath, overwrite: true);
        }

        GeoLiteProvenanceFile.SetPrivateFileMode(databasePath);
        var temporaryPath = TemporaryPath(
            paths.DataDirectory,
            "provenance");
        try
        {
            await GeoLiteProvenanceFile.Write(
                temporaryPath,
                provenance,
                cancellationToken);
            return new PreparedActivation(
                temporaryPath,
                paths.ProvenancePath);
        }
        catch
        {
            DeleteIfPresent(temporaryPath);
            throw;
        }
    }

    internal void Secure(ActiveDatabase active)
    {
        GeoLiteProvenanceFile.SetPrivateFileMode(active.DatabasePath);
        if (active.Provenance is not null)
        {
            GeoLiteProvenanceFile.SetPrivateFileMode(paths.ProvenancePath);
        }
    }

    internal void RetainForReaders(
        ActiveDatabase active,
        DateTimeOffset deactivatedAt) =>
        File.SetLastWriteTimeUtc(
            active.DatabasePath,
            deactivatedAt.UtcDateTime);

    internal void RemoveInactiveDatabases(DateTimeOffset currentTime)
    {
        var provenance = ReadProvenance();
        if (provenance is null || !Directory.Exists(paths.DatabaseDirectory))
        {
            return;
        }

        var activePath = paths.ManagedDatabasePath(provenance.Digest);
        if (!File.Exists(activePath))
        {
            return;
        }

        var deleteBefore = currentTime - ReaderRetention;
        TryDeleteExpired(paths.LegacyDatabasePath, deleteBefore);
        try
        {
            foreach (var path in Directory.EnumerateFiles(
                paths.DatabaseDirectory,
                "*.mmdb",
                SearchOption.TopDirectoryOnly))
            {
                if (!string.Equals(
                        path,
                        activePath,
                        StringComparison.Ordinal) &&
                    IsManagedDatabaseName(Path.GetFileName(path)))
                {
                    TryDeleteExpired(path, deleteBefore);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is retried by the next serialized update.
        }
    }

    internal static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    internal static FileStream CreatePrivateCandidate(string path) =>
        new(path, PrivateFileOptions(FileMode.CreateNew, FileAccess.Write));

    private GeoLiteProvenance? ReadProvenance()
    {
        if (!File.Exists(paths.ProvenancePath) ||
            File.GetAttributes(paths.ProvenancePath)
                .HasFlag(FileAttributes.ReparsePoint))
        {
            return null;
        }

        return GeoLiteProvenanceFile.Read(paths.ProvenancePath);
    }

    private static async ValueTask<bool> HasDigest(
        string path,
        string expectedDigest,
        CancellationToken cancellationToken) =>
        File.Exists(path) &&
        string.Equals(
            await GeoLiteDigest.Compute(path, cancellationToken),
            expectedDigest,
            StringComparison.Ordinal);

    private static bool IsManagedDatabaseName(string fileName) =>
        fileName is [.. var digest, '.', 'm', 'm', 'd', 'b'] &&
        digest.Length == 64 &&
        digest.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void TryDeleteExpired(
        string path,
        DateTimeOffset deleteBefore)
    {
        try
        {
            if (File.GetLastWriteTimeUtc(path) <= deleteBefore.UtcDateTime)
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup cannot invalidate the already selected active pair.
        }
    }

    private static FileStreamOptions PrivateFileOptions(
        FileMode mode,
        FileAccess access)
    {
        var options = new FileStreamOptions
        {
            Mode = mode,
            Access = access,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous |
                FileOptions.SequentialScan,
        };
        if (OperatingSystem.IsLinux())
        {
            options.UnixCreateMode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    private static string TemporaryPath(string directory, string role) =>
        Path.Combine(directory, $".{role}.{Guid.NewGuid():N}.tmp");

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

    internal sealed record ActiveDatabase(
        string DatabasePath,
        GeoLiteProvenance? Provenance);

    internal sealed class PreparedActivation : IDisposable
    {
        private string? temporaryPath;
        private readonly string provenancePath;

        internal PreparedActivation(
            string temporaryPath,
            string provenancePath)
        {
            this.temporaryPath = temporaryPath;
            this.provenancePath = provenancePath;
        }

        internal void Commit()
        {
            var preparedPath = temporaryPath ??
                throw new InvalidOperationException(
                    "The GeoLite activation is no longer prepared.");
            File.Move(preparedPath, provenancePath, overwrite: true);
            temporaryPath = null;
        }

        public void Dispose()
        {
            if (temporaryPath is not null)
            {
                DeleteIfPresent(temporaryPath);
                temporaryPath = null;
            }
        }
    }
}
