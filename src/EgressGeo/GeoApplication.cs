using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace EgressGeo;

public sealed class GeoApplication(GeoApplicationDependencies dependencies)
{
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
        PublicIpResponse response;
        try
        {
            response = await dependencies.PublicIp.GetIpifyIPv4(
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return await Write(HumanLookupOutput.PublicAddressUnavailable());
        }

        if (response is not PublicIpResponse.Received received)
        {
            return await Write(HumanLookupOutput.PublicAddressUnavailable());
        }

        if (!IPAddress.TryParse(received.Content.Trim(), out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
        {
            return await Write(HumanLookupOutput.PublicAddressUnavailable());
        }

        var lookup = dependencies.Geolocation.Lookup(address);
        var outcome = LookupDecision.Decide(address, lookup);

        return await Write(HumanLookupOutput.Render(outcome));
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
