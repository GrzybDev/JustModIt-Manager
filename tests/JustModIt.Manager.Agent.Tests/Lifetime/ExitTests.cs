using JustModIt.Manager.Agent.Ipc;
using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Agent.Tray;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace JustModIt.Manager.Agent.Tests.Lifetime;

public sealed class ExitTests : IAsyncDisposable
{
    private readonly HeldRecoveryEngine _engine = new();
    private readonly AgentHarness _agent;

    public ExitTests()
    {
        _agent = new AgentHarness { Configure = services => services.AddSingleton<IInstallEngine>(_engine) };
    }

    [Fact]
    public async Task A_launch_while_the_exit_waits_for_jobs_opens_the_window_which_closes_again_when_they_are_done()
    {
        _agent.Start();
        var first = await _agent.ConnectWindowAsync();
        await HeldRecoveryEngine.StartJobAsync(_agent);
        await ExitAfterJobsAsync(first);

        var launcher = await _agent.ConnectLauncherAsync();
        await launcher.RequestAsync(new Activate(["example-app://settings"]), cancellationToken: AgentHarness.Token);

        _agent.Shell.WindowStarts.Should().Be(1);
        var second = await _agent.ConnectWindowAsync();
        await second.StateAsync(state => state.Activity.IsExitPending);
        (await second.NextAsync<Navigate>()).Target.Should().Be(new SettingsTarget());
        _agent.Run.IsCompleted.Should().BeFalse();

        _engine.Release();

        (await second.ShutdownRequests.Reader.ReadAsync(AgentHarness.Token)).Should().Be(ShutdownReason.Exit);
        await _agent.Run.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token);
    }

    [Fact]
    public async Task An_exit_from_the_apps_menu_entry_waits_for_the_jobs_as_the_trays_exit_does_and_opens_no_window()
    {
        _agent.Start();
        await HeldRecoveryEngine.StartJobAsync(_agent);

        var launcher = await _agent.ConnectLauncherAsync();
        await launcher.RequestAsync(new Activate([LaunchArguments.ExitSwitch]), cancellationToken: AgentHarness.Token);

        (await _agent.ServiceAsync<AgentLifetime>()).IsExiting.Should().BeTrue();
        _agent.Shell.WindowStarts.Should().Be(0);
        _agent.Run.IsCompleted.Should().BeFalse("the exit waits for the running job");
        _engine.Release();
        await _agent.Run.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token);
    }

    [Fact]
    public async Task Exiting_now_cuts_short_an_exit_that_waits_for_jobs()
    {
        _agent.Start();
        var first = await _agent.ConnectWindowAsync();
        var job = await HeldRecoveryEngine.StartJobAsync(_agent);
        await ExitAfterJobsAsync(first);
        var second = await _agent.ConnectWindowAsync();

        await second.RequestAsync(new ExitApp(ExitMode.Now));

        (await job.Completion.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token)).Outcome.Should().Be(JobOutcome.Cancelled);
        (await second.ShutdownRequests.Reader.ReadAsync(AgentHarness.Token)).Should().Be(ShutdownReason.Exit);
        await _agent.Run.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token);
    }

    [Fact]
    public async Task The_tray_exits_after_the_jobs_and_its_exit_now_cuts_the_wait_short()
    {
        _agent.Start();
        var job = await HeldRecoveryEngine.StartJobAsync(_agent);
        await Eventually.HoldsAsync(() => _agent.Tray.Tooltip is not null);

        _agent.Tray.Pick(TrayCommands.Exit);

        await Eventually.HoldsAsync(() => TrayExits().SequenceEqual([TrayCommands.ExitNow]));
        job.Completion.IsCompleted.Should().BeFalse();
        _agent.Run.IsCompleted.Should().BeFalse();

        _agent.Tray.Pick(TrayCommands.ExitNow);

        (await job.Completion.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token)).Outcome.Should().Be(JobOutcome.Cancelled);
        await _agent.Run.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token);
    }

    [Fact]
    public async Task Exiting_at_once_cancels_the_jobs_and_does_not_ask_the_window()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync(acceptsShutdown: false);
        var job = await HeldRecoveryEngine.StartJobAsync(_agent);

        (await _agent.ServiceAsync<AgentLifetime>()).ExitAtOnce();

        (await job.Completion.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token)).Outcome.Should().Be(JobOutcome.Cancelled);
        await _agent.Run.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token);
        window.ShutdownRequests.Reader.TryRead(out _).Should().BeFalse("a window that keeps unsaved work cannot hold up the system");
    }

    public async ValueTask DisposeAsync() => await _agent.DisposeAsync();

    private IEnumerable<string> TrayExits() =>
        _agent.Tray.Menu.Select(item => item.Command).Where(command => command is TrayCommands.Exit or TrayCommands.ExitNow);

    // The agent publishes its state on a loop of its own, so a window that connects right after the exit began may first get a
    // state from before it, and waiting for the next one would skip the page the window was sent meanwhile.
    private async Task ExitAfterJobsAsync(FakeWindow window)
    {
        var publisher = await _agent.ServiceAsync<StatePublisher>();
        await window.RequestAsync(new ExitApp(ExitMode.AfterJobs));
        (await window.ShutdownRequests.Reader.ReadAsync(AgentHarness.Token)).Should().Be(ShutdownReason.Exit);
        await Eventually.HoldsAsync(() => publisher.Latest.Activity.IsExitPending);
    }
}
