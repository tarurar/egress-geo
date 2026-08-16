using System.Text;

namespace EgressGeo;

internal static class DoctorOutput
{
    internal static CommandResult Render(DoctorReport report)
    {
        var output = new StringBuilder();
        foreach (var check in report.Checks)
        {
            output.Append('[');
            output.Append(Status(check.Status));
            output.Append("] ");
            output.Append(check.Name);
            output.Append(": ");
            output.AppendLine(check.Detail);
        }

        if (report.IsHealthy)
        {
            output.AppendLine("Result: healthy.");
        }
        else
        {
            output.Append("Result: ");
            output.Append(report.FailureCount);
            output.Append(" actionable ");
            output.Append(report.FailureCount == 1 ? "check" : "checks");
            output.AppendLine(" failed.");
        }

        return new CommandResult(
            report.IsHealthy ? 0 : 1,
            output.ToString(),
            string.Empty);
    }

    private static string Status(DoctorCheckStatus status) =>
        status switch
        {
            DoctorCheckStatus.Healthy => "ok",
            DoctorCheckStatus.Information => "info",
            DoctorCheckStatus.Failed => "fail",
            _ => throw new InvalidOperationException(
                $"Unknown doctor check status: {status}"),
        };
}
