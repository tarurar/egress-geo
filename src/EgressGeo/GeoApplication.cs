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
            GeoCommand.Lookup => RunLookup(cancellationToken),
            GeoCommand.Help => Write(CommandLineOutput.Help()),
            GeoCommand.Version => Write(CommandLineOutput.Version(GetVersion())),
            GeoCommand.Invalid => Write(CommandLineOutput.InvalidArguments()),
            _ => throw new InvalidOperationException("Unknown geo command."),
        };

    private ValueTask<int> RunLookup(CancellationToken cancellationToken)
    {
        if (!dependencies.Geolocation.IsAvailable)
        {
            return Write(HumanLookupOutput.MissingDatabase());
        }

        return RunConfiguredLookup(cancellationToken);
    }

    private async ValueTask<int> RunConfiguredLookup(
        CancellationToken cancellationToken)
    {
        var address = await DiscoverIPv4(cancellationToken);
        if (address is null)
        {
            return await Write(HumanLookupOutput.PublicAddressUnavailable());
        }

        var lookup = dependencies.Geolocation.Lookup(address);
        var outcome = LookupDecision.Decide(address, lookup);

        return await Write(HumanLookupOutput.Render(outcome));
    }

    private async ValueTask<IPAddress?> DiscoverIPv4(
        CancellationToken cancellationToken)
    {
        using var liveDeadline = new CancellationTokenSource(
            LiveDiscoveryBudget,
            dependencies.TimeProvider);
        using var liveDiscovery =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                liveDeadline.Token);
        using var primaryDeadline = new CancellationTokenSource(
            PrimaryProviderBudget,
            dependencies.TimeProvider);
        using var primaryRequest =
            CancellationTokenSource.CreateLinkedTokenSource(
                liveDiscovery.Token,
                primaryDeadline.Token);

        var response = await RequestPublicIp(
            dependencies.PublicIp.GetIpifyIPv4,
            primaryRequest.Token,
            cancellationToken);
        if (ParseIPv4(response) is null &&
            !liveDiscovery.IsCancellationRequested)
        {
            response = await RequestPublicIp(
                dependencies.PublicIp.GetIdentMeIPv4,
                liveDiscovery.Token,
                cancellationToken);
        }

        return ParseIPv4(response);
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

    private static IPAddress? ParseIPv4(PublicIpResponse response)
    {
        if (response is not PublicIpResponse.Received received)
        {
            return null;
        }

        var candidate = received.Content.Trim();
        if (!IPAddress.TryParse(candidate, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            !string.Equals(
                candidate,
                address.ToString(),
                StringComparison.Ordinal))
        {
            return null;
        }

        return address;
    }

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
