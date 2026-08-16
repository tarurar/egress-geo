using System.Net;

namespace EgressGeo.Tests;

[TestClass]
public sealed class GeoApplicationTests
{
    [TestMethod]
    public async Task Lookup_prints_approximate_IPv4_location()
    {
        var address = IPAddress.Parse("203.0.113.7");
        var output = new StringWriter();
        var error = new StringWriter();
        var dependencies = new GeoApplicationDependencies(
            new FakePublicIpClient(address.ToString()),
            new FakeGeolocationDatabase(
                address,
                new GeolocationLookup.Found("Manama", "bh")),
            output,
            error);
        var application = new GeoApplication(dependencies);

        var exitCode = await application.Run([], CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual(
            "Approximate location: 🇧🇭 Manama, BH\n" +
            "Public address (IPv4): 203.0.113.7\n",
            output.ToString());
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task Lookup_uses_country_when_approximate_city_is_missing()
    {
        var address = IPAddress.Parse("203.0.113.7");
        var output = new StringWriter();
        var error = new StringWriter();
        var dependencies = new GeoApplicationDependencies(
            new FakePublicIpClient(address.ToString()),
            new FakeGeolocationDatabase(
                address,
                new GeolocationLookup.Found(null, "bh")),
            output,
            error);
        var application = new GeoApplication(dependencies);

        var exitCode = await application.Run([], CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual(
            "Approximate location: 🇧🇭 BH\n" +
            "Public address (IPv4): 203.0.113.7\n",
            output.ToString());
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task Lookup_reports_location_unavailable_when_country_is_missing()
    {
        var address = IPAddress.Parse("203.0.113.7");
        var output = new StringWriter();
        var error = new StringWriter();
        var dependencies = new GeoApplicationDependencies(
            new FakePublicIpClient(address.ToString()),
            new FakeGeolocationDatabase(
                address,
                new GeolocationLookup.Found("Manama", null)),
            output,
            error);
        var application = new GeoApplication(dependencies);

        var exitCode = await application.Run([], CancellationToken.None);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            "Approximate location unavailable for IPv4 203.0.113.7: " +
            "GeoLite2 City has no country for this address.\n",
            error.ToString());
    }

    [TestMethod]
    public async Task Lookup_points_to_setup_when_database_is_unavailable()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var dependencies = new GeoApplicationDependencies(
            new UnexpectedPublicIpClient(),
            new UnavailableGeolocationDatabase(),
            output,
            error);
        var application = new GeoApplication(dependencies);

        var exitCode = await application.Run([], CancellationToken.None);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            "GeoLite2 City database is missing or unreadable.\n" +
            "Run: geo setup\n",
            error.ToString());
    }

    [TestMethod]
    public async Task Help_describes_lookup_setup_and_GeoLite_attribution()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var dependencies = new GeoApplicationDependencies(
            new UnexpectedPublicIpClient(),
            new UnexpectedGeolocationDatabase(),
            output,
            error);
        var application = new GeoApplication(dependencies);

        var exitCode = await application.Run(["--help"], CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual(
            """
            Usage:
              geo
              geo --help
              geo --version

            Shows the approximate city and country of this machine's public IPv4 egress.

            Setup:
              geo setup

            This product includes GeoLite Data created by MaxMind, available from https://www.maxmind.com.

            """,
            output.ToString());
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task Version_prints_the_command_version()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var dependencies = new GeoApplicationDependencies(
            new UnexpectedPublicIpClient(),
            new UnexpectedGeolocationDatabase(),
            output,
            error);
        var application = new GeoApplication(dependencies);

        var exitCode = await application.Run(["--version"], CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual("geo 0.1.0\n", output.ToString());
        Assert.AreEqual(string.Empty, error.ToString());
    }

    private sealed class FakePublicIpClient(string response) : IPublicIpClient
    {
        public ValueTask<string> GetIpifyIPv4(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(response);
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
        public ValueTask<string> GetIpifyIPv4(
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "This command must not perform an HTTP request.");
    }

    private sealed class UnexpectedGeolocationDatabase : IGeolocationDatabase
    {
        public bool IsAvailable =>
            throw new AssertFailedException("Informational commands must not query GeoLite.");

        public GeolocationLookup Lookup(IPAddress address) =>
            throw new AssertFailedException("Informational commands must not query GeoLite.");
    }

    private sealed class UnavailableGeolocationDatabase : IGeolocationDatabase
    {
        public bool IsAvailable => false;

        public GeolocationLookup Lookup(IPAddress address) =>
            throw new AssertFailedException(
                "An unavailable database must be reported before lookup.");
    }
}
