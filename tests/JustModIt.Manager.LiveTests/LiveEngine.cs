using JustModIt.Manager.Agent;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Downloads;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Host;
using JustModIt.Manager.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.LiveTests;

/// <summary>The release the site offers a game now: its project and the package of the recommended variant.</summary>
/// <param name="Project">The project that offers the release.</param>
/// <param name="Locator">What the download endpoint needs to name the package.</param>
/// <param name="File">The package as the release lists it.</param>
internal sealed record OfferedRelease(ProjectLocator Project, ReleaseFileLocator Locator, ReleaseFile File);

/// <summary>
/// The engine composed as the app composes it, over a data folder of its own, undoing whatever it installed when disposed
/// or, after a stopped run, at the next start, and keeping downloads across runs, since the site counts every one.
/// </summary>
internal sealed partial class LiveEngine : IAsyncDisposable
{
    // Long enough for a job to download, install and check a real release.
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(1);

    private readonly ServiceProvider _services;
    private readonly ILoggerFactory _loggers;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _loops = [];

    private LiveEngine(ServiceProvider services, AppDataLocations locations, SettingsService settings, ILoggerFactory loggers)
    {
        _services = services;
        _loggers = loggers;
        Locations = locations;
        Settings = settings;
        Library = services.GetRequiredService<LibraryService>();
        Catalogue = services.GetRequiredService<CatalogueService>();
        Downloads = services.GetRequiredService<CountingDownloader>();
        Engine = services.GetRequiredService<IInstallEngine>();
        Jobs = services.GetRequiredService<JobRunner>();
        var jobLogger = loggers.CreateLogger<LiveEngine>();
        Jobs.Changed += (_, job) =>
            LogJob(jobLogger, job.Kind, job.Game, job.Stage, job.WaitReason, string.Join(", ", job.RunningProcesses));
    }

    public SettingsService Settings { get; }

    public LibraryService Library { get; }

    public CatalogueService Catalogue { get; }

    public CountingDownloader Downloads { get; }

    public IInstallEngine Engine { get; }

    public JobRunner Jobs { get; }

    public AppDataLocations Locations { get; }

    /// <summary>Composes the engine over the data folder named <paramref name="name"/> and scans the stores once.</summary>
    /// <param name="name">Names the data folder; each test class uses the same one in every run.</param>
    /// <param name="cancellationToken">Stops the scan and the recovery.</param>
    public static async Task<LiveEngine> StartAsync(string name, CancellationToken cancellationToken)
    {
        var data = Path.Combine(LiveApp.TestFolder, name);
        // The test classes share the cache, so the downloads, which the site counts, are kept across them and across runs.
        var locations = new AppDataLocations(data, LiveApp.TestFolder, Path.Combine(data, "logs"));
        if (Directory.Exists(data))
        {
            // A run that stopped half-way may have left a game changed; its journal and records say how to undo that.
            await (await ComposeAsync(locations, cancellationToken)).DisposeAsync();
        }

        return await ComposeAsync(locations, cancellationToken);
    }

    /// <summary>Starts the state coordinator and the catalogue's refresh loop, as the agent runs them.</summary>
    public StateCoordinator StartCoordinator()
    {
        var coordinator = _services.GetRequiredService<StateCoordinator>();
        _loops.Add(coordinator.RunAsync(_stop.Token));
        _loops.Add(_services.GetRequiredService<CatalogueRefreshLoop>().RunAsync(TimeSpan.Zero, _stop.Token));
        return coordinator;
    }

    /// <summary>The game as its store describes it now; skips the test unless it is installed and idle.</summary>
    /// <param name="key">A game from the local live-games file.</param>
    /// <param name="cancellationToken">Stops the probe.</param>
    public async Task<InstalledGame> ReadyGameAsync(GameKey key, CancellationToken cancellationToken)
    {
        var probe = await Library.ProbeAsync(key, cancellationToken);
        Assert.SkipWhen(probe.Game is null, $"{key} is not installed on this machine.");
        var running = string.Join(", ", probe.RunningProcesses.Select(process => process.Name));
        Assert.SkipUnless(probe.AllowsChange(forUninstall: false) && probe.RunningProcesses.Count == 0,
            $"{probe.Game!.Name} is not idle: {probe.Game.State} ({probe.Game.Reason}), running [{running}].");
        return probe.Game;
    }

    /// <summary>What the site offers the game now, as the install job will choose it.</summary>
    /// <param name="game">The game as <see cref="ReadyGameAsync"/> found it.</param>
    /// <param name="cancellationToken">Stops the catalogue refresh.</param>
    public async Task<OfferedRelease> FindReleaseAsync(InstalledGame game, CancellationToken cancellationToken)
    {
        var snapshot = await Catalogue.RefreshAsync(new IndexRequest([game.Key], []), cancellationToken);
        var offer = GameOffer.For(snapshot.Index, game.Key, game.Platform, LiveApp.Language).Projects.Should().ContainSingle().Subject;
        var release = offer.Selection.Release!;
        var file = offer.Selection.Plan(FeatureChoices.None)!.Parts[0].File;
        var locator = new ReleaseFileLocator(offer.Project.Team.Slug, offer.Project.ProjectSlug, release.Uuid, file.Uuid);
        return new OfferedRelease(offer.Project.Locator, locator, file);
    }

