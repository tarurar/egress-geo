namespace EgressGeo;

public sealed record DoctorCheck(
    DoctorCheckStatus Status,
    string Name,
    string Detail);
