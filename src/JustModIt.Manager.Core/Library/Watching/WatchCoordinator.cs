using JustModIt.Manager.Core.Games;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Library.Watching;

/// <summary>
/// Owns every system watcher of every store and keeps them in line with the stores' latest watch plans, so a library
/// added, removed or moved to another drive is followed after the scan that found it; it watches nothing until a plan is
/// applied.
/// </summary>
/// <param name="folders">Watches folders.</param>
/// <param name="registryKeys">Null on platforms without a registry.</param>
/// <param name="onSignal">Called on any thread with the store whose records changed.</param>
/// <param name="logger">Logs what could not be watched.</param>
internal sealed partial class WatchCoordinator(IFolderWatcher folders, IRegistryWatcher? registryKeys,
    Action<StoreId, WatchSignal> onSignal, ILogger logger) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<StoreId, WatchPlan> _plans = [];
    private readonly Dictionary<(StoreId Store, object Target), IDisposable> _active = [];
    private readonly HashSet<(StoreId Store, object Target)> _starting = [];
    private bool _disposed;

    /// <summary>The number of system watchers running.</summary>
    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _active.Count;
            }
        }
    }

    /// <summary>Watches what <paramref name="plan"/> names for the store and stops watching what it no longer names.</summary>
    /// <param name="store">The store the plan is for.</param>
    /// <param name="plan">The watch plan of the store's latest scan.</param>
    /// <returns>Whether a watch started, since a change made before it started went unseen.</returns>
    public bool Apply(StoreId store, WatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            _plans[store] = plan;
        }

        return Follow(store);
    }

    /// <summary>Recreates every watch after one lost changes, starting the new watches before the old ones stop.</summary>
    public void Rewatch()
    {
        List<StoreId> stores;
        List<IDisposable> old;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            stores = [.. _plans.Keys];
            old = [.. _active.Values];
            _active.Clear();
        }

        foreach (var store in stores)
        {
            Follow(store);
        }

        Dispose(old);
    }

    public void Dispose()
    {
        List<IDisposable> active;
        lock (_gate)
        {
            _disposed = true;
            active = [.. _active.Values];
            _active.Clear();
        }

        Dispose(active);
    }

    // Always the store's latest plan: one applied while a slow drive held up a rewatch must not be undone by it.
    private bool Follow(StoreId store)
    {
        List<object> added;
        List<IDisposable> removed;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            var targets = Targets(_plans[store]).ToHashSet();
            var stale = _active.Keys.Where(key => key.Store == store && !targets.Contains(key.Target)).ToList();
            removed = [.. stale.Select(key => _active[key])];
            foreach (var key in stale)
            {
                _active.Remove(key);
            }

            // A watch still starting on a slow drive is not started a second time meanwhile.
            added = [.. targets.Where(target => !_active.ContainsKey((store, target)) && _starting.Add((store, target)))];
        }

        // Outside the lock: creating a watcher on a drive that stopped answering may take until the system gives up.
        Dispose(removed);
        var started = Start(store, added);
        var kept = false;
        var unwanted = new List<IDisposable>();
        lock (_gate)
        {
            _starting.ExceptWith(added.Select(target => (store, target)));

            // A newer plan may have been applied meanwhile; only what it names is kept.
            HashSet<object> wanted = _disposed ? [] : [.. Targets(_plans[store])];
            foreach (var (target, watch) in started)
            {
                if (wanted.Contains(target) && _active.TryAdd((store, target), watch))
                {
                    kept = true;
                }
                else
                {
                    unwanted.Add(watch);
                }
            }
        }

        Dispose(unwanted);
        return kept;
    }

    private IEnumerable<object> Targets(WatchPlan plan) =>
        registryKeys is null ? plan.Folders : plan.Folders.Cast<object>().Concat(plan.RegistryKeys);

    private List<(object Target, IDisposable Watch)> Start(StoreId store, List<object> targets)
    {
        var started = new List<(object, IDisposable)>();
        foreach (var target in targets)
        {
            try
            {
                void Report(WatchSignal signal) => onSignal(store, signal);
                started.Add((target, target is WatchedFolder folder ? folders.Watch(folder, Report) : registryKeys!.Watch((WatchedRegistryKey)target, Report)));
            }
            // The periodic rescan covers what cannot be watched, e.g. a folder on some network drives, and the next plan
            // tries again.
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
                or PlatformNotSupportedException)
            {
                LogWatchFailed(store, exception.GetType().Name);
            }
        }

        return started;
    }

    private static void Dispose(IEnumerable<IDisposable> watches)
    {
        foreach (var watch in watches)
        {
            watch.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot watch a {Store} library: {Error}")]
    private partial void LogWatchFailed(StoreId store, string error);
}
