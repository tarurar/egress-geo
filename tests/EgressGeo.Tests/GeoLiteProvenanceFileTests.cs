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
        var provenance = new GeoLiteProvenance(
            "P3TERX/GeoLite.mmdb",
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
}
