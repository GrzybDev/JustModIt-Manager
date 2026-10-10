using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Agent.Tray;
using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Agent.Updates;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using Microsoft.Extensions.DependencyInjection;

namespace JustModIt.Manager.Agent.Tests.Updates;

public sealed class UpdateServiceTests : IAsyncDisposable
{
    private static readonly UpdateSchedule Soon = new(_ => TimeSpan.FromMilliseconds(10), TimeSpan.FromHours(4), TimeSpan.FromHours(1));

    private readonly FakeUpdater _updater;
    private readonly AgentHarness _agent;

    public UpdateServiceTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");
        _updater = new FakeUpdater();
        _agent = Harness(_updater, Soon);
    }

    private static TimeSpan Timeout => TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_copy_that_cannot_update_itself_never_looks()
    {
        await using var agent = Harness(new FakeUpdater { IsSupported = false }, Soon);
        agent.Start();
        var window = await agent.ConnectWindowAsync();

        (await window.StateAsync(state => state.IsLoaded)).Update.State.Should().Be(AppUpdateState.Unavailable);
    }

    [Fact]
    public async Task The_newest_version_needs_nothing()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();

        var state = await window.StateAsync(state => state.Update.State == AppUpdateState.UpToDate);

        state.Update.CheckedAt.Should().NotBeNull();
        _updater.Applied.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task An_update_is_applied_quietly_when_no_window_is_open_and_the_app_comes_back_in_the_tray()
    {
        _updater.Newest = "1.1.0";
        _agent.Start();

        (await _updater.Applied.Task.WaitAsync(Timeout, AgentHarness.Token)).Should().Equal("--background");
        await _agent.Run.WaitAsync(Timeout, AgentHarness.Token);
    }

    [Fact]
    public async Task An_update_waits_for_the_window_to_close()
    {
        await using var agent = Harness(_updater, Soon with { FirstCheck = _ => System.Threading.Timeout.InfiniteTimeSpan });
        _updater.Newest = "1.1.0";
        agent.Start();
        var window = await agent.ConnectWindowAsync();
        await window.RequestAsync(new CheckNow());

        var ready = await window.StateAsync(state => state.Update.State == AppUpdateState.Ready);

        ready.Update.Version.Should().Be("1.1.0");
        await Eventually.HoldsAsync(() => agent.Tray.Menu.Any(item => item.Command == TrayCommands.RestartToUpdate));
        await Task.Delay(200, AgentHarness.Token);
        _updater.Applied.Task.IsCompleted.Should().BeFalse();

        await window.Connection.DisposeAsync();
        (await _updater.Applied.Task.WaitAsync(Timeout, AgentHarness.Token)).Should().Equal("--background");
    }

    [Fact]
    public async Task An_update_waits_for_running_jobs()
    {
        var engine = new HeldRecoveryEngine();
        await using var agent = Harness(_updater, Soon with { FirstCheck = _ => System.Threading.Timeout.InfiniteTimeSpan }, engine);
        _updater.Newest = "1.1.0";
        agent.Start();
        await HeldRecoveryEngine.StartJobAsync(agent);
        await Eventually.HoldsAsync(() => agent.Tray.Tooltip is not null);

        agent.Tray.Pick(TrayCommands.CheckNow);

        await Eventually.HoldsAsync(() => agent.Tray.Menu.Any(item => item.Command == TrayCommands.RestartToUpdate));
        await Task.Delay(200, AgentHarness.Token);
        _updater.Applied.Task.IsCompleted.Should().BeFalse();
        engine.Release();
        (await _updater.Applied.Task.WaitAsync(Timeout, AgentHarness.Token)).Should().Equal("--background");
    }

    [Fact]
    public async Task An_update_waits_while_a_window_is_on_its_way()
    {
        await using var agent = Harness(_updater, Soon with { FirstCheck = _ => System.Threading.Timeout.InfiniteTimeSpan });
        _updater.Newest = "1.1.0";
        agent.Start();
        await agent.ServiceAsync<UiLauncher>();
        agent.Tray.Click();
        await Eventually.HoldsAsync(() => agent.Starter.Starts == 1);

        agent.Tray.Pick(TrayCommands.CheckNow);

        await Eventually.HoldsAsync(() => agent.Tray.Menu.Any(item => item.Command == TrayCommands.RestartToUpdate));
        await Task.Delay(200, AgentHarness.Token);
        _updater.Applied.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Without_automatic_updates_a_new_version_is_only_announced_until_the_player_asks()
    {
        await _agent.SaveSettingsAsync(AppSettings.Default with { AutoUpdateApp = false });
        _updater.Newest = "1.1.0";
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();

        (await window.StateAsync(state => state.Update.State == AppUpdateState.Available)).Update.Version.Should().Be("1.1.0");

        await window.RequestAsync(new CheckNow());
        await window.StateAsync(state => state.Update.State == AppUpdateState.Ready);
        await window.Connection.DisposeAsync();
        await Task.Delay(200, AgentHarness.Token);
        _updater.Applied.Task.IsCompleted.Should().BeFalse("the player decides when to restart");
    }

    [Fact]
    public async Task A_check_the_player_asks_for_during_a_scheduled_one_still_downloads()
    {
        var updater = new FakeUpdater { Newest = "1.1.0", SlowFirstCheck = new TaskCompletionSource() };
        await using var agent = Harness(updater, Soon);
        await agent.SaveSettingsAsync(AppSettings.Default with { AutoUpdateApp = false });
        agent.Start();
        var window = await agent.ConnectWindowAsync();
        await updater.FirstCheckStarted.Task.WaitAsync(Timeout, AgentHarness.Token);

        await window.RequestAsync(new CheckNow());
        updater.SlowFirstCheck.SetResult();

        (await window.StateAsync(state => state.Update.State == AppUpdateState.Ready)).Update.Version.Should().Be("1.1.0");
    }

    [Fact]
    public async Task Restarting_from_the_window_closes_it_first_and_brings_it_back_after_the_update()
    {
        await _agent.SaveSettingsAsync(AppSettings.Default with { AutoUpdateApp = false });
        _updater.Newest = "1.1.0";
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        await window.RequestAsync(new CheckNow());
        await window.StateAsync(state => state.Update.State == AppUpdateState.Ready);

        await window.RequestAsync(new ApplyAppUpdate());

        (await window.ShutdownRequests.Reader.ReadAsync(AgentHarness.Token)).Should().Be(ShutdownReason.Update);
        (await _updater.Applied.Task.WaitAsync(Timeout, AgentHarness.Token)).Should().BeEmpty();
        await _agent.Run.WaitAsync(Timeout, AgentHarness.Token);
    }

    [Fact]
    public async Task A_restart_the_player_asks_for_while_jobs_run_waits_for_them()
    {
        var engine = new HeldRecoveryEngine();
        await using var agent = Harness(_updater, Soon with { FirstCheck = _ => System.Threading.Timeout.InfiniteTimeSpan }, engine);
        _updater.Newest = "1.1.0";
        agent.Start();
        var window = await agent.ConnectWindowAsync();
        await HeldRecoveryEngine.StartJobAsync(agent);
        await window.RequestAsync(new CheckNow());
        await window.StateAsync(state => state.Update.State == AppUpdateState.Ready);

        await window.RequestAsync(new ApplyAppUpdate());

        await Task.Delay(200, AgentHarness.Token);
        window.ShutdownRequests.Reader.TryRead(out _).Should().BeFalse();
        engine.Release();
        (await window.ShutdownRequests.Reader.ReadAsync(AgentHarness.Token)).Should().Be(ShutdownReason.Update);
        (await _updater.Applied.Task.WaitAsync(Timeout, AgentHarness.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_window_with_unsaved_work_keeps_the_update_waiting()
    {
        await _agent.SaveSettingsAsync(AppSettings.Default with { AutoUpdateApp = false });
        _updater.Newest = "1.1.0";
        _agent.Start();
        var window = await _agent.ConnectWindowAsync(acceptsShutdown: false);
        await window.RequestAsync(new CheckNow());
        await window.StateAsync(state => state.Update.State == AppUpdateState.Ready);

        await window.RequestAsync(new ApplyAppUpdate());

        (await window.ShutdownRequests.Reader.ReadAsync(AgentHarness.Token)).Should().Be(ShutdownReason.Update);
        await Task.Delay(200, AgentHarness.Token);
        _updater.Applied.Task.IsCompleted.Should().BeFalse();
        _agent.Run.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Restarting_is_refused_while_no_update_is_ready()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => state.Update.State == AppUpdateState.UpToDate);

        var restart = () => window.RequestAsync(new ApplyAppUpdate());

        (await restart.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.InvalidRequest);
    }

    [Fact]
    public async Task A_failed_check_is_reported_and_the_app_keeps_running()
    {
        _updater.CheckFails = true;
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();

        await window.StateAsync(state => state.Update.State == AppUpdateState.Failed);

        _updater.CheckFails = false;
        await window.RequestAsync(new CheckNow());
        await window.StateAsync(state => state.Update.State == AppUpdateState.UpToDate);
    }

    [Fact]
    public async Task An_update_downloaded_earlier_is_applied_before_anything_starts_with_the_same_command_line()
    {
        _updater.PendingVersion = "1.1.0";

        _agent.Start(new LaunchArguments(ProcessRole.Launcher, Background: false, new SettingsTarget()));

        await _agent.Run.WaitAsync(Timeout, AgentHarness.Token);
        (await _updater.Applied.Task).Should().Equal("example-app://settings");
        _agent.Starter.Starts.Should().Be(0);
    }

    [Fact]
    public async Task An_update_the_updater_failed_to_apply_waits_for_the_player_instead_of_restarting_again()
    {
        _updater.PendingVersion = "1.1.0";
        _agent.Options = _agent.Options with { RestartedByUpdater = true };
        _agent.Start();

        await Eventually.HoldsAsync(() => _agent.Tray.Menu.Any(item => item.Command == TrayCommands.RestartToUpdate));
        await Task.Delay(200, AgentHarness.Token);
        _updater.Applied.Task.IsCompleted.Should().BeFalse();
        _updater.Checks.Should().Be(0, "an update downloaded earlier is ready without looking again");

        _agent.Tray.Pick(TrayCommands.RestartToUpdate);
        (await _updater.Applied.Task.WaitAsync(Timeout, AgentHarness.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task Downloads_that_cannot_be_read_do_not_keep_the_app_from_starting()
    {
        await using var agent = Harness(new FakeUpdater { PendingUnreadable = true }, Soon);
        agent.Start();
        var window = await agent.ConnectWindowAsync();

        await window.StateAsync(state => state.IsLoaded && state.Update.State == AppUpdateState.UpToDate);
    }

    public async ValueTask DisposeAsync() => await _agent.DisposeAsync();

    private static AgentHarness Harness(FakeUpdater updater, UpdateSchedule schedule, IInstallEngine? engine = null) => new()
    {
        Configure = services =>
        {
            services.AddSingleton<IAppUpdater>(updater);
            services.AddSingleton(schedule);
            if (engine is not null)
            {
                services.AddSingleton(engine);
            }
        },
    };
}
