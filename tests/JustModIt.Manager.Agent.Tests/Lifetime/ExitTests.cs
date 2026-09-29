using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace JustModIt.Manager.Agent.Tests.Lifetime;

public sealed class ExitTests : IAsyncDisposable
{
    private readonly HeldRecoveryEngine _engine = new();
    private readonly AgentHarness _agent;

    public ExitTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");
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

        _agent.Starter.Starts.Should().Be(1);
        var second = await _agent.ConnectWindowAsync();
        await second.StateAsync(state => state.Activity.IsExitPending);
        (await second.NextAsync<Navigate>()).Target.Should().Be(new SettingsTarget());
        _agent.Run.IsCompleted.Should().BeFalse();

        _engine.Release();

        (await second.ShutdownRequests.Reader.ReadAsync(AgentHarness.Token)).Should().Be(ShutdownReason.Exit);
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

    public async ValueTask DisposeAsync() => await _agent.DisposeAsync();

    private async Task ExitAfterJobsAsync(FakeWindow window)
    {
        var lifetime = await _agent.ServiceAsync<AgentLifetime>();
        await window.RequestAsync(new ExitApp(ExitMode.AfterJobs));
        (await window.ShutdownRequests.Reader.ReadAsync(AgentHarness.Token)).Should().Be(ShutdownReason.Exit);
        await Eventually.HoldsAsync(() => lifetime.IsExiting);
    }
}
