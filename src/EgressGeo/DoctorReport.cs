using System.Collections.ObjectModel;

namespace EgressGeo;

public sealed class DoctorReport
{
    public DoctorReport(IReadOnlyCollection<DoctorCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        Checks = new ReadOnlyCollection<DoctorCheck>(checks.ToArray());
    }

    public IReadOnlyList<DoctorCheck> Checks { get; }

    public bool IsHealthy => FailureCount == 0;

    internal int FailureCount =>
        Checks.Count(check => check.Status == DoctorCheckStatus.Failed);
}
