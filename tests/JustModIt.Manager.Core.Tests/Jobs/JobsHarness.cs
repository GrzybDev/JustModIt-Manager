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
    private readonly TempDirectory _markers = new();

    private JobsHarness(IInstallPolicy? policy, IInstallEngine? engine)
    {
        Markers = new ReplacementMarkers(_markers.Path, Time, NullLogger<ReplacementMarkers>.Instance);
        Runner = new JobRunner(engine ?? Engine, Catalogue, Downloader, Library, RunningGames, Settings, Markers, Time,
            NullLogger<JobRunner>.Instance, policy);
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

    public ReplacementMarkers Markers { get; }

    /// <summary>The markers of the replacements under way, as files in their folder.</summary>
    public IReadOnlyList<string> MarkerFiles => [.. _markers.Files()];

    public JobRunner Runner { get; }

    public ConcurrentQueue<JobSnapshot> Changes { get; } = new();

    public ConcurrentQueue<JobSnapshot> Progress { get; } = new();

    public ConcurrentQueue<JobResult> Results { get; } = new();

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Creates the harness, by default with crash recovery done, which every job waits for.</summary>
    /// <param name="recover">False to leave crash recovery to the test, so jobs wait for it.</param>
    /// <param name="policy">The app's install policy; null for the platform's, which lets projects share a game.</param>
    /// <param name="engine">The engine jobs run on; null for <see cref="Engine"/>, which keeps installs in memory.</param>
    public static async Task<JobsHarness> CreateAsync(bool recover = true, IInstallPolicy? policy = null, IInstallEngine? engine = null)
    {
        var harness = new JobsHarness(policy, engine);
        if (recover)
        {
            await harness.Runner.RecoverAsync(Token);
        }

        return harness;
    }

    public static InstallJobRequest Install(GameKey? game = null, JobOrigin origin = JobOrigin.User) =>
        new(game ?? TestProjects.Game, "Example Game", TestProjects.Locator(), origin);

    /// <summary>Chooses one of the project's variants, or turns its add-ons on or off, as the player does on the game's page.</summary>
    /// <param name="choices">Whether each add-on or variant is on, by key.</param>
    public void Choose(params (string Key, bool On)[] choices) =>
        Settings.Update(settings => settings.WithGame(TestProjects.Game, preferences =>
            preferences.WithFeatures(TestProjects.Project, choices.ToDictionary(choice => choice.Key, choice => choice.On))));

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
        await Runner.Start(request).Completion.WaitAsync(TimeSpan.FromSeconds(30), Token);

    public IReadOnlyList<JobStage> Stages(Guid job) => [.. Changes.Where(change => change.Id == job).Select(change => change.Stage)];

    public JobSnapshot? LastChange(Guid job) => Changes.LastOrDefault(change => change.Id == job);

    public Task WaitsAsync(Guid job, JobWaitReason reason) =>
        Eventually.HoldsAsync(() => LastChange(job) is { Stage: JobStage.Waiting } waiting && waiting.WaitReason == reason,
            $"the job waits for {reason}");

    public async ValueTask DisposeAsync()
    {
        await Runner.DisposeAsync();
        _markers.Dispose();
    }
}
