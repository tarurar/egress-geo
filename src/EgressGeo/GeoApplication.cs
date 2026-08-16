using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace EgressGeo;

public sealed class GeoApplication(GeoApplicationDependencies dependencies)
{
    private const string HelpText =
        """
        Usage:
          geo
          geo --help
          geo --version

        Shows the approximate city and country of this machine's public IPv4 egress.

        Setup:
          geo setup

        This product includes GeoLite Data created by MaxMind, available from https://www.maxmind.com.
        """;

    public async ValueTask<int> Run(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        if (arguments is ["--help"] or ["-h"])
        {
            await dependencies.Output.WriteLineAsync(HelpText);
            return 0;
        }

        if (arguments is ["--version"])
        {
            var informationalVersion = typeof(GeoApplication).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion;
            var version = informationalVersion.Split('+', 2)[0];
            await dependencies.Output.WriteLineAsync($"geo {version}");
            return 0;
        }

        if (!dependencies.Geolocation.IsAvailable)
        {
            return await Write(HumanLookupOutput.MissingDatabase());
        }

        return await RunLookup(cancellationToken);
    }

    private async ValueTask<int> RunLookup(CancellationToken cancellationToken)
    {
        var response = await dependencies.PublicIp.GetIpifyIPv4(
            cancellationToken);
        var address = IPAddress.Parse(response.Trim());

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return 1;
        }

        var lookup = dependencies.Geolocation.Lookup(address);
        var outcome = LookupDecision.Decide(address, lookup);

        return await Write(HumanLookupOutput.Render(outcome));
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
