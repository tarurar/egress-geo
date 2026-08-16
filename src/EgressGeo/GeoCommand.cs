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
            ["setup"] => new Setup(),
            ["setup", "--verify-database"] => new VerifyDatabase(),
            ["doctor"] => new Doctor(),
            ["--help"] or ["-h"] => new Help(),
            ["--version"] => new Version(),
            _ => new Invalid(),
        };

    internal sealed record Lookup(LookupOutputFormat OutputFormat) :
        GeoCommand;

    internal sealed record Setup : GeoCommand;

    internal sealed record VerifyDatabase : GeoCommand;

    internal sealed record Doctor : GeoCommand;

    internal sealed record Help : GeoCommand;

    internal sealed record Version : GeoCommand;

    internal sealed record Invalid : GeoCommand;
}
