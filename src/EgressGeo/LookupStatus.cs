namespace EgressGeo;

internal sealed record LookupStatus
{
    private LookupStatus(string value, int exitCode)
    {
        Value = value;
        ExitCode = exitCode;
    }

    internal static LookupStatus Healthy { get; } = new("healthy", 0);

    internal static LookupStatus CountryMismatch { get; } =
        new("country-mismatch", 2);

    internal static LookupStatus Cached { get; } = new("cached", 3);

    internal static LookupStatus Failed { get; } = new("failed", 1);

    internal string Value { get; }

    internal int ExitCode { get; }

    public override string ToString() => Value;
}
