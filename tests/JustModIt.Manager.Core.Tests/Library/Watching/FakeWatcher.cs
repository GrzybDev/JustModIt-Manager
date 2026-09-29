using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;

namespace JustModIt.Manager.Core.Tests.Library.Watching;

/// <summary>Records what is watched and lets a test report changes, like the system watchers do.</summary>
internal sealed class FakeWatcher : IFolderWatcher, IRegistryWatcher
{
    private readonly Lock _gate = new();
    private readonly List<ActiveWatch> _active = [];

    public int Started { get; private set; }

    /// <summary>Targets that fail to start, as a folder on some network drives does.</summary>
    public HashSet<object> Failing { get; } = [];

    public IReadOnlyList<object> Active
    {
        get
        {
            lock (_gate)
            {
                return [.. _active.Select(watch => watch.Target)];
            }
        }
    }

    public IDisposable Watch(WatchedFolder folder, Action<WatchSignal> onSignal) => Start(folder, onSignal);

    public IDisposable Watch(WatchedRegistryKey key, Action<WatchSignal> onSignal) => Start(key, onSignal);

    /// <summary>Reports a signal from every active watch of <paramref name="target"/>.</summary>
    /// <param name="signal">What the watches report.</param>
    /// <param name="target">The watched folder or registry key; the first active watch when null.</param>
    public void Report(WatchSignal signal = WatchSignal.Changed, object? target = null)
    {
        List<ActiveWatch> watches;
        lock (_gate)
        {
            watches = [.. _active.Where(watch => target is null ? watch == _active[0] : watch.Target.Equals(target))];
        }

        foreach (var watch in watches)
        {
            watch.OnSignal(signal);
        }
    }

    private ActiveWatch Start(object target, Action<WatchSignal> onSignal)
    {
        if (Failing.Contains(target))
        {
            throw new IOException("The folder cannot be watched.");
        }

        var watch = new ActiveWatch(this, target, onSignal);
        lock (_gate)
        {
            Started++;
            _active.Add(watch);
        }

        return watch;
    }

    private sealed class ActiveWatch(FakeWatcher owner, object target, Action<WatchSignal> onSignal) : IDisposable
    {
        public object Target { get; } = target;

        public Action<WatchSignal> OnSignal { get; } = onSignal;

        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._active.Remove(this);
            }
        }
    }
}
