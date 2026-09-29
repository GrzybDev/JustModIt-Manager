using JustModIt.Manager.Agent.Notifications;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Agent.Tray;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Agent.Tests.Tray;

public sealed class TrayTests : IAsyncDisposable
{
    private static readonly GameKey Game = new(StoreId.Gog, "1207658924");

    private readonly AgentHarness _agent = new();

    public TrayTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");

    [Fact]
    public async Task The_icon_shows_the_app_and_what_it_does()
    {
        _agent.Start();

        await Tooltip(tooltip => tooltip == "Example App – Everything is up to date");
        _agent.Tray.Menu[0].Text.Should().Be("Open Example App");
    }

    [Fact]
    public async Task Clicking_the_icon_opens_the_window()
    {
        _agent.Start();
        await Tooltip(_ => true);

        _agent.Tray.Click();

        await Eventually(() => _agent.Starter.Starts == 1);
    }

    [Fact]
    public async Task Exit_from_the_tray_stops_an_idle_agent()
    {
        _agent.Start();
        await Tooltip(_ => true);

        _agent.Tray.Pick(TrayCommands.Exit);

        await _agent.Run.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token);
    }

    [Fact]
    public async Task A_notice_goes_to_the_tray_while_no_window_is_in_front_and_its_click_opens_the_game()
    {
        _agent.Start();
        var notifier = await _agent.ServiceAsync<Notifier>();

        notifier.Notify(new AutomationNotice(NoticeKind.Installed) { Game = Game, GameName = "Example Game" });

        var shown = await _agent.Tray.Notifications.Reader.ReadAsync(AgentHarness.Token);
        shown.Should().Be(("Installed for Example Game", "The project is ready. Have fun!", "example-app://game/gog/1207658924"));

        _agent.Tray.ClickNotification(shown.Tag);
        await Eventually(() => _agent.Starter.Starts == 1);
        var window = await _agent.ConnectWindowAsync();
        (await window.NextAsync<Navigate>()).Target.Should().Be(new GameTarget("gog", "1207658924"));
    }

    [Fact]
    public async Task A_notice_goes_to_the_window_while_it_is_in_front()
    {
        _agent.Start();
        var notifier = await _agent.ServiceAsync<Notifier>();
        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => state.IsLoaded);
        window.Connection.Post(new WindowActivity(IsActive: true));
        await window.Connection.RequestAsync(new Ping(), cancellationToken: AgentHarness.Token);

        notifier.Notify(new AutomationNotice(NoticeKind.Updated) { Game = Game, GameName = "Example Game", ReleaseLabel = "v2" });

        (await window.NextAsync<Notify>()).Notice.ReleaseLabel.Should().Be("v2");
        _agent.Tray.Notifications.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task No_notice_reaches_the_tray_when_notifications_are_off()
    {
        await _agent.SaveSettingsAsync(AppSettings.Default with { ShowNotifications = false });
        _agent.Start();
        var notifier = await _agent.ServiceAsync<Notifier>();

        notifier.Notify(new AutomationNotice(NoticeKind.Installed) { Game = Game, GameName = "Example Game" });

        _agent.Tray.Notifications.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Closing_the_window_tells_once_that_the_app_keeps_running()
    {
        await _agent.SaveSettingsAsync(AppSettings.Default with { OnboardingCompleted = true });
        _agent.Start();

        await (await _agent.ConnectWindowAsync()).Connection.DisposeAsync();
        (await _agent.Tray.Notifications.Reader.ReadAsync(AgentHarness.Token)).Title.Should().Be("Example App keeps running");
        await (await _agent.ConnectWindowAsync()).Connection.DisposeAsync();

        await Task.Delay(200, AgentHarness.Token);
        _agent.Tray.Notifications.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Closing_the_window_before_the_setup_is_done_asks_to_finish_it_every_time()
    {
        _agent.Start();

        for (var close = 0; close < 2; close++)
        {
            await (await _agent.ConnectWindowAsync()).Connection.DisposeAsync();
            var shown = await _agent.Tray.Notifications.Reader.ReadAsync(AgentHarness.Token);
            shown.Title.Should().Be("Finish setting up Example App");
            shown.Tag.Should().Be("example-app://open");
        }
    }

    public async ValueTask DisposeAsync() => await _agent.DisposeAsync();

    private static async Task Eventually(Func<bool> condition)
    {
        for (var attempt = 0; !condition(); attempt++)
        {
            attempt.Should().BeLessThan(200, "the condition should hold within ten seconds");
            await Task.Delay(50, AgentHarness.Token);
        }
    }

    private async Task Tooltip(Func<string, bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AgentHarness.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (!condition(await _agent.Tray.Tooltips.Reader.ReadAsync(timeout.Token)))
        {
        }
    }
}
