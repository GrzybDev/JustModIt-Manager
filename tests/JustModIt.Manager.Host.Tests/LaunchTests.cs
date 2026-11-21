using System.Globalization;
using JustModIt.Manager.Protocol.Activation;

namespace JustModIt.Manager.Host.Tests;

public sealed class LaunchTests : IDisposable
{
    private readonly string _lock = OperatingSystem.IsWindows()
        ? $@"Local\ExampleApp.Tests.{Guid.NewGuid():N}"
        : Path.Join(Path.GetTempPath(), $"ExampleApp.Tests.{Guid.NewGuid():N}.lock");

    private readonly List<string> _steps = [];

    [Fact]
    public void Launch_routes_follow_the_session_and_who_started_the_launch()
    {
        var asked = 0;
        Func<bool> InGamescope(bool session) => () =>
        {
            asked++;
            return session;
        };

        Roles.RouteOf(background: true, startedBySteam: false, InGamescope(true)).Should().Be(LaunchRoute.Background);
        Roles.RouteOf(background: true, startedBySteam: true, InGamescope(false)).Should().Be(LaunchRoute.Background);
        Roles.RouteOf(background: false, startedBySteam: true, InGamescope(true)).Should().Be(LaunchRoute.Window,
            "Steam shows the windows of the programs it starts");
        asked.Should().Be(0, "only a launch whose way depends on the session asks logind, which may take a second");
        // A Steam id of 0 is that of a program Steam did not start, such as a link from its browser.
        Roles.RouteOf(background: false, HostEnvironment.IsSteamLaunch("0"), InGamescope(true)).Should().Be(LaunchRoute.HandOver);
        Roles.RouteOf(background: false, startedBySteam: false, InGamescope(false)).Should().Be(LaunchRoute.Window);
        asked.Should().Be(2);
    }

    // Swedish writes the minus of a window dragged past the screen's left edge as one that an invariant reader refuses.
    [Fact]
    public void A_restart_carries_the_agents_version_and_the_windows_place_to_the_window_it_becomes()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
        try
        {
            var restart = new Ui.WindowRestart("1.1.0-beta.2", Placement: new Ui.WindowPlacement(-1280, 40, 1366.5, 768, Maximized: true));
            Environment.SetEnvironmentVariable("MANAGER_WINDOW_RESTARTED_FOR",
                WindowRestarts.EnvironmentFor(restart)["MANAGER_WINDOW_RESTARTED_FOR"]);

            WindowRestarts.Take().Should().Be(restart);
            Environment.GetEnvironmentVariable("MANAGER_WINDOW_RESTARTED_FOR").Should().BeNull("no program the window starts inherits it");
            WindowRestarts.Take().Should().BeNull();
            // Game Mode's screen is the session's, and there only the player's update asks for it.
            WindowRestarts.Parse(WindowRestarts.Format(restart with { Asked = true, Placement = null })).Should()
                .Be(restart with { Asked = true, Placement = null });
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

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
