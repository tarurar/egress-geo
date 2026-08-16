using System.Net;
using MaxMind.Db;
using MaxMind.GeoIP2;

namespace EgressGeo;

public sealed class MaxMindGeolocationDatabase :
    IGeolocationDatabase,
    IDisposable
{
    private readonly DatabaseReader? reader;

    public MaxMindGeolocationDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        try
        {
            reader = new DatabaseReader(databasePath, ["en"]);
        }
        catch (Exception exception) when (IsUnavailableDatabase(exception))
        {
            reader = null;
        }
    }

    public bool IsAvailable => reader is not null;

    public GeolocationLookup Lookup(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (reader is null)
        {
            return new GeolocationLookup.DatabaseUnavailable();
        }

        try
        {
            return reader.TryCity(address, out var response)
                ? new GeolocationLookup.Found(
                    response.City.Names.GetValueOrDefault("en"),
                    response.Country.IsoCode)
                : new GeolocationLookup.LocationUnavailable();
        }
        catch (InvalidDatabaseException)
        {
            return new GeolocationLookup.DatabaseUnavailable();
        }
    }

    public void Dispose() => reader?.Dispose();

    private static bool IsUnavailableDatabase(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
        InvalidDatabaseException;
}
