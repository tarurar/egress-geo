using System.Security.Cryptography;
using System.Runtime.Versioning;
using Microsoft.Extensions.Time.Testing;

namespace EgressGeo.Tests;

[TestClass]
[SupportedOSPlatform("linux")]
public sealed class GeoLiteDatabaseUpdaterTests
{
    [TestMethod]
    public async Task Update_activates_a_verified_City_database_with_provenance()
    {
        using var environment = new UpdateTestEnvironment();
        var database = await File.ReadAllBytesAsync(FixturePath);
        var digest = "sha256:" + Convert.ToHexStringLower(
            SHA256.HashData(database));
        using var fixture = new MaxMindGeolocationDatabase(FixturePath);
        var buildTime = fixture.BuildTime ??
            throw new AssertFailedException(
                "The synthetic City fixture must have a build time.");
        var activatedAt = buildTime + TimeSpan.FromDays(1);
        var release = new GeoLiteRelease(
            "P3TERX/GeoLite.mmdb",
            buildTime.ToString("yyyy.MM.dd"),
            buildTime + TimeSpan.FromHours(1),
            new Uri(
                "https://github.com/P3TERX/GeoLite.mmdb/releases/download/" +
                $"{buildTime:yyyy.MM.dd}/GeoLite2-City.mmdb"),
            digest);
        var updater = new GeoLiteDatabaseUpdater(
            environment.Paths,
            new FakeGeoLiteReleaseSource(release, database),
            new FakeTimeProvider(activatedAt));

        var result = await updater.Update(CancellationToken.None);

        Assert.AreEqual(
            new GeoLiteUpdateResult.Activated(
                new GeoLiteProvenance(
                    release.Repository,
                    release.Tag,
                    release.PublishedAt,
                    release.AssetUrl,
                    release.Digest,
                    buildTime,
                    activatedAt)),
            result);
        CollectionAssert.AreEqual(
            database,
            await File.ReadAllBytesAsync(environment.Paths.DatabasePath));
        Assert.AreEqual(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(environment.Paths.DatabasePath));
        Assert.AreEqual(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(environment.Paths.ProvenancePath));
        Assert.HasCount(0, environment.WorkFiles);
    }

    [TestMethod]
    public async Task Activation_failure_restores_the_previous_database()
    {
        using var environment = new UpdateTestEnvironment();
        var candidate = await File.ReadAllBytesAsync(FixturePath);
        var previous = candidate.Append((byte)0).ToArray();
        Directory.CreateDirectory(
            Path.GetDirectoryName(environment.Paths.DatabasePath)!);
        await File.WriteAllBytesAsync(
            environment.Paths.DatabasePath,
            previous);
        Assert.IsNotNull(
            new MaxMindGeoLiteDatabaseInspector().Read(
                environment.Paths.DatabasePath),
            "The previous test database must remain readable.");
        Directory.CreateDirectory(environment.Paths.ProvenancePath);
        using var fixture = new MaxMindGeolocationDatabase(FixturePath);
        var buildTime = fixture.BuildTime!.Value;
        var release = CreateRelease(buildTime, candidate);
        var updater = new GeoLiteDatabaseUpdater(
            environment.Paths,
            new FakeGeoLiteReleaseSource(release, candidate),
            new FakeTimeProvider(buildTime + TimeSpan.FromDays(1)));

        var result = await updater.Update(CancellationToken.None);

        Assert.AreEqual(
            new GeoLiteUpdateResult.Failed(
                "verified City database could not be activated"),
            result);
        CollectionAssert.AreEqual(
            previous,
            await File.ReadAllBytesAsync(environment.Paths.DatabasePath));
        Assert.HasCount(0, environment.WorkFiles);
    }

