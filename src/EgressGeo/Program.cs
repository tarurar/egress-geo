using EgressGeo;

using var httpClient = new HttpClient();
using var database = new MaxMindGeolocationDatabase(
    UserDataPaths.GetDatabasePath());
var dependencies = new GeoApplicationDependencies(
    new PublicIpHttpClient(httpClient),
    database,
    Console.Out,
    Console.Error,
    TimeProvider.System);
var application = new GeoApplication(dependencies);

return await application.Run(args, CancellationToken.None);
