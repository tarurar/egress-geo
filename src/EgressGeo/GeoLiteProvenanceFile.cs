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
            ProvenanceDocument.From(provenance),
            SerializerOptions,
            cancellationToken);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
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
            var document = await JsonSerializer
                .DeserializeAsync<ProvenanceDocument>(
                    stream,
                    SerializerOptions,
                    cancellationToken);
            return document?.ToProvenance();
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

    internal static GeoLiteProvenance? Read(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return JsonSerializer.Deserialize<ProvenanceDocument>(
                stream,
                SerializerOptions)?.ToProvenance();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                JsonException)
        {
            return null;
        }
    }

    private sealed record ProvenanceDocument(
        string? Repository,
        string? ReleaseTag,
        DateTimeOffset PublishedAt,
        Uri? AssetUrl,
        string? Digest,
        DateTimeOffset DatabaseBuildTime,
        DateTimeOffset ActivatedAt)
    {
        internal static ProvenanceDocument From(
            GeoLiteProvenance provenance) =>
            new(
                provenance.Repository,
                provenance.ReleaseTag,
                provenance.PublishedAt,
                provenance.AssetUrl,
                provenance.Digest,
                provenance.DatabaseBuildTime,
                provenance.ActivatedAt);

        internal GeoLiteProvenance? ToProvenance() =>
            GeoLiteProvenance.Parse(
                Repository,
                ReleaseTag,
                PublishedAt,
                AssetUrl,
                Digest,
                DatabaseBuildTime,
                ActivatedAt);
    }
}
