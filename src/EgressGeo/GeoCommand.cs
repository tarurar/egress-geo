namespace EgressGeo;

internal abstract record GeoCommand
{
    private GeoCommand()
    {
    }

    internal static GeoCommand Parse(string[] arguments) =>
        arguments switch
        {
            [] => new Lookup(LookupOutputFormat.Human),
            ["--json"] => new Lookup(LookupOutputFormat.Json),
            ["--help"] or ["-h"] => new Help(),
            ["--version"] => new Version(),
            _ => new Invalid(),
        };

    internal sealed record Lookup(LookupOutputFormat OutputFormat) :
        GeoCommand;

    internal sealed record Help : GeoCommand;

    internal sealed record Version : GeoCommand;

    internal sealed record Invalid : GeoCommand;
}
