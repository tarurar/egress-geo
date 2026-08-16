namespace EgressGeo;

internal sealed record LiveLookupStatus
{
    private LiveLookupStatus(string value, int exitCode)
    {
        Value = value;
        ExitCode = exitCode;
    }

    internal static LiveLookupStatus Healthy { get; } = new("healthy", 0);

    internal static LiveLookupStatus CountryMismatch { get; } =
        new("country-mismatch", 2);

    internal static LiveLookupStatus Failed { get; } = new("failed", 1);

    internal string Value { get; }

    internal int ExitCode { get; }

    public override string ToString() => Value;
}
