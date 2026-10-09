using System.Collections.Concurrent;
using System.Collections.Immutable;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Downloads;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Core.Tests.TestSupport;

/// <summary>The stores' games as a test sets them; a fresh look returns what <see cref="Probes"/> say, or the snapshot.</summary>
internal sealed class FakeLibrary : ILibraryService
{
    private LibrarySnapshot _snapshot = LibrarySnapshot.Empty;
    private int _probes;

    public event EventHandler<LibraryChangedEventArgs>? Changed;

    public LibrarySnapshot Snapshot => Volatile.Read(ref _snapshot);

    /// <summary>What a fresh look at a game finds, when it differs from the snapshot.</summary>
    public ConcurrentDictionary<GameKey, GameProbe> Probes { get; } = new();

    public int ProbeCount => Volatile.Read(ref _probes);

    public int CheckRequests { get; private set; }

    /// <summary>Replaces the snapshot and raises <see cref="Changed"/>, as a scan does.</summary>
    /// <param name="games">The games the stores report.</param>
    public void Publish(params InstalledGame[] games) => Publish(games, cached: false);

    /// <summary>Replaces the snapshot and raises <see cref="Changed"/>, as a scan or loading the saved snapshot does.</summary>
    /// <param name="games">The games the stores report.</param>
    /// <param name="cached">Whether the snapshot stands for the saved one rather than a fresh scan.</param>
    /// <param name="stores">The stores' state; none for Steam with one online library.</param>
    public void Publish(IEnumerable<InstalledGame> games, bool cached, params StoreStatus[] stores)
    {
        var snapshot = new LibrarySnapshot
        {
            Version = Snapshot.Version + 1,
            Games = new ValueDictionary<GameKey, InstalledGame>(games.ToImmutableDictionary(game => game.Key)),
            Stores = stores.Length > 0 ? [.. stores] : [new StoreStatus(StoreId.Steam, true, false, true,
                [new LibraryLocation("library-1", "steam-library", "library", LibraryState.Online)])],
            ScannedAt = TestProjects.Now,
            IsCached = cached,
        };
        Volatile.Write(ref _snapshot, snapshot);
        Changed?.Invoke(this, new LibraryChangedEventArgs(snapshot));
    }

    public void RaiseChanged() => Changed?.Invoke(this, new LibraryChangedEventArgs(Snapshot));

    public void Runs(GameKey game, params string[] processes)
    {
        var installed = Snapshot.Find(game) ?? TestProjects.Installed(game);
        Probes[game] = new GameProbe(game, installed with { State = GameInstallState.Running, Reason = GameStateReason.GameRunning },
            [.. processes.Select((name, index) => new RunningProcess(4000 + index, name, Path.Join(installed.InstallDirectory, name + ".exe")))]);
    }

    /// <summary>Makes a fresh look at the game find it as given, whatever the snapshot says.</summary>
    /// <param name="game">The game as the store's records now describe it.</param>
    public void Describe(InstalledGame game) => Probes[game.Key] = new GameProbe(game.Key, game, []);

    public Task<GameProbe> ProbeAsync(GameKey key, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _probes);
        return Task.FromResult(Probes.TryGetValue(key, out var probe) ? probe : new GameProbe(key, Snapshot.Find(key), []));
    }

    public void RequestCheck() => CheckRequests++;

    public Task CheckNowAsync(CancellationToken cancellationToken)
    {
        CheckRequests++;
        return Task.CompletedTask;
    }

    public Task<LibraryFolderResult> AddLibraryFolderAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(LibraryFolderResult.Rejected(LibraryFolderRejection.Unreachable));

    public void RemoveLibraryFolder(LibraryFolder folder)
    {
    }
}

internal sealed class FakeRunningGames : IRunningGameDetector
{
    private TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConcurrentDictionary<string, ValueList<RunningProcess>> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int Waits { get; private set; }

    public ValueList<RunningProcess> FindProcessesIn(string directory, CancellationToken cancellationToken) =>
        Running.TryGetValue(directory, out var processes) ? processes : [];

