using EgressGeo;

using var httpClient = new HttpClient();
using var database = new MaxMindGeolocationDatabase(
    UserDataPaths.GetDatabasePath());
var cache = new FileEgressSnapshotCache(UserDataPaths.GetCachePath());
var publicIp = new PublicIpHttpClient(httpClient);
var timeProvider = TimeProvider.System;
var doctor = new InstallationDoctor(
    UserDataPaths.GetDoctorPaths(),
    publicIp,
    database,
    cache,
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
    new BashSetupWizard(Console.Error));

return await application.Run(args, CancellationToken.None);
