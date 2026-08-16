using System.Net;
using Microsoft.Extensions.Time.Testing;

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
    public async Task Lookup_accepts_surrounding_whitespace_from_ipify()
    {
        var address = IPAddress.Parse("203.0.113.7");
        var publicIp = new FakePublicIpClient(" \t203.0.113.7\r\n");
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
    public async Task Lookup_uses_ident_me_when_ipify_request_fails()
    {
        var publicIp = new OrderedPublicIpClient(
            _ => UnavailableResponse(),
            _ => ReceivedResponse("203.0.113.7"));

        var result = await RunManamaLookup(publicIp);

        AssertManamaLookup(result);
    }

    [TestMethod]
    public async Task Lookup_uses_ident_me_when_ipify_times_out()
    {
        var timeProvider = new FakeTimeProvider();
        var publicIp = new OrderedPublicIpClient(
            _ => NeverResponse(),
            _ => ReceivedResponse("203.0.113.7"));

        var lookup = RunManamaLookup(publicIp, timeProvider);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await lookup.AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        AssertManamaLookup(result);
    }

    [TestMethod]
    public async Task Lookup_rejects_ambiguous_ipify_response_before_fallback()
    {
        var publicIp = new OrderedPublicIpClient(
            _ => ReceivedResponse("127.1"),
            _ => ReceivedResponse("203.0.113.7"));

        var result = await RunManamaLookup(publicIp);

        AssertManamaLookup(result);
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
    public async Task Lookup_reports_both_providers_unavailable()
    {
        var result = await RunApplication(
            [],
            new OrderedPublicIpClient(
                _ => UnavailableResponse(),
                _ => UnavailableResponse()),
            new AvailableUnexpectedGeolocationDatabase());

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Public IPv4 address is unavailable.\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_shares_live_discovery_budget_between_providers()
    {
        var timeProvider = new FakeTimeProvider();
        var fallbackStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var publicIp = new OrderedPublicIpClient(
            _ => NeverResponse(),
            _ =>
            {
                fallbackStarted.SetResult(true);
                return NeverResponse();
            });

        var lookup = RunApplication(
            [],
            publicIp,
            new AvailableUnexpectedGeolocationDatabase(),
            timeProvider);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await fallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await lookup.AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Public IPv4 address is unavailable.\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_reports_invalid_ident_me_response_unavailable()
    {
        var result = await RunApplication(
            [],
            new OrderedPublicIpClient(
                _ => UnavailableResponse(),
                _ => ReceivedResponse("not an address")),
            new AvailableUnexpectedGeolocationDatabase());

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Public IPv4 address is unavailable.\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_uses_ident_me_after_malformed_ipify_response()
    {
        var publicIp = new OrderedPublicIpClient(
            _ => ReceivedResponse("not an address"),
            _ => ReceivedResponse("203.0.113.7"));

        var result = await RunManamaLookup(publicIp);

        AssertManamaLookup(result);
    }

    [TestMethod]
    public async Task Lookup_uses_ident_me_after_IPv6_from_ipify()
    {
        var publicIp = new OrderedPublicIpClient(
            _ => ReceivedResponse("2001:db8::1"),
            _ => ReceivedResponse("203.0.113.7"));

        var result = await RunManamaLookup(publicIp);

        AssertManamaLookup(result);
    }

    [TestMethod]
    public async Task Lookup_uses_ident_me_after_multiple_ipify_addresses()
    {
        var publicIp = new OrderedPublicIpClient(
            _ => ReceivedResponse("203.0.113.7\n198.51.100.5"),
            _ => ReceivedResponse("203.0.113.7"));

        var result = await RunManamaLookup(publicIp);

        AssertManamaLookup(result);
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
        IGeolocationDatabase geolocation,
        TimeProvider? timeProvider = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var dependencies = new GeoApplicationDependencies(
            publicIp,
            geolocation,
            output,
            error,
            timeProvider ?? TimeProvider.System);
        var application = new GeoApplication(dependencies);

        var exitCode = await application.Run(arguments, CancellationToken.None);

        return new ApplicationResult(
            exitCode,
            output.ToString(),
            error.ToString());
    }

    private static ValueTask<ApplicationResult> RunManamaLookup(
        IPublicIpClient publicIp,
        TimeProvider? timeProvider = null)
    {
        var address = IPAddress.Parse("203.0.113.7");
        var geolocation = new FakeGeolocationDatabase(
            address,
            new GeolocationLookup.Found("Manama", "bh"));
        return RunApplication([], publicIp, geolocation, timeProvider);
    }

    private static void AssertManamaLookup(ApplicationResult result)
    {
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "Approximate location: 🇧🇭 Manama, BH\n" +
            "Public address (IPv4): 203.0.113.7\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    private sealed record ApplicationResult(
        int ExitCode,
        string Output,
        string Error);

    private static ValueTask<PublicIpResponse> ReceivedResponse(
        string content) =>
        ValueTask.FromResult<PublicIpResponse>(
            new PublicIpResponse.Received(content));

    private static ValueTask<PublicIpResponse> UnavailableResponse() =>
        ValueTask.FromResult<PublicIpResponse>(
            new PublicIpResponse.Unavailable());

    private static ValueTask<PublicIpResponse> NeverResponse() =>
        new(new TaskCompletionSource<PublicIpResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously).Task);

    private sealed class FakePublicIpClient(string response) : IPublicIpClient
    {
        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<PublicIpResponse>(
                new PublicIpResponse.Received(response));

        public ValueTask<PublicIpResponse> GetIdentMeIPv4(
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "Primary success must not contact the fallback provider.");
    }

    private sealed class OrderedPublicIpClient(
        Func<CancellationToken, ValueTask<PublicIpResponse>> ipify,
        Func<CancellationToken, ValueTask<PublicIpResponse>> identMe) :
        IPublicIpClient
    {
        private bool ipifyRequested;

        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken)
        {
            ipifyRequested = true;
            return ipify(cancellationToken);
        }

        public ValueTask<PublicIpResponse> GetIdentMeIPv4(
            CancellationToken cancellationToken)
        {
            Assert.IsTrue(
                ipifyRequested,
                "ipify must be requested before ident.me.");
            return identMe(cancellationToken);
        }
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

        public ValueTask<PublicIpResponse> GetIdentMeIPv4(
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "This command must not perform an HTTP request.");
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
