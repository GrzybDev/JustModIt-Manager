using JustModIt.Manager.Core.Games;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Library.Watching;

/// <summary>
/// Owns every system watcher of every store and keeps them in line with the stores' latest watch plans, so a library
/// added, removed or moved to another drive is followed after the scan that found it.
/// </summary>
/// <remarks>
/// Watchers are created and disposed outside the lock: creating one on a slow network drive may take a while, and
/// disposing one never waits for a report in progress.
/// </remarks>
public sealed partial class WatchCoordinator : IDisposable
{
    private readonly IFolderWatcher _folders;
    private readonly IRegistryWatcher? _registryKeys;
    private readonly Action<StoreId, WatchSignal> _onSignal;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<StoreId, WatchPlan> _plans = [];
    private readonly Dictionary<(StoreId Store, object Target), IDisposable> _active = [];
    private bool _disposed;

    /// <summary>Creates a coordinator that watches nothing until a plan is applied.</summary>
    /// <param name="folders">Watches folders.</param>
    /// <param name="registryKeys">Null on platforms without a registry.</param>
    /// <param name="onSignal">Called on any thread with the store whose records changed.</param>
    /// <param name="logger">Logs what could not be watched.</param>
    public WatchCoordinator(IFolderWatcher folders, IRegistryWatcher? registryKeys, Action<StoreId, WatchSignal> onSignal, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(onSignal);
        ArgumentNullException.ThrowIfNull(logger);
        _folders = folders;
        _registryKeys = registryKeys;
        _onSignal = onSignal;
        _logger = logger;
    }

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
        List<object> added;
        List<IDisposable> removed;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            _plans[store] = plan;
            var targets = Targets(plan).ToHashSet();
            removed = [.. _active.Where(entry => entry.Key.Store == store && !targets.Contains(entry.Key.Target)).Select(entry => entry.Value)];
            foreach (var key in _active.Keys.Where(key => key.Store == store && !targets.Contains(key.Target)).ToList())
            {
                _active.Remove(key);
            }

            added = [.. targets.Where(target => !_active.ContainsKey((store, target)))];
        }

        Dispose(removed);
        var started = Start(store, added);
        lock (_gate)
        {
            if (!_disposed && _plans.GetValueOrDefault(store) == plan)
            {
                foreach (var (target, watch) in started)
                {
                    if (!_active.TryAdd((store, target), watch))
                    {
                        watch.Dispose();
                    }
                }

                return started.Count > 0;
            }
        }

        // Disposed, or a newer plan replaced this one meanwhile.
        Dispose(started.Select(entry => entry.Watch));
        return false;
    }

    /// <summary>Recreates every watch after one lost changes, starting the new watches before the old ones stop.</summary>
    public void Rewatch()
    {
        List<(StoreId Store, WatchPlan Plan)> plans;
        List<IDisposable> old;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            plans = [.. _plans.Select(entry => (entry.Key, entry.Value))];
            old = [.. _active.Values];
            _active.Clear();
        }

        foreach (var (store, plan) in plans)
        {
            Apply(store, plan);
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

    private IEnumerable<object> Targets(WatchPlan plan) =>
        _registryKeys is null ? plan.Folders : plan.Folders.Cast<object>().Concat(plan.RegistryKeys);

    private List<(object Target, IDisposable Watch)> Start(StoreId store, List<object> targets)
    {
        var started = new List<(object, IDisposable)>();
        foreach (var target in targets)
        {
            try
            {
                void Report(WatchSignal signal) => _onSignal(store, signal);
                started.Add((target, target is WatchedFolder folder ? _folders.Watch(folder, Report) : _registryKeys!.Watch((WatchedRegistryKey)target, Report)));
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
