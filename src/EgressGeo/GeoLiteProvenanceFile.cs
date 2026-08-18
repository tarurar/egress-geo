using System.Text.Json;

namespace EgressGeo;

internal static class GeoLiteProvenanceFile
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

    internal static async ValueTask Write(
        string path,
        GeoLiteProvenance provenance,
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

        await using var stream = new FileStream(path, options);
        await JsonSerializer.SerializeAsync(
            stream,
            provenance,
            SerializerOptions,
            cancellationToken);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    internal static async ValueTask<GeoLiteProvenance?> Read(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous |
                        FileOptions.SequentialScan,
                });
            var provenance = await JsonSerializer
                .DeserializeAsync<GeoLiteProvenance>(
                    stream,
                    SerializerOptions,
                    cancellationToken);
            return provenance is not null && IsValid(provenance)
                ? provenance
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                JsonException)
        {
            return null;
        }
    }

    internal static void SetPrivateFileMode(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static bool IsValid(GeoLiteProvenance provenance)
    {
        return GeoLiteReleaseContract.IsValidIdentity(
                provenance.Repository,
                provenance.ReleaseTag,
                provenance.AssetUrl,
                provenance.Digest) &&
            provenance.DatabaseBuildTime <= provenance.PublishedAt &&
            provenance.DatabaseBuildTime <= provenance.ActivatedAt &&
            provenance.PublishedAt <= provenance.ActivatedAt;
    }

}