    public Task WaitForExitAsync(IEnumerable<RunningProcess> processes, CancellationToken cancellationToken)
    {
        Waits++;
        return _exit.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Everything waited on exits.</summary>
    public void ExitAll()
    {
        Running.Clear();
        Interlocked.Exchange(ref _exit, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }
}

/// <summary>Project pages kept in memory; each read is counted, and a test may make them fail or wait.</summary>
internal sealed class FakeCatalogue : ICatalogue
{
    private CatalogueSnapshot _current = CatalogueSnapshot.Empty;

    public event EventHandler<CatalogueSnapshot>? Changed;

    public CatalogueSnapshot Current => Volatile.Read(ref _current);

    public ConcurrentDictionary<Guid, ProjectPage> Pages { get; } = new();

    public ConcurrentQueue<ProjectLocator> PageRequests { get; } = new();

    public ConcurrentQueue<IndexRequest> Rebuilds { get; } = new();

    public Exception? PageFailure { get; set; }

    public TaskCompletionSource? PageGate { get; set; }

    public CatalogueSnapshot? Cached { get; set; }

    public void Add(ProjectPage page) => Pages[page.Uuid] = page;

    /// <summary>Publishes an index of the given projects, as a refresh does.</summary>
    /// <param name="state">The catalogue's state, such as fresh or stale.</param>
    /// <param name="entries">The index's projects.</param>
    public void Publish(CatalogueState state, params IndexEntry[] entries)
    {
        var snapshot = new CatalogueSnapshot
        {
            State = state,
            Index = new InstallableIndex(TestProjects.Now, entries),
            FetchedAt = TestProjects.Now,
        };
        Volatile.Write(ref _current, snapshot);
        Changed?.Invoke(this, snapshot);
    }

    public Task<CatalogueSnapshot> LoadCachedAsync(CancellationToken cancellationToken)
    {
        if (Cached is { } cached)
        {
            Volatile.Write(ref _current, cached);
            Changed?.Invoke(this, cached);
        }

        return Task.FromResult(Current);
    }

    public Task<CatalogueSnapshot> RebuildIndexAsync(IndexRequest request, CancellationToken cancellationToken)
    {
        Rebuilds.Enqueue(request);
        return Task.FromResult(Current);
    }

    public async Task<CataloguePage?> GetProjectPageAsync(ProjectLocator project, PageFreshness freshness, CancellationToken cancellationToken)
    {
        PageRequests.Enqueue(project);
        if (PageGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (PageFailure is { } failure)
        {
            throw failure;
        }

        return Pages.TryGetValue(project.ProjectUuid, out var page) ? new CataloguePage(new ProjectPageDocument(page, default), TestProjects.Now) : null;
    }
}

/// <summary>Downloads that take no time unless a test holds them; every package is a file name in a folder that need not exist.</summary>
internal sealed class FakeDownloader : IPackageDownloader
{
    public ConcurrentQueue<Guid> Downloads { get; } = new();

    public ConcurrentDictionary<Guid, bool> Cached { get; } = new();

    public ConcurrentQueue<IReadOnlySet<Guid>> Prunes { get; } = new();

    public ConcurrentDictionary<Guid, TaskCompletionSource> Gates { get; } = new();

    public Exception? Failure { get; set; }

    public string Folder { get; set; } = Path.Join(Path.GetTempPath(), "ExampleApp.Tests", "downloads");

    public string PathOf(Guid file, PackageFormat format = PackageFormat.Zip) =>
        Path.Join(Folder, file.ToString("D") + (format == PackageFormat.Manifest ? ".examplemod" : ".zip"));

    public async Task<DownloadedPackage> DownloadAsync(PackageRequest request, IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (await FindCachedAsync(request, cancellationToken) is { } cached)
        {
            return cached;
        }

        Downloads.Enqueue(request.Locator.FileUuid);
        progress?.Report(new DownloadProgress(0, 1024));
        if (Gates.TryGetValue(request.Locator.FileUuid, out var gate))
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is { } failure)
        {
            throw failure;
        }

        progress?.Report(new DownloadProgress(1024, 1024));
        Cached[request.Locator.FileUuid] = true;
        return new DownloadedPackage(PathOf(request.Locator.FileUuid, request.Format), 1024, "", null, request.Format, FromCache: false);
    }

    public Task<DownloadedPackage?> FindCachedAsync(PackageRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Cached.ContainsKey(request.Locator.FileUuid)
            ? new DownloadedPackage(PathOf(request.Locator.FileUuid, request.Format), 1024, "", null, request.Format, FromCache: true)
            : null);

    public Task<int> PruneAsync(IReadOnlySet<Guid> keep, CancellationToken cancellationToken)
    {
        Prunes.Enqueue(keep);
        var removed = 0;
        foreach (var file in Cached.Keys.Where(file => !keep.Contains(file)))
        {
            removed += Cached.TryRemove(file, out _) ? 1 : 0;
        }

        return Task.FromResult(removed);
    }
}

/// <summary>
/// Installs as records in memory, with every call logged and steps a test replaces to make them fail, wait or report progress.
/// </summary>
internal sealed class FakeInstallEngine : IInstallEngine
{
    private readonly Lock _gate = new();
    private readonly List<string> _calls = [];
    private int _verifications;

    public ConcurrentDictionary<Guid, InstallRecord> Records { get; } = new();

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public ConcurrentQueue<InstallRequest> Installs { get; } = new();

    public TaskCompletionSource<IReadOnlyList<RecoveredOperation>>? Recovery { get; set; }

    /// <summary>Replaces the next installs, one each, before the default one applies.</summary>
    public ConcurrentQueue<Func<InstallRequest, IProgress<InstallProgress>?, CancellationToken, Task<InstallResult>>> NextInstalls { get; } = new();

    public Func<Guid, CancellationToken, Task<UninstallResult>>? OnUninstall { get; set; }

    public PreflightReport Prediction { get; set; } = new([], []);

    public IReadOnlyList<GameManagedByOtherAppIssue> OtherApps { get; set; } = [];

    public Func<Guid, VerificationResult>? OnVerify { get; set; }

    public Func<Guid, InstalledGame, InstallRecord?>? OnAdopt { get; set; }

    /// <summary>What forgetting an install finds; <see cref="ForgetOutcome.Kept"/> stands for a folder that is there.</summary>
    public ForgetOutcome Forgetting { get; set; }

    /// <summary>Installs that keep originals of replaced files.</summary>
    public ConcurrentDictionary<Guid, bool> Backups { get; } = new();

    public TaskCompletionSource? VerifyGate { get; set; }

    public int Verifications => Volatile.Read(ref _verifications);

    public InstallRecord Add(InstallRecord record)
    {
        Records[record.Id] = record;
        return record;
    }

    public Task<IReadOnlyList<RecoveredOperation>> RecoverAsync(CancellationToken cancellationToken)
    {
        Log("recover");
        return Recovery?.Task.WaitAsync(cancellationToken) ?? Task.FromResult<IReadOnlyList<RecoveredOperation>>([]);
    }

    public Task<IReadOnlyList<GameManagedByOtherAppIssue>> FindOtherAppsAsync(string gameDirectory, CancellationToken cancellationToken) =>
        Task.FromResult(OtherApps);

    public Task<PreflightReport> CheckInstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) => Task.FromResult(new PreflightReport([], []));

    public Task<InstallResult> InstallAsync(InstallRequest request, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Log("install " + Path.GetFileName(request.PackagePath));
        Installs.Enqueue(request);
        if (NextInstalls.TryDequeue(out var install))
        {
            return install(request, progress, cancellationToken);
        }

        progress?.Report(new InstallProgress(InstallStage.Checking, null, 0, 1, 0, 0));
        progress?.Report(new InstallProgress(InstallStage.Installing, null, 0, 1, 0, 0));
        return InstallNowAsync(request);
    }

    public Task<InstallResult> InstallNowAsync(InstallRequest request)
    {
        var record = new InstallRecord
        {
            Id = Guid.NewGuid(),
            Game = request.Game.Key,
            GameName = request.Game.Name,
            InstallDirectory = request.Game.InstallDirectory,
            InstalledAt = TestProjects.Now,
            Package = new InstalledPackage(PackageKind.Zip, Path.GetFileName(request.PackagePath)),
            Source = request.Source,
            Backup = new BackupFolder(request.Backup.Location, Path.Join(request.Game.InstallDirectory, ".example-app", "backups")),
            FileCount = 2,
            IsTest = request.IsTest,
        };
        Records[record.Id] = record;
        return Task.FromResult(new InstallResult(record, []));
    }

    public Task<PreflightReport> PredictReplacementAsync(Guid recordId, InstallRequest replacement, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        Log("predict");
        progress?.Report(new InstallProgress(InstallStage.Checking, null, 0, 1, 0, 0));
        return Task.FromResult(Prediction);
    }

    public Task<UninstallResult> UninstallAsync(Guid recordId, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        Log("uninstall");
        if (OnUninstall is { } uninstall)
        {
            return uninstall(recordId, cancellationToken);
        }

        progress?.Report(new InstallProgress(InstallStage.Checking, null, 0, 1, 0, 0));
        progress?.Report(new InstallProgress(InstallStage.Uninstalling, null, 0, 1, 0, 0));

        Records.TryRemove(recordId, out _);
        return Task.FromResult(new UninstallResult([]));
    }

    public async Task<VerificationResult> VerifyAsync(Guid recordId, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        Log("verify");
        Interlocked.Increment(ref _verifications);
        if (VerifyGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        return OnVerify?.Invoke(recordId) ?? new VerificationResult([]);
    }

    public Task<InstallRecord> MoveBackupsAsync(Guid recordId, BackupChoice destination, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        Log("move backups");
        var moved = Records[recordId] with { Backup = new BackupFolder(destination.Location, Path.Join("moved", recordId.ToString("N"))) };
        Records[recordId] = moved;
        return Task.FromResult(moved);
    }

    public Task<InstallRecord?> AdoptMovedAsync(Guid recordId, InstalledGame game, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        Log("adopt");
        var adopted = OnAdopt is { } adopt ? adopt(recordId, game) : Records[recordId] with { InstallDirectory = game.InstallDirectory };
        if (adopted is not null)
        {
            Records[recordId] = adopted;
        }

        return Task.FromResult(adopted);
    }

    public Task<ForgetOutcome> ForgetIfGoneAsync(Guid recordId, bool keepBackups, CancellationToken cancellationToken)
    {
        Log("forget if gone");
        if (Forgetting != ForgetOutcome.Kept)
        {
            Records.TryRemove(recordId, out _);
        }

        return Task.FromResult(Forgetting);
    }

    public Task<IReadOnlyList<InstallRecord>> LoadRecordsAsync(CancellationToken cancellationToken)
    {
        Log("load records");
        return Task.FromResult<IReadOnlyList<InstallRecord>>([.. Records.Values]);
    }

    public Task<InstalledFiles> LoadFilesAsync(Guid recordId, CancellationToken cancellationToken) =>
        Task.FromResult(new InstalledFiles
        {
            Files = Backups.ContainsKey(recordId)
                ? [new InstalledFile("data/strings.pak", FileChange.Replaced, Backup: "strings.pak.original")]
                : [new InstalledFile("data/new.pak", FileChange.Added)],
            CreatedDirectories = [],
        });

    public Task<IReadOnlyList<LeftoverBackup>> FindLeftoverBackupsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<bool> RemoveLeftoverBackupsAsync(Guid recordId, CancellationToken cancellationToken) => throw new NotSupportedException();

    private void Log(string call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }
    }
}

/// <summary>An app's rule that a game holds one project, so another one takes its place.</summary>
internal sealed class OneProjectPolicy : IInstallPolicy
{
    public InstallAdmission Admit(IReadOnlyList<InstallRecord> installs) =>
        installs is [var installed] ? InstallAdmission.Replacing(installed) : InstallAdmission.Alongside;
}

/// <summary>Settings in memory, changed at once and announced on the caller's thread.</summary>
/// <param name="settings">The settings to start with; null for the defaults with the first-run setup finished.</param>
internal sealed class FakeSettings(AppSettings? settings = null) : ISettingsService
{
    private readonly Lock _gate = new();
    private AppSettings _current = settings ?? AppSettings.Default with { OnboardingCompleted = true };

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public event EventHandler? SaveFailed
    {
        add { }
        remove { }
    }

    public AppSettings Current => Volatile.Read(ref _current);

    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings previous;
        AppSettings updated;
        lock (_gate)
        {
            previous = _current;
            updated = change(previous);
            if (updated == previous)
            {
                return previous;
            }

            Volatile.Write(ref _current, updated);
        }

        Changed?.Invoke(this, new SettingsChangedEventArgs(previous, updated));
        return updated;
    }

    public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
