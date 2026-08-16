using System.Net;

namespace EgressGeo;

public interface IGeolocationDatabase
{
    bool IsAvailable { get; }

    GeolocationLookup Lookup(IPAddress address);
}
