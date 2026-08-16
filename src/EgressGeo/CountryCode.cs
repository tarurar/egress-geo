namespace EgressGeo;

internal sealed record CountryCode
{
    private CountryCode(string value)
    {
        Value = value;
    }

    internal string Value { get; }

    internal static CountryCode? Parse(string? value)
    {
        var normalized = value?.ToUpperInvariant();
        return normalized is [>= 'A' and <= 'Z', >= 'A' and <= 'Z']
            ? new CountryCode(normalized)
            : null;
    }

    public override string ToString() => Value;
}
