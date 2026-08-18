using System.Net;
using System.Text.Json;
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
    public async Task Lookup_prints_compact_dual_stack_location()
    {
        var ipv4Address = IPAddress.Parse("203.0.113.7");
        var ipv6Address = IPAddress.Parse("2001:db8::7");
        var publicIp = new DualStackPublicIpClient(
            ipv4Address.ToString(),
            ipv6Address.ToString());
        var geolocation = new DualStackGeolocationDatabase(
            ipv4Address,
            new GeolocationLookup.Found("Manama", "bh"),
            ipv6Address,
            new GeolocationLookup.Found("Manama", "bh"));

        var result = await RunApplication([], publicIp, geolocation);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "Approximate location: 🇧🇭 Manama, BH\n" +
            "Public address (IPv4): 203.0.113.7\n" +
            "Public address (IPv6): 2001:db8::7\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Lookup_stores_successful_dual_stack_snapshot()
    {
        var observedAt = new DateTimeOffset(
            2026,
            8,
            16,
            12,
            34,
            56,
            TimeSpan.Zero);
        var ipv4Address = IPAddress.Parse("203.0.113.7");
        var ipv6Address = IPAddress.Parse("2001:db8::7");
        var publicIp = new DualStackPublicIpClient(
            ipv4Address.ToString(),
            ipv6Address.ToString());
        var geolocation = new DualStackGeolocationDatabase(
            ipv4Address,
            new GeolocationLookup.Found("Manama", "BH"),
            ipv6Address,
            new GeolocationLookup.Found("London", "GB"));
        var cache = new FakeEgressSnapshotCache();

        var result = await RunApplication(
            [],
            publicIp,
            geolocation,
            new FakeTimeProvider(observedAt),
            cache);

        Assert.AreEqual(2, result.ExitCode);
        Assert.IsNotNull(cache.WrittenSnapshot);
        Assert.AreEqual(observedAt, cache.WrittenSnapshot.ObservedAt);
        Assert.HasCount(2, cache.WrittenSnapshot.Families);
        Assert.AreEqual(
            CachedFamily(
                "IPv4",
                "203.0.113.7",
                "Manama",
                "BH",
                "ipify"),
            cache.WrittenSnapshot.Families[0]);
        Assert.AreEqual(
            CachedFamily(
                "IPv6",
                "2001:db8::7",
                "London",
                "GB",
                "ipify"),
            cache.WrittenSnapshot.Families[1]);
    }

    [TestMethod]
    public async Task Lookup_preserves_complete_snapshot_after_IPv4_only_success()
    {
        var observedAt = new DateTimeOffset(
            2026,
            8,
            16,
            12,
            34,
            56,
            TimeSpan.Zero);
        var address = IPAddress.Parse("203.0.113.7");
        var cache = new FakeEgressSnapshotCache(
            CachedSnapshot(
                observedAt - TimeSpan.FromHours(1),
                CachedFamily(
                    "IPv4",
                    "198.51.100.5",
                    "London",
                    "GB",
                    "ident.me"),
                CachedFamily(
                    "IPv6",
                    "2001:db8::5",
                    "London",
                    "GB",
                    "ident.me")));

        var result = await RunApplication(
            [],
            new FakePublicIpClient(address.ToString()),
            new FakeGeolocationDatabase(
                address,
                new GeolocationLookup.Found("Manama", "BH")),
            new FakeTimeProvider(observedAt),
            cache);

        Assert.AreEqual(0, result.ExitCode);
        Assert.IsNull(cache.WrittenSnapshot);
    }

    [TestMethod]
    public async Task Lookup_prints_separate_cities_without_leak_warning()
    {
        var ipv4Address = IPAddress.Parse("203.0.113.7");
        var ipv6Address = IPAddress.Parse("2001:db8::7");
        var publicIp = new DualStackPublicIpClient(
            ipv4Address.ToString(),
            ipv6Address.ToString());
        var geolocation = new DualStackGeolocationDatabase(
            ipv4Address,
            new GeolocationLookup.Found("Manama", "BH"),
            ipv6Address,
            new GeolocationLookup.Found("Riffa", "BH"));

        var result = await RunApplication([], publicIp, geolocation);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "Approximate location (IPv4): 🇧🇭 Manama, BH\n" +
            "Public address (IPv4): 203.0.113.7\n" +
            "Approximate location (IPv6): 🇧🇭 Riffa, BH\n" +
            "Public address (IPv6): 2001:db8::7\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Lookup_shows_discovered_family_without_location()
    {
        var ipv4Address = IPAddress.Parse("203.0.113.7");
        var ipv6Address = IPAddress.Parse("2001:db8::7");
        var publicIp = new DualStackPublicIpClient(
            ipv4Address.ToString(),
            ipv6Address.ToString());
        var geolocation = new DualStackGeolocationDatabase(
            ipv4Address,
            new GeolocationLookup.Found("Manama", "BH"),
            ipv6Address,
            new GeolocationLookup.Found("Riffa", null));

        var result = await RunApplication([], publicIp, geolocation);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "Approximate location (IPv4): 🇧🇭 Manama, BH\n" +
            "Public address (IPv4): 203.0.113.7\n" +
            "Approximate location (IPv6): unavailable\n" +
            "Public address (IPv6): 2001:db8::7\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Lookup_warns_when_family_countries_differ()
    {
        var ipv4Address = IPAddress.Parse("203.0.113.7");
        var ipv6Address = IPAddress.Parse("2001:db8::7");
        var publicIp = new DualStackPublicIpClient(
            ipv4Address.ToString(),
            ipv6Address.ToString());
        var geolocation = new DualStackGeolocationDatabase(
            ipv4Address,
            new GeolocationLookup.Found("Manama", "BH"),
            ipv6Address,
            new GeolocationLookup.Found("London", "GB"));

        var result = await RunApplication([], publicIp, geolocation);

        Assert.AreEqual(2, result.ExitCode);
        Assert.AreEqual(
            "WARNING: Possible VPN leak: IPv4 and IPv6 egress resolve " +
            "to different countries.\n" +
            "Approximate location (IPv4): 🇧🇭 Manama, BH\n" +
            "Public address (IPv4): 203.0.113.7\n" +
            "Approximate location (IPv6): 🇬🇧 London, GB\n" +
            "Public address (IPv6): 2001:db8::7\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Lookup_prints_approximate_IPv6_location()
    {
        var address = IPAddress.Parse("2001:db8::7");
        var publicIp = OrderedPublicIpClient.ForIPv6(
            _ => ReceivedResponse(address.ToString()),
            _ => throw new AssertFailedException(
                "Primary success must not contact the fallback provider."));
        var geolocation = new FakeGeolocationDatabase(
            address,
            new GeolocationLookup.Found("Manama", "bh"));

        var result = await RunApplication([], publicIp, geolocation);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "Approximate location: 🇧🇭 Manama, BH\n" +
            "Public address (IPv6): 2001:db8::7\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Lookup_reports_missing_IPv6_country_as_unavailable()
    {
        var address = IPAddress.Parse("2001:db8::7");
        var publicIp = OrderedPublicIpClient.ForIPv6(
            _ => ReceivedResponse(address.ToString()),
            _ => throw new AssertFailedException(
                "Primary success must not contact the fallback provider."));
        var geolocation = new FakeGeolocationDatabase(
            address,
            new GeolocationLookup.Found("Manama", null));

        var result = await RunApplication([], publicIp, geolocation);

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Approximate location unavailable for IPv6 2001:db8::7: " +
            "GeoLite2 City has no country for this address.\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_uses_ident_me_when_IPv6_ipify_fails()
    {
        var address = IPAddress.Parse("2001:db8::7");
        var publicIp = OrderedPublicIpClient.ForIPv6(
            _ => UnavailableResponse(),
            _ => ReceivedResponse(address.ToString()));
        var geolocation = new FakeGeolocationDatabase(
            address,
            new GeolocationLookup.Found("Manama", "bh"));

        var result = await RunApplication([], publicIp, geolocation);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "Approximate location: 🇧🇭 Manama, BH\n" +
            "Public address (IPv6): 2001:db8::7\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Lookup_discovers_both_families_concurrently()
    {
        var timeProvider = new FakeTimeProvider();
        var ipv4Address = IPAddress.Parse("203.0.113.7");
        var ipv6Address = IPAddress.Parse("2001:db8::7");
        var publicIp = new DelayedFallbackPublicIpClient(timeProvider);
        var geolocation = new DualStackGeolocationDatabase(
            ipv4Address,
            new GeolocationLookup.Found("Manama", "BH"),
            ipv6Address,
            new GeolocationLookup.Found("Manama", "BH"));

        var lookup = RunApplication(
            [],
            publicIp,
            geolocation,
            timeProvider);
        timeProvider.Advance(TimeSpan.FromSeconds(1.5));
        var result = await lookup.AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "Approximate location: 🇧🇭 Manama, BH\n" +
            "Public address (IPv4): 203.0.113.7\n" +
            "Public address (IPv6): 2001:db8::7\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Json_lookup_reports_stable_dual_stack_contract()
    {
        var observedAt = new DateTimeOffset(
            2026,
            8,
            16,
            12,
            34,
            56,
            TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(observedAt);
        var ipv4Address = IPAddress.Parse("203.0.113.7");
        var ipv6Address = IPAddress.Parse("2001:db8::7");
        var publicIp = new MixedProviderPublicIpClient();
        var geolocation = new DualStackGeolocationDatabase(
            ipv4Address,
            new GeolocationLookup.Found("Manama", "BH"),
            ipv6Address,
            new GeolocationLookup.Found(null, "BH"));

        var result = await RunApplication(
            ["--json"],
            publicIp,
            geolocation,
            timeProvider);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        Assert.AreEqual("healthy", root.GetProperty("status").GetString());
        Assert.AreEqual(
            observedAt,
            root.GetProperty("observedAt").GetDateTimeOffset());
        Assert.IsFalse(root.GetProperty("cached").GetBoolean());
        Assert.AreEqual(
            JsonValueKind.Null,
            root.GetProperty("cacheAgeSeconds").ValueKind);
        Assert.AreEqual(0, root.GetProperty("warnings").GetArrayLength());

        var families = root.GetProperty("families").EnumerateArray().ToArray();
        Assert.AreEqual(2, families.Length);
        AssertFamily(
            families[0],
            "IPv4",
            "203.0.113.7",
            "Manama",
            "BH",
            "ipify");
        AssertFamily(
            families[1],
            "IPv6",
            "2001:db8::7",
            null,
            "BH",
            "ident.me");
    }

    [TestMethod]
    public async Task Json_lookup_reports_country_mismatch()
    {
        var ipv4Address = IPAddress.Parse("203.0.113.7");
        var ipv6Address = IPAddress.Parse("2001:db8::7");
        var publicIp = new DualStackPublicIpClient(
            ipv4Address.ToString(),
            ipv6Address.ToString());
        var geolocation = new DualStackGeolocationDatabase(
            ipv4Address,
            new GeolocationLookup.Found("Manama", "BH"),
            ipv6Address,
            new GeolocationLookup.Found("London", "GB"));

        var result = await RunApplication(["--json"], publicIp, geolocation);

        Assert.AreEqual(2, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        Assert.AreEqual(
            "country-mismatch",
            root.GetProperty("status").GetString());
        var warnings = root.GetProperty("warnings");
        Assert.AreEqual(1, warnings.GetArrayLength());
        Assert.AreEqual(
            "possible-vpn-leak",
            warnings[0].GetString());
        Assert.AreEqual(2, root.GetProperty("families").GetArrayLength());
    }

    [TestMethod]
    public async Task Json_lookup_reports_unavailable_location()
    {
        var address = IPAddress.Parse("203.0.113.7");
        var publicIp = new FakePublicIpClient(address.ToString());
        var geolocation = new FakeGeolocationDatabase(
            address,
            new GeolocationLookup.Found("Manama", null));

        var result = await RunApplication(["--json"], publicIp, geolocation);

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        Assert.AreEqual("failed", root.GetProperty("status").GetString());
        Assert.AreEqual(0, root.GetProperty("warnings").GetArrayLength());
        var families = root.GetProperty("families");
        Assert.AreEqual(1, families.GetArrayLength());
        AssertFamily(
            families[0],
            "IPv4",
            "203.0.113.7",
            null,
            null,
            "ipify");
    }

    [TestMethod]
    public async Task Json_lookup_uses_recent_snapshot_when_live_discovery_fails()
    {
        var currentTime = new DateTimeOffset(
            2026,
            8,
            16,
            12,
            34,
            56,
            TimeSpan.Zero);
        var cachedAt = currentTime - TimeSpan.FromHours(1);
        var cache = new FakeEgressSnapshotCache(
            CachedSnapshot(
                cachedAt,
                CachedFamily(
                    "IPv4",
                    "203.0.113.7",
                    "Manama",
                    "BH",
                    "ipify"),
                CachedFamily(
                    "IPv6",
                    "2001:db8::7",
                    "Manama",
                    "BH",
                    "ident.me")));

        var result = await RunApplication(
            ["--json"],
            OrderedPublicIpClient.ForIPv4(
                _ => UnavailableResponse(),
                _ => UnavailableResponse()),
            new AvailableUnexpectedGeolocationDatabase(),
            new FakeTimeProvider(currentTime),
            cache);

        Assert.AreEqual(3, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        Assert.AreEqual("cached", root.GetProperty("status").GetString());
        Assert.AreEqual(
            cachedAt,
            root.GetProperty("observedAt").GetDateTimeOffset());
        Assert.IsTrue(root.GetProperty("cached").GetBoolean());
        Assert.AreEqual(
            3600,
            root.GetProperty("cacheAgeSeconds").GetInt64());
        Assert.AreEqual(0, root.GetProperty("warnings").GetArrayLength());
        var families = root.GetProperty("families");
        Assert.AreEqual(2, families.GetArrayLength());
        AssertFamily(
            families[0],
            "IPv4",
            "203.0.113.7",
            "Manama",
            "BH",
            "ipify");
        AssertFamily(
            families[1],
            "IPv6",
            "2001:db8::7",
            "Manama",
            "BH",
            "ident.me");
    }

    [TestMethod]
    public async Task Lookup_marks_cached_country_mismatch_and_exits_three()
    {
        var currentTime = new DateTimeOffset(
            2026,
            8,
            16,
            12,
            34,
            56,
            TimeSpan.Zero);
        var cache = new FakeEgressSnapshotCache(
            CachedSnapshot(
                currentTime - TimeSpan.FromHours(1),
                CachedFamily(
                    "IPv4",
                    "203.0.113.7",
                    "Manama",
                    "BH",
                    "ipify"),
                CachedFamily(
                    "IPv6",
                    "2001:db8::7",
                    "London",
                    "GB",
                    "ident.me")));

        var result = await RunApplication(
            [],
            OrderedPublicIpClient.ForIPv4(
                _ => UnavailableResponse(),
                _ => UnavailableResponse()),
            new AvailableUnexpectedGeolocationDatabase(),
            new FakeTimeProvider(currentTime),
            cache);

        Assert.AreEqual(3, result.ExitCode);
        Assert.AreEqual(
            "CACHED EGRESS SNAPSHOT (age 1 hour): live public-address " +
            "discovery is unavailable.\n" +
            "WARNING: Possible VPN leak: IPv4 and IPv6 egress resolve " +
            "to different countries.\n" +
            "Approximate location (IPv4): 🇧🇭 Manama, BH\n" +
            "Public address (IPv4): 203.0.113.7\n" +
            "Approximate location (IPv6): 🇬🇧 London, GB\n" +
            "Public address (IPv6): 2001:db8::7\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Lookup_reuses_cached_location_for_exact_live_address()
    {
        var currentTime = new DateTimeOffset(
            2026,
            8,
            16,
            12,
            34,
            56,
            TimeSpan.Zero);
        var address = IPAddress.Parse("203.0.113.7");
        var cache = new FakeEgressSnapshotCache(
            CachedSnapshot(
                currentTime - TimeSpan.FromHours(2),
                CachedFamily(
                    "IPv4",
                    address.ToString(),
                    "Manama",
                    "BH",
                    "ident.me")));
        var publicIp = new FakePublicIpClient(address.ToString());
        var geolocation = new FakeGeolocationDatabase(
            address,
            new GeolocationLookup.LocationUnavailable());

        var result = await RunApplication(
            [],
            publicIp,
            geolocation,
            new FakeTimeProvider(currentTime),
            cache);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "CACHED LOCATION (age 2 hours): reused for an exact live " +
            "public-address match.\n" +
            "Approximate location: 🇧🇭 Manama, BH\n" +
            "Public address (IPv4): 203.0.113.7\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Lookup_does_not_reuse_location_for_different_live_address()
    {
        var currentTime = new DateTimeOffset(
            2026,
            8,
            16,
            12,
            34,
            56,
            TimeSpan.Zero);
        var liveAddress = IPAddress.Parse("203.0.113.7");
        var cache = new FakeEgressSnapshotCache(
            CachedSnapshot(
                currentTime - TimeSpan.FromHours(2),
                CachedFamily(
                    "IPv4",
                    "198.51.100.5",
                    "London",
                    "GB",
                    "ident.me")));
        var publicIp = new FakePublicIpClient(liveAddress.ToString());
        var geolocation = new FakeGeolocationDatabase(
            liveAddress,
            new GeolocationLookup.LocationUnavailable());

        var result = await RunApplication(
            [],
            publicIp,
            geolocation,
            new FakeTimeProvider(currentTime),
            cache);

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Approximate location unavailable for IPv4 203.0.113.7: " +
            "GeoLite2 City has no country for this address.\n",
            result.Error);
        Assert.IsNull(cache.WrittenSnapshot);
    }

    [TestMethod]
    public async Task Lookup_does_not_replace_snapshot_with_reused_location()
    {
        var currentTime = new DateTimeOffset(
            2026,
            8,
            16,
            12,
            34,
            56,
            TimeSpan.Zero);
        var ipv4Address = IPAddress.Parse("203.0.113.7");
        var ipv6Address = IPAddress.Parse("2001:db8::7");
        var publicIp = new DualStackPublicIpClient(
            ipv4Address.ToString(),
            ipv6Address.ToString());
        var geolocation = new DualStackGeolocationDatabase(
            ipv4Address,
            new GeolocationLookup.Found("Manama", "BH"),
            ipv6Address,
            new GeolocationLookup.LocationUnavailable());
        var cache = new FakeEgressSnapshotCache(
            CachedSnapshot(
                currentTime - TimeSpan.FromHours(2),
                CachedFamily(
                    "IPv6",
                    ipv6Address.ToString(),
                    "London",
                    "GB",
                    "ident.me")));

        var result = await RunApplication(
            [],
            publicIp,
            geolocation,
            new FakeTimeProvider(currentTime),
            cache);

        Assert.AreEqual(2, result.ExitCode);
        Assert.IsNull(cache.WrittenSnapshot);
    }

    [TestMethod]
    public async Task Lookup_uses_snapshot_at_exact_24_hour_boundary()
    {
        var currentTime = new DateTimeOffset(
            2026,
            8,
            16,
            12,
            34,
            56,
            TimeSpan.Zero);
        var cache = new FakeEgressSnapshotCache(
            CachedSnapshot(
                currentTime - TimeSpan.FromHours(24),
                CachedFamily(
                    "IPv4",
                    "203.0.113.7",
                    "Manama",
                    "BH",
                    "ipify")));

        var result = await RunApplication(
            ["--json"],
            new RecordingUnavailablePublicIpClient(),
            new AvailableUnexpectedGeolocationDatabase(),
            new FakeTimeProvider(currentTime),
            cache);

        Assert.AreEqual(3, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.AreEqual(
            24 * 60 * 60,
            document.RootElement
                .GetProperty("cacheAgeSeconds")
                .GetInt64());
    }

    [TestMethod]
    public async Task Lookup_rejects_snapshot_older_than_24_hours()
    {
        var currentTime = new DateTimeOffset(
            2026,
            8,
            16,
            12,
            34,
            56,
            TimeSpan.Zero);
        var cache = new FakeEgressSnapshotCache(
            CachedSnapshot(
                currentTime - TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1),
                CachedFamily(
                    "IPv4",
                    "203.0.113.7",
                    "Manama",
                    "BH",
                    "ipify")));

        var result = await RunApplication(
            [],
            new RecordingUnavailablePublicIpClient(),
            new AvailableUnexpectedGeolocationDatabase(),
            new FakeTimeProvider(currentTime),
            cache);

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Public IPv4 and IPv6 addresses are unavailable.\n",
            result.Error);
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
        var publicIp = OrderedPublicIpClient.ForIPv4(
            _ => UnavailableResponse(),
            _ => ReceivedResponse("203.0.113.7"));

        var result = await RunManamaLookup(publicIp);

        AssertManamaLookup(result);
    }

    [TestMethod]
    public async Task Lookup_uses_ident_me_when_ipify_times_out()
    {
        var timeProvider = new FakeTimeProvider();
        var publicIp = OrderedPublicIpClient.ForIPv4(
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
        var publicIp = OrderedPublicIpClient.ForIPv4(
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
        var publicIp = new RecordingUnavailablePublicIpClient();

        var result = await RunApplication(
            [],
            publicIp,
            new UnavailableGeolocationDatabase());

        Assert.IsFalse(publicIp.WasRequested);
        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "GeoLite2 City database is missing, unreadable, or stale.\n" +
            "Run: geo setup\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_rejects_a_stale_database_before_discovery()
    {
        var currentTime = new DateTimeOffset(
            2026,
            8,
            18,
            0,
            0,
            0,
            TimeSpan.Zero);
        var publicIp = new RecordingUnavailablePublicIpClient();

        var result = await RunApplication(
            [],
            publicIp,
            new StaleGeolocationDatabase(
                currentTime - TimeSpan.FromDays(31)),
            new FakeTimeProvider(currentTime));

        Assert.IsFalse(publicIp.WasRequested);
        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "GeoLite2 City database is missing, unreadable, or stale.\n" +
            "Run: geo setup\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_reports_both_families_unavailable()
    {
        var result = await RunApplication(
            [],
            OrderedPublicIpClient.ForIPv4(
                _ => UnavailableResponse(),
                _ => UnavailableResponse()),
            new AvailableUnexpectedGeolocationDatabase());

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Public IPv4 and IPv6 addresses are unavailable.\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_shares_live_discovery_budget_between_providers()
    {
        var timeProvider = new FakeTimeProvider();
        var fallbackStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var publicIp = OrderedPublicIpClient.ForIPv4(
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
            "Public IPv4 and IPv6 addresses are unavailable.\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_reports_invalid_ident_me_response_unavailable()
    {
        var result = await RunApplication(
            [],
            OrderedPublicIpClient.ForIPv4(
                _ => UnavailableResponse(),
                _ => ReceivedResponse("not an address")),
            new AvailableUnexpectedGeolocationDatabase());

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "Public IPv4 and IPv6 addresses are unavailable.\n",
            result.Error);
    }

    [TestMethod]
    public async Task Lookup_uses_ident_me_after_malformed_ipify_response()
    {
        var publicIp = OrderedPublicIpClient.ForIPv4(
            _ => ReceivedResponse("not an address"),
            _ => ReceivedResponse("203.0.113.7"));

        var result = await RunManamaLookup(publicIp);

        AssertManamaLookup(result);
    }

    [TestMethod]
    public async Task Lookup_uses_ident_me_after_IPv6_from_ipify()
    {
        var publicIp = OrderedPublicIpClient.ForIPv4(
            _ => ReceivedResponse("2001:db8::1"),
            _ => ReceivedResponse("203.0.113.7"));

        var result = await RunManamaLookup(publicIp);

        AssertManamaLookup(result);
    }

    [TestMethod]
    public async Task Lookup_uses_ident_me_after_multiple_ipify_addresses()
    {
        var publicIp = OrderedPublicIpClient.ForIPv4(
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
            "  geo --json\n" +
            "  geo doctor\n" +
            "  geo --help\n" +
            "  geo --version\n" +
            "\n" +
            "Shows the approximate city and country of this machine's " +
            "public IPv4 and IPv6 egress.\n" +
            "\n" +
            "Setup:\n" +
            "  geo setup\n" +
            "\n" +
            "Setup downloads GeoLite2 City from P3TERX, a third-party " +
            "source, and verifies the GitHub Release digest.\n" +
            "\n" +
            "This product includes GeoLite Data created by MaxMind, " +
            "available from https://www.maxmind.com.\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Setup_activates_a_verified_P3TERX_release_without_lookup()
    {
        var provenance = P3terxProvenance();
        var updater = new FakeGeoLiteDatabaseUpdater(
            new GeoLiteUpdateResult.Activated(provenance));

        var result = await RunApplication(
            ["setup"],
            new UnexpectedPublicIpClient(),
            new UnexpectedGeolocationDatabase(),
            updater: updater);

        Assert.IsTrue(updater.WasRun);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "GeoLite2 City activated from P3TERX release 2026.08.17.\n" +
            "Verified digest: sha256:" + new string('a', 64) + "\n" +
            "This product includes GeoLite Data created by MaxMind, " +
            "available from https://www.maxmind.com.\n" +
            "Run: geo\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Scheduled_setup_reports_only_a_generic_update_boundary()
    {
        var provenance = P3terxProvenance();
        var updater = new FakeGeoLiteDatabaseUpdater(
            new GeoLiteUpdateResult.Activated(provenance));

        var result = await RunApplication(
            ["setup", "--scheduled"],
            new UnexpectedPublicIpClient(),
            new UnexpectedGeolocationDatabase(),
            updater: updater);

        Assert.IsTrue(updater.WasRun);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "geo update: started.\n" +
            "geo update: database updated and verified.\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
        Assert.IsFalse(
            result.Output.Contains(
                provenance.ReleaseTag,
                StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Scheduled_setup_hides_failure_details_from_the_journal()
    {
        const string safeDetail = "release metadata is not valid JSON";
        var updater = new FakeGeoLiteDatabaseUpdater(
            new GeoLiteUpdateResult.Failed(safeDetail));

        var result = await RunApplication(
            ["setup", "--scheduled"],
            new UnexpectedPublicIpClient(),
            new UnexpectedGeolocationDatabase(),
            updater: updater);

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual("geo update: started.\n", result.Output);
        Assert.AreEqual(
            "geo update: failed; previous database preserved.\n",
            result.Error);
        Assert.IsFalse(
            result.Error.Contains(safeDetail, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Doctor_reports_all_checks_and_returns_nonzero_for_failures()
    {
        var doctor = new FakeInstallationDoctor(
            new DoctorReport(
            [
                new DoctorCheck(
                    DoctorCheckStatus.Healthy,
                    "application",
                    "installed"),
                new DoctorCheck(
                    DoctorCheckStatus.Failed,
                    "database",
                    "missing; run: geo setup"),
                new DoctorCheck(
                    DoctorCheckStatus.Information,
                    "IPv6 endpoints",
                    "unavailable; IPv6 may not be configured"),
            ]));

        var result = await RunApplication(
            ["doctor"],
            new UnexpectedPublicIpClient(),
            new UnexpectedGeolocationDatabase(),
            doctor: doctor);

        Assert.IsTrue(doctor.WasRun);
        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(
            "[ok] application: installed\n" +
            "[fail] database: missing; run: geo setup\n" +
            "[info] IPv6 endpoints: unavailable; IPv6 may not be configured\n" +
            "Result: 1 actionable check failed.\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Doctor_returns_zero_when_required_checks_are_healthy()
    {
        var doctor = new FakeInstallationDoctor(
            new DoctorReport(
            [
                new DoctorCheck(
                    DoctorCheckStatus.Healthy,
                    "database",
                    "readable and fresh"),
                new DoctorCheck(
                    DoctorCheckStatus.Information,
                    "IPv6 endpoints",
                    "unavailable; IPv6 may not be configured"),
            ]));

        var result = await RunApplication(
            ["doctor"],
            new UnexpectedPublicIpClient(),
            new UnexpectedGeolocationDatabase(),
            doctor: doctor);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            "[ok] database: readable and fresh\n" +
            "[info] IPv6 endpoints: unavailable; IPv6 may not be configured\n" +
            "Result: healthy.\n",
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

    private static CachedEgressSnapshot CachedSnapshot(
        DateTimeOffset observedAt,
        params CachedEgressFamily[] observedFamilies)
    {
        var families = observedFamilies.ToList();
        var template = observedFamilies.First();
        foreach (var family in new[] { "IPv4", "IPv6" })
        {
            if (families.All(candidate => candidate.Family != family))
            {
                var address = family == "IPv4"
                    ? "198.51.100.254"
                    : "2001:db8::ffff";
                families.Add(
                    CachedFamily(
                        family,
                        address,
                        template.ApproximateCity,
                        template.CountryCode,
                        template.DiscoverySource));
            }
        }

        return CachedEgressSnapshot.Create(
                observedAt,
                families.Cast<CachedEgressFamily?>().ToArray()) ??
            throw new AssertFailedException(
                "The test cache snapshot must be valid and complete.");
    }

    private static GeoLiteProvenance P3terxProvenance() =>
        new(
            "P3TERX/GeoLite.mmdb",
            "2026.08.17",
            new DateTimeOffset(2026, 8, 17, 1, 2, 3, TimeSpan.Zero),
            new Uri(
                "https://github.com/P3TERX/GeoLite.mmdb/releases/download/" +
                "2026.08.17/GeoLite2-City.mmdb"),
            "sha256:" + new string('a', 64),
            new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero));

    private static CachedEgressFamily CachedFamily(
        string family,
        string? address = null,
        string? approximateCity = null,
        string? countryCode = null,
        string? discoverySource = null) =>
        CachedEgressFamily.Create(
            family,
            address,
            approximateCity,
            countryCode,
            discoverySource) ??
        throw new AssertFailedException(
            $"The cached {family} test outcome must be valid.");

    private static async ValueTask<ApplicationResult> RunApplication(
        string[] arguments,
        IPublicIpClient publicIp,
        IGeolocationDatabase geolocation,
        TimeProvider? timeProvider = null,
        IEgressSnapshotCache? cache = null,
        IGeoLiteDatabaseUpdater? updater = null,
        IInstallationDoctor? doctor = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var dependencies = new GeoApplicationDependencies(
            publicIp,
            geolocation,
            cache ?? new FakeEgressSnapshotCache(),
            output,
            error,
            timeProvider ?? TimeProvider.System,
            doctor ?? new UnexpectedInstallationDoctor());
        var application = new GeoApplication(
            dependencies,
            updater ?? new UnexpectedGeoLiteDatabaseUpdater());

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

    private static void AssertFamily(
        JsonElement family,
        string expectedFamily,
        string expectedAddress,
        string? expectedCity,
        string? expectedCountryCode,
        string expectedDiscoverySource)
    {
        Assert.AreEqual(
            expectedFamily,
            family.GetProperty("family").GetString());
        Assert.AreEqual(
            expectedAddress,
            family.GetProperty("address").GetString());
        Assert.AreEqual(
            expectedCity,
            family.GetProperty("approximateCity").GetString());
        Assert.AreEqual(
            expectedCountryCode,
            family.GetProperty("countryCode").GetString());
        Assert.AreEqual(
            expectedDiscoverySource,
            family.GetProperty("discoverySource").GetString());
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

    private sealed class DualStackPublicIpClient(
        string ipv4Response,
        string ipv6Response) : IPublicIpClient
    {
        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken) =>
            ReceivedResponse(ipv4Response);

        public ValueTask<PublicIpResponse> GetIdentMeIPv4(
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "IPv4 primary success must not contact its fallback.");

        public ValueTask<PublicIpResponse> GetIpifyIPv6(
            CancellationToken cancellationToken) =>
            ReceivedResponse(ipv6Response);

        public ValueTask<PublicIpResponse> GetIdentMeIPv6(
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "IPv6 primary success must not contact its fallback.");
    }

    private sealed class OrderedPublicIpClient : IPublicIpClient
    {
        private readonly RequestedIpFamily family;
        private readonly Func<
            CancellationToken,
            ValueTask<PublicIpResponse>> ipify;
        private readonly Func<
            CancellationToken,
            ValueTask<PublicIpResponse>> identMe;
        private bool ipifyRequested;

        private OrderedPublicIpClient(
            RequestedIpFamily family,
            Func<CancellationToken, ValueTask<PublicIpResponse>> ipify,
            Func<CancellationToken, ValueTask<PublicIpResponse>> identMe)
        {
            this.family = family;
            this.ipify = ipify;
            this.identMe = identMe;
        }

        internal static OrderedPublicIpClient ForIPv4(
            Func<CancellationToken, ValueTask<PublicIpResponse>> ipify,
            Func<CancellationToken, ValueTask<PublicIpResponse>> identMe) =>
            new(RequestedIpFamily.IPv4, ipify, identMe);

        internal static OrderedPublicIpClient ForIPv6(
            Func<CancellationToken, ValueTask<PublicIpResponse>> ipify,
            Func<CancellationToken, ValueTask<PublicIpResponse>> identMe) =>
            new(RequestedIpFamily.IPv6, ipify, identMe);

        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken) =>
            GetIpify(RequestedIpFamily.IPv4, cancellationToken);

        public ValueTask<PublicIpResponse> GetIdentMeIPv4(
            CancellationToken cancellationToken) =>
            GetIdentMe(RequestedIpFamily.IPv4, cancellationToken);

        public ValueTask<PublicIpResponse> GetIpifyIPv6(
            CancellationToken cancellationToken) =>
            GetIpify(RequestedIpFamily.IPv6, cancellationToken);

        public ValueTask<PublicIpResponse> GetIdentMeIPv6(
            CancellationToken cancellationToken) =>
            GetIdentMe(RequestedIpFamily.IPv6, cancellationToken);

        private ValueTask<PublicIpResponse> GetIpify(
            RequestedIpFamily requestedFamily,
            CancellationToken cancellationToken)
        {
            if (requestedFamily != family)
            {
                return UnavailableResponse();
            }

            ipifyRequested = true;
            return ipify(cancellationToken);
        }

        private ValueTask<PublicIpResponse> GetIdentMe(
            RequestedIpFamily requestedFamily,
            CancellationToken cancellationToken)
        {
            if (requestedFamily != family)
            {
                return UnavailableResponse();
            }

            Assert.IsTrue(
                ipifyRequested,
                "ipify must be requested before ident.me.");
            return identMe(cancellationToken);
        }
    }

    private enum RequestedIpFamily
    {
        IPv4,
        IPv6,
    }

    private sealed class DelayedFallbackPublicIpClient(
        TimeProvider timeProvider) : IPublicIpClient
    {
        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken) =>
            UnavailableResponse();

        public ValueTask<PublicIpResponse> GetIdentMeIPv4(
            CancellationToken cancellationToken) =>
            RespondAfterDelay("203.0.113.7", cancellationToken);

        public ValueTask<PublicIpResponse> GetIpifyIPv6(
            CancellationToken cancellationToken) =>
            UnavailableResponse();

        public ValueTask<PublicIpResponse> GetIdentMeIPv6(
            CancellationToken cancellationToken) =>
            RespondAfterDelay("2001:db8::7", cancellationToken);

        private async ValueTask<PublicIpResponse> RespondAfterDelay(
            string address,
            CancellationToken cancellationToken)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(1.5),
                timeProvider,
                cancellationToken);
            return new PublicIpResponse.Received(address);
        }
    }

    private sealed class MixedProviderPublicIpClient : IPublicIpClient
    {
        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken) =>
            ReceivedResponse("203.0.113.7");

        public ValueTask<PublicIpResponse> GetIdentMeIPv4(
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "IPv4 primary success must not contact its fallback.");

        public ValueTask<PublicIpResponse> GetIpifyIPv6(
            CancellationToken cancellationToken) =>
            UnavailableResponse();

        public ValueTask<PublicIpResponse> GetIdentMeIPv6(
            CancellationToken cancellationToken) =>
            ReceivedResponse("2001:db8::7");
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

    private sealed class DualStackGeolocationDatabase(
        IPAddress ipv4Address,
        GeolocationLookup ipv4Result,
        IPAddress ipv6Address,
        GeolocationLookup ipv6Result) : IGeolocationDatabase
    {
        public bool IsAvailable => true;

        public GeolocationLookup Lookup(IPAddress address)
        {
            if (address.Equals(ipv4Address))
            {
                return ipv4Result;
            }

            return address.Equals(ipv6Address)
                ? ipv6Result
                : new GeolocationLookup.LocationUnavailable();
        }
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

        public ValueTask<PublicIpResponse> GetIpifyIPv6(
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "This command must not perform an HTTP request.");

        public ValueTask<PublicIpResponse> GetIdentMeIPv6(
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "This command must not perform an HTTP request.");
    }

    private sealed class RecordingUnavailablePublicIpClient : IPublicIpClient
    {
        internal bool WasRequested { get; private set; }

        public ValueTask<PublicIpResponse> GetIpifyIPv4(
            CancellationToken cancellationToken) => Unavailable();

        public ValueTask<PublicIpResponse> GetIdentMeIPv4(
            CancellationToken cancellationToken) => Unavailable();

        public ValueTask<PublicIpResponse> GetIpifyIPv6(
            CancellationToken cancellationToken) => Unavailable();

        public ValueTask<PublicIpResponse> GetIdentMeIPv6(
            CancellationToken cancellationToken) => Unavailable();

        private ValueTask<PublicIpResponse> Unavailable()
        {
            WasRequested = true;
            return UnavailableResponse();
        }
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

    private sealed class FakeGeoLiteDatabaseUpdater(
        GeoLiteUpdateResult result) : IGeoLiteDatabaseUpdater
    {
        internal bool WasRun { get; private set; }

        public ValueTask<GeoLiteUpdateResult> Update(
            CancellationToken cancellationToken)
        {
            WasRun = true;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class UnexpectedGeoLiteDatabaseUpdater :
        IGeoLiteDatabaseUpdater
    {
        public ValueTask<GeoLiteUpdateResult> Update(
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "Only the setup command may acquire GeoLite.");
    }

    private sealed class FakeInstallationDoctor(DoctorReport report) :
        IInstallationDoctor
    {
        internal bool WasRun { get; private set; }

        public ValueTask<DoctorReport> Examine(
            CancellationToken cancellationToken)
        {
            WasRun = true;
            return ValueTask.FromResult(report);
        }
    }

    private sealed class UnexpectedInstallationDoctor : IInstallationDoctor
    {
        public ValueTask<DoctorReport> Examine(
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "Only the doctor command may inspect the installation.");
    }

    private sealed class UnavailableGeolocationDatabase : IGeolocationDatabase
    {
        public bool IsAvailable => false;

        public GeolocationLookup Lookup(IPAddress address) =>
            throw new AssertFailedException(
                "An unavailable database must be reported before lookup.");
    }

    private sealed class StaleGeolocationDatabase(
        DateTimeOffset buildTime) : IGeolocationDatabase
    {
        public bool IsAvailable => true;

        public DateTimeOffset? BuildTime => buildTime;

        public GeolocationLookup Lookup(IPAddress address) =>
            throw new AssertFailedException(
                "A stale database must be rejected before lookup.");
    }

    private sealed class AvailableUnexpectedGeolocationDatabase :
        IGeolocationDatabase
    {
        public bool IsAvailable => true;

        public GeolocationLookup Lookup(IPAddress address) =>
            throw new AssertFailedException(
                "Address discovery failure must precede GeoLite lookup.");
    }

    private sealed class FakeEgressSnapshotCache(
        CachedEgressSnapshot? snapshot = null) : IEgressSnapshotCache
    {
        internal CachedEgressSnapshot? WrittenSnapshot { get; private set; }

        public ValueTask<CachedEgressSnapshot?> Read(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(snapshot);

        public ValueTask Write(
            CachedEgressSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            WrittenSnapshot = snapshot;
            return ValueTask.CompletedTask;
        }
    }
}
