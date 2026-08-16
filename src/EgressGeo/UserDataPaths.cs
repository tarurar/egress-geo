namespace EgressGeo;

public static class UserDataPaths
{
    public static string GetDatabasePath()
    {
        var configuredDataHome = Environment.GetEnvironmentVariable(
            "XDG_DATA_HOME");
        var dataHome = string.IsNullOrWhiteSpace(configuredDataHome)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share")
            : configuredDataHome;

        return Path.Combine(dataHome, "egress-geo", "GeoLite2-City.mmdb");
    }

    public static string GetCachePath()
    {
        var configuredCacheHome = Environment.GetEnvironmentVariable(
            "XDG_CACHE_HOME");
        var cacheHome = string.IsNullOrWhiteSpace(configuredCacheHome)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache")
            : configuredCacheHome;

        return Path.Combine(cacheHome, "egress-geo", "snapshot.json");
    }
}
