using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace EgressGeo.Tests;

[TestClass]
[SupportedOSPlatform("linux")]
public sealed class GeoLiteInstallationStoreTests
{
    [TestMethod]
    public async Task Abandoned_activation_keeps_the_previous_pair_selected()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"egress-geo-store-{Guid.NewGuid():N}");
        var paths = new GeoLiteUpdatePaths(rootPath);
        try
        {
            var previousDatabase = new byte[] { 9, 8, 7 };
            var previous = Provenance(
                new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero),
                previousDatabase);
            Directory.CreateDirectory(paths.DatabaseDirectory);
            await File.WriteAllBytesAsync(
                paths.ManagedDatabasePath(previous.Digest),
                previousDatabase);
            await GeoLiteProvenanceFile.Write(
                paths.ProvenancePath,
                previous,
                CancellationToken.None);
            var candidateDatabase = new byte[] { 1, 2, 3 };
            var candidate = Provenance(
                new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero),
                candidateDatabase);
            var candidatePath = Path.Combine(rootPath, ".candidate.tmp");
            await File.WriteAllBytesAsync(candidatePath, candidateDatabase);
            var store = new GeoLiteInstallationStore(paths);

            using (await store.PrepareActivation(
                candidatePath,
                candidate,
                CancellationToken.None))
            {
                AssertPreviousPairSelected(
                    store,
                    paths,
                    previous,
                    previousDatabase);
                Assert.IsTrue(
                    File.Exists(
                        paths.ManagedDatabasePath(candidate.Digest)),
                    "The candidate must be durable before the pointer moves.");
            }

            AssertPreviousPairSelected(
                store,
                paths,
                previous,
                previousDatabase);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(59, true)]
    [DataRow(61, false)]
    public async Task Maintenance_honors_the_reader_grace_period(
        int inactiveMinutes,
        bool shouldRemain)
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"egress-geo-store-{Guid.NewGuid():N}");
        var paths = new GeoLiteUpdatePaths(rootPath);
        try
        {
            var inactive = Provenance(
                new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero),
                [9, 8, 7]);
            var active = Provenance(
                new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero),
                [1, 2, 3]);
            var inactivePath = paths.ManagedDatabasePath(inactive.Digest);
            Directory.CreateDirectory(paths.DatabaseDirectory);
            await File.WriteAllBytesAsync(inactivePath, [9, 8, 7]);
            await File.WriteAllBytesAsync(
                paths.ManagedDatabasePath(active.Digest),
                [1, 2, 3]);
            await GeoLiteProvenanceFile.Write(
                paths.ProvenancePath,
                active,
                CancellationToken.None);
            var currentTime = new DateTimeOffset(
                2026,
                8,
                19,
                12,
                0,
                0,
                TimeSpan.Zero);
            File.SetLastWriteTimeUtc(
                inactivePath,
                (currentTime - TimeSpan.FromMinutes(inactiveMinutes))
                    .UtcDateTime);
            var store = new GeoLiteInstallationStore(paths);

            store.RemoveInactiveDatabases(currentTime);

            Assert.AreEqual(shouldRemain, File.Exists(inactivePath));
            Assert.IsTrue(
                File.Exists(paths.ManagedDatabasePath(active.Digest)));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static void AssertPreviousPairSelected(
        GeoLiteInstallationStore store,
        GeoLiteUpdatePaths paths,
        GeoLiteProvenance expected,
        byte[] expectedDatabase)
    {
        var active = store.ReadActive() ??
            throw new AssertFailedException(
                "The previous installation must remain active.");
        Assert.AreEqual(expected, active.Provenance);
        Assert.AreEqual(
            paths.ManagedDatabasePath(expected.Digest),
            active.DatabasePath);
        CollectionAssert.AreEqual(
            expectedDatabase,
            File.ReadAllBytes(active.DatabasePath));
    }

    private static GeoLiteProvenance Provenance(
        DateTimeOffset buildTime,
        byte[] database)
    {
        var tag = buildTime.ToString("yyyy.MM.dd");
        return GeoLiteTestData.Provenance(
            tag,
            buildTime + TimeSpan.FromHours(1),
            new Uri(
                "https://github.com/P3TERX/GeoLite.mmdb/releases/download/" +
                $"{tag}/GeoLite2-City.mmdb"),
            "sha256:" +
                Convert.ToHexStringLower(SHA256.HashData(database)),
            buildTime,
            buildTime + TimeSpan.FromDays(1));
    }
}