    /// <summary>The release's package, from the kept downloads when an earlier run downloaded it.</summary>
    /// <param name="release">The release <see cref="FindReleaseAsync"/> found.</param>
    /// <param name="cancellationToken">Stops the download.</param>
    public Task<DownloadedPackage> DownloadAsync(OfferedRelease release, CancellationToken cancellationToken) =>
        Downloads.DownloadAsync(PackageRequest.For(release.Locator, release.File), progress: null, cancellationToken);

    /// <summary>Starts a job and waits up to ten minutes for its result.</summary>
    /// <param name="request">The job, run through the job runner as the app runs it.</param>
    /// <param name="cancellationToken">Stops the wait, not the job.</param>
    public Task<JobResult> RunAsync(JobRequest request, CancellationToken cancellationToken) =>
        Jobs.Start(request).Completion.WaitAsync(JobTimeout, cancellationToken);

    /// <summary>The installs the engine's records list, as the package creator is told of them.</summary>
    /// <param name="cancellationToken">Stops reading the records.</param>
    public async Task<IReadOnlyList<ExistingInstall>> ExistingInstallsAsync(CancellationToken cancellationToken) =>
        [.. (await Engine.LoadRecordsAsync(cancellationToken)).Select(record => new ExistingInstall(record.Id, record.InstallDirectory))];

    public async ValueTask DisposeAsync()
    {
        var restored = false;
        try
        {
            await RestoreGamesAsync(CancellationToken.None);
            restored = true;
        }
        finally
        {
            await _stop.CancelAsync();
            foreach (var loop in _loops)
            {
                await loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            await _services.DisposeAsync();
            await Settings.DisposeAsync();
            _stop.Dispose();
            _loggers.Dispose();
            // A data folder whose installs could not be undone stays, so the next start tries again.
            if (restored)
            {
                Directory.Delete(Locations.DataDirectory, recursive: true);
            }
        }
    }

    private static async Task<LiveEngine> ComposeAsync(AppDataLocations locations, CancellationToken cancellationToken)
    {
        var loggers = new TestOutputLoggers(TestContext.Current.TestOutputHelper);
        var settings = await SettingsService.LoadAsync(locations, TimeProvider.System, loggers, cancellationToken);
        var options = LiveApp.ThisProcess().AgentOptions() with { Locations = locations };
        var services = new ServiceCollection().AddAgent(options, settings, loggers);
        AgentPlatform.Compose(services, options, app: null, configure: null);
        services.TryAddSingleton<ISystemEvents>(QuietSystemEvents.Instance);
        var downloader = services.Single(service => service.ServiceType == typeof(IPackageDownloader)).ImplementationFactory!;
        services.AddSingleton(provider => new CountingDownloader((IPackageDownloader)downloader(provider)));
        services.AddSingleton<IPackageDownloader>(provider => provider.GetRequiredService<CountingDownloader>());
        var engine = new LiveEngine(services.BuildServiceProvider(), locations, settings, loggers);
        engine._loops.Add(engine.Library.RunAsync(engine._stop.Token));
        try
        {
            await engine.Library.CheckNowAsync(cancellationToken).WaitAsync(ScanTimeout, cancellationToken);
            // Every job waits until crash recovery has run, as in the app.
            await engine.Jobs.RecoverAsync(cancellationToken);
            return engine;
        }
        catch
        {
            await engine.DisposeAsync();
            throw;
        }
    }

    private async Task RestoreGamesAsync(CancellationToken cancellationToken)
    {
        var stuck = (await Jobs.RecoverAsync(cancellationToken))
            .Where(operation => operation.Outcome is RecoveryOutcome.Failed or RecoveryOutcome.Unavailable)
            .ToList();
        if (stuck.Count > 0)
        {
            throw new InvalidOperationException($"Interrupted operations could not be undone: {string.Join(", ", stuck)}.");
        }

        foreach (var record in await Engine.LoadRecordsAsync(cancellationToken))
        {
            var result = await RunAsync(new UninstallJobRequest(record), cancellationToken);
            if (!result.IsSuccess || result.KeptFiles.Count > 0)
            {
                throw new InvalidOperationException($"The install on {record.GameName} could not be undone: {result.Outcome} "
                    + $"{result.Failure}, kept [{string.Join(", ", result.KeptFiles)}].");
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} of {Game}: {Stage} {WaitReason} {Processes}")]
    private static partial void LogJob(ILogger logger, JobKind kind, GameKey game, JobStage stage, JobWaitReason? waitReason,
        string processes);
}
