using MaxMind.Db;
using MaxMind.GeoIP2;

namespace EgressGeo;

internal sealed class MaxMindGeoLiteDatabaseInspector :
    IGeoLiteDatabaseInspector
{
    public GeoLiteDatabaseMetadata? Read(string path)
    {
        try
        {
            using var reader = new DatabaseReader(path, ["en"]);
            return string.Equals(
                    reader.Metadata.DatabaseType,
                    "GeoLite2-City",
                    StringComparison.Ordinal)
                ? new GeoLiteDatabaseMetadata(reader.Metadata.BuildDate)
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDatabaseException)
        {
            return null;
        }
    }
}
