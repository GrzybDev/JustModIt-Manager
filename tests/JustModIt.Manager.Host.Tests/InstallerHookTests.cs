using JustModIt.Manager.Agent.Diagnostics;

namespace JustModIt.Manager.Host.Tests;

public sealed class InstallerHookTests : IDisposable
{
    private readonly string _state = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"), "state");

    [Fact]
    public void Runs_the_installer_stopped_count_as_clean_exits_while_a_crash_no_window_told_yet_stays()
    {
        var crash = new UncleanExit(DateTimeOffset.UnixEpoch, "0.9.0", typeof(InvalidOperationException).FullName, DateTimeOffset.UnixEpoch);
        new SessionMarker(_state, "agent", "1.0.0", TimeProvider.System).Begin();
        UntoldExit.Carry(_state, crash).Should().NotBeNull("the start after the crash ran with the system and opened no window");
        new SessionMarker(_state, "ui", "1.0.0", TimeProvider.System).Begin();

        Roles.EndStoppedRuns(_state);

        var previous = new SessionMarker(_state, "agent", "1.1.0", TimeProvider.System).Begin();
        previous.Should().BeNull("the installer stopped the agent after the player agreed to replace the app");
        new SessionMarker(_state, "ui", "1.1.0", TimeProvider.System).Begin().Should().BeNull();
        UntoldExit.Carry(_state, previous).Should().BeEquivalentTo(new { Version = "0.9.0", CrashType = crash.CrashType });
    }

    [Fact]
    public void A_crash_just_before_the_installer_ran_is_still_told()
    {
        var crashed = new SessionMarker(_state, "agent", "1.0.0", TimeProvider.System);
        crashed.Begin();
        crashed.RecordCrash(new InvalidOperationException());

        Roles.EndStoppedRuns(_state);

        new SessionMarker(_state, "agent", "1.1.0", TimeProvider.System).Begin()
            .Should().BeEquivalentTo(new { Version = "1.0.0", CrashType = typeof(InvalidOperationException).FullName });
    }

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_state)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
