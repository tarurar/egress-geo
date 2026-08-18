using EgressGeo;

using var httpClient = new HttpClient();
using var geoLiteHttpClient = new HttpClient();
var databasePath = UserDataPaths.GetDatabasePath();
using var database = new MaxMindGeolocationDatabase(databasePath);
var cache = new FileEgressSnapshotCache(UserDataPaths.GetCachePath());
var publicIp = new PublicIpHttpClient(httpClient);
var timeProvider = TimeProvider.System;
var geoLiteSource = new P3terxGeoLiteReleaseSource(
    geoLiteHttpClient,
    timeProvider);
var updater = new GeoLiteDatabaseUpdater(
    UserDataPaths.GetUpdatePaths(),
    geoLiteSource,
    timeProvider);
var doctor = new InstallationDoctor(
    UserDataPaths.GetDoctorPaths(databasePath),
    publicIp,
    database,
    cache,
    geoLiteSource,
    new SystemctlUserTimerStateReader(),
    timeProvider);
var dependencies = new GeoApplicationDependencies(
    publicIp,
    database,
    cache,
    Console.Out,
    Console.Error,
    timeProvider,
    doctor);
var application = new GeoApplication(
    dependencies,
    updater);

return await application.Run(args, CancellationToken.None);
