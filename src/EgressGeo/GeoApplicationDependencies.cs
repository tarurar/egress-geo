namespace EgressGeo;

public sealed record GeoApplicationDependencies(
    IPublicIpClient PublicIp,
    IGeolocationDatabase Geolocation,
    IEgressSnapshotCache Cache,
    TextWriter Output,
    TextWriter Error,
    TimeProvider TimeProvider);
