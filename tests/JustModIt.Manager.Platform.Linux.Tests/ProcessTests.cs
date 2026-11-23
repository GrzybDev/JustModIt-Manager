using System.Diagnostics;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Platform.Linux.Tests;

[SupportedOSPlatform("linux")]
public sealed class ProcessTests : IDisposable
{
    private static readonly GameKey Game = new(StoreId.Steam, "4242");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _children = [];

    public ProcessTests()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The processes are read from /proc, which only Linux has.");
        Directory.CreateDirectory(_directory);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static LinuxProcessSource Processes => LinuxProcessSource.Instance;

    [Fact]
    public void A_game_a_steam_launch_started_is_found_by_its_folder_and_by_its_app_id()
    {
        var folder = Path.Join(_directory, "Example Game");
        // Named as the program it copies, since a multi-call coreutils (uutils on newer Ubuntu) runs what its file's name says.
        var program = Path.Join(folder, "sleep");
        var elsewhere = Path.Join(_directory, "Other Game");
        Directory.CreateDirectory(folder);
        File.Copy("/bin/sleep", program);
        var child = Start(program, "30", steamAppId: "4242");
        var detector = new RunningGameDetector(Processes, PhysicalFileSystem.Instance, TimeProvider.System);

        Processes.Snapshot().Should().ContainSingle(process => process.Id == child.Id).Which.Should()
            .Be(new ProcessEntry(child.Id, "sleep", Processes.CurrentSessionId, PathCanonicalizer.Canonicalize(program), Game));
        detector.FindProcessesIn(Game, folder, Token).Select(process => process.ProcessId).Should().Equal(child.Id);
        detector.FindProcessesIn(Game, elsewhere, Token).Select(process => process.ProcessId).Should().Equal(child.Id);
        detector.FindProcessesIn(new GameKey(StoreId.Steam, "1"), elsewhere, Token).Should().BeEmpty();
    }

    [Fact]
    public async Task Waiting_ends_when_the_process_exits()
    {
        var child = Start("/bin/sleep", "1");

        var waited = await Processes.WaitForExitAsync(child.Id, "sleep", Token).WaitAsync(TimeSpan.FromSeconds(30), Token);

        waited.Should().BeTrue();
        // .NET reaps its child on a thread of its own, so the child may still be a zombie here, which lists as exited.
        Processes.Snapshot().Should().NotContain(process => process.Id == child.Id);
        (await Processes.WaitForExitAsync(int.MaxValue - 1, "sleep", Token)).Should().BeTrue();
    }

    [Fact]
    public async Task A_process_id_held_by_another_program_counts_as_exited() =>
        (await Processes.WaitForExitAsync(Environment.ProcessId, "ExampleGame", Token).WaitAsync(TimeSpan.FromSeconds(10), Token))
            .Should().BeTrue();

    public void Dispose()
    {
        foreach (var child in _children)
        {
            child.Kill();
            child.WaitForExit();
            child.Dispose();
        }

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private Process Start(string program, string arguments, string? steamAppId = null)
    {
        var start = new ProcessStartInfo(program, arguments);
        if (steamAppId is not null)
        {
            start.Environment["SteamAppId"] = steamAppId;
        }

        var child = Process.Start(start)!;
        _children.Add(child);
        return child;
    }
}
