using System.Net;

namespace EgressGeo.Tests;

[TestClass]
public sealed class MaxMindGeolocationDatabaseTests
{
    [TestMethod]
    public void Lookup_reads_the_synthetic_GeoLite_city_record()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "GeoLite2-City-Test.mmdb");
        using var database = new MaxMindGeolocationDatabase(fixturePath);

        var result = database.Lookup(IPAddress.Parse("81.2.69.142"));

        Assert.AreEqual(
            new GeolocationLookup.Found("London", "GB"),
            result);
    }

    [TestMethod]
    public void Build_time_comes_from_the_database_metadata()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "GeoLite2-City-Test.mmdb");
        using var database = new MaxMindGeolocationDatabase(fixturePath);

        var buildTime = database.BuildTime;

        Assert.IsNotNull(buildTime);
        Assert.AreEqual(TimeSpan.Zero, buildTime.Value.Offset);
    }
}
