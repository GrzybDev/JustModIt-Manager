using System.Globalization;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Platform.Linux.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Platform.Linux.Tests;

public sealed class ScopedDesktopShellTests : IDisposable
{
    private const string DesktopId = "com.example.ExampleApp";
    private const string Window = "/home/player/Applications/Example App.AppImage";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task Every_start_runs_in_a_scope_of_its_own_with_the_users_systemd_environment_of_that_moment()
    {
        await using var bus = await PrivateBus.StartAsync(Token);
        var systemd = await FakeSystemd.StartAsync(bus);
        systemd.Environment = ["PATH=/usr/bin:/bin", "XDG_CURRENT_DESKTOP=gamescope"];
        var shell = new ScopedDesktopShell(DesktopId, bus.Address, NullLogger<ScopedDesktopShell>.Instance) { ScopeRunner = Recorder() };
        var game = Directory.CreateDirectory(Path.Combine(_folder, "Example Game")).FullName;

        var window = await RecordedAsync(shell.Run(Window, ["--ui", "say \"hi\"", @"ends\"]));
        // The desktop of a new session, such as the switch from Game Mode, comes with the next start.
        systemd.Environment = ["PATH=/usr/bin:/bin", "XDG_CURRENT_DESKTOP=KDE", "DISPLAY=:0"];
        shell.Start(new ProgramStart(Path.Combine(game, "start.sh"), "--windowed \"slot 2\"", game)).Should().BeTrue();
        shell.Open("steam://open/main").Should().BeTrue();

        window.Arguments.Should().HaveCount(10).And.Subject.Take(4).Should().Equal("--user", "--scope", "--collect", "--quiet");
        window.Arguments[4].Should().MatchRegex($"^--unit=app-{DesktopId}-[0-9a-f]{{8}}$");
        window.Arguments.Skip(5).Should().Equal("--", Window, "--ui", "say \"hi\"", @"ends\");
        window.Environment.Should().BeEquivalentTo("PATH=/usr/bin:/bin", "XDG_CURRENT_DESKTOP=gamescope");
        // A scope's start returns once its runner has moved into the scope, or here, where none can, once it has finished.
        List<Recorded> later = [.. Directory.GetFiles(Path.Combine(_folder, "runs")).Select(Recorded.Read)];
        later.Should().HaveCount(3);
        later.Single(run => run.Arguments.Contains("--windowed")).Should().Match<Recorded>(run =>
            run.Arguments.Skip(5).SequenceEqual(new[] { "--", Path.Combine(game, "start.sh"), "--windowed", "slot 2" })
            && run.Environment.Contains("XDG_CURRENT_DESKTOP=KDE") && run.Environment.Contains("DISPLAY=:0")
            && run.Folder == game);
        later.Single(run => run.Arguments.Contains("xdg-open")).Arguments.Skip(5).Should().Equal("--", "xdg-open", "steam://open/main");
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task Without_the_users_systemd_a_program_starts_without_a_scope()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "systemd starts scopes only on Linux.");
        var shell = new ScopedDesktopShell(DesktopId, "unix:path=/nonexistent", NullLogger<ScopedDesktopShell>.Instance)
        {
            ScopeRunner = "/nonexistent/systemd-run",
        };

        var started = shell.Run(Recorder(), ["--ui"]);

        (await RecordedAsync(started)).Arguments.Should().Equal("--ui");
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task A_program_whose_scope_is_slow_to_start_counts_as_not_started_only_for_a_run_the_agent_may_exit_after()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "systemd starts scopes only on Linux.");
        await using var bus = await PrivateBus.StartAsync(Token);
        await FakeSystemd.StartAsync(bus);
        var slow = Path.Combine(Directory.CreateDirectory(_folder).FullName, "slow.sh");
        File.WriteAllText(slow, "#!/bin/sh\nexec sleep 30\n");
        File.SetUnixFileMode(slow, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var shell = new ScopedDesktopShell(DesktopId, bus.Address, NullLogger<ScopedDesktopShell>.Instance) { ScopeRunner = slow };

        shell.Run(Window, ["--ui"]).Should().BeNull("the unit's stop would take an updater with the agent that exits for it");
        shell.Open("steam://open/main").Should().BeTrue("a link the shell opened late still opens");
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // Writes down the words, environment and folder it ran with, once per run, as systemd-run would run the program.
    [SupportedOSPlatform("linux")]
    private string Recorder()
    {
        var path = Path.Combine(_folder, "record.sh");
        Directory.CreateDirectory(Path.Combine(_folder, "runs"));
        File.WriteAllText(path, """
            #!/bin/sh
            out="$(dirname "$0")/runs/$$"
            { printf '%s\n' "$@"; echo ---; tr '\0' '\n' < /proc/$$/environ; echo ---; pwd; } > "$out.part" && mv "$out.part" "$out"
            """);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private async Task<Recorded> RecordedAsync(int? processId)
    {
        processId.Should().NotBeNull();
        var path = Path.Combine(_folder, "runs", processId.Value.ToString(CultureInfo.InvariantCulture));
        for (var tries = 0; !File.Exists(path) && tries < 500; tries++)
        {
            await Task.Delay(10, Token);
        }

        return Recorded.Read(path);
    }

    private sealed record Recorded(IReadOnlyList<string> Arguments, IReadOnlyList<string> Environment, string Folder)
    {
        public static Recorded Read(string path)
        {
            var lines = File.ReadAllLines(path);
            var first = Array.IndexOf(lines, "---");
            var second = Array.IndexOf(lines, "---", first + 1);
            return new Recorded(lines[..first], lines[(first + 1)..second], lines[second + 1]);
        }
    }
}
