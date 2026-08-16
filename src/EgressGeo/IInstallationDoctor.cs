namespace EgressGeo;

public interface IInstallationDoctor
{
    ValueTask<DoctorReport> Examine(
        CancellationToken cancellationToken);
}
