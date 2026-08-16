namespace EgressGeo;

public sealed record GeoApplicationDependencies(
    IPublicIpClient PublicIp,
    IGeolocationDatabase Geolocation,
    TextWriter Output,
    TextWriter Error,
    TimeProvider TimeProvider);
