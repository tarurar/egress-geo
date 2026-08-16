using System.ComponentModel;
using System.Diagnostics;

namespace EgressGeo;

public sealed class SystemctlUserTimerStateReader(string executable) :
    IUserTimerStateReader
{
    private const string TimerUnit = "egress-geo-update.timer";
    private readonly string executable =
        string.IsNullOrWhiteSpace(executable)
            ? throw new ArgumentException(
                "The systemctl executable is required.",
                nameof(executable))
            : executable;

    public SystemctlUserTimerStateReader()
        : this("systemctl")
    {
    }

    public async ValueTask<UserTimerState> Read(
        CancellationToken cancellationToken)
    {
        var enabled = await Query("is-enabled", cancellationToken);
        var active = await Query("is-active", cancellationToken);
        var available = enabled is not null && active is not null &&
            enabled.State.Length > 0 && active.State.Length > 0;
        return new UserTimerState(
            available,
            IsEnabled(enabled?.State),
            string.Equals(
                active?.State,
                "active",
                StringComparison.Ordinal));
    }

    private async ValueTask<SystemctlResult?> Query(
        string operation,
        CancellationToken cancellationToken)
    {
        Process? process;
        try
        {
            process = Process.Start(CreateStartInfo(operation));
        }
        catch (Exception exception)
            when (exception is Win32Exception or InvalidOperationException)
        {
            return null;
        }

        if (process is null)
        {
            return null;
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(
                cancellationToken);
            var error = process.StandardError.ReadToEndAsync(
                cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await Terminate(process);
                throw;
            }

            await error;
            return new SystemctlResult(FirstLine(await output));
        }
    }

    private ProcessStartInfo CreateStartInfo(string operation)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--user");
        startInfo.ArgumentList.Add(operation);
        startInfo.ArgumentList.Add(TimerUnit);
        startInfo.Environment["LC_ALL"] = "C";
        return startInfo;
    }

    private static async ValueTask Terminate(Process process)
    {
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between HasExited and Kill.
            }
        }

        await process.WaitForExitAsync(CancellationToken.None);
    }

    private static string FirstLine(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim() ?? string.Empty;

    private static bool IsEnabled(string? state) =>
        string.Equals(state, "enabled", StringComparison.Ordinal) ||
        string.Equals(state, "enabled-runtime", StringComparison.Ordinal);

    private sealed record SystemctlResult(string State);
}
