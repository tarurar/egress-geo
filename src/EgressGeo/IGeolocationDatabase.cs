using System.Net;

namespace EgressGeo;

public interface IGeolocationDatabase
{
    bool IsAvailable { get; }

    DateTimeOffset? BuildTime => null;

    GeolocationLookup Lookup(IPAddress address);
}
