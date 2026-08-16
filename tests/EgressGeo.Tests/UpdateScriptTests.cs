using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace EgressGeo.Tests;

[TestClass]
[SupportedOSPlatform("linux")]
public sealed class UpdateScriptTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestMethod]
    public async Task Failed_update_preserves_the_database_without_leaking_output()
    {
        using var environment = new UpdateTestEnvironment("valid-old");

        var result = await environment.Run("failure");

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual("geo update: started.\n", result.Output);
        Assert.AreEqual(
            "geo update: failed; previous database preserved.\n",
            result.Error);
        Assert.AreEqual(
            "valid-old",
            await File.ReadAllTextAsync(environment.DatabasePath));
        Assert.IsFalse(result.Error.Contains("123456", StringComparison.Ordinal));
        Assert.IsFalse(
            result.Error.Contains("license-secret", StringComparison.Ordinal));
        Assert.IsFalse(
            result.Error.Contains(
                "https://sensitive.example",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task No_change_preserves_the_current_database()
    {
        using var environment = new UpdateTestEnvironment("valid-current");

        var result = await environment.Run("no-change");

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.AreEqual(
            "geo update: started.\n" +
            "geo update: no update available; current database preserved.\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
        Assert.AreEqual(
            "valid-current",
            await File.ReadAllTextAsync(environment.DatabasePath));
        Assert.HasCount(0, environment.UpdateWorkspaces);
    }

    [TestMethod]
    public async Task Successful_update_atomically_replaces_the_database()
    {
        using var environment = new UpdateTestEnvironment("valid-old");
        await using var previousDatabase = new FileStream(
            environment.DatabasePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        var result = await environment.Run("success");

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.AreEqual(
            "geo update: started.\n" +
            "geo update: database updated and verified.\n",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
        Assert.AreEqual(
            "valid-new",
            await File.ReadAllTextAsync(environment.DatabasePath));
        using var previousReader = new StreamReader(previousDatabase);
        Assert.AreEqual("valid-old", await previousReader.ReadToEndAsync());
        Assert.HasCount(
            1,
            Directory.GetFiles(environment.ApplicationRootPath, "*.mmdb"));
        Assert.HasCount(0, environment.UpdateWorkspaces);
    }

    [TestMethod]
    public async Task Unreadable_candidate_preserves_the_current_database()
    {
        using var environment = new UpdateTestEnvironment("valid-old");

        var result = await environment.Run("invalid");

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(
            "valid-old",
            await File.ReadAllTextAsync(environment.DatabasePath));
        Assert.AreEqual(
            "geo update: failed; previous database preserved.\n",
            result.Error);
        Assert.HasCount(0, environment.UpdateWorkspaces);
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

    private sealed class UpdateTestEnvironment : IDisposable
    {
        private const string FakeUpdater = """
            #!/usr/bin/env bash
            set -euo pipefail

            configuration=''
            database_directory=''
            while (( $# > 0 )); do
              case $1 in
                -f) configuration=$2; shift 2 ;;
                -d) database_directory=$2; shift 2 ;;
                *) exit 64 ;;
              esac
            done
            [[ -f $configuration && -d $database_directory ]]

            case ${FAKE_UPDATE_BEHAVIOR:?} in
              failure)
                printf 'invalid-new' > \
                  "$database_directory/GeoLite2-City.mmdb"
                printf '%s\n' \
                  '123456 license-secret https://sensitive.example' >&2
                exit 9
                ;;
              no-change) ;;
              success)
                printf 'valid-new' > \
                  "$database_directory/GeoLite2-City.mmdb"
                ;;
              invalid)
                printf 'invalid-new' > \
                  "$database_directory/GeoLite2-City.mmdb"
                ;;
              *) exit 64 ;;
            esac
            """;

        private const string FakeApplication = """
            #!/usr/bin/env bash
            set -euo pipefail

            [[ ${1:-} == setup && ${2:-} == --verify-database ]]
            database="${XDG_DATA_HOME:?}/egress-geo/GeoLite2-City.mmdb"
            [[ -f $database && $(< "$database") == valid-* ]]
            """;

        private readonly string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"egress geo update {Guid.NewGuid():N}");

        internal UpdateTestEnvironment(string databaseContent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(UpdaterPath)!);
            Directory.CreateDirectory(
                Path.GetDirectoryName(ConfigurationPath)!);
            File.WriteAllText(DatabasePath, databaseContent);
            File.WriteAllText(
                ConfigurationPath,
                "AccountID 123456\n" +
                "LicenseKey license-secret\n" +
                "Host https://sensitive.example\n");
            WriteExecutable(UpdaterPath, FakeUpdater);
            WriteExecutable(ApplicationPath, FakeApplication);
        }

        internal string DatabasePath => Path.Combine(
            ApplicationRootPath,
            "GeoLite2-City.mmdb");

        internal string ApplicationRootPath => Path.Combine(
            rootPath,
            "data home",
            "egress-geo");

        internal string[] UpdateWorkspaces =>
            Directory.GetDirectories(ApplicationRootPath, ".update.*");

        private string UpdaterPath => Path.Combine(
            rootPath,
            "data home",
            "egress-geo",
            "updater",
            "geoipupdate");

        private string ConfigurationPath => Path.Combine(
            rootPath,
            "config home",
            "egress-geo",
            "GeoIP.conf");

        private string ApplicationPath => Path.Combine(
            rootPath,
            "data home",
            "egress-geo",
            "app",
            "geo");

        internal async Task<ProcessResult> Run(string behavior)
        {
            using var process = Process.Start(CreateStartInfo(behavior))!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ProcessResult(
                process.ExitCode,
                await output,
                await error);
        }

        private ProcessStartInfo CreateStartInfo(string behavior)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/bash",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(
                Path.Combine(RepositoryRoot, "scripts", "update.sh"));
            startInfo.ArgumentList.Add(UpdaterPath);
            startInfo.ArgumentList.Add(ConfigurationPath);
            startInfo.ArgumentList.Add(DatabasePath);
            startInfo.ArgumentList.Add(ApplicationPath);
            startInfo.Environment.Clear();
            startInfo.Environment["FAKE_UPDATE_BEHAVIOR"] = behavior;
            startInfo.Environment["PATH"] = "/usr/bin:/bin";
            startInfo.Environment["LC_ALL"] = "C";
            return startInfo;
        }

        public void Dispose() => Directory.Delete(rootPath, recursive: true);

        private static void WriteExecutable(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string Error);
}
