using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace EgressGeo;

public sealed class GeoApplication(GeoApplicationDependencies dependencies)
{
    private static readonly TimeSpan LiveDiscoveryBudget =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PrimaryProviderBudget =
        TimeSpan.FromSeconds(1);

    public ValueTask<int> Run(
        string[] arguments,
        CancellationToken cancellationToken) =>
        GeoCommand.Parse(arguments) switch
        {
            GeoCommand.Lookup lookup => RunLookup(
                lookup.OutputFormat,
                cancellationToken),
            GeoCommand.Help => Write(CommandLineOutput.Help()),
            GeoCommand.Version => Write(CommandLineOutput.Version(GetVersion())),
            GeoCommand.Invalid => Write(CommandLineOutput.InvalidArguments()),
            _ => throw new InvalidOperationException("Unknown geo command."),
        };

    private ValueTask<int> RunLookup(
        LookupOutputFormat outputFormat,
        CancellationToken cancellationToken)
    {
        if (!dependencies.Geolocation.IsAvailable)
        {
            var unavailable = new LookupOutcome.DatabaseUnavailable();
            var outcome = new LiveLookupOutcome(
                dependencies.TimeProvider.GetUtcNow(),
                unavailable,
                unavailable);
            return Write(Render(outcome, outputFormat));
        }

        return RunConfiguredLookup(outputFormat, cancellationToken);
    }

    private async ValueTask<int> RunConfiguredLookup(
        LookupOutputFormat outputFormat,
        CancellationToken cancellationToken)
    {
        using var liveDeadline = new CancellationTokenSource(
            LiveDiscoveryBudget,
            dependencies.TimeProvider);
        using var liveDiscovery =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                liveDeadline.Token);

        var ipv4Task = Discover(
            IpFamily.IPv4,
            AddressFamily.InterNetwork,
            dependencies.PublicIp.GetIpifyIPv4,
            dependencies.PublicIp.GetIdentMeIPv4,
            liveDiscovery.Token,
            cancellationToken).AsTask();
        var ipv6Task = Discover(
            IpFamily.IPv6,
            AddressFamily.InterNetworkV6,
            dependencies.PublicIp.GetIpifyIPv6,
            dependencies.PublicIp.GetIdentMeIPv6,
            liveDiscovery.Token,
            cancellationToken).AsTask();

        await Task.WhenAll(ipv4Task, ipv6Task);
        var outcome = new LiveLookupOutcome(
            dependencies.TimeProvider.GetUtcNow(),
            Locate(await ipv4Task),
            Locate(await ipv6Task));

        return await Write(Render(outcome, outputFormat));
    }

    private async ValueTask<PublicIpDiscovery> Discover(
        IpFamily family,
        AddressFamily addressFamily,
        Func<CancellationToken, ValueTask<PublicIpResponse>> primary,
        Func<CancellationToken, ValueTask<PublicIpResponse>> fallback,
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

        var response = await RequestPublicIp(
            primary,
            primaryRequest.Token,
            callerCancellationToken);
        var address = Parse(response, addressFamily);
        if (address is not null)
        {
            return new PublicIpDiscovery.Found(
                new DiscoveredPublicIp(
                    family,
                    address,
                    PublicIpProvider.Ipify));
        }

        if (!liveDiscoveryToken.IsCancellationRequested)
        {
            response = await RequestPublicIp(
                fallback,
                liveDiscoveryToken,
                callerCancellationToken);
            address = Parse(response, addressFamily);
        }

        return address is null
            ? new PublicIpDiscovery.Unavailable(family)
            : new PublicIpDiscovery.Found(
                new DiscoveredPublicIp(
                    family,
                    address,
                    PublicIpProvider.IdentMe));
    }

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

    private static IPAddress? Parse(
        PublicIpResponse response,
        AddressFamily requiredFamily)
    {
        if (response is not PublicIpResponse.Received received)
        {
            return null;
        }

        var candidate = received.Content.Trim();
        if (!IPAddress.TryParse(candidate, out var address) ||
            address.AddressFamily != requiredFamily ||
            requiredFamily == AddressFamily.InterNetwork &&
            !string.Equals(candidate, address.ToString(),
                StringComparison.Ordinal))
        {
            return null;
        }

        return address;
    }

    private LookupOutcome Locate(PublicIpDiscovery discovery) =>
        discovery switch
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

    private static CommandResult Render(
        LiveLookupOutcome outcome,
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
