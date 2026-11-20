using System.Diagnostics;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Platform.Linux.Tests.TestSupport;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux.Tests;

public sealed class AgentUnitTests : IDisposable
{
    private const string DesktopId = "com.example.ExampleApp";
    private const string Name = DesktopId + ".service";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string UnitFile => Path.Join(_folder, Name);

    [Fact]
    public void The_unit_runs_the_agent_with_its_words_quoted_as_systemd_reads_them_and_gives_up_on_one_that_keeps_failing()
    {
        using var nowhere = new DBusConnection("unix:path=/nonexistent");
        var unit = new AgentUnit(DesktopId, "Example App 100%", "/home/player/Apps $HOME/Example \"App\" \\ 100%.AppImage",
            ["--agent", "--background"], _folder, nowhere);

        var text = unit.Text(install: false);

        text.Should().StartWith("[Unit]\nDescription=Example App 100%%\nStartLimitIntervalSec=300\nStartLimitBurst=5\n\n[Service]\n")
            .And.Contain("\nExecStart=\"/home/player/Apps $$HOME/Example \\\"App\\\" \\\\ 100%%.AppImage\" \"--agent\" \"--background\"\n")
            .And.NotContain("[Install]");
        unit.Text(install: true).Should().Be(text + "\n[Install]\nWantedBy=default.target\n");
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task The_mounts_cleanup_after_a_stop_unmounts_the_appimages_mounts_whose_folder_cannot_be_read()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The unit's commands run only on Linux.");
        Assert.SkipWhen(Environment.IsPrivilegedProcess, "Root reads every folder.");
        using var nowhere = new DBusConnection("unix:path=/nonexistent");
        var line = new AgentUnit(DesktopId, "Example App", "/home/player/Example App.AppImage", ["--agent"], _folder, nowhere)
            .Text(install: false).Split('\n').Single(line => line.StartsWith("ExecStopPost=/bin/sh -c \"", StringComparison.Ordinal));
        var mounts = Path.Join(_folder, "mountinfo");
        // The script reads the test's own list of mounts, with systemd's escapes undone: a quote's first, since its backslash
        // may follow an escaped one.
        var script = line["ExecStopPost=/bin/sh -c \"".Length..line.LastIndexOf("\" sh \"", StringComparison.Ordinal)]
            .Replace("\\\"", "\"", StringComparison.Ordinal).Replace(@"\\", @"\", StringComparison.Ordinal)
            .Replace("$$", "$", StringComparison.Ordinal).Replace("/proc/self/mountinfo", mounts, StringComparison.Ordinal);
        line.Should().EndWith(" sh \"Example App.AppImage\"");

        // A mount the AppImage's runtime left dead still answers stat, but its folder cannot be read, like a folder the user
        // may not read; the test's fusermount3 notes what it unmounts. The mount table writes a space as \040.
        var dead = Directory.CreateDirectory(Path.Join(_folder, "dead mount")).FullName;
        var live = Directory.CreateDirectory(Path.Join(_folder, "live mount")).FullName;
        var other = Directory.CreateDirectory(Path.Join(_folder, "other mount")).FullName;
        var programs = Directory.CreateDirectory(Path.Join(_folder, "bin")).FullName;
        var fusermount = Path.Join(programs, "fusermount3");
        File.WriteAllText(fusermount, "#!/bin/sh\necho \"$*\" >> \"$0.log\"\n");
        File.SetUnixFileMode(fusermount, File.GetUnixFileMode(fusermount) | UnixFileMode.UserExecute);
        static string Escaped(string folder) => folder.Replace(" ", @"\040", StringComparison.Ordinal);
        File.WriteAllText(mounts, $"""
            50 30 0:60 / {Escaped(dead)} ro,nosuid,nodev,relatime shared:1 - fuse.Example\040App.AppImage Example\040App.AppImage ro
            51 30 0:61 / {Escaped(live)} ro,nosuid,nodev,relatime shared:2 - fuse.Example\040App.AppImage Example\040App.AppImage ro
            52 30 0:62 / {Escaped(other)} ro,nosuid,nodev,relatime shared:3 - fuse.Example\040App\040Beta.AppImage Other ro
            """);
        File.SetUnixFileMode(dead, UnixFileMode.None);
        File.SetUnixFileMode(other, UnixFileMode.None);
        try
        {
            var start = new ProcessStartInfo("/bin/sh") { RedirectStandardError = true, UseShellExecute = false };
            start.Environment["PATH"] = $"{programs}:{Environment.GetEnvironmentVariable("PATH")}";
            foreach (var argument in new[] { "-c", script, "sh", "Example App.AppImage" })
            {
                start.ArgumentList.Add(argument);
            }

            using var shell = Process.Start(start)!;
            var errors = await shell.StandardError.ReadToEndAsync(Token);
            await shell.WaitForExitAsync(Token);

            (shell.ExitCode, errors).Should().Be((0, ""));
            File.ReadAllText(fusermount + ".log").Should().Be($"-uz {dead}\n");
            (Directory.Exists(dead), Directory.Exists(live), Directory.Exists(other)).Should().Be((false, true, true),
                "a live mount, such as the window's, stays, and so does another AppImage's");
        }
        finally
        {
            // A folder left unreadable would fail the clean-up after the test with an error of its own.
            foreach (var folder in new[] { dead, other }.Where(Directory.Exists))
            {
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    [Fact]
    public async Task Starting_with_the_system_enables_the_unit_and_a_disable_by_anyone_else_turns_it_off()
    {
        await using var bus = await PrivateBus.StartAsync(Token);
        var systemd = await FakeSystemd.StartAsync(bus);
        var unit = new AgentUnit(DesktopId, "Example App", "/home/player/Applications/ExampleApp.AppImage", ["--agent", "--background"],
            _folder, await bus.ConnectAsync());

        unit.GetState().Should().Be(AutostartState.Disabled);
        systemd.Calls.Should().BeEmpty("a unit without its file is not asked about");

        unit.Enable();
        systemd.State = "enabled";

        File.ReadAllText(UnitFile).Should().Be(unit.Text(install: true));
        unit.GetState().Should().Be(AutostartState.Enabled);
        Drain(systemd).Should().Equal($"EnableUnitFiles {Name}", "Reload", $"GetUnitFileState {Name}");
        unit.Enable();
        Drain(systemd).Should().Equal([$"EnableUnitFiles {Name}"], "nothing changed, so systemd needs no reload at every start");

        foreach (var state in new[] { "disabled", "masked" })
        {
            systemd.State = state;
            unit.GetState().Should().Be(AutostartState.DisabledBySystem, $"a {state} unit was turned off by someone else");
        }

        unit.Disable();
        systemd.State = "static";

        File.ReadAllText(UnitFile).Should().Be(unit.Text(install: false));
        unit.GetState().Should().Be(AutostartState.Disabled, "the app's own unit without [Install] is static");
        Drain(systemd).Should().Equal($"GetUnitFileState {Name}", $"GetUnitFileState {Name}", $"DisableUnitFiles {Name}", "Reload",
            $"GetUnitFileState {Name}");
    }

    [Fact]
    public async Task A_systemd_that_refuses_fails_as_a_file_error_would()
    {
        await using var bus = await PrivateBus.StartAsync(Token);
        var systemd = await FakeSystemd.StartAsync(bus);
        systemd.Error = "org.freedesktop.DBus.Error.AccessDenied";
        var unit = new AgentUnit(DesktopId, "Example App", "/home/player/ExampleApp.AppImage", [], _folder, await bus.ConnectAsync());

        unit.Invoking(autostart => autostart.Enable()).Should().Throw<IOException>();
        unit.Invoking(autostart => autostart.GetState()).Should().Throw<IOException>();
    }

    [Fact]
    public void A_session_without_the_users_systemd_reads_as_off_and_fails_to_enable_as_a_file_error_would()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The user's systemd answers on the session bus only on Linux.");
        using var nowhere = new DBusConnection(new DBusConnectionOptions("unix:path=/nonexistent") { AutoConnect = true });
        var unit = new AgentUnit(DesktopId, "Example App", "/home/player/ExampleApp.AppImage", [], _folder, nowhere);

        unit.Invoking(autostart => autostart.Enable()).Should().Throw<IOException>();
        unit.GetState().Should().Be(AutostartState.Disabled, "with no systemd to ask, nothing could have enabled the unit");
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static List<string> Drain(FakeSystemd systemd)
    {
        List<string> calls = [];
        while (systemd.Calls.TryDequeue(out var call))
        {
            calls.Add(call);
        }

        return calls;
    }
}
