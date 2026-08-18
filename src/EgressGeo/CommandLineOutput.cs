namespace EgressGeo;

internal static class CommandLineOutput
{
    private const string HelpText =
        "Usage:\n" +
        "  geo\n" +
        "  geo --json\n" +
        "  geo doctor\n" +
        "  geo --help\n" +
        "  geo --version\n" +
        "\n" +
        "Shows the approximate city and country of this machine's public " +
        "IPv4 and IPv6 egress.\n" +
        "\n" +
        "Setup:\n" +
        "  geo setup\n" +
        "\n" +
        "Setup downloads GeoLite2 City from P3TERX, a third-party " +
        "source, and verifies the GitHub Release digest.\n" +
        "\n" +
        "This product includes GeoLite Data created by MaxMind, available " +
        "from https://www.maxmind.com.\n";

    internal static CommandResult Help() =>
        new(0, HelpText, string.Empty);

    internal static CommandResult Version(string version) =>
        new(0, $"geo {version}\n", string.Empty);

    internal static CommandResult InvalidArguments() =>
        new(1, string.Empty, "Unknown arguments. Run: geo --help\n");
}
