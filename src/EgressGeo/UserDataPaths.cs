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
}
