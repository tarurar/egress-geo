using System.Reflection;

namespace EgressGeo;

public sealed class GeoApplication
{
    private static readonly TimeSpan LiveDiscoveryBudget =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PrimaryProviderBudget =
        TimeSpan.FromSeconds(1);
    private readonly GeoApplicationDependencies dependencies;
    private readonly ISetupWizard setupWizard;

    public GeoApplication(GeoApplicationDependencies dependencies)
        : this(
            dependencies,
            new BashSetupWizard(dependencies?.Error ??
                throw new ArgumentNullException(nameof(dependencies))))
    {
    }

    public GeoApplication(
        GeoApplicationDependencies dependencies,
        ISetupWizard setupWizard)
    {
        this.dependencies = dependencies ??
            throw new ArgumentNullException(nameof(dependencies));
        this.setupWizard = setupWizard ??
            throw new ArgumentNullException(nameof(setupWizard));
    }

    public ValueTask<int> Run(
        string[] arguments,
        CancellationToken cancellationToken) =>
        GeoCommand.Parse(arguments) switch
        {
            GeoCommand.Lookup lookup => RunConfiguredLookup(
                lookup.OutputFormat,
                cancellationToken),
            GeoCommand.Setup => setupWizard.Run(cancellationToken),
            GeoCommand.VerifyDatabase => Write(
                CommandLineOutput.DatabaseVerification(
                    dependencies.Geolocation.IsAvailable)),
            GeoCommand.Help => Write(CommandLineOutput.Help()),
            GeoCommand.Version => Write(CommandLineOutput.Version(GetVersion())),
            GeoCommand.Invalid => Write(CommandLineOutput.InvalidArguments()),
            _ => throw new InvalidOperationException("Unknown geo command."),
        };

    private async ValueTask<int> RunConfiguredLookup(
        LookupOutputFormat outputFormat,
        CancellationToken cancellationToken)
    {
        var geolocationAvailable = dependencies.Geolocation.IsAvailable;
        using var liveDeadline = new CancellationTokenSource(
            LiveDiscoveryBudget,
            dependencies.TimeProvider);
        using var liveDiscovery =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                liveDeadline.Token);

        var ipv4Task = Discover(
            IpFamily.IPv4,
            liveDiscovery.Token,
            cancellationToken).AsTask();
        var ipv6Task = Discover(
            IpFamily.IPv6,
            liveDiscovery.Token,
            cancellationToken).AsTask();

        await Task.WhenAll(ipv4Task, ipv6Task);
        var liveOutcome = new LookupResult(
            dependencies.TimeProvider.GetUtcNow(),
            Locate(await ipv4Task, geolocationAvailable),
            Locate(await ipv6Task, geolocationAvailable),
            new CacheUsage.None());
        var cachedSnapshot = await dependencies.Cache.Read(cancellationToken);
        var decision = CacheDecision.Decide(liveOutcome, cachedSnapshot);
        if (decision.SnapshotToWrite is { } snapshot)
        {
            await dependencies.Cache.Write(snapshot, cancellationToken);
        }

