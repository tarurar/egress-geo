namespace EgressGeo.Tests;

[TestClass]
[DoNotParallelize]
public sealed class UserDataPathsTests
{
    [TestMethod]
    public void Cache_path_uses_XDG_cache_home()
    {
        var previous = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var configured = Path.Combine(
            Path.GetTempPath(),
            $"egress-geo-xdg-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", configured);

            var path = UserDataPaths.GetCachePath();

            Assert.AreEqual(
                Path.Combine(configured, "egress-geo", "snapshot.json"),
                path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", previous);
        }
    }

    [TestMethod]
    public async Task Database_path_is_selected_by_the_provenance_digest()
    {
        var previous = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var configured = Path.Combine(
            Path.GetTempPath(),
            $"egress-geo-xdg-{Guid.NewGuid():N}");
        var provenance = GeoLiteTestData.Provenance(
            "2026.08.17",
            new DateTimeOffset(2026, 8, 17, 1, 0, 0, TimeSpan.Zero),
            new Uri(
                "https://github.com/P3TERX/GeoLite.mmdb/releases/download/" +
                "2026.08.17/GeoLite2-City.mmdb"),
            "sha256:" + new string('a', 64),
            new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero));
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", configured);
            var dataDirectory = Path.Combine(configured, "egress-geo");
            Directory.CreateDirectory(dataDirectory);
            await GeoLiteProvenanceFile.Write(
                Path.Combine(dataDirectory, "provenance.json"),
                provenance,
                CancellationToken.None);

            var path = UserDataPaths.GetDatabasePath();

            Assert.AreEqual(
                Path.Combine(
                    dataDirectory,
                    "databases",
                    new string('a', 64) + ".mmdb"),
                path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previous);
            Directory.Delete(configured, recursive: true);
        }
    }

    [TestMethod]
    public void Doctor_paths_use_the_configured_XDG_homes()
    {
        var previousData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var previousConfig = Environment.GetEnvironmentVariable(
            "XDG_CONFIG_HOME");
        var previousCache = Environment.GetEnvironmentVariable(
            "XDG_CACHE_HOME");
        var root = Path.Combine(
            Path.GetTempPath(),
            $"egress-geo-xdg-{Guid.NewGuid():N}");
        var data = Path.Combine(root, "data");
        var config = Path.Combine(root, "config");
        var cache = Path.Combine(root, "cache");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", data);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", config);
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", cache);

            var paths = UserDataPaths.GetDoctorPaths();

            Assert.AreEqual(
                Path.Combine(data, "egress-geo", "app", "geo"),
                paths.ApplicationPath);
            Assert.AreEqual(
                Path.Combine(data, "egress-geo", "GeoLite2-City.mmdb"),
                paths.DatabasePath);
            Assert.AreEqual(
                Path.Combine(data, "egress-geo", "provenance.json"),
                paths.ProvenancePath);
            Assert.AreEqual(
                Path.Combine(
                    config,
                    "systemd",
                    "user",
                    "egress-geo-update.timer"),
                paths.UpdateTimerPath);
            Assert.AreEqual(
                Path.Combine(cache, "egress-geo", "snapshot.json"),
                paths.CachePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previousData);
            Environment.SetEnvironmentVariable(
                "XDG_CONFIG_HOME",
                previousConfig);
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", previousCache);
        }
    }

    [TestMethod]
    [DataRow("XDG_DATA_HOME")]
    [DataRow("XDG_CONFIG_HOME")]
    [DataRow("XDG_CACHE_HOME")]
    public void Doctor_paths_reject_relative_XDG_homes(string variable)
    {
        var variables = new[]
        {
            "XDG_DATA_HOME",
            "XDG_CONFIG_HOME",
            "XDG_CACHE_HOME",
        };
        var previous = variables.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable);
        var root = Path.Combine(
            Path.GetTempPath(),
            $"egress-geo-xdg-{Guid.NewGuid():N}");
        try
        {
            foreach (var name in variables)
            {
                Environment.SetEnvironmentVariable(
                    name,
                    Path.Combine(root, name));
            }

            Environment.SetEnvironmentVariable(variable, "relative/path");

            var exception = Assert.ThrowsExactly<InvalidOperationException>(
                UserDataPaths.GetDoctorPaths);
            Assert.AreEqual(
                $"{variable} must be an absolute path.",
                exception.Message);
        }
        finally
        {
            foreach (var (name, value) in previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
