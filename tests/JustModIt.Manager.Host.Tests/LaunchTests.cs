using JustModIt.Manager.Protocol.Activation;

namespace JustModIt.Manager.Host.Tests;

public sealed class LaunchTests : IDisposable
{
    private readonly string _lock = OperatingSystem.IsWindows()
        ? $@"Local\ExampleApp.Tests.{Guid.NewGuid():N}"
        : Path.Join(Path.GetTempPath(), $"ExampleApp.Tests.{Guid.NewGuid():N}.lock");

    private readonly List<string> _steps = [];

    [Fact]
    public void A_launch_holds_the_windows_lock_before_the_agent_starts()
    {
        Launch(startedBySteam: false, unitStarts: true).Should().Be(0);
        _steps.Should().Equal("unit started, window locked", "window, window locked");
        InstanceLock.IsHeld(_lock).Should().BeFalse("the window lets go of its lock when it ends");

        Launch(startedBySteam: false, unitStarts: false).Should().Be(0);
        _steps.Should().Equal(["unit refused, window locked", "agent, window free"],
            "a launch that runs the agent leaves the window's role to the window the agent starts");

        using (InstanceLock.TryAcquire(_lock))
        {
            Launch(startedBySteam: false, unitStarts: true).Should().Be(0);
        }

        _steps.Should().Equal("unit started, window locked", "hand-over");
    }

    [Fact]
    public void A_Steam_launch_whose_unit_fails_becomes_the_window()
    {
        HostEnvironment.IsSteamLaunch("3797322547").Should().Be(OperatingSystem.IsLinux());
        HostEnvironment.IsSteamLaunch("0").Should().BeFalse("Steam's id of a program it did not start is 0");
        HostEnvironment.IsSteamLaunch(null).Should().BeFalse();

        Launch(startedBySteam: true, unitStarts: false).Should().Be(0);
        _steps.Should().Equal(["unit refused, window locked", "window, window locked"],
            "Steam ends every process of its launch, so an agent there would end with the window");

        using (InstanceLock.TryAcquire(_lock))
        {
            Launch(startedBySteam: true, unitStarts: false).Should().Be(0);
        }

        _steps.Should().Equal("unit refused, window locked", "hand-over");
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            File.Delete(_lock);
        }
    }

    private int Launch(bool startedBySteam, bool unitStarts)
    {
        _steps.Clear();
        return Roles.LaunchWindow(startedBySteam, () => InstanceLock.TryAcquire(_lock),
            () =>
            {
                Step(unitStarts ? "unit started" : "unit refused");
                return unitStarts;
            },
            windowLock =>
            {
                using (windowLock)
                {
                    Step("window");
                }

                return 0;
            },
            () =>
            {
                _steps.Add("hand-over");
                return 0;
            },
            () =>
            {
                Step("agent");
                return 0;
            });
    }

    private void Step(string step) => _steps.Add($"{step}, window {(InstanceLock.IsHeld(_lock) ? "locked" : "free")}");
}
