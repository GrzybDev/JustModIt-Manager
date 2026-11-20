using System.Runtime.Versioning;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Platform.Linux.Tests;

[SupportedOSPlatform("linux")]
public sealed class LiveRunningGameTests
{
    public LiveRunningGameTests() =>
        Assert.SkipUnless(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("MANAGER_LIVE_TESTS") == "1",
            "Live tests run only with MANAGER_LIVE_TESTS=1 on Linux.");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void A_running_steam_game_is_found_by_its_folder_and_by_its_launch()
    {
        var folder = Environment.GetEnvironmentVariable("MANAGER_LIVE_RUNNING_GAME_FOLDER");
        Assert.SkipUnless(GameKey.TryParse(Environment.GetEnvironmentVariable("MANAGER_LIVE_RUNNING_GAME"), out var game)
            && !string.IsNullOrEmpty(folder), "Set MANAGER_LIVE_RUNNING_GAME=steam:<id> and MANAGER_LIVE_RUNNING_GAME_FOLDER to its folder.");
        var launch = LinuxProcessSource.Instance.Snapshot().Where(process => process.Game == game).Select(process => process.Id).ToList();
        Assert.SkipWhen(launch.Count == 0, "Start the game first.");
        var detector = new RunningGameDetector(LinuxProcessSource.Instance, PhysicalFileSystem.Instance, TimeProvider.System);
        var canonical = PathCanonicalizer.Canonicalize(folder);

        detector.FindProcessesIn(game, folder, Token).Should().NotBeEmpty()
            .And.OnlyContain(process => process.ExecutablePath != null && PathComparison.IsWithin(canonical, process.ExecutablePath));
        detector.FindProcessesIn(game, Path.GetTempPath(), Token).Select(process => process.ProcessId).Should().IntersectWith(launch);
    }
}
