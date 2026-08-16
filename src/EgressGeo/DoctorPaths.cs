namespace EgressGeo;

public sealed record DoctorPaths(
    string ApplicationPath,
    string DatabasePath,
    string UpdaterPath,
    string CredentialPath,
    string UpdateServicePath,
    string UpdateTimerPath,
    string CachePath);
