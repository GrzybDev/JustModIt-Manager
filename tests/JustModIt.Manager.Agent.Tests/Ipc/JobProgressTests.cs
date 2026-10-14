using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace JustModIt.Manager.Agent.Tests.Ipc;

public sealed class JobProgressTests : IAsyncDisposable
{
    private static readonly GameKey Game = new(StoreId.Steam, "1000");

    private readonly ClosingEngine _engine = new();
    private readonly AgentHarness _agent;

    public JobProgressTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The agent runs only on Windows so far.");
        var folder = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", "Example Game");
        _agent = new AgentHarness
        {
            Configure = services =>
            {
                services.AddSingleton<IInstallEngine>(_engine);
                services.AddSingleton<ILibraryProvider>(new OneGameLibrary(Game, folder));
            },
        };
    }

    [Fact]
    public async Task A_report_made_just_before_a_job_ended_never_reaches_the_window_after_the_state_that_ended_it()
    {
        _agent.Start();
        var window = await _agent.ConnectWindowAsync();
        await window.StateAsync(state => state.IsLoaded);
        var job = (await _agent.ServiceAsync<JobRunner>()).Start(new TestInstallJobRequest(Game, "Example Game",
            [Path.Combine(_agent.Directory, "example.zip")]));

        await window.NextAsync<JobProgress>(progress => progress.Jobs.Any(report => report.Id == job.Job.Id));
        _engine.Finish.TrySetResult();
        await window.StateAsync(state => state.Jobs.Count == 0);
        // The publisher sends reports a tenth of a second apart, so the last one would have come by now.
        await Task.Delay(TimeSpan.FromMilliseconds(300), AgentHarness.Token);

        List<Message> after = [];
        while (window.Events.Reader.TryRead(out var message))
        {
            after.Add(message);
        }

        after.OfType<JobProgress>().Should().BeEmpty();
        (await job.Completion).Failure!.Reason.Should().Be(JobFailureReason.DiskError);
    }

    public ValueTask DisposeAsync() => _agent.DisposeAsync();

    private sealed class ClosingEngine : StubEngine
    {
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<InstallResult> InstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress!.Report(new InstallProgress(InstallStage.Installing, "data.pak", 1, 4, 1, 4));
            // A job publishes a report only a tenth of a second after its last one.
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
            progress.Report(new InstallProgress(InstallStage.Installing, "data.pak", 2, 4, 2, 4));
            await Finish.Task.WaitAsync(cancellationToken);
            progress.Report(new InstallProgress(InstallStage.Installing, "data.pak", 4, 4, 4, 4));
            throw new IOException("The disk failed.");
        }
    }
}
