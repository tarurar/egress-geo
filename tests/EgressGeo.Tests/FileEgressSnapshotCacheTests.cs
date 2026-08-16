using System.Runtime.Versioning;
using System.Text.Json;

namespace EgressGeo.Tests;

[TestClass]
[SupportedOSPlatform("linux")]
public sealed class FileEgressSnapshotCacheTests
{
    [TestMethod]
    public async Task Write_makes_snapshot_readable()
    {
        using var directory = new TemporaryDirectory();
        var cache = new FileEgressSnapshotCache(
            Path.Combine(directory.Path, "egress-geo", "snapshot.json"));
        var snapshot = Snapshot(
            new DateTimeOffset(
                2026,
                8,
                16,
                12,
                34,
                56,
                TimeSpan.Zero),
            "203.0.113.7",
            "Manama",
            "BH",
            "ipify");

        await cache.Write(snapshot, CancellationToken.None);
        var restored = await cache.Read(CancellationToken.None);

        Assert.IsNotNull(restored);
        Assert.AreEqual(snapshot.ObservedAt, restored.ObservedAt);
        Assert.HasCount(2, restored.Families);
        Assert.AreEqual(snapshot.Families[0], restored.Families[0]);
    }

    [TestMethod]
    public async Task Write_keeps_cache_data_private()
    {
        using var directory = new TemporaryDirectory();
        var cacheDirectory = Path.Combine(directory.Path, "egress-geo");
        var cachePath = Path.Combine(cacheDirectory, "snapshot.json");
        var cache = new FileEgressSnapshotCache(cachePath);
        var snapshot = Snapshot(
            new DateTimeOffset(
                2026,
                8,
                16,
                12,
                34,
                56,
                TimeSpan.Zero),
            "203.0.113.7",
            "Manama",
            "BH",
            "ipify");

        await cache.Write(snapshot, CancellationToken.None);

        Assert.AreEqual(
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute,
            File.GetUnixFileMode(cacheDirectory));
        Assert.AreEqual(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(cachePath));
    }

    [TestMethod]
    public async Task Write_replaces_snapshot_atomically()
    {
        using var directory = new TemporaryDirectory();
        var cachePath = Path.Combine(
            directory.Path,
            "egress-geo",
            "snapshot.json");
        var cache = new FileEgressSnapshotCache(cachePath);
        var previous = Snapshot(
            new DateTimeOffset(
                2026,
                8,
                16,
                11,
                34,
                56,
                TimeSpan.Zero),
            "203.0.113.7",
            "Manama",
            "BH",
            "ipify");
        var replacement = Snapshot(
            previous.ObservedAt + TimeSpan.FromHours(1),
            "198.51.100.5",
            "London",
            "GB",
            "ident.me");
        await cache.Write(previous, CancellationToken.None);
        await using var previousFile = new FileStream(
            cachePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        await cache.Write(replacement, CancellationToken.None);

        var current = await cache.Read(CancellationToken.None);
        Assert.IsNotNull(current);
        Assert.AreEqual(replacement.ObservedAt, current.ObservedAt);
        previousFile.Position = 0;
        using var stillPrevious = await JsonDocument.ParseAsync(previousFile);
        Assert.AreEqual(
            previous.ObservedAt,
            stillPrevious.RootElement
                .GetProperty("observedAt")
                .GetDateTimeOffset());
    }

    [TestMethod]
    public async Task Read_ignores_truncated_snapshot()
    {
        using var directory = new TemporaryDirectory();
        var cachePath = Path.Combine(directory.Path, "snapshot.json");
        await File.WriteAllTextAsync(cachePath, "{\"observedAt\":");
        var cache = new FileEgressSnapshotCache(cachePath);

        var snapshot = await cache.Read(CancellationToken.None);

        Assert.IsNull(snapshot);
    }

    [TestMethod]
    public async Task Read_ignores_malformed_snapshot()
    {
        using var directory = new TemporaryDirectory();
        var cachePath = Path.Combine(directory.Path, "snapshot.json");
        await File.WriteAllTextAsync(cachePath, "not JSON");
        var cache = new FileEgressSnapshotCache(cachePath);

        var snapshot = await cache.Read(CancellationToken.None);

        Assert.IsNull(snapshot);
    }

    [TestMethod]
    public async Task Read_ignores_missing_snapshot()
    {
        using var directory = new TemporaryDirectory();
        var cache = new FileEgressSnapshotCache(
            Path.Combine(directory.Path, "missing.json"));

        var snapshot = await cache.Read(CancellationToken.None);

        Assert.IsNull(snapshot);
    }

    [TestMethod]
    public async Task Read_propagates_unexpected_file_failure()
    {
        using var directory = new TemporaryDirectory();
        var cache = new FileEgressSnapshotCache(directory.Path);

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(
            () => cache.Read(CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task Read_ignores_incomplete_snapshot()
    {
        using var directory = new TemporaryDirectory();
        var cachePath = Path.Combine(directory.Path, "snapshot.json");
        await File.WriteAllTextAsync(
            cachePath,
            """
            {
              "observedAt": "2026-08-16T12:34:56+00:00",
              "families": [
                {
                  "family": "IPv4",
                  "address": "203.0.113.7",
                  "approximateCity": "Manama",
                  "countryCode": "BH",
                  "discoverySource": "ipify"
                }
              ]
            }
            """);
        var cache = new FileEgressSnapshotCache(cachePath);

        var snapshot = await cache.Read(CancellationToken.None);

        Assert.IsNull(snapshot);
    }

    private static CachedEgressSnapshot Snapshot(
        DateTimeOffset observedAt,
        string address,
        string city,
        string country,
        string source)
    {
        var ipv4 = CachedEgressFamily.Create(
            "IPv4",
            address,
            city,
            country,
            source);
        var ipv6 = CachedEgressFamily.Create(
            "IPv6",
            "2001:db8::7",
            city,
            country,
            source);
        return CachedEgressSnapshot.Create(observedAt, [ipv4, ipv6]) ??
            throw new AssertFailedException(
                "The filesystem test snapshot must be valid and complete.");
    }

    [TestMethod]
    public async Task Read_ignores_semantically_invalid_snapshot()
    {
        using var directory = new TemporaryDirectory();
        var cachePath = Path.Combine(directory.Path, "snapshot.json");
        await File.WriteAllTextAsync(
            cachePath,
            """
            {
              "observedAt": "2026-08-16T12:34:56+00:00",
              "families": [
                {
                  "family": "IPv4",
                  "address": "2001:db8::7",
                  "approximateCity": "Manama",
                  "countryCode": "BH",
                  "discoverySource": "ipify"
                },
                {
                  "family": "IPv6",
                  "address": "2001:db8::8",
                  "approximateCity": "Manama",
                  "countryCode": "BH",
                  "discoverySource": "ipify"
                }
              ]
            }
            """);
        var cache = new FileEgressSnapshotCache(cachePath);

        var snapshot = await cache.Read(CancellationToken.None);

        Assert.IsNull(snapshot);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"egress-geo-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
