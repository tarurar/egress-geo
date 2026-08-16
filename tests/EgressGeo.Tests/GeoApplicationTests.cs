using System.Net;

namespace EgressGeo.Tests;

[TestClass]
public sealed class GeoApplicationTests
{
    [TestMethod]
    public async Task Lookup_prints_approximate_IPv4_location()
    {
        var address = IPAddress.Parse("203.0.113.7");
        var publicIp = new FakePublicIpClient(address.ToString());
        var geolocation = new FakeGeolocationDatabase(
            address,
            new GeolocationLookup.Found("Manama", "bh"));

        var result = await RunApplication([], publicIp, geolocation);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "Approximate location: 🇧🇭 Manama, BH\n" +
            "Public address (IPv4): 203.0.113.7\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Lookup_uses_country_when_city_is_missing()
    {
        var address = IPAddress.Parse("203.0.113.7");
        var publicIp = new FakePublicIpClient(address.ToString());
        var geolocation = new FakeGeolocationDatabase(
            address,
            new GeolocationLookup.Found(null, "bh"));

        var result = await RunApplication([], publicIp, geolocation);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "Approximate location: 🇧🇭 BH\n" +
            "Public address (IPv4): 203.0.113.7\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Lookup_reports_missing_country_as_unavailable()
    {
        var address = IPAddress.Parse("203.0.113.7");
        var publicIp = new FakePublicIpClient(address.ToString());
        var geolocation = new FakeGeolocationDatabase(
            address,
            new GeolocationLookup.Found("Manama", null));

        var result = await RunApplication([], publicIp, geolocation);

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Approximate location unavailable for IPv4 203.0.113.7: " +
            "GeoLite2 City has no country for this address.\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_points_to_setup_when_database_is_unavailable()
    {
        var result = await RunApplication(
            [],
            new UnexpectedPublicIpClient(),
            new UnavailableGeolocationDatabase());

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "GeoLite2 City database is missing or unreadable.\n" +
            "Run: geo setup\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_reports_public_IPv4_address_unavailable()
    {
        var result = await RunApplication(
            [],
            new UnavailablePublicIpClient(),
            new AvailableUnexpectedGeolocationDatabase());

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Public IPv4 address is unavailable.\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_reports_public_IPv4_client_exception_unavailable()
    {
        var result = await RunApplication(
            [],
            new FailingPublicIpClient(),
            new AvailableUnexpectedGeolocationDatabase());

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Public IPv4 address is unavailable.\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_reports_malformed_IPv4_response_unavailable()
    {
        var result = await RunApplication(
            [],
            new FakePublicIpClient("not an address"),
            new AvailableUnexpectedGeolocationDatabase());

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Public IPv4 address is unavailable.\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_reports_IPv6_from_IPv4_source_unavailable()
    {
        var result = await RunApplication(
            [],
            new FakePublicIpClient("2001:db8::1"),
            new AvailableUnexpectedGeolocationDatabase());

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Public IPv4 address is unavailable.\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_reports_multiple_IPv4_addresses_unavailable()
    {
        var result = await RunApplication(
            [],
            new FakePublicIpClient("203.0.113.7\n198.51.100.5"),
            new AvailableUnexpectedGeolocationDatabase());

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Public IPv4 address is unavailable.\n",
            result.Error);
    }

    [TestMethod]
    public async Task Help_describes_lookup_setup_and_GeoLite_attribution()
    {
        var result = await RunApplication(
            ["--help"],
            new UnexpectedPublicIpClient(),
            new UnexpectedGeolocationDatabase());

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "Usage:\n" +
            "  geo\n" +
            "  geo --help\n" +
            "  geo --version\n" +
            "\n" +
            "Shows the approximate city and country of this machine's " +
            "public IPv4 egress.\n" +
            "\n" +
            "Setup:\n" +
            "  geo setup\n" +
            "\n" +
            "This product includes GeoLite Data created by MaxMind, " +
            "available from https://www.maxmind.com.\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Version_prints_the_command_version()
    {
        var result = await RunApplication(
            ["--version"],
            new UnexpectedPublicIpClient(),
            new UnexpectedGeolocationDatabase());

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("geo 0.1.0\n", result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Unknown_arguments_are_rejected_without_lookup()
    {
        var result = await RunApplication(
            ["--unknown"],
            new UnexpectedPublicIpClient(),
            new UnexpectedGeolocationDatabase());

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Unknown arguments. Run: geo --help\n",
            result.Error);
    }

    private static async ValueTask<ApplicationResult> RunApplication(
        string[] arguments,
        IPublicIpClient publicIp,
        IGeolocationDatabase geolocation)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var dependencies = new GeoApplicationDependencies(
            publicIp,
            geolocation,
            output,
            error);
        var application = new GeoApplication(dependencies);

        var exitCode = await application.Run(arguments, CancellationToken.None);

        return new ApplicationResult(
            exitCode,
            output.ToString(),
            error.ToString());
    }

    private sealed record ApplicationResult(
        int ExitCode,
        string Output,
        string Error);

    private sealed class FakePublicIpClient(string response) : IPublicIpClient
    {
        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<PublicIpResponse>(
                new PublicIpResponse.Received(response));
    }

    private sealed class FakeGeolocationDatabase(
        IPAddress expectedAddress,
        GeolocationLookup result) : IGeolocationDatabase
    {
        public bool IsAvailable => true;

        public GeolocationLookup Lookup(IPAddress address) =>
            address.Equals(expectedAddress)
                ? result
                : new GeolocationLookup.LocationUnavailable();
    }

    private sealed class UnexpectedPublicIpClient : IPublicIpClient
    {
        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "This command must not perform an HTTP request.");
    }

    private sealed class FailingPublicIpClient : IPublicIpClient
    {
        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Synthetic provider failure.");
    }

    private sealed class UnavailablePublicIpClient : IPublicIpClient
    {
        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<PublicIpResponse>(
                new PublicIpResponse.Unavailable());
    }

    private sealed class UnexpectedGeolocationDatabase : IGeolocationDatabase
    {
        public bool IsAvailable =>
            throw new AssertFailedException(
                "Informational commands must not query GeoLite.");

        public GeolocationLookup Lookup(IPAddress address) =>
            throw new AssertFailedException(
                "Informational commands must not query GeoLite.");
    }

    private sealed class UnavailableGeolocationDatabase : IGeolocationDatabase
    {
        public bool IsAvailable => false;

        public GeolocationLookup Lookup(IPAddress address) =>
            throw new AssertFailedException(
                "An unavailable database must be reported before lookup.");
    }

    private sealed class AvailableUnexpectedGeolocationDatabase :
        IGeolocationDatabase
    {
        public bool IsAvailable => true;

        public GeolocationLookup Lookup(IPAddress address) =>
            throw new AssertFailedException(
                "Address discovery failure must precede GeoLite lookup.");
    }
}
