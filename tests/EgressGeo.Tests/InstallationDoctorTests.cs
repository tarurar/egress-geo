using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;

namespace EgressGeo.Tests;

[TestClass]
[SupportedOSPlatform("linux")]
public sealed class InstallationDoctorTests
{
    private static readonly DateTimeOffset CurrentTime = new(
        2026,
        8,
        17,
        12,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public async Task Healthy_installation_passes_every_required_check()
    {
        using var environment = new DoctorTestEnvironment();
        environment.CreateHealthyFiles();
        var doctor = environment.CreateDoctor(
            databaseBuildTime: CurrentTime - TimeSpan.FromDays(2),
            snapshot: Snapshot(CurrentTime - TimeSpan.FromHours(1)));

        var report = await doctor.Examine(CancellationToken.None);

        Assert.IsTrue(report.IsHealthy);
        CollectionAssert.AreEqual(
            new[]
            {
                new DoctorCheck(
                    DoctorCheckStatus.Healthy,
                    "application",
                    "installed and executable"),
                new DoctorCheck(
                    DoctorCheckStatus.Healthy,
                    "database",
                    "readable; 2 days old (built 2026-08-15 UTC)"),
                new DoctorCheck(
                    DoctorCheckStatus.Healthy,
                    "provenance",
                    "P3TERX release 2026.08.15; digest verified"),
                new DoctorCheck(
                    DoctorCheckStatus.Healthy,
                    "GeoLite source",
                    "P3TERX release 2026.08.15 is reachable"),
                new DoctorCheck(
                    DoctorCheckStatus.Healthy,
                    "update timer",
                    "installed, enabled, and active"),
                new DoctorCheck(
                    DoctorCheckStatus.Healthy,
                    "cache",
                    "valid; 1 hour old"),
                new DoctorCheck(
                    DoctorCheckStatus.Healthy,
                    "IPv4 endpoints",
                    "ipify and ident.me reachable"),
                new DoctorCheck(
                    DoctorCheckStatus.Healthy,
                    "IPv6 endpoints",
                    "ipify and ident.me reachable"),
            },
            report.Checks.ToArray());
    }

    [TestMethod]
    public async Task Missing_database_fails_without_stopping_other_checks()
    {
        using var environment = new DoctorTestEnvironment();
        environment.CreateHealthyFiles();
        File.Delete(environment.Paths.DatabasePath);
        var doctor = environment.CreateDoctor(
            CurrentTime - TimeSpan.FromDays(2),
            Snapshot(CurrentTime - TimeSpan.FromHours(1)));

        var report = await doctor.Examine(CancellationToken.None);

        Assert.AreEqual(
            new DoctorCheck(
                DoctorCheckStatus.Failed,
                "database",
                "missing; run: geo setup"),
            FindCheck(report, "database"));
        Assert.HasCount(8, report.Checks);
        Assert.AreEqual(
            DoctorCheckStatus.Healthy,
            FindCheck(report, "IPv4 endpoints").Status);
    }

    [TestMethod]
    public async Task Missing_application_fails_with_install_repair_remediation()
    {
        using var environment = new DoctorTestEnvironment();
        environment.CreateHealthyFiles();
        File.Delete(environment.Paths.ApplicationPath);
        var doctor = environment.CreateDoctor(
            CurrentTime - TimeSpan.FromDays(2),
            Snapshot(CurrentTime - TimeSpan.FromHours(1)));

        var report = await doctor.Examine(CancellationToken.None);

        Assert.AreEqual(
            new DoctorCheck(
                DoctorCheckStatus.Failed,
                "application",
                "missing; re-run install.sh"),
            FindCheck(report, "application"));
    }

    [TestMethod]
    public async Task Unreadable_database_fails_without_attempting_a_lookup()
    {
        using var environment = new DoctorTestEnvironment();
        environment.CreateHealthyFiles();
        var doctor = environment.CreateDoctor(
            CurrentTime - TimeSpan.FromDays(2),
            Snapshot(CurrentTime - TimeSpan.FromHours(1)),
            databaseAvailable: false);

        var report = await doctor.Examine(CancellationToken.None);

        Assert.AreEqual(
            new DoctorCheck(
                DoctorCheckStatus.Failed,
                "database",
                "present but unreadable"),
            FindCheck(report, "database"));
    }

    [TestMethod]
    public async Task Stale_database_reports_build_age_and_fails()
    {
        using var environment = new DoctorTestEnvironment();
        environment.CreateHealthyFiles();
        var doctor = environment.CreateDoctor(
            CurrentTime - TimeSpan.FromDays(31),
            Snapshot(CurrentTime - TimeSpan.FromHours(1)));

        var report = await doctor.Examine(CancellationToken.None);

        Assert.AreEqual(
            new DoctorCheck(
                DoctorCheckStatus.Failed,
                "database",
                "readable but stale; 31 days old " +
                "(built 2026-07-17 UTC)"),
            FindCheck(report, "database"));
    }

    [TestMethod]
    public async Task Malformed_provenance_fails_with_setup_remediation()
    {
        using var environment = new DoctorTestEnvironment();
        environment.CreateHealthyFiles();
        await File.WriteAllTextAsync(
            environment.Paths.ProvenancePath,
            "{ not provenance }");
        File.SetUnixFileMode(
            environment.Paths.ProvenancePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var doctor = environment.CreateDoctor(
            CurrentTime - TimeSpan.FromDays(2),
            Snapshot(CurrentTime - TimeSpan.FromHours(1)));

        var report = await doctor.Examine(CancellationToken.None);

        Assert.AreEqual(
            new DoctorCheck(
                DoctorCheckStatus.Failed,
                "provenance",
                "malformed; run: geo setup"),
            FindCheck(report, "provenance"));
    }

    [TestMethod]
    public async Task Unreachable_P3TERX_source_fails_without_leaking_details()
    {
        using var environment = new DoctorTestEnvironment();
        environment.CreateHealthyFiles();
        var doctor = environment.CreateDoctor(
            CurrentTime - TimeSpan.FromDays(2),
            Snapshot(CurrentTime - TimeSpan.FromHours(1)),
            sourceStatus: new GeoLiteSourceStatus.Unavailable());

        var report = await doctor.Examine(CancellationToken.None);

        Assert.AreEqual(
            new DoctorCheck(
                DoctorCheckStatus.Failed,
                "GeoLite source",
                "P3TERX release source is unreachable"),
            FindCheck(report, "GeoLite source"));
    }

    [TestMethod]
    public async Task Disabled_timer_fails_with_install_repair_remediation()
    {
        using var environment = new DoctorTestEnvironment();
        environment.CreateHealthyFiles();
        var doctor = environment.CreateDoctor(
            CurrentTime - TimeSpan.FromDays(2),
            Snapshot(CurrentTime - TimeSpan.FromHours(1)),
            timerState: new UserTimerState.Available(
                IsEnabled: false,
                IsActive: false));

        var report = await doctor.Examine(CancellationToken.None);

        Assert.AreEqual(
            new DoctorCheck(
                DoctorCheckStatus.Failed,
                "update timer",
                "disabled and inactive; re-run install.sh"),
            FindCheck(report, "update timer"));
    }

    [TestMethod]
    public async Task Corrupt_cache_fails_with_safe_remediation()
    {
        using var environment = new DoctorTestEnvironment();
        environment.CreateHealthyFiles();
        var doctor = environment.CreateDoctor(
            CurrentTime - TimeSpan.FromDays(2),
            snapshot: null);

        var report = await doctor.Examine(CancellationToken.None);

        Assert.AreEqual(
            new DoctorCheck(
                DoctorCheckStatus.Failed,
                "cache",
                "corrupt or invalid; remove the cache file"),
            FindCheck(report, "cache"));
    }

    [TestMethod]
    public async Task Unreachable_IPv4_endpoints_fail_after_the_shared_deadline()
    {
        using var environment = new DoctorTestEnvironment();
        environment.CreateHealthyFiles();
        var timeProvider = new FakeTimeProvider(CurrentTime);
        var doctor = environment.CreateDoctor(
            CurrentTime - TimeSpan.FromDays(2),
            Snapshot(CurrentTime - TimeSpan.FromHours(1)),
            publicIp: new NeverPublicIpClient(),
            timeProvider: timeProvider);
        var examining = doctor.Examine(CancellationToken.None).AsTask();

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var report = await examining.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(
            new DoctorCheck(
                DoctorCheckStatus.Failed,
                "IPv4 endpoints",
                "unreachable through ipify and ident.me"),
            FindCheck(report, "IPv4 endpoints"));
    }

    [TestMethod]
    public async Task Missing_IPv6_is_an_informational_capability_result()
    {
        using var environment = new DoctorTestEnvironment();
        environment.CreateHealthyFiles();
        var doctor = environment.CreateDoctor(
            CurrentTime - TimeSpan.FromDays(2),
            Snapshot(CurrentTime - TimeSpan.FromHours(1)),
            publicIp: new IPv4OnlyPublicIpClient());

        var report = await doctor.Examine(CancellationToken.None);

        Assert.IsTrue(report.IsHealthy);
        Assert.AreEqual(
            new DoctorCheck(
                DoctorCheckStatus.Information,
                "IPv6 endpoints",
                "unavailable; IPv6 may not be configured"),
            FindCheck(report, "IPv6 endpoints"));
    }

    [TestMethod]
    public async Task Timer_timeout_fails_without_stopping_later_checks()
    {
        using var environment = new DoctorTestEnvironment();
        environment.CreateHealthyFiles();
        var timeProvider = new FakeTimeProvider(CurrentTime);
        var doctor = environment.CreateDoctor(
            CurrentTime - TimeSpan.FromDays(2),
            Snapshot(CurrentTime - TimeSpan.FromHours(1)),
            timeProvider: timeProvider,
            timerStateReader: new NeverUserTimerStateReader());
        var examining = doctor.Examine(CancellationToken.None).AsTask();

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var report = await examining.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(
            new DoctorCheck(
                DoctorCheckStatus.Failed,
                "update timer",
                "state check timed out; inspect user systemd"),
            FindCheck(report, "update timer"));
        Assert.AreEqual(
            DoctorCheckStatus.Healthy,
            FindCheck(report, "cache").Status);
        Assert.HasCount(8, report.Checks);
    }

    [TestMethod]
    public async Task Unexpected_cache_failure_escapes_diagnostics()
    {
        using var environment = new DoctorTestEnvironment();
        environment.CreateHealthyFiles();
        var doctor = environment.CreateDoctor(
            CurrentTime - TimeSpan.FromDays(2),
            Snapshot(CurrentTime - TimeSpan.FromHours(1)),
            cache: new FailingEgressSnapshotCache());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => doctor.Examine(CancellationToken.None).AsTask());
    }

    private static DoctorCheck FindCheck(DoctorReport report, string name) =>
        report.Checks.Single(check => check.Name == name);

    private static CachedEgressSnapshot Snapshot(DateTimeOffset observedAt)
    {
        var ipv4 = CachedEgressFamily.Create(
            "IPv4",
            "203.0.113.7",
            "Manama",
            "BH",
            "ipify");
        var ipv6 = CachedEgressFamily.Create(
            "IPv6",
            "2001:db8::7",
            "Manama",
            "BH",
            "ipify");
        return CachedEgressSnapshot.Create(observedAt, [ipv4, ipv6]) ??
            throw new AssertFailedException(
                "The doctor test snapshot must be valid.");
    }

    private sealed class DoctorTestEnvironment : IDisposable
    {
        private readonly string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"egress-geo-doctor-{Guid.NewGuid():N}");

        internal DoctorTestEnvironment()
        {
            Paths = new DoctorPaths(
                Path.Combine(rootPath, "data", "egress-geo", "app", "geo"),
                Path.Combine(
                    rootPath,
                    "data",
                    "egress-geo",
                    "GeoLite2-City.mmdb"),
                Path.Combine(
                    rootPath,
                    "data",
                    "egress-geo",
                    "provenance.json"),
                Path.Combine(
                    rootPath,
                    "config",
                    "systemd",
                    "user",
                    "egress-geo-update.service"),
                Path.Combine(
                    rootPath,
                    "config",
                    "systemd",
                    "user",
                    "egress-geo-update.timer"),
                Path.Combine(
                    rootPath,
                    "cache",
                    "egress-geo",
                    "snapshot.json"));
        }

        internal DoctorPaths Paths { get; }

        internal void CreateHealthyFiles()
        {
            WriteExecutable(Paths.ApplicationPath);
            WriteFile(Paths.DatabasePath, "database");
            var digest = "sha256:" + Convert.ToHexStringLower(
                SHA256.HashData("database"u8));
            GeoLiteProvenanceFile.Write(
                    Paths.ProvenancePath,
                    new GeoLiteProvenance(
                        "P3TERX/GeoLite.mmdb",
                        "2026.08.15",
                        CurrentTime - TimeSpan.FromDays(2),
                        new Uri(
                            "https://github.com/P3TERX/GeoLite.mmdb/" +
                            "releases/download/2026.08.15/" +
                            "GeoLite2-City.mmdb"),
                        digest,
                        CurrentTime - TimeSpan.FromDays(2),
                        CurrentTime - TimeSpan.FromDays(1)),
                    CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            WriteFile(Paths.UpdateServicePath, "service");
            WriteFile(Paths.UpdateTimerPath, "timer");
            WriteFile(Paths.CachePath, "cache");
        }

        internal InstallationDoctor CreateDoctor(
            DateTimeOffset databaseBuildTime,
            CachedEgressSnapshot? snapshot,
            IPublicIpClient? publicIp = null,
            UserTimerState? timerState = null,
            TimeProvider? timeProvider = null,
            bool databaseAvailable = true,
            IUserTimerStateReader? timerStateReader = null,
            IEgressSnapshotCache? cache = null,
            GeoLiteSourceStatus? sourceStatus = null) =>
            new(
                Paths,
                publicIp ?? new ReachablePublicIpClient(),
                new FakeGeolocationDatabase(
                    databaseBuildTime,
                    databaseAvailable),
                cache ?? new FakeEgressSnapshotCache(snapshot),
                new FakeGeoLiteSourceHealth(
                    sourceStatus ??
                        new GeoLiteSourceStatus.Reachable("2026.08.15")),
                timerStateReader ?? new FakeUserTimerStateReader(
                    timerState ?? new UserTimerState.Available(
                        IsEnabled: true,
                        IsActive: true)),
                timeProvider ?? new FakeTimeProvider(CurrentTime));

        public void Dispose() => Directory.Delete(rootPath, recursive: true);

        private static void WriteExecutable(string path)
        {
            WriteFile(path, "executable");
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }

        private static void WriteFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }

    private sealed class ReachablePublicIpClient : IPublicIpClient
    {
        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken) =>
            Received("203.0.113.7");

        public ValueTask<PublicIpResponse> GetIdentMeIPv4(
            CancellationToken cancellationToken) =>
            Received("198.51.100.5");

        public ValueTask<PublicIpResponse> GetIpifyIPv6(
            CancellationToken cancellationToken) =>
            Received("2001:db8::7");

        public ValueTask<PublicIpResponse> GetIdentMeIPv6(
            CancellationToken cancellationToken) =>
            Received("2001:db8::5");

        private static ValueTask<PublicIpResponse> Received(string value) =>
            ValueTask.FromResult<PublicIpResponse>(
                new PublicIpResponse.Received(value));
    }

