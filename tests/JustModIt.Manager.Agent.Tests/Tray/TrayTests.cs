using JustModIt.Manager.Agent.Notifications;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Agent.Tray;
using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Agent.Tests.Tray;

public sealed class TrayTests : IAsyncDisposable
{
    private static readonly GameKey Game = new(StoreId.Gog, "1100000001");

    private readonly AgentHarness _agent = new();

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

        await Eventually.HoldsAsync(() => _agent.Shell.WindowStarts == 1);
    }

    [Fact]
    public async Task The_window_hears_how_it_comes_back_and_the_agent_starts_none_where_only_the_stores_library_shows_it()
    {
        _agent.Start();
        var ui = await _agent.ServiceAsync<UiLauncher>();
        await Tooltip(_ => true);

        _agent.Tray.ReturnBy(WindowReturn.StoreLibrary);
        ui.Show();
        _agent.Shell.WindowStarts.Should().Be(0, "the session would not show a window the agent starts");
        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => state.Activity.WindowReturn == WindowReturn.StoreLibrary);

        _agent.Tray.ReturnBy(WindowReturn.AppMenu);
        await window.StateAsync(state => state.Activity.WindowReturn == WindowReturn.AppMenu);
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
        shown.Should().Be(("Installed for Example Game", "The project is ready. Have fun!", "example-app://game/gog/1100000001"));

        _agent.Tray.ClickNotification(shown.Tag);
        await Eventually.HoldsAsync(() => _agent.Shell.WindowStarts == 1);
        var window = await _agent.ConnectWindowAsync();
        (await window.NextAsync<Navigate>()).Target.Should().Be(new GameTarget("gog", "1100000001"));
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

    [Theory]
    [InlineData(WindowReturn.Tray, "It keeps looking after the games. Its icon in the system tray opens the window.")]
    [InlineData(WindowReturn.AppMenu, "It keeps looking after the games. The window opens again from the system's app list.")]
    public async Task Closing_the_window_tells_once_that_the_app_keeps_running_and_how_the_window_comes_back(WindowReturn windowReturn,
        string message)
    {
        await _agent.SaveSettingsAsync(AppSettings.Default with { OnboardingCompleted = true });
        _agent.Tray.ReturnBy(windowReturn);
        _agent.Start();

        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => state.Activity.WindowReturn == windowReturn);
        await window.Connection.DisposeAsync();
        (await _agent.Tray.Notifications.Reader.ReadAsync(AgentHarness.Token)).Should()
            .Be(("Example App keeps running", message, "example-app://open"));
        await (await _agent.ConnectWindowAsync()).Connection.DisposeAsync();

        await Task.Delay(200, AgentHarness.Token);
        _agent.Tray.Notifications.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Closing_the_window_in_a_stores_game_session_tells_nothing_and_leaves_the_notice_to_the_desktop()
    {
        _agent.Tray.ReturnBy(WindowReturn.StoreLibrary);
        _agent.Start();
        var settings = await _agent.ServiceAsync<ISettingsService>();

        await CloseWindowAsync(WindowReturn.StoreLibrary);
        settings.Update(current => current with { OnboardingCompleted = true });
        await CloseWindowAsync(WindowReturn.StoreLibrary);
        _agent.Tray.ReturnBy(WindowReturn.Tray);
        await CloseWindowAsync(WindowReturn.Tray);

        (await _agent.Tray.Notifications.Reader.ReadAsync(AgentHarness.Token)).Should().Be(("Example App keeps running",
            "It keeps looking after the games. Its icon in the system tray opens the window.", "example-app://open"),
            "the game session neither asked to finish the setup nor spent the one-time notice");
    }

    [Fact]
    public async Task A_closed_window_in_a_session_without_notifications_is_told_at_a_close_that_shows_one()
    {
        await _agent.SaveSettingsAsync(AppSettings.Default with { OnboardingCompleted = true });
        _agent.Tray.ShowsNotifications = false;
        _agent.Start();
        var settings = await _agent.ServiceAsync<ISettingsService>();

        await (await _agent.ConnectWindowAsync()).Connection.DisposeAsync();
        (await _agent.Tray.Notifications.Reader.ReadAsync(AgentHarness.Token)).Title.Should().Be("Example App keeps running");
        settings.Current.BackgroundNoticeShown.Should().BeFalse("no notification showed it");
        _agent.Tray.ShowsNotifications = true;
        await (await _agent.ConnectWindowAsync()).Connection.DisposeAsync();

        (await _agent.Tray.Notifications.Reader.ReadAsync(AgentHarness.Token)).Title.Should().Be("Example App keeps running");
        await Eventually.HoldsAsync(() => settings.Current.BackgroundNoticeShown);
    }

    [Fact]
    public async Task Closing_the_window_before_the_setup_is_done_asks_to_finish_it_every_time_by_a_click_where_clicks_reach_the_app()
    {
        _agent.Start();

        foreach (var (clickable, message) in new[]
        {
            (true, "Until then, the app changes no game. Clicking the notification opens the setup."),
            (false, "Until then, the app changes no game."),
        })
        {
            _agent.Tray.IsClickable = clickable;
            await (await _agent.ConnectWindowAsync()).Connection.DisposeAsync();
            (await _agent.Tray.Notifications.Reader.ReadAsync(AgentHarness.Token)).Should()
                .Be(("Finish setting up Example App", message, "example-app://open"));
        }
    }

    public async ValueTask DisposeAsync() => await _agent.DisposeAsync();

    private async Task CloseWindowAsync(WindowReturn windowReturn)
    {
        var ui = await _agent.ServiceAsync<UiLauncher>();
        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => state.Activity.WindowReturn == windowReturn);
        await window.Connection.DisposeAsync();
        await Eventually.HoldsAsync(() => ui.Window is null);
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
