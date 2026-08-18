namespace EgressGeo;

internal interface IGeoLiteDatabaseInspector
{
    GeoLiteDatabaseMetadata? Read(string path);
}