    [TestMethod]
    public async Task Older_candidate_is_rejected_as_a_rollback()
    {
        using var environment = new UpdateTestEnvironment();
        var buildTime = new DateTimeOffset(
            2026,
            8,
            16,
            0,
            0,
            0,
            TimeSpan.Zero);
        var previous = new byte[] { 9, 8, 7 };
        var candidate = new byte[] { 1, 2, 3 };
        Directory.CreateDirectory(
            Path.GetDirectoryName(environment.Paths.DatabasePath)!);
        await File.WriteAllBytesAsync(
            environment.Paths.DatabasePath,
            previous);
        var release = CreateRelease(buildTime, candidate);
        var inspector = new FakeGeoLiteDatabaseInspector(
            environment.Paths.DatabasePath,
            activeBuildTime: buildTime + TimeSpan.FromDays(1),
            candidateBuildTime: buildTime);
        var updater = new GeoLiteDatabaseUpdater(
            environment.Paths,
            new FakeGeoLiteReleaseSource(release, candidate),
            inspector,
            new FakeTimeProvider(buildTime + TimeSpan.FromDays(2)));

        var result = await updater.Update(CancellationToken.None);

        Assert.AreEqual(
            new GeoLiteUpdateResult.Failed(
                "downloaded City database would be a rollback"),
            result);
        CollectionAssert.AreEqual(
            previous,
            await File.ReadAllBytesAsync(environment.Paths.DatabasePath));
    }

    [TestMethod]
    public async Task Stale_candidate_is_rejected_before_activation()
    {
        using var environment = new UpdateTestEnvironment();
        var buildTime = new DateTimeOffset(
            2026,
            7,
            17,
            0,
            0,
            0,
            TimeSpan.Zero);
        var candidate = new byte[] { 1, 2, 3 };
        var release = CreateRelease(buildTime, candidate);
        var inspector = new FakeGeoLiteDatabaseInspector(
            environment.Paths.DatabasePath,
            activeBuildTime: buildTime,
            candidateBuildTime: buildTime);
        var updater = new GeoLiteDatabaseUpdater(
            environment.Paths,
            new FakeGeoLiteReleaseSource(release, candidate),
            inspector,
            new FakeTimeProvider(buildTime + TimeSpan.FromDays(31)));

        var result = await updater.Update(CancellationToken.None);

        Assert.AreEqual(
            new GeoLiteUpdateResult.Failed(
                "downloaded City database build time is not fresh"),
            result);
        Assert.IsFalse(File.Exists(environment.Paths.DatabasePath));
        Assert.IsFalse(File.Exists(environment.Paths.ProvenancePath));
    }

