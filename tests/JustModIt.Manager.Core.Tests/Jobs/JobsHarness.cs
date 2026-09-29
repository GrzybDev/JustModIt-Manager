using System.Collections.Concurrent;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Jobs;

/// <summary>A job runner over fakes: one installed, idle game whose project offers release one.</summary>
internal sealed class JobsHarness : IAsyncDisposable
{
    private JobsHarness()
    {
        Runner = new JobRunner(Engine, Catalogue, Downloader, Library, RunningGames, Settings, Time, NullLogger<JobRunner>.Instance);
        Runner.Changed += (_, job) => Changes.Enqueue(job);
        Runner.Progressed += (_, job) => Progress.Enqueue(job);
        Runner.Finished += (_, result) => Results.Enqueue(result);
        Library.Publish(TestProjects.Installed());
        Catalogue.Add(TestProjects.Page(release: TestProjects.Release(TestProjects.ReleaseOne, 1, TestProjects.File(TestProjects.FileOne),
            TestProjects.File(TestProjects.VoiceFile, "Text and voice"))));
    }

    public CountingTimeProvider Time { get; } = new(TestProjects.Now);

    public FakeLibrary Library { get; } = new();

    public FakeRunningGames RunningGames { get; } = new();

    public FakeCatalogue Catalogue { get; } = new();

    public FakeDownloader Downloader { get; } = new();

    public FakeInstallEngine Engine { get; } = new();

    public FakeSettings Settings { get; } = new();

    public JobRunner Runner { get; }

    public ConcurrentQueue<JobSnapshot> Changes { get; } = new();

    public ConcurrentQueue<JobSnapshot> Progress { get; } = new();

    public ConcurrentQueue<JobResult> Results { get; } = new();

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public static async Task<JobsHarness> CreateAsync(bool recover = true)
    {
        var harness = new JobsHarness();
        if (recover)
        {
            await harness.Runner.RecoverAsync(Token);
        }

        return harness;
    }

    public static InstallJobRequest Install(GameKey? game = null, string? variant = null, JobOrigin origin = JobOrigin.User) =>
        new(game ?? TestProjects.Game, "Example Game", TestProjects.Locator(), variant, origin);

    /// <summary>Publishes release two of the project.</summary>
    /// <param name="files">The release's files; none for a text-only and a text-and-voice file.</param>
    public void PublishReleaseTwo(params ReleaseFile[] files) =>
        Catalogue.Add(TestProjects.Page(release: TestProjects.Release(TestProjects.ReleaseTwo, 2, files.Length > 0 ? files
            : [TestProjects.File(TestProjects.FileTwo), TestProjects.File(TestProjects.VoiceFile, "Text and voice")])));

    /// <summary>An install of release one, as a job before this one left it, with its package kept.</summary>
    /// <param name="variant">The name of the installed variant.</param>
    public InstallRecord Installed(string variant = "Text only")
    {
        Downloader.Cached[TestProjects.FileOne] = true;
        return Engine.Add(TestProjects.Record(source: TestProjects.Source(TestProjects.ReleaseOne, TestProjects.FileOne, 1, variant)));
    }

    public async Task<JobResult> RunAsync(JobRequest request) =>
        await Runner.Start(request).Completion.WaitAsync(TimeSpan.FromSeconds(10), Token);

    public IReadOnlyList<JobStage> Stages(Guid job) => [.. Changes.Where(change => change.Id == job).Select(change => change.Stage)];

    public JobSnapshot? LastChange(Guid job) => Changes.LastOrDefault(change => change.Id == job);

    public Task WaitsAsync(Guid job, JobWaitReason reason) =>
        Eventually.HoldsAsync(() => LastChange(job) is { Stage: JobStage.Waiting } waiting && waiting.WaitReason == reason,
            $"the job waits for {reason}");

    public ValueTask DisposeAsync() => Runner.DisposeAsync();
}