        return await Write(Render(decision.Outcome, outputFormat));
    }

    private async ValueTask<PublicIpDiscovery> Discover(
        IpFamily family,
        CancellationToken liveDiscoveryToken,
        CancellationToken callerCancellationToken)
    {
        using var primaryDeadline = new CancellationTokenSource(
            PrimaryProviderBudget,
            dependencies.TimeProvider);
        using var primaryRequest =
            CancellationTokenSource.CreateLinkedTokenSource(
                liveDiscoveryToken,
                primaryDeadline.Token);

        var publicIp = await DiscoverFromProvider(
            family,
            PublicIpProvider.Ipify,
            primaryRequest.Token,
            callerCancellationToken);
        if (publicIp is not null)
        {
            return new PublicIpDiscovery.Found(publicIp);
        }

        if (!liveDiscoveryToken.IsCancellationRequested)
        {
            publicIp = await DiscoverFromProvider(
                family,
                PublicIpProvider.IdentMe,
                liveDiscoveryToken,
                callerCancellationToken);
        }

        return publicIp is null
            ? new PublicIpDiscovery.Unavailable(family)
            : new PublicIpDiscovery.Found(publicIp);
    }

    private async ValueTask<DiscoveredPublicIp?> DiscoverFromProvider(
        IpFamily family,
        PublicIpProvider provider,
        CancellationToken requestToken,
        CancellationToken callerCancellationToken)
    {
        var response = await RequestPublicIp(
            GetRequest(family, provider),
            requestToken,
            callerCancellationToken);
        return Parse(response, family, provider);
    }

    private Func<CancellationToken, ValueTask<PublicIpResponse>> GetRequest(
        IpFamily family,
        PublicIpProvider provider) =>
        (family, provider) switch
        {
            (IpFamily.IPv4, PublicIpProvider.Ipify) =>
                dependencies.PublicIp.GetIpifyIPv4,
            (IpFamily.IPv4, PublicIpProvider.IdentMe) =>
                dependencies.PublicIp.GetIdentMeIPv4,
            (IpFamily.IPv6, PublicIpProvider.Ipify) =>
                dependencies.PublicIp.GetIpifyIPv6,
            (IpFamily.IPv6, PublicIpProvider.IdentMe) =>
                dependencies.PublicIp.GetIdentMeIPv6,
            _ => throw new InvalidOperationException(
                $"Unknown public IP request: {family}, {provider}"),
        };

    private static async ValueTask<PublicIpResponse> RequestPublicIp(
        Func<CancellationToken, ValueTask<PublicIpResponse>> request,
        CancellationToken requestToken,
        CancellationToken callerCancellationToken)
    {
        try
        {
            return await request(requestToken)
                .AsTask()
                .WaitAsync(requestToken);
        }
        catch (OperationCanceledException)
            when (!callerCancellationToken.IsCancellationRequested)
        {
            return new PublicIpResponse.Unavailable();
        }
    }

    private static DiscoveredPublicIp? Parse(
        PublicIpResponse response,
        IpFamily family,
        PublicIpProvider provider) =>
        response is PublicIpResponse.Received received
            ? DiscoveredPublicIp.Parse(received.Content, family, provider)
            : null;

    private LookupOutcome Locate(
        PublicIpDiscovery discovery,
        bool geolocationAvailable)
    {
        if (!geolocationAvailable)
        {
            return discovery switch
            {
                PublicIpDiscovery.Found found =>
                    new LookupOutcome.DatabaseUnavailable(found.PublicIp),
                PublicIpDiscovery.Unavailable =>
                    new LookupOutcome.DatabaseUnavailable(null),
                _ => throw new InvalidOperationException(
                    $"Unknown public IP discovery: " +
                    discovery.GetType().Name),
            };
        }

        return discovery switch
        {
            PublicIpDiscovery.Found found => LookupDecision.Decide(
                found.PublicIp,
                dependencies.Geolocation.Lookup(found.PublicIp.Address)),
            PublicIpDiscovery.Unavailable unavailable =>
                new LookupOutcome.PublicAddressUnavailable(
                    unavailable.Family),
            _ => throw new InvalidOperationException(
                $"Unknown public IP discovery: " +
                discovery.GetType().Name),
        };
    }

    private static CommandResult Render(
        LookupResult outcome,
        LookupOutputFormat outputFormat) =>
        outputFormat switch
        {
            LookupOutputFormat.Human => HumanLookupOutput.Render(outcome),
            LookupOutputFormat.Json => JsonLookupOutput.Render(outcome),
            _ => throw new InvalidOperationException(
                $"Unknown lookup output format: {outputFormat}"),
        };

    private static string GetVersion()
    {
        var informationalVersion = typeof(GeoApplication).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        return informationalVersion.Split('+', 2)[0];
    }

    private async ValueTask<int> Write(CommandResult result)
    {
        if (result.Output.Length > 0)
        {
            await dependencies.Output.WriteAsync(result.Output);
        }

        if (result.Error.Length > 0)
        {
            await dependencies.Error.WriteAsync(result.Error);
        }

        return result.ExitCode;
    }
}
