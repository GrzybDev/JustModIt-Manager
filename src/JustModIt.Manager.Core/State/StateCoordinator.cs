using System.Collections.Immutable;
using System.Threading.Channels;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.State;

public sealed record StateCoordinatorOptions
{
    /// <summary>The language the app's projects bring to games; <see cref="TargetLanguage.None"/> skips the rule it drives.</summary>
    public TargetLanguage TargetLanguage { get; init; } = TargetLanguage.None;
}

/// <summary>
/// The single owner of what the app knows, whose one loop applies changes in batches, publishes an immutable
/// <see cref="AppState"/> after each and lets the automation act on it, so no handler runs under a lock and no caller
/// waits for I/O.
/// </summary>
public sealed partial class StateCoordinator : IAsyncDisposable
{
    private readonly ILibraryService _library;
    private readonly ICatalogue _catalogue;
    private readonly ISettingsService _settings;
    private readonly IInstallEngine _engine;
    private readonly JobRunner _jobs;
    private readonly IRunningGameDetector _runningGames;
    private readonly IDocumentStore<FailedAttempts> _failureStore;
    private readonly LatestDocumentWriter<FailedAttempts> _failureWriter;
    private readonly IDocumentStore<AutomationHistory> _historyStore;
    private readonly AutomationDriver _automation;
    private readonly Channel<AutomationNotice> _notices = Channel.CreateUnbounded<AutomationNotice>(new UnboundedChannelOptions
    {
        SingleWriter = true,
    });

    private readonly StateCoordinatorOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<StateCoordinator> _logger;
    private readonly Channel<Trigger> _triggers = Channel.CreateUnbounded<Trigger>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Lock _publishGate = new();
    private readonly LibraryAccess _backupChecks;

    // Only the loop touches these, so they need no lock.
    private readonly Dictionary<Guid, InstallRecord> _records = [];
    private readonly Dictionary<GameKey, JobSnapshot> _activeJobs = [];
    private readonly InstallVerifications _verifications = new();
    private readonly MovedInstalls _moved = new();
    private readonly Dictionary<Guid, bool> _hasBackups = [];
    private readonly Dictionary<GameKey, ValueList<RunningProcess>> _running = [];
    private readonly Dictionary<Guid, BackupChoice> _backupMoves = [];
    private LibrarySnapshot _snapshot = LibrarySnapshot.Empty;
    private CatalogueSnapshot _catalogueSnapshot = CatalogueSnapshot.Empty;
    private AppSettings _current = AppSettings.Default;
    private ImmutableHashSet<Guid> _unavailableBackups = [];
    private ImmutableDictionary<FailureKey, FailedAttempt> _failures = ImmutableDictionary<FailureKey, FailedAttempt>.Empty;
    private IReadOnlyList<RecoveredOperation> _recoveries = [];
    private bool _checkingBackups;
    private bool _backupsChanged = true;
    private bool _pruneWanted;
    private CancellationToken _stopping;

    private AppState _state = AppState.Loading;
    private TaskCompletionSource<AppState> _next = NewSignal();
    private IndexRequest _indexRequest = new([], []);
    private IndexRequest? _indexed;

