using System.Text.Json;

namespace EgressGeo;

public sealed class FileEgressSnapshotCache(string path) :
    IEgressSnapshotCache
{
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite |
        UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async ValueTask<CachedEgressSnapshot?> Read(
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = OpenForRead();
            var document = await JsonSerializer
                .DeserializeAsync<SnapshotDocument>(
                    stream,
                    SerializerOptions,
                    cancellationToken);
            return document?.ToSnapshot();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public async ValueTask Write(
        CachedEgressSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var directory = GetDirectory();
        EnsurePrivateDirectory(directory);
        var temporaryPath = CreateTemporaryPath(directory);
        var replaced = false;
        try
        {
            await WriteTemporarySnapshot(
                temporaryPath,
                snapshot,
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
            replaced = true;
        }
        finally
        {
            if (!replaced)
            {
                DeleteTemporaryFile(temporaryPath);
            }
        }
    }

    private FileStream OpenForRead() =>
        new(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read | FileShare.Delete,
                Options = FileOptions.Asynchronous,
            });

    private string GetDirectory() =>
        Path.GetDirectoryName(path) ??
        throw new InvalidOperationException(
            "The cache path must include a directory.");

    private static void EnsurePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(directory, PrivateDirectoryMode);
            File.SetUnixFileMode(directory, PrivateDirectoryMode);
            return;
        }

        Directory.CreateDirectory(directory);
    }

    private string CreateTemporaryPath(string directory) =>
        Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

    private static async ValueTask WriteTemporarySnapshot(
        string temporaryPath,
        CachedEgressSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using (var stream = new FileStream(
            temporaryPath,
            CreatePrivateFileOptions()))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                SnapshotDocument.From(snapshot),
                SerializerOptions,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(temporaryPath, PrivateFileMode);
        }
    }

    private static FileStreamOptions CreatePrivateFileOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
        };
        if (OperatingSystem.IsLinux())
        {
            options.UnixCreateMode = PrivateFileMode;
        }

        return options;
    }

    private static void DeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record SnapshotDocument(
        DateTimeOffset ObservedAt,
        IReadOnlyList<FamilyDocument?>? Families)
    {
        internal CachedEgressSnapshot? ToSnapshot()
        {
            var families = Families?
                .Select(family => family?.ToFamily())
                .ToArray();
            return CachedEgressSnapshot.Create(ObservedAt, families);
        }

        internal static SnapshotDocument From(CachedEgressSnapshot snapshot) =>
            new(
                snapshot.ObservedAt,
                snapshot.Families.Select(FamilyDocument.From).ToArray());
    }

    private sealed record FamilyDocument(
        string? Family,
        string? Address,
        string? ApproximateCity,
        string? CountryCode,
        string? DiscoverySource)
    {
        internal CachedEgressFamily? ToFamily() =>
            CachedEgressFamily.Create(
                Family,
                Address,
                ApproximateCity,
                CountryCode,
                DiscoverySource);

        internal static FamilyDocument From(CachedEgressFamily family) =>
            new(
                family.Family,
                family.Address,
                family.ApproximateCity,
                family.CountryCode,
                family.DiscoverySource);
    }
}
