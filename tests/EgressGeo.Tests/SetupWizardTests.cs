using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace EgressGeo.Tests;

[TestClass]
[SupportedOSPlatform("linux")]
public sealed class SetupWizardTests
{
    private const string ArchiveChecksum =
        "941eb4dd8c1eafb6ee1d56ccd5f4c62ffbdaca5f65a9f9cadc4008c8d805f2a2";
    private const string LicenseKey = "test-license-key";
    private const string NewSetupInput =
        "\n\n123456\n" + LicenseKey + "\n";
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestMethod]
    public async Task Setup_opens_the_official_credential_journeys()
    {
        using var environment = new SetupTestEnvironment();

        var result = await environment.Run(NewSetupInput);

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.AreEqual(string.Empty, result.Error);
        var operations = await File.ReadAllTextAsync(
            environment.OperationsLogPath);
        Assert.IsTrue(
            operations.Contains(
                "opened https://www.maxmind.com/en/geolite2/signup\n",
                StringComparison.Ordinal));
        Assert.IsTrue(
            operations.Contains(
                "opened https://www.maxmind.com/en/accounts/current/edit\n",
                StringComparison.Ordinal));
        Assert.IsTrue(
            operations.Contains(
                "opened https://www.maxmind.com/en/accounts/current/" +
                "license-key\n",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Setup_keeps_the_license_key_out_of_output_and_logs()
    {
        using var environment = new SetupTestEnvironment();

        var result = await environment.Run(NewSetupInput);

        Assert.AreEqual(0, result.ExitCode, result.Error);
        var operations = await File.ReadAllTextAsync(
            environment.OperationsLogPath);
        Assert.IsFalse(
            result.Output.Contains(LicenseKey, StringComparison.Ordinal));
        Assert.IsFalse(
            operations.Contains(LicenseKey, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Setup_writes_only_the_private_GeoLite_configuration()
    {
        using var environment = new SetupTestEnvironment();

        var result = await environment.Run(NewSetupInput);

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.AreEqual(
            "AccountID 123456\n" +
            $"LicenseKey {LicenseKey}\n" +
            "Host https://updates.maxmind.com\n" +
            $"DatabaseDirectory {environment.ApplicationRootPath}\n" +
            "EditionIDs GeoLite2-City\n",
            await File.ReadAllTextAsync(environment.ConfigurationPath));
        Assert.AreEqual(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(environment.ConfigurationPath));
    }

    [TestMethod]
    public async Task Setup_installs_and_verifies_the_GeoLite_assets()
    {
        using var environment = new SetupTestEnvironment();

        var result = await environment.Run(NewSetupInput);

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.IsTrue(File.Exists(environment.UpdaterPath));
        Assert.IsTrue(File.Exists(environment.DatabasePath));
        var operations = await File.ReadAllTextAsync(
            environment.OperationsLogPath);
        Assert.IsTrue(
            operations.Contains(
                "downloaded https://github.com/maxmind/geoipupdate/" +
                "releases/download/v8.0.0/" +
                "geoipupdate_8.0.0_linux_amd64.tar.gz\n",
                StringComparison.Ordinal));
        Assert.IsTrue(
            operations.Contains(
                "updated GeoLite2-City\n",
                StringComparison.Ordinal));
        Assert.IsTrue(
            operations.Contains("verified geo\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Setup_finishes_with_attribution_and_the_next_command()
    {
        using var environment = new SetupTestEnvironment();

        var result = await environment.Run(NewSetupInput);

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.IsTrue(
            result.Output.Contains(
                "This product includes GeoLite Data created by MaxMind, " +
                "available from https://www.maxmind.com.\n",
                StringComparison.Ordinal));
        Assert.IsTrue(
            result.Output.Contains("Run: geo\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Setup_rerun_keeps_credentials_and_repairs_missing_assets()
    {
        using var environment = new SetupTestEnvironment();
        Assert.AreEqual(
            0,
            (await environment.Run(
                "\n\n123456\n" + LicenseKey + "\n")).ExitCode);
        File.Delete(environment.UpdaterPath);
        File.Delete(environment.DatabasePath);

        var result = await environment.Run("\n\n\n\n");

        Assert.AreEqual(0, result.ExitCode, result.Error);
        var configuration = await File.ReadAllTextAsync(
            environment.ConfigurationPath);
        Assert.IsTrue(
            configuration.Contains(
                "AccountID 123456\n",
                StringComparison.Ordinal));
        Assert.IsTrue(
            configuration.Contains(
                $"LicenseKey {LicenseKey}\n",
                StringComparison.Ordinal));
        Assert.IsTrue(File.Exists(environment.UpdaterPath));
        Assert.IsTrue(File.Exists(environment.DatabasePath));
    }

    [TestMethod]
    public async Task Checksum_mismatch_preserves_the_working_installation()
    {
        using var environment = new SetupTestEnvironment();
        Assert.AreEqual(
            0,
            (await environment.Run(
                "\n\n123456\n" + LicenseKey + "\n")).ExitCode);
        var configuration = await File.ReadAllTextAsync(
            environment.ConfigurationPath);
        var updater = await File.ReadAllTextAsync(environment.UpdaterPath);
        var database = await File.ReadAllTextAsync(environment.DatabasePath);
        environment.ArchiveChecksum = new string('0', 64);

        var result = await environment.Run("\n\n\n\n");

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(
            "geo setup: geoipupdate archive checksum verification failed.\n",
            result.Error);
        Assert.AreEqual(
            configuration,
            await File.ReadAllTextAsync(environment.ConfigurationPath));
        Assert.AreEqual(
            updater,
            await File.ReadAllTextAsync(environment.UpdaterPath));
        Assert.AreEqual(
            database,
            await File.ReadAllTextAsync(environment.DatabasePath));
    }

    [TestMethod]
    public async Task Failed_download_preserves_the_working_installation()
    {
        using var environment = new SetupTestEnvironment();
        Assert.AreEqual(
            0,
            (await environment.Run(
                "\n\n123456\n" + LicenseKey + "\n")).ExitCode);
        var configuration = await File.ReadAllTextAsync(
            environment.ConfigurationPath);
        var updater = await File.ReadAllTextAsync(environment.UpdaterPath);
        var database = await File.ReadAllTextAsync(environment.DatabasePath);
        environment.DownloadFails = true;

        var result = await environment.Run("\n\n\n\n");

        Assert.AreNotEqual(0, result.ExitCode);
        Assert.AreEqual(
            configuration,
            await File.ReadAllTextAsync(environment.ConfigurationPath));
        Assert.AreEqual(
            updater,
            await File.ReadAllTextAsync(environment.UpdaterPath));
        Assert.AreEqual(
            database,
            await File.ReadAllTextAsync(environment.DatabasePath));
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

    private sealed class SetupTestEnvironment : IDisposable
    {
        private const string FakeCurl = """
            #!/usr/bin/env bash
            set -euo pipefail

            output=''
            url=''
            while (( $# > 0 )); do
              case "$1" in
                --output)
                  output=$2
                  shift 2
                  ;;
                --proto)
                  shift 2
                  ;;
                --fail|--location|--silent|--show-error|--tlsv1.2)
                  shift
                  ;;
                *)
                  url=$1
                  shift
                  ;;
              esac
            done
            [[ -n $output && -n $url ]]
            [[ ${FAKE_DOWNLOAD_FAIL:-0} != 1 ]] || exit 22
            printf '%s\n' 'fake archive' > "$output"
            printf 'downloaded %s\n' "$url" >> "${FAKE_OPERATIONS_LOG:?}"
            """;

        private const string FakeSha256Sum = """
            #!/usr/bin/env bash
            set -euo pipefail

            [[ $# -eq 1 ]]
            printf '%s  %s\n' "${FAKE_ARCHIVE_CHECKSUM:?}" "$1"
            """;

        private const string FakeTar = """
            #!/usr/bin/env bash
            set -euo pipefail

            destination=''
            while (( $# > 0 )); do
              case "$1" in
                --directory|-C)
                  destination=$2
                  shift 2
                  ;;
                *)
                  shift
                  ;;
              esac
            done
            [[ -n $destination ]]
            extracted="$destination/geoipupdate_8.0.0_linux_amd64"
            mkdir -p -- "$extracted"
            updater="$extracted/geoipupdate"
            {
              printf '%s\n' '#!/usr/bin/env bash'
              printf '%s\n' 'set -euo pipefail'
              printf '%s\n' 'config=""'
              printf '%s\n' 'while (( $# > 0 )); do'
              printf '%s\n' '  case "$1" in'
              printf '%s\n' '    -f) config=$2; shift 2 ;;'
              printf '%s\n' '    *) exit 64 ;;'
              printf '%s\n' '  esac'
              printf '%s\n' 'done'
              printf '%s\n' '[[ -f $config ]]'
              printf '%s\n' 'database_directory=""'
              printf '%s\n' 'while IFS= read -r line; do'
              printf '%s\n' '  case "$line" in'
              printf '%s\n' '    "DatabaseDirectory "*) database_directory=${line#DatabaseDirectory } ;;'
              printf '%s\n' '  esac'
              printf '%s\n' 'done < "$config"'
              printf '%s\n' '[[ -n $database_directory ]]'
              printf '%s\n' 'mkdir -p -- "$database_directory"'
              printf '%s\n' 'printf database > "$database_directory/GeoLite2-City.mmdb"'
              printf '%s\n' 'printf "%s\n" "updated GeoLite2-City" >> "${FAKE_OPERATIONS_LOG:?}"'
            } > "$updater"
            chmod 0755 "$updater"
            """;

        private const string FakeBrowser = """
            #!/usr/bin/env bash
            set -euo pipefail

            printf 'opened %s\n' "$1" >> "${FAKE_OPERATIONS_LOG:?}"
            """;

        private const string FakeGeo = """
            #!/usr/bin/env bash
            set -euo pipefail

            [[ $# -eq 2 ]]
            [[ $1 == setup && $2 == --verify-database ]]
            [[ -r "${XDG_DATA_HOME:?}/egress-geo/GeoLite2-City.mmdb" ]]
            printf '%s\n' 'verified geo' >> "${FAKE_OPERATIONS_LOG:?}"
            """;

        private readonly string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"egress geo setup {Guid.NewGuid():N}");

        public SetupTestEnvironment()
        {
            Directory.CreateDirectory(ToolsPath);
            Directory.CreateDirectory(HomePath);
            WriteExecutable("curl", FakeCurl);
            WriteExecutable("sha256sum", FakeSha256Sum);
            WriteExecutable("tar", FakeTar);
            foreach (var browser in
                     new[] { "wslview", "explorer.exe", "xdg-open", "open" })
            {
                WriteExecutable(browser, FakeBrowser);
            }
            WriteExecutable(LauncherPath, FakeGeo);
        }

        public string ArchiveChecksum { get; set; } =
            SetupWizardTests.ArchiveChecksum;

        public bool DownloadFails { get; set; }

        public string HomePath => Path.Combine(rootPath, "test home");

        public string DataHomePath => Path.Combine(rootPath, "xdg data");

        public string ConfigHomePath => Path.Combine(rootPath, "xdg config");

        public string CacheHomePath => Path.Combine(rootPath, "xdg cache");

        public string ToolsPath => Path.Combine(rootPath, "test tools");

        public string LauncherPath => Path.Combine(
            HomePath,
            ".local",
            "bin",
            "geo");

        public string ApplicationRootPath => Path.Combine(
            DataHomePath,
            "egress-geo");

        public string ConfigurationPath => Path.Combine(
            ConfigHomePath,
            "egress-geo",
            "GeoIP.conf");

        public string UpdaterPath => Path.Combine(
            ApplicationRootPath,
            "updater",
            "geoipupdate");

        public string DatabasePath => Path.Combine(
            ApplicationRootPath,
            "GeoLite2-City.mmdb");

        public string OperationsLogPath => Path.Combine(
            rootPath,
            "operations.log");

        public async Task<ProcessResult> Run(string input)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/bash",
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(
                Path.Combine(RepositoryRoot, "scripts", "setup.sh"));
            ConfigureEnvironment(startInfo);

            using var process = Process.Start(startInfo)!;
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ProcessResult(
                process.ExitCode,
                await output,
                await error);
        }

        public void Dispose() => Directory.Delete(rootPath, recursive: true);

        private void ConfigureEnvironment(ProcessStartInfo startInfo)
        {
            startInfo.Environment.Clear();
            startInfo.Environment["HOME"] = HomePath;
            startInfo.Environment["XDG_CONFIG_HOME"] = ConfigHomePath;
            startInfo.Environment["XDG_DATA_HOME"] = DataHomePath;
            startInfo.Environment["XDG_CACHE_HOME"] = CacheHomePath;
            startInfo.Environment["FAKE_ARCHIVE_CHECKSUM"] = ArchiveChecksum;
            startInfo.Environment["FAKE_OPERATIONS_LOG"] = OperationsLogPath;
            startInfo.Environment["FAKE_DOWNLOAD_FAIL"] =
                DownloadFails ? "1" : "0";
            startInfo.Environment["PATH"] =
                $"{ToolsPath}:{Path.GetDirectoryName(LauncherPath)}:" +
                "/usr/bin:/bin";
            startInfo.Environment["LC_ALL"] = "C";
        }

        private void WriteExecutable(string nameOrPath, string content)
        {
            var path = Path.IsPathRooted(nameOrPath)
                ? nameOrPath
                : Path.Combine(ToolsPath, nameOrPath);
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