    /// <summary>Creates the coordinator, which reads nothing before <see cref="RunAsync"/>.</summary>
    /// <param name="library">The detected games.</param>
    /// <param name="catalogue">The site's catalogue and the index of the player's games.</param>
    /// <param name="settings">The player's settings.</param>
    /// <param name="engine">Reads, checks and forgets install records.</param>
    /// <param name="jobs">Runs the jobs and recovers what a crash interrupted.</param>
    /// <param name="runningGames">Tells when a game that held a job back exits.</param>
    /// <param name="failures">Where failed jobs are kept across restarts.</param>
    /// <param name="automationHistory">Where the automation's history is kept.</param>
    /// <param name="options">The app's target language.</param>
    /// <param name="time">The clock for checks and retries.</param>
    /// <param name="logger">Logs background work that failed.</param>
    public StateCoordinator(ILibraryService library, ICatalogue catalogue, ISettingsService settings, IInstallEngine engine,
        JobRunner jobs, IRunningGameDetector runningGames, IDocumentStore<FailedAttempts> failures,
        IDocumentStore<AutomationHistory> automationHistory, StateCoordinatorOptions options, TimeProvider time,
        ILogger<StateCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(runningGames);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(automationHistory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _library = library;
        _catalogue = catalogue;
        _settings = settings;
        _engine = engine;
        _jobs = jobs;
        _runningGames = runningGames;
        _failureStore = failures;
        _failureWriter = new LatestDocumentWriter<FailedAttempts>(failures, "list of failed jobs", logger);
        _historyStore = automationHistory;
        _automation = new AutomationDriver(jobs, settings, automationHistory, time, logger);
        _options = options;
        _time = time;
        _logger = logger;
        _backupChecks = new LibraryAccess(time);
    }

    public AppState Current
    {
        get
        {
            lock (_publishGate)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// What the automation did or found, for the agent to tell the player, kept until read so none is lost while the
    /// window is closed.
    /// </summary>
    public ChannelReader<AutomationNotice> Notices => _notices.Reader;

    /// <summary>
    /// The latest state once it is newer than <paramref name="afterVersion"/>, so a consumer slower than the changes skips
    /// the states in between and never falls behind.
    /// </summary>
    /// <param name="afterVersion">The <see cref="AppState.Version"/> of the state the caller has.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    public Task<AppState> WaitForChangeAsync(long afterVersion, CancellationToken cancellationToken)
    {
        lock (_publishGate)
        {
            return _state.Version > afterVersion ? Task.FromResult(_state) : _next.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The games and installs for the catalogue's next refresh to index; games found after it are indexed from the saved
    /// catalogue without reading the whole site again.
    /// </summary>
    public IndexRequest IndexRequestForRefresh()
    {
        var request = Volatile.Read(ref _indexRequest);
        Volatile.Write(ref _indexed, request);
        return request;
    }

    /// <summary>Checks the game's installs now, e.g. because the player opened the game's page.</summary>
    /// <param name="game">The game whose installs to check.</param>
    public void RequestVerification(GameKey game) => Post(new VerificationRequested(game));

    /// <summary>The player saw the failure of an install, or with no install, of the game, and wants it gone.</summary>
    /// <param name="game">The game the failure shows on.</param>
    /// <param name="install">The install's record id; null for a failure of the game itself.</param>
    public void DismissFailure(GameKey game, Guid? install) => Post(new FailureDismissed(new FailureKey(game, install)));

    /// <summary>
    /// Moves every install's backups to where the settings now keep them, one job per install, each once its game has no
    /// other job.
    /// </summary>
    /// <param name="destination">Where the settings now keep backups.</param>
    public void MoveBackups(BackupChoice destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Post(new BackupsMoveRequested(destination));
    }

    /// <summary>Reads the local data, publishes the first state from it, then keeps the state up to date until cancelled.</summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _stopping = cancellationToken;
        Subscribe();
        try
        {
            await LoadAsync(cancellationToken).ConfigureAwait(false);
            Publish();
            while (true)
            {
                if (Schedule())
                {
                    Publish();
                }

                foreach (var trigger in await NextBatchAsync(cancellationToken).ConfigureAwait(false))
                {
                    Apply(trigger);
                }

                Publish();
            }
        }
        finally
        {
            Unsubscribe();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Unsubscribe();
        _notices.Writer.TryComplete();
        await _failureWriter.DisposeAsync().ConfigureAwait(false);
        await _automation.DisposeAsync().ConfigureAwait(false);
    }

    private static TaskCompletionSource<AppState> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static bool Covers(IndexRequest indexed, IndexRequest wanted) =>
        wanted.Games.All(indexed.Games.Contains)
        && wanted.InstalledProjects.All(project => indexed.InstalledProjects.Any(known => known.ProjectUuid == project.ProjectUuid));

    // Everything read here is local: no network and no hashing stand before the first state.
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        _current = _settings.Current;
        _snapshot = _library.Snapshot;
        _catalogueSnapshot = await _catalogue.LoadCachedAsync(cancellationToken).ConfigureAwait(false);
        // The records are trusted only once what a crash interrupted has been finished or undone.
        _recoveries = await _jobs.RecoverAsync(cancellationToken).ConfigureAwait(false);
        foreach (var record in await _engine.LoadRecordsAsync(cancellationToken).ConfigureAwait(false))
        {
            _records[record.Id] = record;
        }

        _failures = (await _failureStore.LoadAsync(cancellationToken).ConfigureAwait(false))?.Failures.ToImmutableDictionary()
            ?? ImmutableDictionary<FailureKey, FailedAttempt>.Empty;
        _automation.Load(await _historyStore.LoadAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<List<Trigger>> NextBatchAsync(CancellationToken cancellationToken)
    {
        var reader = _triggers.Reader;
        if (!reader.TryRead(out var first))
        {
            await _time.WaitForSignalAsync(token => reader.WaitToReadAsync(token).AsTask(), NextDue() - _time.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            if (!reader.TryRead(out first))
            {
                first = new Tick();
            }
        }

        var batch = new List<Trigger> { first };
        while (reader.TryRead(out var more))
        {
            batch.Add(more);
        }

        return batch;
    }

    private void Apply(Trigger trigger)
    {
        switch (trigger)
        {
            case LibraryChanged changed when changed.Snapshot.Version >= _snapshot.Version:
                _moved.OnLibraryChanged();
                _snapshot = changed.Snapshot;
                _verifications.OnLibraryChanged(_records.Values, _snapshot);
                _automation.OnLibraryChanged();
                _backupsChanged = true;
                break;
            case CatalogueChanged changed:
                _catalogueSnapshot = changed.Snapshot;
                break;
            // The service's own settings are never older than a change reported to the loop; one the loop made itself may
            // be newer than a report still queued.
            case SettingsChanged:
                _current = _settings.Current;
                break;
            case JobChanged changed:
                if (!_activeJobs.TryGetValue(changed.Job.Game, out var known) || known.Id != changed.Job.Id
                    || known.Sequence < changed.Job.Sequence)
                {
                    _activeJobs[changed.Job.Game] = changed.Job;
                }

                break;
            case JobFinished finished:
                ApplyResult(finished.Result);
                break;
            case Verified verified:
                _verifications.OnChecked(verified.RecordId, verified.BuildId, verified.Verification, _records.ContainsKey(verified.RecordId),
                    _time.GetUtcNow());
                break;
            // The player looking at a game is also when a drive holding its originals may have come back, or its old
            // folder may have gone.
            case VerificationRequested requested:
                List<Guid> installs = [.. _records.Values.Where(record => record.Game == requested.Game).Select(record => record.Id)];
                _verifications.Request(installs);
                _moved.Recheck(installs);
                _backupsChanged = true;
                break;
            case BackupsMoveRequested move:
                _backupMoves.Clear();
                foreach (var record in _records.Values.Where(record => record.Backup.Location != move.Destination.Location
                    || move.Destination.Location == BackupLocation.CustomFolder))
                {
                    _backupMoves[record.Id] = move.Destination;
                }

                break;
            case GoneChecked gone:
                ApplyGoneCheck(gone);
                break;
            case BackupsChecked checkedBackups:
                _checkingBackups = false;
                _unavailableBackups = checkedBackups.Unavailable;
                foreach (var (record, hasBackups) in checkedBackups.HasBackups)
                {
                    _hasBackups[record] = hasBackups;
                }

                break;
            case FailureDismissed dismissed:
                SetFailure(dismissed.Key, null);
                break;
            case GameExited exited:
                _running.Remove(exited.Game);
                _moved.RetryDeferred();
                break;
        }
    }

    private void ApplyResult(JobResult result)
    {
        var game = result.Job.Game;
        foreach (var notice in _automation.OnFinished(result, Current.Find(game)))
        {
            _notices.Writer.TryWrite(notice);
        }

        // What the automation learned from the player applies to its very next look.
        _current = _settings.Current;
        if (_activeJobs.TryGetValue(game, out var job) && job.Id == result.Job.Id)
        {
            _activeJobs.Remove(game);
        }

        if (result.RemovedRecord is { } removed)
        {
            _records.Remove(removed);
            _verifications.Forget(removed);
        }

        if (result.Installed is { } installed)
        {
            _records[installed.Id] = installed;
            _verifications.Forget(installed.Id);
        }

        // A job may also have failed on a backup folder that is gone.
        _backupsChanged = true;

        // Following a moved game is the app's own housekeeping, not something the player tried.
        if (result.Job is { Kind: JobKind.FollowMove, InstallId: { } followed })
        {
            _moved.OnFollowEnded(followed, result);
        }
        else if (FailedAttempt.From(result, _time.GetUtcNow()) is { } failed)
        {
            SetFailure(FailedAttempt.KeyOf(result), failed);
        }
        // What failed on the game as a whole, such as a lost release, is over once any job on it works.
        else if (result.IsSuccess)
        {
            SetFailure(new FailureKey(game, null), null);
            if (result.Job.InstallId is { } install)
            {
                SetFailure(new FailureKey(game, install), null);
            }
        }

        if (result is { Outcome: JobOutcome.Deferred, DeferredBy: JobWaitReason.GameRunning, RunningProcesses.Count: > 0 })
        {
            _running[game] = result.RunningProcesses;
            _ = WatchExitAsync(game, result.RunningProcesses);
        }

        _pruneWanted = true;
    }

    private void ApplyGoneCheck(GoneChecked gone)
    {
        _moved.OnGoneChecked();
        var game = Current.Find(gone.Game);
        foreach (var (record, outcome) in gone.Outcomes)
        {
            if (outcome == ForgetOutcome.Kept || !_records.Remove(record.Id))
            {
                continue;
            }

            _verifications.Forget(record.Id);
            _hasBackups.Remove(record.Id);
            // A hidden game is left alone; its leftover backups still show in the settings.
            if (!_current.GetGame(gone.Game).IsHidden)
            {
                _notices.Writer.TryWrite(new AutomationNotice(NoticeKind.Forgotten)
                {
                    Game = gone.Game,
                    GameName = game?.Name ?? record.GameName,
                    ProjectName = AutomationNotice.ProjectNameFor(game, record.Id),
                    KeptBackups = outcome == ForgetOutcome.ForgottenKeepingBackups,
                });
            }
        }
    }

    private void SetFailure(FailureKey key, FailedAttempt? failure)
    {
        var updated = failure is null ? _failures.Remove(key) : _failures.SetItem(key, failure);
        if (updated != _failures)
        {
            _failures = updated;
            _failureWriter.Save(new FailedAttempts(new ValueDictionary<FailureKey, FailedAttempt>(_failures)));
        }
    }

    private void Publish()
    {
        var games = MyGamesComposer.Compose(new MyGamesInput
        {
            Library = _snapshot,
            Records = [.. _records.Values],
            Index = _catalogueSnapshot.Index,
            Settings = _current,
            Language = _options.TargetLanguage,
            InstallPolicy = _jobs.InstallPolicy,
            Jobs = _activeJobs.ToImmutableDictionary(),
            Repairs = _verifications.Repairs(),
            UnavailableBackups = _unavailableBackups,
            Failures = _failures,
            Running = _running.ToImmutableDictionary(),
        });

        // A failure of an install that is gone, or of a game that left the list, has no card left to show it on. Until a
        // scan and the catalogue are in, a game missing from the list may only be missing from what was read so far.
        var settled = _snapshot is { ScannedAt: not null, IsCached: false } && _catalogueSnapshot.FetchedAt is not null;
        foreach (var key in _failures.Keys.Where(key => key.Install is { } install
            ? !_records.ContainsKey(install)
            : settled && !games.Any(listed => listed.Key == key.Game)).ToList())
        {
            SetFailure(key, null);
        }

        TaskCompletionSource<AppState> next;
        AppState state;
        lock (_publishGate)
        {
            state = new AppState
            {
                Version = _state.Version + 1,
                IsLoaded = true,
                Games = [.. games],
                Jobs = [.. _activeJobs.Values],
                Stores = _snapshot.Stores,
                IsLibraryCached = _snapshot.IsCached,
                LibraryScannedAt = _snapshot.ScannedAt,
                CatalogueState = _catalogueSnapshot.State,
                CatalogueFetchedAt = _catalogueSnapshot.FetchedAt,
                CatalogueFailure = _catalogueSnapshot.Failure,
                PendingRecoveries = [.. _recoveries.Where(operation => operation.Outcome is RecoveryOutcome.Failed or RecoveryOutcome.Unavailable)],
            };
            _state = state;
            next = _next;
            _next = NewSignal();
        }

        Volatile.Write(ref _indexRequest, new IndexRequest(
            [.. _snapshot.Games.Keys],
            [.. _records.Values.Select(record => InstallSources.ProjectOf(record.Source)).OfType<ProjectLocator>()
                .Select(_catalogueSnapshot.Index.Locate).Distinct()]));
        next.TrySetResult(state);
    }

    // True when a job was started, which changes the state.
    private bool Schedule()
    {
        var changed = FollowMovedGames();
        changed |= MoveBackupsOfFreeGames();
        changed |= Automate();
        StartVerification();
        CheckBackups();
        ForgetGoneInstalls();
        IndexNewGames();
        PruneDownloads();
        return changed;
    }

    // An install follows its game to the folder the store moved it to when its files are there; one job per game, so the
    // next install on it follows once this one is done.
    private bool FollowMovedGames()
    {
        var changed = false;
        foreach (var game in Current.Games)
        {
            if (_activeJobs.ContainsKey(game.Key) || _running.ContainsKey(game.Key) || _moved.NextFollow(game) is not (var record, var folder))
            {
                continue;
            }

            var handle = _jobs.Start(new FollowMoveJobRequest(record));
            if (!IsRefusedAsBusy(handle))
            {
                _moved.OnFollowStarted(record, folder);
                changed |= Track(handle);
            }
        }

        return changed;
    }

    private bool MoveBackupsOfFreeGames()
    {
        var changed = false;
        foreach (var (id, destination) in _backupMoves.ToList())
        {
            if (!_records.TryGetValue(id, out var record))
            {
                _backupMoves.Remove(id);
                continue;
            }

            if (_activeJobs.ContainsKey(record.Game))
            {
                continue;
            }

            var handle = _jobs.Start(new MoveBackupsJobRequest(record, destination));
            if (!IsRefusedAsBusy(handle))
            {
                _backupMoves.Remove(id);
                changed |= Track(handle);
            }
        }

        return changed;
    }

    // The engine forgets an install only when it also finds the folder gone from a drive that is there.
    private void ForgetGoneInstalls()
    {
        if (_moved.NextGone(_records.Values, _snapshot, game => _activeJobs.ContainsKey(game) || _running.ContainsKey(game)) is { } gone)
        {
            _ = ForgetAsync(gone);
        }
    }

    private async Task ForgetAsync(IReadOnlyList<InstallRecord> records)
    {
        var outcomes = new List<(InstallRecord, ForgetOutcome)>();
        foreach (var record in records)
        {
            var outcome = ForgetOutcome.Kept;
            try
            {
                outcome = await _engine.ForgetIfGoneAsync(record.Id, keepBackups: true, _stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogForgetFailed(record.Id, exception.GetType().Name);
            }

            outcomes.Add((record, outcome));
        }

        Post(new GoneChecked(records[0].Game, outcomes));
    }

    // Waits for the first-run setup and for a fresh scan, since the games saved at the last run may have changed while the
    // app was not running.
    private bool Automate()
    {
        if (!_current.OnboardingCompleted || _snapshot.IsCached || _snapshot.ScannedAt is null)
        {
            return false;
        }

        var step = _automation.Evaluate(Current.Games, _current);
        foreach (var notice in step.Notices)
        {
            _notices.Writer.TryWrite(notice);
        }

        var changed = false;
        foreach (var handle in step.Started)
        {
            changed |= Track(handle);
        }

        return changed;
    }

    // The runner had a job on the game that the loop has not heard of yet; what was refused is tried again once that one is
    // done.
    private static bool IsRefusedAsBusy(JobHandle handle) =>
        handle.Completion is { IsCompletedSuccessfully: true, Result.Failure.Reason: JobFailureReason.AlreadyRunning };

    // Shows a job this loop started at once, rather than when its first snapshot arrives.
    private bool Track(JobHandle handle)
    {
        if (handle.Completion.IsCompleted)
        {
            return false;
        }

        _activeJobs.TryAdd(handle.Job.Game, handle.Job);
        return true;
    }

    private void StartVerification()
    {
        if (_verifications.TryStart(_records.Values, _snapshot, _activeJobs.ContainsKey, _time.GetUtcNow()) is { } record)
        {
            _ = VerifyAsync(record.Id, _snapshot.Find(record.Game)?.BuildId);
        }
    }

    // Null when no check or automatic retry falls due without some other change.
    private DateTimeOffset? NextDue()
    {
        var now = _time.GetUtcNow();
        var verification = _verifications.NextDueAt(_records.Values, _snapshot, _activeJobs.ContainsKey, now);
        var retry = _automation.NextRetryAt(now);
        return verification is null ? retry : retry is null || verification < retry ? verification : retry;
    }

    private async Task VerifyAsync(Guid recordId, string? buildId)
    {
        Verification? verification = null;
        try
        {
            var result = await _engine.VerifyAsync(recordId, null, _stopping).ConfigureAwait(false);
            verification = new Verification(_time.GetUtcNow(), buildId, Verification.RepairOf(result));
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }
        // One install that cannot be checked must not stop the others from being checked.
        catch (Exception exception)
        {
            LogVerificationFailed(recordId, exception.GetType().Name);
        }

        Post(new Verified(recordId, buildId, verification));
    }

    // An originals' folder may be gone, e.g. with a disconnected drive.
    private void CheckBackups()
    {
        if (_checkingBackups || !_backupsChanged)
        {
            return;
        }

        _checkingBackups = true;
        _backupsChanged = false;
        _ = CheckBackupsAsync([.. _records.Values], new Dictionary<Guid, bool>(_hasBackups), _unavailableBackups);
    }

    // Every folder is checked at once, so a drive that does not answer holds up only the installs on it.
    private async Task CheckBackupsAsync(IReadOnlyList<InstallRecord> records, Dictionary<Guid, bool> known,
        ImmutableHashSet<Guid> wasUnavailable)
    {
        var checks = await Task.WhenAll(records.Select(record => CheckBackupAsync(record, known, wasUnavailable))).ConfigureAwait(false);
        if (!_stopping.IsCancellationRequested)
        {
            Post(new BackupsChecked([.. checks.Where(check => check.IsUnavailable).Select(check => check.Id)], checks
                .Where(check => check.KeepsOriginals is not null)
                .ToDictionary(check => check.Id, check => check.KeepsOriginals!.Value)));
        }
    }

    private async Task<(Guid Id, bool? KeepsOriginals, bool IsUnavailable)> CheckBackupAsync(InstallRecord record,
        Dictionary<Guid, bool> known, ImmutableHashSet<Guid> wasUnavailable)
    {
        bool? has = known.TryGetValue(record.Id, out var knownHas) ? knownHas : null;
        try
        {
            // An install that only added files keeps no originals, and its empty backup folder was removed.
            has ??= BackupFolders.KeepsOriginals(await _engine.LoadFilesAsync(record.Id, _stopping).ConfigureAwait(false));
            var directory = record.Backup.Directory;
            return (record.Id, has, has.Value && !await _backupChecks.ReadAsync(directory,
                _ => Task.FromResult(BackupFolders.IsAvailable(directory)), _stopping).ConfigureAwait(false));
        }
        catch (TimeoutException)
        {
            // A drive that does not answer keeps what was last known of the folder on it.
            return (record.Id, has, wasUnavailable.Contains(record.Id));
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return (record.Id, has, false);
        }
        catch (Exception exception)
        {
            LogBackupCheckFailed(record.Id, exception.GetType().Name);
            return (record.Id, has, false);
        }
    }

    // Games found after the catalogue's last refresh get their projects from the saved catalogue.
    private void IndexNewGames()
    {
        var wanted = Volatile.Read(ref _indexRequest);
        if (Volatile.Read(ref _indexed) is not { } indexed || Covers(indexed, wanted))
        {
            return;
        }

        Volatile.Write(ref _indexed, wanted);
        _ = RebuildIndexAsync(wanted);
    }

    private async Task RebuildIndexAsync(IndexRequest request)
    {
        try
        {
            await _catalogue.RebuildIndexAsync(request, _stopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogIndexFailed(exception);
        }
    }

    // Packages of installs and of failed jobs are kept, since a retry must not count a new download. The runner keeps what
    // jobs that finished meanwhile used, which this list may not know yet.
    private void PruneDownloads()
    {
        if (!_pruneWanted || _activeJobs.Count > 0)
        {
            return;
        }

        _pruneWanted = false;
        var keep = _records.Values.Select(record => record.Source?.ReleaseFileUuid)
            .Concat(_failures.Values.Select(failure => failure.PackageFileId))
            .OfType<Guid>()
            .ToHashSet();
        _ = PruneAsync(keep);
    }

    private async Task PruneAsync(HashSet<Guid> keep)
    {
        try
        {
            await _jobs.PruneDownloadsAsync(keep, _stopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogPruneFailed(exception);
        }
    }

    private async Task WatchExitAsync(GameKey game, ValueList<RunningProcess> processes)
    {
        try
        {
            await _runningGames.WaitForExitAsync(processes, _stopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }

        Post(new GameExited(game));
    }

    private void Post(Trigger trigger) => _triggers.Writer.TryWrite(trigger);

    private void Subscribe()
    {
        _library.Changed += OnLibraryChanged;
        _catalogue.Changed += OnCatalogueChanged;
        _settings.Changed += OnSettingsChanged;
        _jobs.Changed += OnJobChanged;
        _jobs.Finished += OnJobFinished;
    }

    private void Unsubscribe()
    {
        _library.Changed -= OnLibraryChanged;
        _catalogue.Changed -= OnCatalogueChanged;
        _settings.Changed -= OnSettingsChanged;
        _jobs.Changed -= OnJobChanged;
        _jobs.Finished -= OnJobFinished;
    }

    private void OnLibraryChanged(object? sender, LibrarySnapshot snapshot) => Post(new LibraryChanged(snapshot));

    private void OnCatalogueChanged(object? sender, CatalogueSnapshot snapshot) => Post(new CatalogueChanged(snapshot));

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Post(new SettingsChanged());

    private void OnJobChanged(object? sender, JobSnapshot job) => Post(new JobChanged(job));

    private void OnJobFinished(object? sender, JobResult result) => Post(new JobFinished(result));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Install {RecordId} could not be checked: {Error}")]
    private partial void LogVerificationFailed(Guid recordId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The backups of install {RecordId} could not be checked: {Error}")]
    private partial void LogBackupCheckFailed(Guid recordId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Install {RecordId}, whose game is gone from its folder, could not be forgotten: {Error}")]
    private partial void LogForgetFailed(Guid recordId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Newly found games could not be indexed")]
    private partial void LogIndexFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Old downloads could not be removed")]
    private partial void LogPruneFailed(Exception exception);

    private abstract record Trigger;

    private sealed record LibraryChanged(LibrarySnapshot Snapshot) : Trigger;

    private sealed record CatalogueChanged(CatalogueSnapshot Snapshot) : Trigger;

    private sealed record SettingsChanged : Trigger;

    private sealed record JobChanged(JobSnapshot Job) : Trigger;

    private sealed record JobFinished(JobResult Result) : Trigger;

    /// <summary>A check of an install's files ended.</summary>
    /// <param name="RecordId">The install that was checked.</param>
    /// <param name="BuildId">The game's build when the check started.</param>
    /// <param name="Verification">Null when the check failed.</param>
    private sealed record Verified(Guid RecordId, string? BuildId, Verification? Verification) : Trigger;

    private sealed record VerificationRequested(GameKey Game) : Trigger;

    private sealed record GoneChecked(GameKey Game, IReadOnlyList<(InstallRecord Record, ForgetOutcome Outcome)> Outcomes) : Trigger;

    private sealed record BackupsChecked(ImmutableHashSet<Guid> Unavailable, IReadOnlyDictionary<Guid, bool> HasBackups) : Trigger;

    private sealed record BackupsMoveRequested(BackupChoice Destination) : Trigger;

    private sealed record FailureDismissed(FailureKey Key) : Trigger;

    private sealed record GameExited(GameKey Game) : Trigger;

    private sealed record Tick : Trigger;
}
