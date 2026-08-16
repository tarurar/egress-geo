using System.Runtime.Versioning;
using System.Text;

namespace EgressGeo.Tests;

[TestClass]
[DoNotParallelize]
[SupportedOSPlatform("linux")]
public sealed class SystemctlUserTimerStateReaderTests
{
    [TestMethod]
    public async Task Read_reports_an_enabled_active_user_timer()
    {
        using var systemctl = new FakeSystemctl(
            enabledState: "enabled",
            activeState: "active");
        var reader = new SystemctlUserTimerStateReader(systemctl.Path);

        var state = await reader.Read(CancellationToken.None);

        Assert.AreEqual(
            new UserTimerState(
                IsAvailable: true,
                IsEnabled: true,
                IsActive: true),
            state);
        CollectionAssert.AreEqual(
            new[]
            {
                "--user is-enabled egress-geo-update.timer",
                "--user is-active egress-geo-update.timer",
            },
            await File.ReadAllLinesAsync(systemctl.LogPath));
    }

    [TestMethod]
    public async Task Read_distinguishes_a_disabled_inactive_timer()
    {
        using var systemctl = new FakeSystemctl(
            enabledState: "disabled",
            activeState: "inactive");
        var reader = new SystemctlUserTimerStateReader(systemctl.Path);

        var state = await reader.Read(CancellationToken.None);

        Assert.AreEqual(
            new UserTimerState(
                IsAvailable: true,
                IsEnabled: false,
                IsActive: false),
            state);
    }

    private sealed class FakeSystemctl : IDisposable
    {
        private const string Script = """
            #!/usr/bin/env bash
            set -euo pipefail

            printf '%s\n' "$*" >> "${FAKE_SYSTEMCTL_LOG:?}"
            case ${2:-} in
              is-enabled)
                printf '%s\n' "${FAKE_ENABLED_STATE:?}"
                [[ $FAKE_ENABLED_STATE == enabled ]]
                ;;
              is-active)
                printf '%s\n' "${FAKE_ACTIVE_STATE:?}"
                [[ $FAKE_ACTIVE_STATE == active ]]
                ;;
              *) exit 64 ;;
            esac
            """;

        private readonly string rootPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"egress-geo-systemctl-{Guid.NewGuid():N}");
        private readonly string? previousLog =
            Environment.GetEnvironmentVariable("FAKE_SYSTEMCTL_LOG");
        private readonly string? previousEnabled =
            Environment.GetEnvironmentVariable("FAKE_ENABLED_STATE");
        private readonly string? previousActive =
            Environment.GetEnvironmentVariable("FAKE_ACTIVE_STATE");

        internal FakeSystemctl(string enabledState, string activeState)
        {
            Directory.CreateDirectory(rootPath);
            Path = System.IO.Path.Combine(rootPath, "systemctl");
            LogPath = System.IO.Path.Combine(rootPath, "systemctl.log");
            File.WriteAllText(Path, Script, new UTF8Encoding(false));
            File.SetUnixFileMode(
                Path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable(
                "FAKE_SYSTEMCTL_LOG",
                LogPath);
            Environment.SetEnvironmentVariable(
                "FAKE_ENABLED_STATE",
                enabledState);
            Environment.SetEnvironmentVariable(
                "FAKE_ACTIVE_STATE",
                activeState);
        }

        internal string Path { get; }

        internal string LogPath { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(
                "FAKE_SYSTEMCTL_LOG",
                previousLog);
            Environment.SetEnvironmentVariable(
                "FAKE_ENABLED_STATE",
                previousEnabled);
            Environment.SetEnvironmentVariable(
                "FAKE_ACTIVE_STATE",
                previousActive);
            Directory.Delete(rootPath, recursive: true);
        }
    }
}
