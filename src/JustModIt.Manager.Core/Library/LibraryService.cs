using System.Collections.Immutable;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Library;

/// <summary>
/// The background loop that keeps the library up to date: it rescans a store once its watched records stop changing,
/// every store after a lost watch, a resume from sleep or a new volume, and every store on a long timer as a safety net.
/// There is no other polling. What a scan could not read keeps its last good data, marked offline or stale, so a hiccup
/// never looks like an uninstall.
/// </summary>
public sealed partial class LibraryService : ILibraryService, IDisposable
{
    /// <summary>Stores rewrite their files many times per job; one rescan after they calm down is enough.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);

    /// <summary>A long download keeps rewriting its records, yet the player should still see the game's state change.</summary>
    public static readonly TimeSpan MaxDebounce = TimeSpan.FromSeconds(30);

    /// <summary>The safety net for changes no watcher reported, e.g. on a network drive.</summary>
    public static readonly TimeSpan RescanInterval = TimeSpan.FromHours(6);

    /// <summary>How long a folder the player picked may take to answer, e.g. on a disconnected network drive.</summary>
    public static readonly TimeSpan FolderCheckTimeout = TimeSpan.FromSeconds(10);

    private readonly IReadOnlyList<ILibraryProvider> _providers;
    private readonly ISettingsService _settings;
    private readonly IRunningGameDetector _runningGames;
    private readonly IFileSystem _fileSystem;
    private readonly ISystemEvents _systemEvents;
    private readonly IDocumentStore<LibrarySnapshot> _snapshots;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly WatchCoordinator _watches;
    private readonly Lock _gate = new();
    private readonly Dictionary<StoreId, Burst> _changedStores = [];
    private readonly List<TaskCompletionSource> _waiters = [];
    private TaskCompletionSource _wake = NewSignal();
    private Burst? _lost;
    private Burst? _systemChange;
    private DateTimeOffset? _lastFullScan;
    private bool _checkRequested;
    private bool _stopped;
    private LibrarySnapshot _snapshot = LibrarySnapshot.Empty;
    private int _started;

