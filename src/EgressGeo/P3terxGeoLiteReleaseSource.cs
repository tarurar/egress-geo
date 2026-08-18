using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace EgressGeo;

internal sealed class P3terxGeoLiteReleaseSource :
    IGeoLiteReleaseSource,
    IGeoLiteSourceHealth
{
    private static readonly Uri LatestReleaseUrl = new(
        "https://api.github.com/repos/P3TERX/GeoLite.mmdb/releases/latest");
    private static readonly TimeSpan MetadataBudget = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DownloadBudget = TimeSpan.FromMinutes(2);
    private const long MaximumMetadataBytes = 2L * 1024 * 1024;
    private const long MaximumDownloadBytes = 128L * 1024 * 1024;
    private readonly HttpClient httpClient;
    private readonly TimeProvider timeProvider;

    internal P3terxGeoLiteReleaseSource(
        HttpClient httpClient,
        TimeProvider timeProvider)
    {
        this.httpClient = httpClient ??
            throw new ArgumentNullException(nameof(httpClient));
        this.timeProvider = timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<GeoLiteReleaseResolution> ResolveLatest(
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(
            MetadataBudget,
            timeProvider);
        using var requestCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            LatestReleaseUrl);
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue("egress-geo", "0.1"));
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                requestCancellation.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new GeoLiteReleaseResolution.Unavailable();
            }

            if (MaximumMetadataBytes <
                response.Content.Headers.ContentLength)
            {
                return new GeoLiteReleaseResolution.Invalid(
                    "release metadata exceeded the size limit");
            }

            await using var content = await response.Content.ReadAsStreamAsync(
                requestCancellation.Token);
            await using var buffered = new MemoryStream();
            if (!await CopyBounded(
                    content,
                    buffered,
                    MaximumMetadataBytes,
                    requestCancellation.Token))
            {
                return new GeoLiteReleaseResolution.Invalid(
                    "release metadata exceeded the size limit");
            }

            buffered.Position = 0;
            using var document = await JsonDocument.ParseAsync(
                buffered,
                cancellationToken: requestCancellation.Token);
            var resolution = Parse(document.RootElement);
            return resolution is GeoLiteReleaseResolution.Found found &&
                timeProvider.GetUtcNow() < found.Release.PublishedAt
                    ? new GeoLiteReleaseResolution.Invalid(
                        "release publication time is in the future")
                    : resolution;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return new GeoLiteReleaseResolution.Unavailable();
        }
        catch (HttpRequestException)
        {
            return new GeoLiteReleaseResolution.Unavailable();
        }
        catch (IOException)
        {
            return new GeoLiteReleaseResolution.Unavailable();
        }
        catch (JsonException)
        {
            return new GeoLiteReleaseResolution.Invalid(
                "release metadata is not valid JSON");
        }
    }

    public async ValueTask<GeoLiteSourceStatus> Check(
        CancellationToken cancellationToken) =>
        await ResolveLatest(cancellationToken) switch
        {
            GeoLiteReleaseResolution.Found found =>
                new GeoLiteSourceStatus.Reachable(found.Release.Tag),
            GeoLiteReleaseResolution.Invalid =>
                new GeoLiteSourceStatus.Invalid(),
            GeoLiteReleaseResolution.Unavailable =>
                new GeoLiteSourceStatus.Unavailable(),
            _ => throw new InvalidOperationException(
                "Unknown GeoLite release resolution."),
        };

    public async ValueTask<GeoLiteAssetDownload> Download(
        GeoLiteRelease release,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(destination);

        using var timeout = new CancellationTokenSource(
            DownloadBudget,
            timeProvider);
        using var requestCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
        try
        {
            using var response = await httpClient.GetAsync(
                release.AssetUrl,
                HttpCompletionOption.ResponseHeadersRead,
                requestCancellation.Token);
            var responseUrl = response.RequestMessage?.RequestUri ??
                release.AssetUrl;
            if (!response.IsSuccessStatusCode ||
                responseUrl.Scheme != Uri.UriSchemeHttps ||
                MaximumDownloadBytes < response.Content.Headers.ContentLength)
            {
                return FailedDownload();
            }

            await using var content = await response.Content.ReadAsStreamAsync(
                requestCancellation.Token);
            return await CopyBounded(
                    content,
                    destination,
                    MaximumDownloadBytes,
                    requestCancellation.Token)
                ? new GeoLiteAssetDownload.Downloaded()
                : FailedDownload();
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return FailedDownload();
        }
        catch (HttpRequestException)
        {
            return FailedDownload();
        }
        catch (IOException)
        {
            return FailedDownload();
        }
    }

    private static async ValueTask<bool> CopyBounded(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            var bytesRead = await source.ReadAsync(
                buffer,
                cancellationToken);
            if (bytesRead == 0)
            {
                return true;
            }

            totalBytes += bytesRead;
            if (maximumBytes < totalBytes)
            {
                return false;
            }

            await destination.WriteAsync(
                buffer.AsMemory(0, bytesRead),
                cancellationToken);
        }
    }

    private static GeoLiteReleaseResolution Parse(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object ||
            GetBoolean(release, "draft") is not false ||
            GetBoolean(release, "prerelease") is not false)
        {
            return Invalid("latest release is not published");
        }

        var tag = GetString(release, "tag_name");
        var published = GetString(release, "published_at");
        if (!GeoLiteReleaseContract.IsDatedTag(tag) ||
            !DateTimeOffset.TryParse(
                published,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out var publishedAt))
        {
            return Invalid("release identity is missing or malformed");
        }

        if (!release.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            return Invalid("release assets are missing");
        }

        var cityAssets = assets
            .EnumerateArray()
            .Where(asset =>
                string.Equals(
                    GetString(asset, "name"),
                    GeoLiteReleaseContract.AssetName,
                    StringComparison.Ordinal))
            .ToArray();
        if (cityAssets.Length != 1)
        {
            return Invalid("release must contain exactly one City database");
        }

        var assetUrlValue = GetString(
            cityAssets[0],
            "browser_download_url");
        var digestValue = GetString(cityAssets[0], "digest");
        if (!GeoLiteReleaseContract.TryParseAssetUrl(
                assetUrlValue,
                tag!,
                out var assetUrl) ||
            !GeoLiteReleaseContract.TryNormalizeDigest(
                digestValue,
                out var digest))
        {
            return Invalid("City asset URL or digest is malformed");
        }

        return new GeoLiteReleaseResolution.Found(
            new GeoLiteRelease(
                GeoLiteReleaseContract.Repository,
                tag!,
                publishedAt,
                assetUrl,
                digest));
    }

    private static bool? GetBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static GeoLiteReleaseResolution Invalid(string reason) =>
        new GeoLiteReleaseResolution.Invalid(reason);

    private static GeoLiteAssetDownload FailedDownload() =>
        new GeoLiteAssetDownload.Failed(
            "City database download failed or exceeded the size limit");
}
