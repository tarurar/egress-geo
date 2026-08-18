using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace EgressGeo;

public sealed class InstallationDoctor : IInstallationDoctor
{
    private static readonly TimeSpan EndpointBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SourceBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TimerBudget = TimeSpan.FromSeconds(2);
    private const UnixFileMode GroupAndOtherPermissions =
        UnixFileMode.GroupRead |
        UnixFileMode.GroupWrite |
        UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead |
        UnixFileMode.OtherWrite |
        UnixFileMode.OtherExecute;
    private readonly DoctorPaths paths;
    private readonly IPublicIpClient publicIp;
    private readonly IGeolocationDatabase geolocation;
    private readonly IEgressSnapshotCache cache;
    private readonly IGeoLiteSourceHealth sourceHealth;
    private readonly IUserTimerStateReader timerState;
    private readonly TimeProvider timeProvider;

    public InstallationDoctor(
        DoctorPaths paths,
        IPublicIpClient publicIp,
        IGeolocationDatabase geolocation,
        IEgressSnapshotCache cache,
        IGeoLiteSourceHealth sourceHealth,
        IUserTimerStateReader timerState,
        TimeProvider timeProvider)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.publicIp = publicIp ??
            throw new ArgumentNullException(nameof(publicIp));
        this.geolocation = geolocation ??
            throw new ArgumentNullException(nameof(geolocation));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        this.sourceHealth = sourceHealth ??
            throw new ArgumentNullException(nameof(sourceHealth));
        this.timerState = timerState ??
            throw new ArgumentNullException(nameof(timerState));
        this.timeProvider = timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<DoctorReport> Examine(
        CancellationToken cancellationToken)
    {
        var endpointChecks = CheckEndpoints(cancellationToken);
        var sourceCheck = CheckSource(cancellationToken);
        var currentTime = timeProvider.GetUtcNow();
        var provenanceCheck = CheckProvenance(
            currentTime,
            cancellationToken);
        var timerCheck = CheckTimer(cancellationToken);
        var cacheCheck = CheckCache(currentTime, cancellationToken);
        var checks = new List<DoctorCheck>
        {
            CheckExecutable(
                "application",
                paths.ApplicationPath,
                "missing; re-run install.sh"),
            CheckDatabase(currentTime),
            await provenanceCheck,
            await sourceCheck,
            await timerCheck,
            await cacheCheck,
        };
        checks.AddRange(await endpointChecks);
        return new DoctorReport(checks);
    }

