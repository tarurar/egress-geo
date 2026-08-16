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
}