    private sealed class IPv4OnlyPublicIpClient : IPublicIpClient
    {
        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken) =>
            Received("203.0.113.7");

        public ValueTask<PublicIpResponse> GetIdentMeIPv4(
            CancellationToken cancellationToken) =>
            Received("198.51.100.5");

        private static ValueTask<PublicIpResponse> Received(string value) =>
            ValueTask.FromResult<PublicIpResponse>(
                new PublicIpResponse.Received(value));
    }

    private sealed class NeverPublicIpClient : IPublicIpClient
    {
        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken) => Never();

        public ValueTask<PublicIpResponse> GetIdentMeIPv4(
            CancellationToken cancellationToken) => Never();

        public ValueTask<PublicIpResponse> GetIpifyIPv6(
            CancellationToken cancellationToken) => Never();

        public ValueTask<PublicIpResponse> GetIdentMeIPv6(
            CancellationToken cancellationToken) => Never();

        private static ValueTask<PublicIpResponse> Never() =>
            new(new TaskCompletionSource<PublicIpResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously).Task);
    }

    private sealed class FakeGeolocationDatabase(
        DateTimeOffset buildTime,
        bool isAvailable) :
        IGeolocationDatabase
    {
        public bool IsAvailable => isAvailable;

        public DateTimeOffset? BuildTime => buildTime;

        public GeolocationLookup Lookup(IPAddress address) =>
            throw new AssertFailedException(
                "Doctor must not perform an address geolocation lookup.");
    }

    private sealed class FakeEgressSnapshotCache(
        CachedEgressSnapshot? snapshot) : IEgressSnapshotCache
    {
        public ValueTask<CachedEgressSnapshot?> Read(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(snapshot);

        public ValueTask Write(
            CachedEgressSnapshot snapshot,
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "Doctor must not write the egress cache.");
    }

    private sealed class FailingEgressSnapshotCache : IEgressSnapshotCache
    {
        public ValueTask<CachedEgressSnapshot?> Read(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unexpected cache failure.");

        public ValueTask Write(
            CachedEgressSnapshot snapshot,
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "Doctor must not write the egress cache.");
    }

    private sealed class FakeUserTimerStateReader(UserTimerState state) :
        IUserTimerStateReader
    {
        public ValueTask<UserTimerState> Read(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(state);
    }

    private sealed class FakeGeoLiteSourceHealth(GeoLiteSourceStatus status) :
        IGeoLiteSourceHealth
    {
        public ValueTask<GeoLiteSourceStatus> Check(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(status);
    }

    private sealed class NeverUserTimerStateReader : IUserTimerStateReader
    {
        public async ValueTask<UserTimerState> Read(
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new AssertFailedException("The infinite delay completed.");
        }
    }
}
