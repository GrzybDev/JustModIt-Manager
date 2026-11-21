using JustModIt.Manager.Platform.Linux.Tests.TestSupport;

namespace JustModIt.Manager.Platform.Linux.Tests;

public sealed class DisplaySessionTests
{
    // Every look here gets an answer, so the limit only keeps a hang from holding the run; with the other test projects running
    // beside this one, the thread pool may take seconds to carry logind's answers.
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_display_sessions_desktop_tells_a_gamescope_session()
    {
        await using var bus = await PrivateBus.StartAsync(Token);
        var system = await bus.ConnectAsync();

        DisplaySession.IsGamescope(system, Limit).Should().BeFalse("a system without logind tells no session");
        var logind = await FakeLogind.StartAsync(bus, ("2", "KDE"), ("3", "gamescope"));
        DisplaySession.IsGamescope(system, Limit).Should().BeFalse();
        logind.SwitchTo("3");
        DisplaySession.IsGamescope(system, Limit).Should().BeTrue();
    }
}