    /// <summary>Creates the service; <see cref="RunAsync"/> starts its loop.</summary>
    /// <param name="providers">One provider per supported store.</param>
    /// <param name="settings">Gives the folders the player added and reports their changes.</param>
    /// <param name="runningGames">Finds the processes running from a game's folder.</param>
    /// <param name="fileSystem">Checks the folders the player picks.</param>
    /// <param name="folderWatcher">Watches the stores' folders.</param>
    /// <param name="registryWatcher">Null on platforms without a registry.</param>
    /// <param name="systemEvents">Reports resume from sleep and new volumes.</param>
    /// <param name="snapshots">Where the last snapshot is kept for the next start.</param>
    /// <param name="time">Paces the debouncing, the rescans and the time limits of slow drives.</param>
    /// <param name="logger">Logs scans and their failures.</param>
    public LibraryService(IEnumerable<ILibraryProvider> providers, ISettingsService settings, IRunningGameDetector runningGames,
        IFileSystem fileSystem, IFolderWatcher folderWatcher, IRegistryWatcher? registryWatcher, ISystemEvents systemEvents,
        IDocumentStore<LibrarySnapshot> snapshots, TimeProvider time, ILogger<LibraryService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runningGames);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(systemEvents);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _providers = [.. providers];
        _settings = settings;
        _runningGames = runningGames;
        _fileSystem = fileSystem;
        _systemEvents = systemEvents;
        _snapshots = snapshots;
        _time = time;
        _logger = logger;
        _watches = new WatchCoordinator(folderWatcher, registryWatcher, OnWatchSignal, logger);
    }

    public event EventHandler<LibraryChangedEventArgs>? Changed;

    public LibrarySnapshot Snapshot => Volatile.Read(ref _snapshot);

    /// <summary>The number of system watchers running.</summary>
    public int ActiveWatchCount => _watches.ActiveCount;

    /// <summary>
    /// Runs until cancelled: shows the snapshot saved at the last run, scans every store, then keeps the library up to
    /// date.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The library service is already running.");
        }

        _settings.Changed += OnSettingsChanged;
        _systemEvents.Resumed += OnSystemChanged;
        _systemEvents.VolumeArrived += OnSystemChanged;
        try
        {
            await LoadSavedSnapshotAsync(cancellationToken).ConfigureAwait(false);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TakeDueWork(out var wait, out var wake) is { } work)
                {
                    await CheckAsync(work, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await SleepAsync(wait, wake, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Unsubscribe();
            _watches.Dispose();
            CancelWaiters();
        }
    }

    public void RequestCheck()
    {
        lock (_gate)
        {
            _checkRequested = true;
        }

        Wake();
    }

    public Task CheckNowAsync(CancellationToken cancellationToken)
    {
        var done = NewSignal();
        lock (_gate)
        {
            if (_stopped)
            {
                return Task.FromCanceled(new CancellationToken(canceled: true));
            }

            _checkRequested = true;
            _waiters.Add(done);
        }

        Wake();
        return done.Task.WaitAsync(cancellationToken);
    }

    public async Task<GameProbe> ProbeAsync(GameKey key, CancellationToken cancellationToken)
    {
        if (_providers.FirstOrDefault(provider => provider.Store == key.Store) is not { } provider)
        {
            return new GameProbe(key, null, []);
        }

        var known = Snapshot.Find(key);
        var context = ContextFor(_settings.Current, key.Store);

        // A game the snapshot does not know yet, e.g. before the first scan has finished, is looked for in the whole store.
        var probed = await Task.Run(async () => known is null
                ? (await provider.ScanAsync(context, cancellationToken).ConfigureAwait(false)).Games.FirstOrDefault(game => game.Key == key)
                : await provider.ProbeAsync(known, context, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        if (probed != (known is null ? null : known with { IsStale = false }))
        {
            // The snapshot is behind; the next scan brings it up to date for everyone else.
            MarkChanged(key.Store);
        }

        if (probed is not { State: GameInstallState.Installed })
        {
            return new GameProbe(key, probed, []);
        }

        var running = await Task.Run(() => _runningGames.FindProcessesIn(probed.InstallDirectory, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        return running.Count == 0
            ? new GameProbe(key, probed, [])
            : new GameProbe(key, probed with { State = GameInstallState.Running, Reason = GameStateReason.GameRunning }, running);
    }

    public async Task<LibraryFolderResult> AddLibraryFolderAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            return LibraryFolderResult.Rejected(LibraryFolderRejection.Unreachable);
        }

        (StoreId Store, LibraryFolderEvidence Evidence)? found;
        try
        {
            found = await Task.Run(() => InspectAsync(path, cancellationToken), cancellationToken)
                .WaitAsync(FolderCheckTimeout, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return LibraryFolderResult.Rejected(LibraryFolderRejection.Unreachable);
        }

        if (found is not { Evidence.Folder: { } accepted } evidence)
        {
            return LibraryFolderResult.Rejected(found?.Evidence.Rejection ?? LibraryFolderRejection.Unreachable);
        }

        var folder = new LibraryFolder(evidence.Store, accepted);
        if (_settings.Current.LibraryFolders.Any(existing => AppSettings.IsSameFolder(existing, folder)))
        {
            return LibraryFolderResult.Rejected(LibraryFolderRejection.AlreadyAdded);
        }

        if (IsDetected(folder))
        {
            return LibraryFolderResult.Rejected(LibraryFolderRejection.AlreadyDetected);
        }

        _settings.Update(settings => settings.WithLibraryFolder(folder));
        RequestCheck();
        return new LibraryFolderResult(folder, null);
    }

    public void RemoveLibraryFolder(LibraryFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        _settings.Update(settings => settings.WithoutLibraryFolder(folder));
        RequestCheck();
    }

    public void Dispose()
    {
        Unsubscribe();
        _watches.Dispose();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static LibraryScanContext ContextFor(AppSettings settings, StoreId store) =>
        new([.. settings.LibraryFolders.Where(folder => folder.Store == store).Select(folder => folder.Path)]);

    private async Task LoadSavedSnapshotAsync(CancellationToken cancellationToken)
    {
        if (await _snapshots.LoadAsync(cancellationToken).ConfigureAwait(false) is { } saved && Snapshot.Version == 0)
        {
            Publish(Snapshot, saved with { Version = 1, IsCached = true });
        }
    }

    private Work? TakeDueWork(out TimeSpan wait, out Task wake)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            wake = _wake.Task;
            var nextFullScan = _lastFullScan + RescanInterval ?? now;
            if (_checkRequested || now >= nextFullScan || _lost?.SettlesAt <= now || _systemChange?.SettlesAt <= now)
            {
                wait = TimeSpan.Zero;
                var work = new Work(FullScan: true, [], Rewatch: _lost is not null, _checkRequested ? [.. _waiters] : []);
                if (_checkRequested)
                {
                    _waiters.Clear();
                    _checkRequested = false;
                }

                _changedStores.Clear();
                _lost = null;
                _systemChange = null;
                return work;
            }

            List<StoreId> stores = [.. _changedStores.Where(change => change.Value.SettlesAt <= now).Select(change => change.Key)];
            if (stores.Count > 0)
            {
                wait = TimeSpan.Zero;
                foreach (var store in stores)
                {
                    _changedStores.Remove(store);
                }

                return new Work(FullScan: false, stores, Rewatch: false, []);
            }

            IEnumerable<Burst> pending = [.. _changedStores.Values, .. new[] { _lost, _systemChange }.OfType<Burst>()];
            wait = pending.Select(burst => burst.SettlesAt).Append(nextFullScan).Min() - now;
            return null;
        }
    }

    private async Task SleepAsync(TimeSpan wait, Task wake, CancellationToken cancellationToken)
    {
        using var sleeping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timer = Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, _time, sleeping.Token);
        await Task.WhenAny(timer, wake).ConfigureAwait(false);
        await sleeping.CancelAsync().ConfigureAwait(false);
    }

    private async Task CheckAsync(Work work, CancellationToken cancellationToken)
    {
        try
        {
            if (work.Rewatch)
            {
                _watches.Rewatch();
            }

            if (work.FullScan)
            {
                lock (_gate)
                {
                    _lastFullScan = _time.GetUtcNow();
                }
            }

            await ScanAsync(work.FullScan ? _providers : [.. _providers.Where(provider => work.Stores.Contains(provider.Store))],
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // The loop must outlive any failure, or the library would never be updated again.
        catch (Exception exception)
        {
            LogCheckFailed(exception);
        }
        finally
        {
            foreach (var waiter in work.Waiters)
            {
                waiter.TrySetResult();
            }
        }
    }

    private async Task ScanAsync(IReadOnlyList<ILibraryProvider> providers, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        var scans = (await Task.WhenAll(providers.Select(provider => ScanStoreAsync(provider, ContextFor(settings, provider.Store),
            cancellationToken))).ConfigureAwait(false)).ToDictionary(result => result.Store, result => result.Scan);
        var previous = Snapshot;
        var now = _time.GetUtcNow();
        var stores = new List<StoreStatus>();
        var games = ImmutableDictionary.CreateBuilder<GameKey, InstalledGame>();
        foreach (var provider in _providers)
        {
            var store = provider.Store;
            var previousStatus = previous.FindStore(store);
            List<InstalledGame> previousGames = [.. previous.Games.Values.Where(game => game.Key.Store == store)];
            var (status, storeGames) = !scans.TryGetValue(store, out var scan) ? (previousStatus, previousGames)
                : scan is null ? Failed(store, previousStatus, previousGames)
                : Merge(scan, previousStatus, previousGames, now);
            if (status is not null)
            {
                stores.Add(status);
            }

            foreach (var game in storeGames.GroupBy(game => game.Key).Select(sameKey => sameKey.MinBy(Trust)!))
            {
                games[game.Key] = game;
            }
        }

        var snapshot = new LibrarySnapshot
        {
            Version = previous.Version + 1,
            Games = new ValueDictionary<GameKey, InstalledGame>(games.ToImmutable()),
            Stores = [.. stores],
            ScannedAt = now,
        };

        foreach (var (store, scan) in scans)
        {
            // A change made before a new watch started went unseen, so the store is read once more.
            if (scan is not null && _watches.Apply(store, scan.Watch))
            {
                MarkChanged(store);
            }
        }

        if (Publish(previous, snapshot))
        {
            await SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(StoreId Store, StoreScan? Scan)> ScanStoreAsync(ILibraryProvider provider, LibraryScanContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            // Providers read many small files and registry keys synchronously; running them side by side keeps one slow
            // or sleeping drive from holding up the other stores.
            return (provider.Store, await Task.Run(() => provider.ScanAsync(context, cancellationToken), cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Paths hide in the messages, so only the type is logged.
            LogScanFailed(provider.Store, exception.GetType().Name);
            return (provider.Store, null);
        }
    }

    private static (StoreStatus Status, List<InstalledGame> Games) Merge(StoreScan scan, StoreStatus? previousStatus,
        List<InstalledGame> previousGames, DateTimeOffset now)
    {
        var games = new List<InstalledGame>(scan.Games);
        var libraries = new List<LibraryLocation>(scan.Libraries);
        var found = games.Select(game => game.Key).ToHashSet();
        void Keep(InstalledGame game)
        {
            if (found.Add(game.Key))
            {
                games.Add(game);
            }
        }

        IEnumerable<InstalledGame> Of(string libraryId) => previousGames.Where(game => game.LibraryId == libraryId);

        // An unplugged drive or a list caught mid-write must not look like an uninstall, which would orphan what was
        // installed into those games.
        foreach (var library in scan.Libraries)
        {
            foreach (var game in library.State switch
            {
                LibraryState.Offline => Of(library.Id).Select(game => game with { State = GameInstallState.Offline, Reason = GameStateReason.LibraryOffline }),
                LibraryState.Unreadable => Of(library.Id).Select(game => game with { IsStale = true }),
                _ => [],
            })
            {
                Keep(game);
            }
        }

        if (!scan.IsComplete && previousStatus is not null)
        {
            foreach (var library in previousStatus.Libraries.Where(library => scan.Libraries.All(current => current.Id != library.Id)))
            {
                libraries.Add(library with { State = LibraryState.Unreadable });
                foreach (var game in Of(library.Id))
                {
                    Keep(game with { IsStale = true });
                }
            }
        }

        foreach (var unreadable in scan.UnreadableGames)
        {
            if (previousGames.FirstOrDefault(game => game.Key == unreadable.Key) is { } last)
            {
                // The store is most likely rewriting the record right now, so the quiet period starts again.
                var activity = unreadable.StoreActivityAt ?? now;
                Keep(last with { IsStale = true, StoreActivityAt = last.StoreActivityAt > activity ? last.StoreActivityAt : activity });
            }
        }

        return (new StoreStatus(scan.Store, scan.IsPresent, ScanFailed: false, scan.IsComplete, [.. libraries]), games);
    }

    private static (StoreStatus Status, List<InstalledGame> Games) Failed(StoreId store, StoreStatus? previousStatus,
        List<InstalledGame> previousGames) =>
        ((previousStatus ?? new StoreStatus(store, IsPresent: false, ScanFailed: true, IsComplete: true, [])) with { ScanFailed = true },
            [.. previousGames.Select(game => game with { IsStale = true })]);

    // A key reported twice (the same game in two libraries, or kept from an earlier scan) goes to the most trustworthy copy.
    private static int Trust(InstalledGame game) =>
        game.IsReady ? 0
        : game.IsStale ? 3
        : game.State == GameInstallState.Offline ? 2
        : 1;

    // Returns whether the snapshot's content changed and it was announced.
    private bool Publish(LibrarySnapshot previous, LibrarySnapshot snapshot)
    {
        if (snapshot.HasSameContent(previous))
        {
            // Only the time of the check is news.
            Volatile.Write(ref _snapshot, previous with { ScannedAt = snapshot.ScannedAt });
            return false;
        }

        Volatile.Write(ref _snapshot, snapshot);
        var added = snapshot.Games.Keys.Count(key => !previous.Games.ContainsKey(key));
        var removed = previous.Games.Keys.Count(key => !snapshot.Games.ContainsKey(key));
        var updated = snapshot.Games.Count(pair => previous.Games.TryGetValue(pair.Key, out var before) && before != pair.Value);
        LogChanged(snapshot.Games.Count, added, removed, updated);
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler<LibraryChangedEventArgs>)handler)(this, new LibraryChangedEventArgs(snapshot));
            }
            catch (Exception exception)
            {
                LogListenerFailed(exception);
            }
        }

        return true;
    }

    private async Task SaveAsync(LibrarySnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            await _snapshots.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        // Only the next start's first picture depends on it.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSnapshotNotSaved(exception.GetType().Name);
        }
    }

    private async Task<(StoreId Store, LibraryFolderEvidence Evidence)?> InspectAsync(string path, CancellationToken cancellationToken)
    {
        if (!_fileSystem.DirectoryExists(path))
        {
            return null;
        }

        (StoreId Store, LibraryFolderEvidence Evidence)? mostSpecific = null;
        foreach (var provider in _providers)
        {
            var evidence = await provider.InspectFolderAsync(path, cancellationToken).ConfigureAwait(false);
            if (evidence.IsAccepted)
            {
                return (provider.Store, evidence);
            }

            if (evidence.Rejection != LibraryFolderRejection.NoEvidence)
            {
                mostSpecific ??= (provider.Store, evidence);
            }
        }

        return mostSpecific ?? (default, LibraryFolderEvidence.None);
    }

    private bool IsDetected(LibraryFolder folder)
    {
        var snapshot = Snapshot;
        return snapshot.FindStore(folder.Store) is { } store
            && (store.Libraries.Any(library => !library.IsManual && library.Path is { } path && PathComparison.AreSame(path, folder.Path))
                || snapshot.Games.Values.Any(game => game.Key.Store == folder.Store
                    && PathComparison.AreSame(game.InstallDirectory, folder.Path)
                    && store.Libraries.Any(library => library.Id == game.LibraryId && !library.IsManual)));
    }

    private void OnWatchSignal(StoreId store, WatchSignal signal)
    {
        if (signal == WatchSignal.Changed)
        {
            MarkChanged(store);
            return;
        }

        bool started;
        lock (_gate)
        {
            started = Extend(ref _lost);
        }

        if (started)
        {
            Wake();
        }
    }

    private void OnSystemChanged(object? sender, EventArgs e)
    {
        bool started;
        lock (_gate)
        {
            started = Extend(ref _systemChange);
        }

        if (started)
        {
            Wake();
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Previous.LibraryFolders == e.Current.LibraryFolders)
        {
            return;
        }

        foreach (var store in e.Previous.LibraryFolders.Concat(e.Current.LibraryFolders).Select(folder => folder.Store).Distinct())
        {
            if (ContextFor(e.Previous, store) != ContextFor(e.Current, store))
            {
                MarkChanged(store);
            }
        }
    }

    // Only a new burst can be due sooner than what the loop waits for, so only a new one wakes it.
    private void MarkChanged(StoreId store)
    {
        bool started;
        lock (_gate)
        {
            var burst = _changedStores.TryGetValue(store, out var ongoing) ? ongoing : (Burst?)null;
            started = Extend(ref burst);
            _changedStores[store] = burst!.Value;
        }

        if (started)
        {
            Wake();
        }
    }

    // Returns true when a new burst started.
    private bool Extend(ref Burst? burst)
    {
        var now = _time.GetUtcNow();
        var started = burst is null;
        burst = burst is { } ongoing ? ongoing with { Last = now } : new Burst(now, now);
        return started;
    }

    private void Wake()
    {
        TaskCompletionSource woken;
        lock (_gate)
        {
            woken = _wake;
            _wake = NewSignal();
        }

        woken.TrySetResult();
    }

    // Nothing would ever run the checks still asked for, so their callers must not wait.
    private void CancelWaiters()
    {
        List<TaskCompletionSource> waiters;
        lock (_gate)
        {
            _stopped = true;
            waiters = [.. _waiters];
            _waiters.Clear();
        }

        foreach (var waiter in waiters)
        {
            waiter.TrySetCanceled();
        }
    }

    private void Unsubscribe()
    {
        _settings.Changed -= OnSettingsChanged;
        _systemEvents.Resumed -= OnSystemChanged;
        _systemEvents.VolumeArrived -= OnSystemChanged;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Library has {Count} games: {Added} added, {Removed} removed, {Updated} changed")]
    private partial void LogChanged(int count, int added, int removed, int updated);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scanning the {Store} library failed: {Error}")]
    private partial void LogScanFailed(StoreId store, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "A library check failed")]
    private partial void LogCheckFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The library snapshot could not be saved: {Error}")]
    private partial void LogSnapshotNotSaved(string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "A library listener failed")]
    private partial void LogListenerFailed(Exception exception);

    /// <summary>What the loop does next.</summary>
    /// <param name="FullScan">Every store is scanned.</param>
    /// <param name="Stores">Stores to rescan on their own when no full scan is due.</param>
    /// <param name="Rewatch">A watcher missed changes, so every watch is recreated before the full scan.</param>
    /// <param name="Waiters">Callers of <see cref="CheckNowAsync"/>, released once the scan has finished.</param>
    private sealed record Work(bool FullScan, IReadOnlyList<StoreId> Stores, bool Rewatch, IReadOnlyList<TaskCompletionSource> Waiters);

    /// <summary>Changes that keep coming, acted on once they pause or after <see cref="MaxDebounce"/> at the latest.</summary>
    /// <param name="First">When the first change of the burst came.</param>
    /// <param name="Last">When the latest change came.</param>
    private readonly record struct Burst(DateTimeOffset First, DateTimeOffset Last)
    {
        public DateTimeOffset SettlesAt => Last + Debounce < First + MaxDebounce ? Last + Debounce : First + MaxDebounce;
    }
}
