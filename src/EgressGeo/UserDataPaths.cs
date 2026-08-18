namespace EgressGeo;

public static class UserDataPaths
{
    public static string GetDatabasePath()
    {
        var paths = GetUpdatePaths();
        return new GeoLiteInstallationStore(paths).ResolveDatabasePath();
    }

    public static string GetCachePath()
    {
        return Path.Combine(GetCacheRoot(), "snapshot.json");
    }

    internal static GeoLiteUpdatePaths GetUpdatePaths()
    {
        var dataRoot = GetDataRoot();
        return new GeoLiteUpdatePaths(dataRoot);
    }

    public static DoctorPaths GetDoctorPaths()
    {
        return GetDoctorPaths(GetDatabasePath());
    }

    internal static DoctorPaths GetDoctorPaths(string databasePath)
    {
        var dataRoot = GetDataRoot();
        var configHome = GetHome("XDG_CONFIG_HOME", ".config");
        var unitRoot = Path.Combine(configHome, "systemd", "user");
        return new DoctorPaths(
            Path.Combine(dataRoot, "app", "geo"),
            databasePath,
            Path.Combine(dataRoot, "provenance.json"),
            Path.Combine(unitRoot, "egress-geo-update.service"),
            Path.Combine(unitRoot, "egress-geo-update.timer"),
            Path.Combine(GetCacheRoot(), "snapshot.json"));
    }

    private static string GetDataRoot() =>
        Path.Combine(
            GetHome("XDG_DATA_HOME", ".local", "share"),
            "egress-geo");

    private static string GetCacheRoot() =>
        Path.Combine(GetHome("XDG_CACHE_HOME", ".cache"), "egress-geo");

    private static string GetHome(
        string variable,
        params string[] fallbackSegments)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Path.Combine(fallbackSegments));
        }

        return Path.IsPathFullyQualified(configured)
            ? configured
            : throw new InvalidOperationException(
                $"{variable} must be an absolute path.");
    }
}
