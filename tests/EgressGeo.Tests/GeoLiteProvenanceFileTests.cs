using System.Runtime.Versioning;

namespace EgressGeo.Tests;

[TestClass]
[SupportedOSPlatform("linux")]
public sealed class GeoLiteProvenanceFileTests
{
    [TestMethod]
    public async Task Written_provenance_round_trips_as_a_private_file()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"egress-geo-provenance-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "provenance.json");
        Directory.CreateDirectory(directory);
        var provenance = GeoLiteTestData.Provenance(
            "2026.08.17",
            new DateTimeOffset(2026, 8, 17, 1, 2, 3, TimeSpan.Zero),
            new Uri(
                "https://github.com/P3TERX/GeoLite.mmdb/releases/download/" +
                "2026.08.17/GeoLite2-City.mmdb"),
            "sha256:" + new string('a', 64),
            new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero));
        try
        {
            await GeoLiteProvenanceFile.Write(
                path,
                provenance,
                CancellationToken.None);

            var read = await GeoLiteProvenanceFile.Read(
                path,
                CancellationToken.None);

            Assert.AreEqual(provenance, read);
            Assert.AreEqual(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Semantically_invalid_provenance_is_rejected()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"egress-geo-provenance-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "provenance.json");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "repository": "P3TERX/GeoLite.mmdb",
                  "releaseTag": "2026.08.17",
                  "publishedAt": "2026-08-17T01:00:00Z",
                  "assetUrl": "https://github.com/P3TERX/GeoLite.mmdb/releases/download/2026.08.17/GeoLite2-City.mmdb",
                  "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "databaseBuildTime": "2026-08-18T00:00:00Z",
                  "activatedAt": "2026-08-18T01:00:00Z"
                }
                """);

            var provenance = await GeoLiteProvenanceFile.Read(
                path,
                CancellationToken.None);

            Assert.IsNull(provenance);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
