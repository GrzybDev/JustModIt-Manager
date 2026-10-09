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

    // Only the loop touches these, so they need no lock.
    private readonly Dictionary<Guid, InstallRecord> _records = [];
    private readonly Dictionary<GameKey, JobSnapshot> _activeJobs = [];
    private readonly Dictionary<Guid, Verification> _verifications = [];
    private readonly HashSet<GameKey> _verificationRequests = [];
    private readonly Dictionary<Guid, bool> _hasBackups = [];
    private readonly Dictionary<GameKey, ValueList<RunningProcess>> _running = [];
    private readonly HashSet<(Guid Record, string Folder)> _followAttempts = [];
    private readonly HashSet<GameKey> _deferredFollows = [];
    private readonly HashSet<GameKey> _followsInVain = [];
    private readonly HashSet<Guid> _goneChecks = [];
    private LibrarySnapshot _snapshot = LibrarySnapshot.Empty;
    private CatalogueSnapshot _catalogueSnapshot = CatalogueSnapshot.Empty;
    private AppSettings _current = AppSettings.Default;
    private ImmutableHashSet<Guid> _unavailableBackups = [];
    private ImmutableDictionary<GameKey, FailedAttempt> _failures = ImmutableDictionary<GameKey, FailedAttempt>.Empty;
    private IReadOnlyList<RecoveredOperation> _recoveries = [];
    private Guid? _verifying;
    private Guid? _forgetting;
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

    /// <summary>Checks the game's install now, e.g. because the player opened the game's page.</summary>
    /// <param name="game">The game whose installs to check.</param>
    public void RequestVerification(GameKey game) => Post(new VerificationRequested(game));

    /// <summary>The player saw the failure and wants it gone.</summary>
    public void DismissFailure(GameKey game) => Post(new FailureDismissed(game));

    /// <summary>Starts moving every install's backups to where the settings now keep them.</summary>
    public IReadOnlyList<JobHandle> MoveBackups(BackupChoice destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return [.. Current.Games.Select(game => game.Installed)
            .OfType<InstallRecord>()
            .Where(record => record.Backup.Location != destination.Location || destination.Location == BackupLocation.CustomFolder)
            .Select(record => _jobs.Start(new MoveBackupsJobRequest(record, destination)))];
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

        _failures = (await _failureStore.LoadAsync(cancellationToken).ConfigureAwait(false))?.Games.ToImmutableDictionary()
            ?? ImmutableDictionary<GameKey, FailedAttempt>.Empty;
        _automation.Load(await _historyStore.LoadAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<List<Trigger>> NextBatchAsync(CancellationToken cancellationToken)
    {
        var reader = _triggers.Reader;
        if (!reader.TryRead(out var first))
        {
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task arrived = reader.WaitToReadAsync(waiting.Token).AsTask();
            var due = NextDue();
            await (due is { } at
                ? Task.WhenAny(arrived, Task.Delay(Max(at - _time.GetUtcNow(), TimeSpan.Zero), _time, waiting.Token))
                : Task.WhenAny(arrived)).ConfigureAwait(false);
            await waiting.CancelAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
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

    private static TimeSpan Max(TimeSpan first, TimeSpan second) => first > second ? first : second;

    private void Apply(Trigger trigger)
    {
        switch (trigger)
        {
            case LibraryChanged changed when changed.Snapshot.Version >= _snapshot.Version:
                RetryDeferredFollows();
                _snapshot = changed.Snapshot;
                ForgetVerificationsOfBusyGames();
                _automation.OnLibraryChanged();
                _backupsChanged = true;
                _goneChecks.Clear();
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
                ApplyVerification(verified);
                break;
            // The player looking at a game is also when a drive holding its originals may have come back, or its old
            // folder may have gone.
            case VerificationRequested requested:
                _verificationRequests.Add(requested.Game);
                _backupsChanged = true;
                _goneChecks.RemoveWhere(id => _records.TryGetValue(id, out var record) && record.Game == requested.Game);
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
                SetFailure(dismissed.Game, null);
                break;
            case GameExited exited:
                _running.Remove(exited.Game);
                RetryDeferredFollows();
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
            _verifications.Remove(removed);
        }

        if (result.Installed is { } installed)
        {
            _records[installed.Id] = installed;
            _verifications.Remove(installed.Id);
        }

        // A job may also have failed on a backup folder that is gone.
        _backupsChanged = true;

        // Following a moved game is the app's own housekeeping, not something the player tried.
        if (result.Job.Kind == JobKind.FollowMove)
        {
            if (result.Outcome == JobOutcome.Deferred)
            {
                _deferredFollows.Add(game);
            }
            else if (result is { IsSuccess: true, Installed: null })
            {
                _followsInVain.Add(game);
            }
        }
        else if (FailedAttempt.From(result, _time.GetUtcNow()) is { } failed)
        {
            SetFailure(game, failed);
        }
        else if (result.IsSuccess)
        {
            SetFailure(game, null);
        }

        if (result is { Outcome: JobOutcome.Deferred, DeferredBy: JobWaitReason.GameRunning, RunningProcesses.Count: > 0 })
        {
            _running[game] = result.RunningProcesses;
            _ = WatchExitAsync(game, result.RunningProcesses);
        }

        _pruneWanted = true;
    }

    private void ApplyVerification(Verified verified)
    {
        if (_verifying == verified.RecordId)
        {
            _verifying = null;
        }

        if (!_records.ContainsKey(verified.RecordId))
        {
            return;
        }

        // A check that failed keeps what the last one found, and waits as long as a successful one would.
        var now = _time.GetUtcNow();
        _verifications[verified.RecordId] = verified.Verification
            ?? (_verifications.TryGetValue(verified.RecordId, out var last) ? last with { At = now } : new Verification(now, verified.BuildId, null));
    }

    private void ApplyGoneCheck(GoneChecked gone)
    {
        _forgetting = null;
        var record = gone.Record;
        if (gone.Outcome == ForgetOutcome.Kept || !_records.Remove(record.Id))
        {
            return;
        }

        _verifications.Remove(record.Id);
        _hasBackups.Remove(record.Id);
        // A hidden game is left alone; its leftover backups still show in the settings.
        if (_current.GetGame(record.Game).IsHidden)
        {
            return;
        }

        _notices.Writer.TryWrite(new AutomationNotice(NoticeKind.Forgotten)
        {
            Game = record.Game,
            GameName = Current.Find(record.Game)?.Name ?? record.GameName,
            KeptBackups = gone.Outcome == ForgetOutcome.ForgottenKeepingBackups,
        });
    }

    // A move that found its game in use is followed once the game exits or the store has changed something.
    private void RetryDeferredFollows()
    {
        _followAttempts.RemoveWhere(attempt => _records.TryGetValue(attempt.Record, out var record) && _deferredFollows.Contains(record.Game));
        _deferredFollows.Clear();
    }

    // What the last check found says nothing once the store works on the game; it is checked again when the store is done.
    private void ForgetVerificationsOfBusyGames()
    {
        foreach (var record in _records.Values)
        {
            if (_snapshot.Find(record.Game) is { IsReady: false } game && PathComparison.AreSame(game.InstallDirectory, record.InstallDirectory))
            {
                _verifications.Remove(record.Id);
            }
        }
    }

    private void SetFailure(GameKey game, FailedAttempt? failure)
    {
        var updated = failure is null ? _failures.Remove(game) : _failures.SetItem(game, failure);
        if (updated != _failures)
        {
            _failures = updated;
            _failureWriter.Save(new FailedAttempts(new ValueDictionary<GameKey, FailedAttempt>(_failures)));
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
            Jobs = _activeJobs.ToImmutableDictionary(),
            Repairs = _verifications.Where(pair => pair.Value.Repair is not null).ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Repair!.Value),
            UnavailableBackups = _unavailableBackups,
            Failures = _failures,
            Running = _running.ToImmutableDictionary(),
        });

        // A failure of a game that left the list has no card left to show it on. Until a scan and the catalogue are in,
        // a game missing from the list may only be missing from what was read so far.
        if (_snapshot is { ScannedAt: not null, IsCached: false } && _catalogueSnapshot.FetchedAt is not null)
        {
            foreach (var game in _failures.Keys.Where(game => !games.Any(listed => listed.Key == game)).ToList())
            {
                SetFailure(game, null);
            }
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
            [.. _records.Values.Select(record => InstallSources.ProjectOf(record.Source)).OfType<ProjectLocator>().Distinct()]));
        next.TrySetResult(state);
    }

    // True when a job was started, which changes the state.
    private bool Schedule()
    {
        var changed = FollowMovedGames();
        changed |= Automate();
        StartVerification();
        CheckBackups();
        ForgetGoneInstall();
        IndexNewGames();
        PruneDownloads();
        return changed;
    }

    /// <summary>An install follows its game to the folder the store moved it to, once per folder, when its files are there.</summary>
    private bool FollowMovedGames()
    {
        var changed = false;
        foreach (var game in Current.Games)
        {
            if (game is { Status.Orphan: OrphanReason.GameMoved, Game: { IsReady: true } moved, Installed: { } record, Job: null }
                && !_running.ContainsKey(game.Key)
                && _followAttempts.Add((record.Id, moved.InstallDirectory)))
            {
                _followsInVain.Remove(game.Key);
                changed |= Track(_jobs.Start(new FollowMoveJobRequest(record)));
            }
        }

        return changed;
    }

    /// <summary>
    /// Forgets an install once its game is surely gone from its folder: a fresh scan that read the whole store reports the
    /// game nowhere or elsewhere, nothing works on it, a game the store moved was looked for the install's files in vain,
    /// and the engine finds the folder gone from a drive that is there. One at a time, and each once per library change.
    /// </summary>
    private void ForgetGoneInstall()
    {
        if (_forgetting is not null)
        {
            return;
        }

        var record = _records.Values.FirstOrDefault(record => !_goneChecks.Contains(record.Id)
            && !_activeJobs.ContainsKey(record.Game) && !_running.ContainsKey(record.Game)
            && _snapshot.ConfirmsGone(record.Game, record.InstallDirectory)
            && (_snapshot.Find(record.Game) is not { } moved
                || (_followAttempts.Contains((record.Id, moved.InstallDirectory)) && _followsInVain.Contains(record.Game))));
        if (record is null)
        {
            return;
        }

        _goneChecks.Add(record.Id);
        _forgetting = record.Id;
        _ = ForgetAsync(record);
    }

    private async Task ForgetAsync(InstallRecord record)
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

        Post(new GoneChecked(record, outcome));
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
        if (_verifying is not null)
        {
            return;
        }

        var now = _time.GetUtcNow();
        foreach (var record in _records.Values)
        {
            var game = _snapshot.Find(record.Game);
            if (VerificationSchedule.DueAt(record, game, _activeJobs.ContainsKey(record.Game), _verifications.GetValueOrDefault(record.Id),
                _verificationRequests.Contains(record.Game), now) is { } due && due <= now)
            {
                _verificationRequests.Remove(record.Game);
                _verifying = record.Id;
                _ = VerifyAsync(record.Id, game!.BuildId);
                return;
            }
        }
    }

    // Null when no check or automatic retry falls due without some other change.
    private DateTimeOffset? NextDue()
    {
        var now = _time.GetUtcNow();
        var verification = _verifying is not null ? null : _records.Values
            .Select(record => VerificationSchedule.DueAt(record, _snapshot.Find(record.Game), _activeJobs.ContainsKey(record.Game),
                _verifications.GetValueOrDefault(record.Id), _verificationRequests.Contains(record.Game), now))
            .Min();
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
        _ = CheckBackupsAsync([.. _records.Values], new Dictionary<Guid, bool>(_hasBackups));
    }

    private async Task CheckBackupsAsync(IReadOnlyList<InstallRecord> records, Dictionary<Guid, bool> known)
    {
        var unavailable = ImmutableHashSet.CreateBuilder<Guid>();
        var hasBackups = new Dictionary<Guid, bool>();
        foreach (var record in records)
        {
            try
            {
                // An install that only added files keeps no originals, and its empty backup folder was removed.
                if (!known.TryGetValue(record.Id, out var has))
                {
                    has = (await _engine.LoadFilesAsync(record.Id, _stopping).ConfigureAwait(false)).Files.Any(file => file.Backup is not null);
                }

                hasBackups[record.Id] = has;
                if (has && !await Task.Run(() => Directory.Exists(record.Backup.Directory), _stopping).ConfigureAwait(false))
                {
                    unavailable.Add(record.Id);
                }
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogBackupCheckFailed(record.Id, exception.GetType().Name);
            }
        }

        Post(new BackupsChecked(unavailable.ToImmutable(), hasBackups));
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
        var keep = _records.Values.Select(record => Guid.TryParse(record.Source?.ReleaseFileId, out var file) ? file : (Guid?)null)
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

    private void OnLibraryChanged(object? sender, LibraryChangedEventArgs e) => Post(new LibraryChanged(e.Snapshot));

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

    private sealed record GoneChecked(InstallRecord Record, ForgetOutcome Outcome) : Trigger;

    private sealed record BackupsChecked(ImmutableHashSet<Guid> Unavailable, IReadOnlyDictionary<Guid, bool> HasBackups) : Trigger;

    private sealed record FailureDismissed(GameKey Game) : Trigger;

    private sealed record GameExited(GameKey Game) : Trigger;

    private sealed record Tick : Trigger;
}
