using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.State;

/// <summary>A state coordinator and a job runner over fakes, started once the test has set the scene.</summary>
internal sealed class CoordinatorHarness : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task _run = Task.CompletedTask;

    /// <param name="settings">The first-run setup is not finished unless the settings say so, which keeps the automation still.</param>
    /// <param name="history">The automation's history, shared by harnesses that stand for two runs of the app.</param>
    public CoordinatorHarness(AppSettings? settings = null, TargetLanguage? language = null,
        InMemoryDocumentStore<AutomationHistory>? history = null)
    {
        Settings = new FakeSettings(settings ?? AppSettings.Default);
        History = history ?? new InMemoryDocumentStore<AutomationHistory>();
        Jobs = new JobRunner(Engine, Catalogue, Downloader, Library, RunningGames, Settings, Time, NullLogger<JobRunner>.Instance);
        Coordinator = new StateCoordinator(Library, Catalogue, Settings, Engine, Jobs, RunningGames, Failures, History,
            new StateCoordinatorOptions { TargetLanguage = language ?? TargetLanguage.None }, Time, NullLogger<StateCoordinator>.Instance);
    }

    public FakeTimeProvider Time { get; } = new(Now);

    public FakeLibrary Library { get; } = new();

    public FakeRunningGames RunningGames { get; } = new();

    public FakeCatalogue Catalogue { get; } = new();

    public FakeDownloader Downloader { get; } = new();

    public FakeInstallEngine Engine { get; } = new();

    public FakeSettings Settings { get; }

    public InMemoryDocumentStore<FailedAttempts> Failures { get; } = new();

    public InMemoryDocumentStore<AutomationHistory> History { get; }

    public JobRunner Jobs { get; }

    public StateCoordinator Coordinator { get; }

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The installed game with release one of its project on offer, the catalogue read and a page for jobs.</summary>
    public CoordinatorHarness WithGameAndProject()
    {
        Library.Publish(Installed());
        var release = Release(ReleaseOne, 1, File(FileOne), File(VoiceFile, "Text and voice"));
        Catalogue.Publish(CatalogueState.Fresh, Entry(release: release));
        Catalogue.Add(Page(release: release));
        return this;
    }

    /// <summary>Publishes release two of the project, which replaces release one on the site.</summary>
    public void PublishReleaseTwo()
    {
        var release = Release(ReleaseTwo, 2, File(FileTwo), File(VoiceFile, "Text and voice"));
        Catalogue.Publish(CatalogueState.Fresh, Entry(release: release));
        Catalogue.Add(Page(release: release));
    }

    public async Task<AppState> StartAsync()
    {
        _run = Coordinator.RunAsync(_stop.Token);
        return await StateAsync(state => state.IsLoaded, "the first state is published");
    }

    /// <summary>Waits up to ten seconds for a published state that satisfies a condition.</summary>
    /// <param name="condition">Checked against the current state and each one published after it.</param>
    /// <param name="because">What the test waits for, as the timeout's message says it.</param>
    public async Task<AppState> StateAsync(Func<AppState, bool> condition, string because)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var state = Coordinator.Current;
        try
        {
            while (!condition(state))
            {
                state = await Coordinator.WaitForChangeAsync(state.Version, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!Token.IsCancellationRequested)
        {
            if (_run.IsFaulted)
            {
                throw new InvalidOperationException("The coordinator failed.", _run.Exception);
            }

            throw new TimeoutException($"Timed out waiting until {because}; the last state was {state.Version}.");
        }

        return state;
    }

    /// <summary>Waits up to ten seconds for a published state in which a game satisfies a condition.</summary>
    /// <param name="condition">Checked against the game in each state that lists it.</param>
    /// <param name="because">What the test waits for, as the timeout's message says it.</param>
    /// <param name="game">The game; null for <see cref="TestProjects.Game"/>.</param>
    public Task<AppState> GameAsync(Func<MyGame, bool> condition, string because, GameKey? game = null) =>
        StateAsync(state => state.Find(game ?? Game) is { } found && condition(found), because);

    public async Task<JobResult> RunJobAsync(JobRequest request) =>
        await Jobs.Start(request).Completion.WaitAsync(TimeSpan.FromSeconds(10), Token);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _run;
        }
        catch (OperationCanceledException)
        {
        }

        await Coordinator.DisposeAsync();
        await Jobs.DisposeAsync();
        _stop.Dispose();
    }
}