    private static DoctorCheck CheckExecutable(
        string name,
        string path,
        string missingDetail)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Failed(name, missingDetail);
            }

            return IsUserExecutable(path)
                ? Healthy(name, "installed and executable")
                : Failed(name, "installed but not executable");
        }
        catch (Exception exception) when (IsFileInspectionFailure(exception))
        {
            return Failed(name, "cannot inspect the installed file");
        }
    }

    private DoctorCheck CheckDatabase(DateTimeOffset currentTime)
    {
        if (!File.Exists(paths.DatabasePath))
        {
            return Failed("database", "missing; run: geo setup");
        }

        try
        {
            if (!geolocation.IsAvailable)
            {
                return Failed("database", "present but unreadable");
            }

            return CheckDatabaseAge(currentTime, geolocation.BuildTime);
        }
        catch (Exception exception) when (IsFileInspectionFailure(exception))
        {
            return Failed("database", "present but unreadable");
        }
    }

    private static DoctorCheck CheckDatabaseAge(
        DateTimeOffset currentTime,
        DateTimeOffset? buildTime)
    {
        if (buildTime is null)
        {
            return Failed("database", "readable but build date is unavailable");
        }

        var age = currentTime - buildTime.Value;
        var buildDescription = buildTime.Value.ToString("yyyy-MM-dd 'UTC'");
        if (age < TimeSpan.Zero)
        {
            return Failed(
                "database",
                $"build date is in the future ({buildDescription})");
        }

        var detail = $"{DescribeAge(age)} old (built {buildDescription})";
        return GeoLiteDatabasePolicy.MaximumAge < age
            ? Failed(
                "database",
                $"readable but stale; {detail}; run: geo setup")
            : Healthy("database", $"readable; {detail}");
    }

    private async ValueTask<DoctorCheck> CheckProvenance(
        DateTimeOffset currentTime,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.ProvenancePath))
        {
            return Failed("provenance", "missing; run: geo setup");
        }

        try
        {
            var attributes = File.GetAttributes(paths.ProvenancePath);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return Failed(
                    "provenance",
                    "must be a regular private file, not a symbolic link");
            }

            if (OperatingSystem.IsLinux() &&
                !IsPrivateReadableFile(paths.ProvenancePath))
            {
                return Failed(
                    "provenance",
                    "permissions are not private; run: geo setup");
            }

            var provenance = await GeoLiteProvenanceFile.Read(
                paths.ProvenancePath,
                cancellationToken);
            if (provenance is null)
            {
                return Failed(
                    "provenance",
                    "malformed; run: geo setup");
            }

            if (currentTime < provenance.ActivatedAt)
            {
                return Failed(
                    "provenance",
                    "activation time is in the future; run: geo setup");
            }

            var digest = await GeoLiteDigest.Compute(
                paths.DatabasePath,
                cancellationToken);
            return string.Equals(
                    digest,
                    provenance.Digest,
                    StringComparison.Ordinal)
                ? Healthy(
                    "provenance",
                    $"P3TERX release {provenance.ReleaseTag}; " +
                    "digest verified")
                : Failed(
                    "provenance",
                    "digest does not match the database; run: geo setup");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileInspectionFailure(exception))
        {
            return Failed("provenance", "cannot inspect; run: geo setup");
        }
    }

    private async ValueTask<DoctorCheck> CheckSource(
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(
            SourceBudget,
            timeProvider);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        try
        {
            var status = await sourceHealth
                .Check(request.Token)
                .AsTask()
                .WaitAsync(request.Token);
            return status switch
            {
                GeoLiteSourceStatus.Reachable reachable => Healthy(
                    "GeoLite source",
                    $"P3TERX release {reachable.ReleaseTag} is reachable"),
                GeoLiteSourceStatus.Invalid => Failed(
                    "GeoLite source",
                    "P3TERX release metadata is invalid"),
                GeoLiteSourceStatus.Unavailable => Failed(
                    "GeoLite source",
                    "P3TERX release source is unreachable"),
                _ => throw new UnreachableException(
                    "Unknown GeoLite source status."),
            };
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
        {
            return Failed("GeoLite source", "P3TERX source check timed out");
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException)
        {
            return Failed(
                "GeoLite source",
                "P3TERX release source is unreachable");
        }
    }

    private async ValueTask<DoctorCheck> CheckTimer(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.UpdateServicePath) ||
            !File.Exists(paths.UpdateTimerPath))
        {
            return Failed(
                "update timer",
                "service or timer unit is missing; re-run install.sh");
        }

        using var timeout = new CancellationTokenSource(
            TimerBudget,
            timeProvider);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        try
        {
            var state = await timerState
                .Read(request.Token)
                .AsTask()
                .WaitAsync(request.Token);
            return DescribeTimerState(state);
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
        {
            return Failed(
                "update timer",
                "state check timed out; inspect user systemd");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
            when (IsTimerInspectionFailure(exception))
        {
            return Failed("update timer", "user systemd state is unavailable");
        }
    }

    private static DoctorCheck DescribeTimerState(UserTimerState state) =>
        state switch
        {
            UserTimerState.Unavailable => Failed(
                "update timer",
                "user systemd state is unavailable"),
            UserTimerState.Available(false, false) => Failed(
                "update timer",
                "disabled and inactive; re-run install.sh"),
            UserTimerState.Available(false, true) => Failed(
                "update timer",
                "active but disabled; re-run install.sh"),
            UserTimerState.Available(true, false) => Failed(
                "update timer",
                "enabled but inactive; re-run install.sh"),
            UserTimerState.Available(true, true) => Healthy(
                "update timer",
                "installed, enabled, and active"),
            _ => throw new UnreachableException(
                "Unknown user timer state."),
        };

    private async ValueTask<DoctorCheck> CheckCache(
        DateTimeOffset currentTime,
        CancellationToken cancellationToken)
    {
        if (!Path.Exists(paths.CachePath))
        {
            return Information("cache", "not created yet");
        }

        try
        {
            var snapshot = await cache.Read(cancellationToken);
            return snapshot is null
                ? Failed("cache", "corrupt or invalid; remove the cache file")
                : CheckCacheAge(currentTime, snapshot.ObservedAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
            when (IsFileInspectionFailure(exception))
        {
            return Failed("cache", "cannot read the cache file");
        }
    }

    private static DoctorCheck CheckCacheAge(
        DateTimeOffset currentTime,
        DateTimeOffset observedAt)
    {
        var age = currentTime - observedAt;
        if (age < TimeSpan.Zero)
        {
            return Failed("cache", "snapshot time is in the future");
        }

        var detail = $"{DescribeAge(age)} old";
        return CacheDecision.MaximumCacheAge < age
            ? Information("cache", $"valid but expired; {detail}")
            : Healthy("cache", $"valid; {detail}");
    }

    private async ValueTask<IReadOnlyList<DoctorCheck>> CheckEndpoints(
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(
            EndpointBudget,
            timeProvider);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        var ipv4Ipify = Probe(
            publicIp.GetIpifyIPv4,
            IpFamily.IPv4,
            PublicIpProvider.Ipify,
            request.Token,
            cancellationToken);
        var ipv4IdentMe = Probe(
            publicIp.GetIdentMeIPv4,
            IpFamily.IPv4,
            PublicIpProvider.IdentMe,
            request.Token,
            cancellationToken);
        var ipv6Ipify = Probe(
            publicIp.GetIpifyIPv6,
            IpFamily.IPv6,
            PublicIpProvider.Ipify,
            request.Token,
            cancellationToken);
        var ipv6IdentMe = Probe(
            publicIp.GetIdentMeIPv6,
            IpFamily.IPv6,
            PublicIpProvider.IdentMe,
            request.Token,
            cancellationToken);
        await Task.WhenAll(
            ipv4Ipify,
            ipv4IdentMe,
            ipv6Ipify,
            ipv6IdentMe);
        return
        [
            DescribeEndpoints(
                IpFamily.IPv4,
                await ipv4Ipify,
                await ipv4IdentMe),
            DescribeEndpoints(
                IpFamily.IPv6,
                await ipv6Ipify,
                await ipv6IdentMe),
        ];
    }

    private static async Task<bool> Probe(
        Func<CancellationToken, ValueTask<PublicIpResponse>> get,
        IpFamily family,
        PublicIpProvider provider,
        CancellationToken requestToken,
        CancellationToken callerCancellationToken)
    {
        try
        {
            var response = await get(requestToken)
                .AsTask()
                .WaitAsync(requestToken);
            return response is PublicIpResponse.Received received &&
                DiscoveredPublicIp.Parse(received.Content, family, provider)
                    is not null;
        }
        catch (OperationCanceledException)
            when (!callerCancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
            when (!callerCancellationToken.IsCancellationRequested &&
                IsEndpointFailure(exception))
        {
            return false;
        }
    }

    private static DoctorCheck DescribeEndpoints(
        IpFamily family,
        bool ipifyReachable,
        bool identMeReachable)
    {
        var name = $"{IpFamilyContract.Format(family)} endpoints";
        if (ipifyReachable && identMeReachable)
        {
            return Healthy(name, "ipify and ident.me reachable");
        }

        if (ipifyReachable || identMeReachable)
        {
            var reachable = ipifyReachable ? "ipify" : "ident.me";
            var unavailable = ipifyReachable ? "ident.me" : "ipify";
            return Information(
                name,
                $"{reachable} reachable; {unavailable} unavailable");
        }

        return family == IpFamily.IPv6
            ? Information(
                name,
                "unavailable; IPv6 may not be configured")
            : Failed(name, "unreachable through ipify and ident.me");
    }

    private static bool IsUserExecutable(string path) =>
        !OperatingSystem.IsLinux() ||
        File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute);

    [SupportedOSPlatform("linux")]
    private static bool IsPrivateReadableFile(string path)
    {
        var mode = File.GetUnixFileMode(path);
        return mode.HasFlag(UnixFileMode.UserRead) &&
            (mode & GroupAndOtherPermissions) == 0;
    }

    private static bool IsFileInspectionFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            NotSupportedException;

    private static bool IsTimerInspectionFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            Win32Exception;

    private static bool IsEndpointFailure(Exception exception) =>
        exception is HttpRequestException or IOException or TimeoutException;

    private static string DescribeAge(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1))
        {
            return "less than 1 minute";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return Plural((int)age.TotalMinutes, "minute");
        }

        return age < TimeSpan.FromDays(1)
            ? Plural((int)age.TotalHours, "hour")
            : Plural((int)age.TotalDays, "day");
    }

    private static string Plural(int value, string unit) =>
        $"{value} {unit}{(value == 1 ? string.Empty : "s")}";

    private static DoctorCheck Healthy(string name, string detail) =>
        new(DoctorCheckStatus.Healthy, name, detail);

    private static DoctorCheck Information(string name, string detail) =>
        new(DoctorCheckStatus.Information, name, detail);

    private static DoctorCheck Failed(string name, string detail) =>
        new(DoctorCheckStatus.Failed, name, detail);
}
