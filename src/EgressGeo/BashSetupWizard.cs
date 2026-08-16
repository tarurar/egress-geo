using System.Diagnostics;

namespace EgressGeo;

internal sealed class BashSetupWizard(TextWriter error) : ISetupWizard
{
    private readonly string scriptPath = Path.Combine(
        AppContext.BaseDirectory,
        "setup.sh");

    public async ValueTask<int> Run(CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath))
        {
            await error.WriteAsync(
                "The geo setup wizard is missing. Re-run install.sh.\n");
            return 1;
        }

        using var process = Process.Start(CreateStartInfo()) ??
            throw new InvalidOperationException(
                "Could not start the geo setup wizard.");
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

        return process.ExitCode;
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

    private ProcessStartInfo CreateStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/env",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("bash");
        startInfo.ArgumentList.Add(scriptPath);
        return startInfo;
    }
}
