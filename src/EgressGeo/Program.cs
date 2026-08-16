using EgressGeo;

using var httpClient = new HttpClient();
using var database = new MaxMindGeolocationDatabase(
    UserDataPaths.GetDatabasePath());
var cache = new FileEgressSnapshotCache(UserDataPaths.GetCachePath());
var dependencies = new GeoApplicationDependencies(
    new PublicIpHttpClient(httpClient),
    database,
    cache,
    Console.Out,
    Console.Error,
    TimeProvider.System);
var application = new GeoApplication(
    dependencies,
    new BashSetupWizard(Console.Error));

return await application.Run(args, CancellationToken.None);
