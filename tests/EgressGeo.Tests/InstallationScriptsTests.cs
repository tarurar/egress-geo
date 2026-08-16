using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EgressGeo.Tests;

[TestClass]
public sealed class InstallationScriptsTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestMethod]
    public async Task Install_publishes_framework_dependent_application_sidecars()
    {
        using var environment = new InstallationTestEnvironment();

        var result = await environment.RunScript("install.sh");

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.AreEqual(string.Empty, result.Error);
        Assert.IsTrue(File.Exists(environment.ApplicationPath("geo")));
        Assert.IsTrue(File.Exists(environment.ApplicationPath("geo.dll")));
        Assert.IsTrue(
            File.Exists(environment.ApplicationPath("geo.deps.json")));
        Assert.IsTrue(
            File.Exists(environment.ApplicationPath("geo.runtimeconfig.json")));
        var publishArguments = await File.ReadAllLinesAsync(
            environment.DotnetLogPath);
        AssertContainsSequence(
            publishArguments,
            "--configuration",
            "Release");
        AssertContainsSequence(publishArguments, "--runtime", "linux-x64");
        AssertContainsSequence(
            publishArguments,
            "--self-contained",
            "false");
    }

    [TestMethod]
    public async Task Installed_launcher_forwards_without_using_the_checkout()
    {
        using var environment = new InstallationTestEnvironment();
        Assert.AreEqual(
            0,
            (await environment.RunScript("install.sh")).ExitCode);

        var launched = await environment.Run(environment.LauncherPath, "probe");

        Assert.AreEqual(0, launched.ExitCode, launched.Error);
        Assert.AreEqual("published geo <probe>\n", launched.Output);
        Assert.AreEqual(string.Empty, launched.Error);
        var launcher = await File.ReadAllTextAsync(environment.LauncherPath);
        Assert.IsFalse(
            launcher.Contains(RepositoryRoot, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Installed_launcher_runs_the_built_geo_command()
    {
        using var environment = new InstallationTestEnvironment
        {
            PublishBuiltApplication = true,
        };
        Assert.AreEqual(
            0,
            (await environment.RunScript("install.sh")).ExitCode);

        var result = await environment.Run(environment.LauncherPath, "--help");

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.IsTrue(
            result.Output.Contains(
                "Setup:\n  geo setup\n",
                StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Installed_unconfigured_command_points_to_setup()
    {
        await using var proxy = new RejectingHttpProxy();
        using var environment = new InstallationTestEnvironment
        {
            HttpProxyUrl = proxy.Url,
            PublishBuiltApplication = true,
        };
        Assert.AreEqual(
            0,
            (await environment.RunScript("install.sh")).ExitCode);

        var result = await environment.Run(environment.LauncherPath);

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            "GeoLite2 City database is missing or unreadable.\n" +
            "Run: geo setup\n",
            result.Error);
    }

    [TestMethod]
    public async Task Reinstall_repairs_the_existing_deployment()
    {
        using var environment = new InstallationTestEnvironment();
        Assert.AreEqual(
            0,
            (await environment.RunScript("install.sh")).ExitCode);
        File.WriteAllText(environment.ApplicationPath("stale"), "stale");
        File.WriteAllText(environment.LauncherPath, "broken");

        var result = await environment.RunScript("install.sh");

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.IsFalse(File.Exists(environment.ApplicationPath("stale")));
        var launched = await environment.Run(environment.LauncherPath, "probe");
        Assert.AreEqual(0, launched.ExitCode, launched.Error);
        Assert.AreEqual("published geo <probe>\n", launched.Output);
        Assert.HasCount(
            0,
            Directory.GetFileSystemEntries(
                environment.ApplicationRootPath,
                ".app.*"));
    }

    [TestMethod]
    public async Task Install_requires_the_launcher_directory_on_PATH()
    {
        using var environment = new InstallationTestEnvironment
        {
            IncludeLauncherDirectoryOnPath = false,
        };

        var result = await environment.RunScript("install.sh");

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(
            $"geo install: add " +
            $"{Path.GetDirectoryName(environment.LauncherPath)} " +
            "to PATH before installing.\n",
            result.Error);
        Assert.IsFalse(File.Exists(environment.DotnetLogPath));
        Assert.IsFalse(File.Exists(environment.LauncherPath));
    }

    [TestMethod]
    public async Task Install_rejects_a_directory_at_the_launcher_path()
    {
        using var environment = new InstallationTestEnvironment();
        Directory.CreateDirectory(environment.LauncherPath);

        var result = await environment.RunScript("install.sh");

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(
            $"geo install: launcher path is a directory: " +
            $"{environment.LauncherPath}\n",
            result.Error);
        Assert.IsTrue(Directory.Exists(environment.LauncherPath));
        Assert.IsFalse(File.Exists(environment.DotnetLogPath));
    }

    [TestMethod]
    public async Task Default_uninstall_removes_deployment_and_preserves_user_data()
    {
        using var environment = new InstallationTestEnvironment();
        Assert.AreEqual(
            0,
            (await environment.RunScript("install.sh")).ExitCode);
        environment.CreateUserDataAndUnits();

        var result = await environment.RunScript("uninstall.sh");

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.AreEqual(string.Empty, result.Error);
        Assert.IsFalse(File.Exists(environment.LauncherPath));
        Assert.IsFalse(Directory.Exists(environment.ApplicationDirectoryPath));
        Assert.IsFalse(File.Exists(environment.UpdateServicePath));
        Assert.IsFalse(File.Exists(environment.UpdateTimerPath));
        Assert.IsTrue(File.Exists(environment.CredentialPath));
        Assert.IsTrue(File.Exists(environment.DatabasePath));
        Assert.IsTrue(File.Exists(environment.CachePath));
    }

    [TestMethod]
    public async Task Default_uninstall_can_be_repeated_safely()
    {
        using var environment = new InstallationTestEnvironment();
        Assert.AreEqual(
            0,
            (await environment.RunScript("install.sh")).ExitCode);
        environment.CreateUserDataAndUnits();
        Assert.AreEqual(
            0,
            (await environment.RunScript("uninstall.sh")).ExitCode);

        var result = await environment.RunScript("uninstall.sh");

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.IsTrue(
            result.Output.Contains(
                "Already absent geo application:",
                StringComparison.Ordinal));
        Assert.IsTrue(File.Exists(environment.CredentialPath));
        Assert.IsTrue(File.Exists(environment.DatabasePath));
        Assert.IsTrue(File.Exists(environment.CachePath));
    }

    [TestMethod]
    public async Task Uninstall_preserves_a_marker_mimicking_launcher()
    {
        using var environment = new InstallationTestEnvironment();
        environment.WriteLauncher(
            "#!/usr/bin/env bash\n" +
            "# Managed by egress-geo install.sh\n" +
            "exec another-command \"$@\"\n");

        var result = await environment.RunScript("uninstall.sh");

        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(File.Exists(environment.LauncherPath));
        Assert.IsTrue(
            result.Error.Contains(
                "Preserved unrecognized launcher:",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Purge_without_exact_confirmation_removes_nothing()
    {
        using var environment = new InstallationTestEnvironment();
        Assert.AreEqual(
            0,
            (await environment.RunScript("install.sh")).ExitCode);
        environment.CreateUserDataAndUnits();

        var result = await environment.RunScriptWithInput(
            "uninstall.sh",
            "no\n",
            "--purge");

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(
            "Purge permanently removes geo credentials, databases, and " +
            "cache.\n" +
            "Type PURGE to confirm:\n" +
            "Purge cancelled; nothing was removed.\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
        Assert.IsTrue(File.Exists(environment.LauncherPath));
        Assert.IsTrue(Directory.Exists(environment.ApplicationDirectoryPath));
        Assert.IsTrue(File.Exists(environment.CredentialPath));
        Assert.IsTrue(File.Exists(environment.DatabasePath));
        Assert.IsTrue(File.Exists(environment.CachePath));
    }

    [TestMethod]
    public async Task Confirmed_purge_removes_deployment_and_user_data()
    {
        using var environment = new InstallationTestEnvironment();
        Assert.AreEqual(
            0,
            (await environment.RunScript("install.sh")).ExitCode);
        environment.CreateUserDataAndUnits();

        var result = await environment.RunScriptWithInput(
            "uninstall.sh",
            "PURGE\n",
            "--purge");

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.AreEqual(string.Empty, result.Error);
        Assert.IsFalse(File.Exists(environment.LauncherPath));
        Assert.IsFalse(
            Directory.Exists(environment.ConfigurationDirectoryPath));
        Assert.IsFalse(Directory.Exists(environment.ApplicationRootPath));
        Assert.IsFalse(Directory.Exists(environment.CacheDirectoryPath));
    }

    [TestMethod]
    public async Task Confirmed_purge_can_be_repeated_safely()
    {
        using var environment = new InstallationTestEnvironment();
        environment.CreateUserDataAndUnits();
        Assert.AreEqual(
            0,
            (await environment.RunScriptWithInput(
                "uninstall.sh",
                "PURGE\n",
                "--purge")).ExitCode);

        var result = await environment.RunScriptWithInput(
            "uninstall.sh",
            "PURGE\n",
            "--purge");

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.IsTrue(
            result.Output.Contains(
                "Already absent user data:",
                StringComparison.Ordinal));
        Assert.IsFalse(
            Directory.Exists(environment.ConfigurationDirectoryPath));
        Assert.IsFalse(Directory.Exists(environment.ApplicationRootPath));
        Assert.IsFalse(Directory.Exists(environment.CacheDirectoryPath));
    }

    [TestMethod]
    public async Task Shell_entry_points_have_valid_Bash_syntax()
    {
        using var environment = new InstallationTestEnvironment();

        var result = await environment.Run(
            "/usr/bin/bash",
            "-n",
            Path.Combine(RepositoryRoot, "scripts", "install.sh"),
            Path.Combine(RepositoryRoot, "scripts", "uninstall.sh"),
            Path.Combine(RepositoryRoot, "scripts", "paths.sh"));

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    private static void AssertContainsSequence(
        string[] actual,
        string first,
        string second)
    {
        var index = Array.IndexOf(actual, first);
        Assert.IsGreaterThanOrEqualTo(0, index);
        Assert.IsLessThan(actual.Length - 1, index);
        Assert.AreEqual(second, actual[index + 1]);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "egress-geo.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not find the egress-geo repository root.");
    }

    private sealed class InstallationTestEnvironment : IDisposable
    {
        private const string FakeDotnet = """
            #!/usr/bin/env bash
            set -euo pipefail

            printf '%s\n' "$@" > "${FAKE_DOTNET_LOG:?}"

            output=''
            while (( $# > 0 )); do
              case "$1" in
                --output)
                  output=$2
                  shift 2
                  ;;
                *)
                  shift
                  ;;
              esac
            done

            [[ -n $output ]]
            mkdir -p -- "$output"
            if [[ -n ${FAKE_PUBLISH_SOURCE:-} ]]; then
              cp -a -- "$FAKE_PUBLISH_SOURCE/." "$output/"
              exit 0
            fi

            printf '%s\n' '#!/usr/bin/env bash' > "$output/geo"
            printf '%s\n' 'printf '\''published geo'\''' >> "$output/geo"
            printf '%s\n' 'printf '\'' <%s>'\'' "$@"' >> "$output/geo"
            printf '%s\n' 'printf '\''\n'\''' >> "$output/geo"
            chmod 0755 "$output/geo"
            printf 'assembly' > "$output/geo.dll"
            printf 'dependencies' > "$output/geo.deps.json"
            printf 'runtime' > "$output/geo.runtimeconfig.json"
            """;

        private readonly string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"egress geo install {Guid.NewGuid():N}");

        public InstallationTestEnvironment()
        {
            Directory.CreateDirectory(ToolsPath);
            Directory.CreateDirectory(HomePath);
            File.WriteAllText(
                Path.Combine(ToolsPath, "dotnet"),
                FakeDotnet,
                new UTF8Encoding(false));
            var chmod = Process.Start(
                "/usr/bin/chmod",
                ["0755", Path.Combine(ToolsPath, "dotnet")])!;
            chmod.WaitForExit();
            if (chmod.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Could not make the fake dotnet command executable.");
            }
        }

        public string HomePath => Path.Combine(rootPath, "test home");

        public string DataHomePath => Path.Combine(rootPath, "xdg data");

        public string ConfigHomePath => Path.Combine(rootPath, "xdg config");

        public string CacheHomePath => Path.Combine(rootPath, "xdg cache");

        public string ToolsPath => Path.Combine(rootPath, "test tools");

        public string DotnetLogPath => Path.Combine(rootPath, "dotnet.log");

        public bool IncludeLauncherDirectoryOnPath { get; init; } = true;

        public string? HttpProxyUrl { get; init; }

        public bool PublishBuiltApplication { get; init; }

        public string LauncherPath => Path.Combine(
            HomePath,
            ".local",
            "bin",
            "geo");

        public string ApplicationDirectoryPath => Path.Combine(
            ApplicationRootPath,
            "app");

        public string ApplicationRootPath => Path.Combine(
            DataHomePath,
            "egress-geo");

        public string ConfigurationDirectoryPath => Path.Combine(
            ConfigHomePath,
            "egress-geo");

        public string CacheDirectoryPath => Path.Combine(
            CacheHomePath,
            "egress-geo");

        public string CredentialPath => Path.Combine(
            ConfigHomePath,
            "egress-geo",
            "GeoIP.conf");

        public string DatabasePath => Path.Combine(
            DataHomePath,
            "egress-geo",
            "GeoLite2-City.mmdb");

        public string CachePath => Path.Combine(
            CacheHomePath,
            "egress-geo",
            "snapshot.json");

        public string UpdateServicePath => Path.Combine(
            ConfigHomePath,
            "systemd",
            "user",
            "egress-geo-update.service");

        public string UpdateTimerPath => Path.Combine(
            ConfigHomePath,
            "systemd",
            "user",
            "egress-geo-update.timer");

        public string ApplicationPath(string fileName) => Path.Combine(
            ApplicationDirectoryPath,
            fileName);

        public void CreateUserDataAndUnits()
        {
            WriteFile(CredentialPath, "secret");
            WriteFile(DatabasePath, "database");
            WriteFile(CachePath, "cache");
            WriteFile(UpdateServicePath, "service");
            WriteFile(UpdateTimerPath, "timer");
        }

        public void WriteLauncher(string content) =>
            WriteFile(LauncherPath, content);

        public Task<ProcessResult> RunScript(
            string scriptName,
            params string[] arguments) =>
            Run(
                "/usr/bin/bash",
                [Path.Combine(RepositoryRoot, "scripts", scriptName),
                    .. arguments]);

        public Task<ProcessResult> RunScriptWithInput(
            string scriptName,
            string input,
            params string[] arguments) =>
            RunWithInput(
                "/usr/bin/bash",
                input,
                [Path.Combine(RepositoryRoot, "scripts", scriptName),
                    .. arguments]);

        public Task<ProcessResult> Run(
            string executable,
            params string[] arguments) =>
            RunProcess(executable, null, arguments);

        public Task<ProcessResult> RunWithInput(
            string executable,
            string input,
            params string[] arguments) =>
            RunProcess(executable, input, arguments);

        private async Task<ProcessResult> RunProcess(
            string executable,
            string? input,
            string[] arguments)
        {
            using var process = Process.Start(
                CreateStartInfo(executable, arguments))!;
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input);
            }

            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ProcessResult(
                process.ExitCode,
                await output,
                await error);
        }

        private ProcessStartInfo CreateStartInfo(
            string executable,
            string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            ConfigureEnvironment(startInfo);
            return startInfo;
        }

        private void ConfigureEnvironment(ProcessStartInfo startInfo)
        {
            startInfo.Environment.Clear();
            startInfo.Environment["HOME"] = HomePath;
            startInfo.Environment["XDG_CONFIG_HOME"] = ConfigHomePath;
            startInfo.Environment["XDG_DATA_HOME"] = DataHomePath;
            startInfo.Environment["XDG_CACHE_HOME"] = CacheHomePath;
            startInfo.Environment["FAKE_DOTNET_LOG"] = DotnetLogPath;
            if (PublishBuiltApplication)
            {
                startInfo.Environment["FAKE_PUBLISH_SOURCE"] =
                    AppContext.BaseDirectory;
            }

            if (HttpProxyUrl is not null)
            {
                startInfo.Environment["HTTP_PROXY"] = HttpProxyUrl;
                startInfo.Environment["HTTPS_PROXY"] = HttpProxyUrl;
                startInfo.Environment["NO_PROXY"] = string.Empty;
            }

            var binaryDirectory = Path.GetDirectoryName(LauncherPath);
            startInfo.Environment["PATH"] = IncludeLauncherDirectoryOnPath
                ? $"{ToolsPath}:{binaryDirectory}:/usr/bin:/bin"
                : $"{ToolsPath}:/usr/bin:/bin";
            startInfo.Environment["LC_ALL"] = "C";
        }

        public void Dispose() => Directory.Delete(rootPath, recursive: true);

        private static void WriteFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string Error);

    private sealed class RejectingHttpProxy : IAsyncDisposable
    {
        private static readonly byte[] Rejection = Encoding.ASCII.GetBytes(
            "HTTP/1.1 502 Bad Gateway\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n");

        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stopping = new();
        private readonly Task accepting;

        public RejectingHttpProxy()
        {
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Url = $"http://127.0.0.1:{endpoint.Port}";
            accepting = RejectConnections(stopping.Token);
        }

        public string Url { get; }

        public async ValueTask DisposeAsync()
        {
            await stopping.CancelAsync();
            listener.Stop();
            await accepting;
            stopping.Dispose();
        }

        private async Task RejectConnections(
            CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync(
                        cancellationToken);
                    await client.GetStream().WriteAsync(
                        Rejection,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (SocketException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
