using System.Net;
using System.Text;

namespace EgressGeo.Tests;

[TestClass]
public sealed class P3terxGeoLiteReleaseSourceTests
{
    private const string Digest =
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [TestMethod]
    public async Task Resolve_selects_the_single_published_City_asset()
    {
        const string metadata = """
            {
              "tag_name": "2026.08.17",
              "draft": false,
              "prerelease": false,
              "published_at": "2026-08-17T01:02:03Z",
              "assets": [
                {
                  "name": "GeoLite2-City.mmdb",
                  "browser_download_url": "https://github.com/P3TERX/GeoLite.mmdb/releases/download/2026.08.17/GeoLite2-City.mmdb",
                  "digest": "sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
                }
              ]
            }
            """;
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        metadata,
                        Encoding.UTF8,
                        "application/json"),
                }));
        var source = new P3terxGeoLiteReleaseSource(
            httpClient,
            TimeProvider.System);

        var result = await source.ResolveLatest(CancellationToken.None);

        Assert.AreEqual(
            new GeoLiteReleaseResolution.Found(
                GeoLiteTestData.Release(
                    "2026.08.17",
                    new DateTimeOffset(
                        2026,
                        8,
                        17,
                        1,
                        2,
                        3,
                        TimeSpan.Zero),
                    new Uri(
                        "https://github.com/P3TERX/GeoLite.mmdb/releases/" +
                        "download/2026.08.17/GeoLite2-City.mmdb"),
                    Digest)),
            result);
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("""
        [
          {
            "name": "GeoLite2-City.mmdb",
            "browser_download_url": "https://github.com/P3TERX/GeoLite.mmdb/releases/download/2026.08.17/GeoLite2-City.mmdb",
            "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
          },
          {
            "name": "GeoLite2-City.mmdb",
            "browser_download_url": "https://github.com/P3TERX/GeoLite.mmdb/releases/download/2026.08.17/GeoLite2-City.mmdb",
            "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
          }
        ]
        """)]
    public async Task Resolve_rejects_missing_or_duplicate_City_assets(
        string assets)
    {
        var result = await Resolve(Metadata(assets));

        Assert.AreEqual(
            new GeoLiteReleaseResolution.Invalid(
                "release must contain exactly one City database"),
            result);
    }

    [TestMethod]
    [DataRow("sha256:abc")]
    [DataRow("")]
    [DataRow("sha512:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Resolve_rejects_a_missing_or_malformed_digest(
        string digest)
    {
        var assets = $$"""
            [
              {
                "name": "GeoLite2-City.mmdb",
                "browser_download_url": "https://github.com/P3TERX/GeoLite.mmdb/releases/download/2026.08.17/GeoLite2-City.mmdb",
                "digest": "{{digest}}"
              }
            ]
            """;

        var result = await Resolve(Metadata(assets));

        Assert.AreEqual(
            new GeoLiteReleaseResolution.Invalid(
                "City asset URL or digest is malformed"),
            result);
    }

    [TestMethod]
    public async Task Resolve_rejects_a_draft_release()
    {
        var metadata = Metadata("[]").Replace(
            "\"draft\": false",
            "\"draft\": true",
            StringComparison.Ordinal);

        var result = await Resolve(metadata);

        Assert.AreEqual(
            new GeoLiteReleaseResolution.Invalid(
                "latest release is not published"),
            result);
    }

    [TestMethod]
    public async Task Download_streams_only_the_selected_release_asset()
    {
        var release = GeoLiteTestData.Release(
            "2026.08.17",
            new DateTimeOffset(2026, 8, 17, 1, 2, 3, TimeSpan.Zero),
            new Uri(
                "https://github.com/P3TERX/GeoLite.mmdb/releases/download/" +
                "2026.08.17/GeoLite2-City.mmdb"),
            Digest);
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4]),
            });
        using var httpClient = new HttpClient(handler);
        var source = new P3terxGeoLiteReleaseSource(
            httpClient,
            TimeProvider.System);
        await using var destination = new MemoryStream();

        var result = await source.Download(
            release,
            destination,
            CancellationToken.None);

        Assert.IsInstanceOfType<GeoLiteAssetDownload.Downloaded>(result);
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 3, 4 },
            destination.ToArray());
        Assert.AreEqual(release.AssetUrl, handler.RequestUri);
    }

    [TestMethod]
    public async Task Download_rejects_an_oversized_asset_before_streaming()
    {
        var release = Release();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        response.Content.Headers.ContentLength = 128L * 1024 * 1024 + 1;
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(response));
        var source = new P3terxGeoLiteReleaseSource(
            httpClient,
            TimeProvider.System);
        await using var destination = new MemoryStream();

        var result = await source.Download(
            release,
            destination,
            CancellationToken.None);

        Assert.IsInstanceOfType<GeoLiteAssetDownload.Failed>(result);
        Assert.AreEqual(0, destination.Length);
    }

    [TestMethod]
    public async Task Download_reports_HTTP_and_redirect_failures_safely()
    {
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                new HttpResponseMessage(HttpStatusCode.Found)));
        var source = new P3terxGeoLiteReleaseSource(
            httpClient,
            TimeProvider.System);
        await using var destination = new MemoryStream();

        var result = await source.Download(
            Release(),
            destination,
            CancellationToken.None);

        Assert.AreEqual(
            new GeoLiteAssetDownload.Failed(
                "City database download failed or exceeded the size limit"),
            result);
    }

    private static async ValueTask<GeoLiteReleaseResolution> Resolve(
        string metadata)
    {
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        metadata,
                        Encoding.UTF8,
                        "application/json"),
                }));
        var source = new P3terxGeoLiteReleaseSource(
            httpClient,
            TimeProvider.System);
        return await source.ResolveLatest(CancellationToken.None);
    }

    private static string Metadata(string assets) => $$"""
        {
          "tag_name": "2026.08.17",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-08-17T01:02:03Z",
          "assets": {{assets}}
        }
        """;

    private static GeoLiteRelease Release() =>
        GeoLiteTestData.Release(
            "2026.08.17",
            new DateTimeOffset(2026, 8, 17, 1, 2, 3, TimeSpan.Zero),
            new Uri(
                "https://github.com/P3TERX/GeoLite.mmdb/releases/download/" +
                "2026.08.17/GeoLite2-City.mmdb"),
            Digest);

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) :
        HttpMessageHandler
    {
        internal Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(response);
        }
    }
}