    [TestMethod]
    public async Task Identical_release_is_a_no_change_and_records_provenance()
    {
        using var environment = new UpdateTestEnvironment();
        var database = await File.ReadAllBytesAsync(FixturePath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(environment.Paths.DatabasePath)!);
        await File.WriteAllBytesAsync(
            environment.Paths.DatabasePath,
            database);
        await using var activeHandle = new FileStream(
            environment.Paths.DatabasePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var fixture = new MaxMindGeolocationDatabase(FixturePath);
        var buildTime = fixture.BuildTime!.Value;
        var release = CreateRelease(buildTime, database);
        var activatedAt = buildTime + TimeSpan.FromDays(1);
        var updater = new GeoLiteDatabaseUpdater(
            environment.Paths,
            new FakeGeoLiteReleaseSource(release, database),
            new FakeTimeProvider(activatedAt));

        var result = await updater.Update(CancellationToken.None);

        var expected = new GeoLiteProvenance(
            release.Repository,
            release.Tag,
            release.PublishedAt,
            release.AssetUrl,
            release.Digest,
            buildTime,
            activatedAt);
        Assert.AreEqual(new GeoLiteUpdateResult.NoChange(expected), result);
        Assert.AreEqual(
            expected,
            await GeoLiteProvenanceFile.Read(
                environment.Paths.ProvenancePath,
                CancellationToken.None));
        Assert.AreEqual(database.Length, activeHandle.Length);
        Assert.HasCount(0, environment.WorkFiles);
    }

    [TestMethod]
    public async Task No_change_preserves_the_original_activation_time()
    {
        using var environment = new UpdateTestEnvironment();
        var database = await File.ReadAllBytesAsync(FixturePath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(environment.Paths.DatabasePath)!);
        await File.WriteAllBytesAsync(
            environment.Paths.DatabasePath,
            database);
        using var fixture = new MaxMindGeolocationDatabase(FixturePath);
        var buildTime = fixture.BuildTime!.Value;
        var release = CreateRelease(buildTime, database);
        var originalActivation = buildTime + TimeSpan.FromDays(1);
        var original = new GeoLiteProvenance(
            release.Repository,
            release.Tag,
            release.PublishedAt,
            release.AssetUrl,
            release.Digest,
            buildTime,
            originalActivation);
        await GeoLiteProvenanceFile.Write(
            environment.Paths.ProvenancePath,
            original,
            CancellationToken.None);
        var updater = new GeoLiteDatabaseUpdater(
            environment.Paths,
            new FakeGeoLiteReleaseSource(release, database),
            new FakeTimeProvider(originalActivation + TimeSpan.FromDays(1)));

        var result = await updater.Update(CancellationToken.None);

        Assert.AreEqual(new GeoLiteUpdateResult.NoChange(original), result);
        Assert.AreEqual(
            original,
            await GeoLiteProvenanceFile.Read(
                environment.Paths.ProvenancePath,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Digest_mismatch_preserves_the_previous_database()
    {
        using var environment = new UpdateTestEnvironment();
        var previous = new byte[] { 9, 8, 7 };
        var candidate = await File.ReadAllBytesAsync(FixturePath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(environment.Paths.DatabasePath)!);
        await File.WriteAllBytesAsync(
            environment.Paths.DatabasePath,
            previous);
        using var fixture = new MaxMindGeolocationDatabase(FixturePath);
        var buildTime = fixture.BuildTime!.Value;
        var release = CreateRelease(buildTime, candidate) with
        {
            Digest = "sha256:" + new string('0', 64),
        };
        var updater = new GeoLiteDatabaseUpdater(
            environment.Paths,
            new FakeGeoLiteReleaseSource(release, candidate),
            new FakeTimeProvider(buildTime + TimeSpan.FromDays(1)));

        var result = await updater.Update(CancellationToken.None);

        Assert.AreEqual(
            new GeoLiteUpdateResult.Failed(
                "City database digest does not match the release"),
            result);
        CollectionAssert.AreEqual(
            previous,
            await File.ReadAllBytesAsync(environment.Paths.DatabasePath));
    }

    [TestMethod]
    public async Task Invalid_MMDB_preserves_the_previous_database()
    {
        using var environment = new UpdateTestEnvironment();
        var previous = new byte[] { 9, 8, 7 };
        var candidate = new byte[] { 1, 2, 3 };
        Directory.CreateDirectory(
            Path.GetDirectoryName(environment.Paths.DatabasePath)!);
        await File.WriteAllBytesAsync(
            environment.Paths.DatabasePath,
            previous);
        var buildTime = new DateTimeOffset(
            2026,
            8,
            16,
            0,
            0,
            0,
            TimeSpan.Zero);
        var release = CreateRelease(buildTime, candidate);
        var updater = new GeoLiteDatabaseUpdater(
            environment.Paths,
            new FakeGeoLiteReleaseSource(release, candidate),
            new FakeTimeProvider(buildTime + TimeSpan.FromDays(1)));

        var result = await updater.Update(CancellationToken.None);

        Assert.AreEqual(
            new GeoLiteUpdateResult.Failed(
                "downloaded asset is not a readable City database"),
            result);
        CollectionAssert.AreEqual(
            previous,
            await File.ReadAllBytesAsync(environment.Paths.DatabasePath));
    }

    [TestMethod]
    public async Task Download_failure_preserves_the_previous_database()
    {
        using var environment = new UpdateTestEnvironment();
        var previous = new byte[] { 9, 8, 7 };
        Directory.CreateDirectory(
            Path.GetDirectoryName(environment.Paths.DatabasePath)!);
        await File.WriteAllBytesAsync(
            environment.Paths.DatabasePath,
            previous);
        var buildTime = new DateTimeOffset(
            2026,
            8,
            16,
            0,
            0,
            0,
            TimeSpan.Zero);
        var release = CreateRelease(buildTime, []);
        var failure = new GeoLiteAssetDownload.Failed(
            "City database download failed or exceeded the size limit");
        var updater = new GeoLiteDatabaseUpdater(
            environment.Paths,
            new FakeGeoLiteReleaseSource(release, [], failure),
            new FakeTimeProvider(buildTime + TimeSpan.FromDays(1)));

        var result = await updater.Update(CancellationToken.None);

        Assert.AreEqual(
            new GeoLiteUpdateResult.Failed(failure.Reason),
            result);
        CollectionAssert.AreEqual(
            previous,
            await File.ReadAllBytesAsync(environment.Paths.DatabasePath));
    }

    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "GeoLite2-City-Test.mmdb");

    private static GeoLiteRelease CreateRelease(
        DateTimeOffset buildTime,
        byte[] database) =>
        new(
            "P3TERX/GeoLite.mmdb",
            buildTime.ToString("yyyy.MM.dd"),
            buildTime + TimeSpan.FromHours(1),
            new Uri(
                "https://github.com/P3TERX/GeoLite.mmdb/releases/download/" +
                $"{buildTime:yyyy.MM.dd}/GeoLite2-City.mmdb"),
            "sha256:" + Convert.ToHexStringLower(SHA256.HashData(database)));

    private sealed class UpdateTestEnvironment : IDisposable
    {
        private readonly string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"egress-geo-update-{Guid.NewGuid():N}");

        internal UpdateTestEnvironment()
        {
            Paths = new GeoLiteUpdatePaths(
                Path.Combine(rootPath, "GeoLite2-City.mmdb"),
                Path.Combine(rootPath, "provenance.json"));
        }

        internal GeoLiteUpdatePaths Paths { get; }

        internal string[] WorkFiles => Directory.Exists(rootPath)
            ? Directory.GetFiles(rootPath, ".*.tmp")
            : [];

        public void Dispose() => Directory.Delete(rootPath, recursive: true);
    }

    private sealed class FakeGeoLiteReleaseSource : IGeoLiteReleaseSource
    {
        private readonly GeoLiteRelease release;
        private readonly byte[] database;
        private readonly GeoLiteAssetDownload downloadResult;

        internal FakeGeoLiteReleaseSource(
            GeoLiteRelease release,
            byte[] database,
            GeoLiteAssetDownload? downloadResult = null)
        {
            this.release = release;
            this.database = database;
            this.downloadResult = downloadResult ??
                new GeoLiteAssetDownload.Downloaded();
        }

        public ValueTask<GeoLiteReleaseResolution> ResolveLatest(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<GeoLiteReleaseResolution>(
                new GeoLiteReleaseResolution.Found(release));

        public async ValueTask<GeoLiteAssetDownload> Download(
            GeoLiteRelease requestedRelease,
            Stream destination,
            CancellationToken cancellationToken)
        {
            Assert.AreEqual(release, requestedRelease);
            await destination.WriteAsync(database, cancellationToken);
            return downloadResult;
        }
    }

    private sealed class FakeGeoLiteDatabaseInspector(
        string activePath,
        DateTimeOffset activeBuildTime,
        DateTimeOffset candidateBuildTime) : IGeoLiteDatabaseInspector
    {
        public GeoLiteDatabaseMetadata? Read(string path) =>
            new(string.Equals(path, activePath, StringComparison.Ordinal)
                ? activeBuildTime
                : candidateBuildTime);
    }
}
