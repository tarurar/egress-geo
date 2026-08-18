namespace EgressGeo;

public sealed record DoctorPaths(
    string ApplicationPath,
    string DatabasePath,
    string ProvenancePath,
    string UpdateServicePath,
    string UpdateTimerPath,
    string CachePath);
