using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;

namespace JustModIt.Manager.Core.Tests.Library.Watching;

/// <summary>Records what is watched and lets a test report changes, like the system watchers do.</summary>
internal sealed class FakeWatcher : IFolderWatcher, IRegistryWatcher, IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<ActiveWatch> _active = [];
    private readonly ManualResetEventSlim _answered = new(initialState: true);
    private object? _hanging;
    private int _blocked;

    public int Started { get; private set; }

    /// <summary>The most watches that were active at once.</summary>
    public int PeakActive { get; private set; }

    /// <summary>Targets that fail to start, as a folder on some network drives does.</summary>
    public HashSet<object> Failing { get; } = [];

    /// <summary>How many watches are blocked while they start.</summary>
    public int Blocked => Volatile.Read(ref _blocked);

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

    /// <summary>Makes starting to watch <paramref name="target"/> block, as on a drive that stopped answering, until <see cref="Answer"/>.</summary>
    /// <param name="target">The watched folder or registry key.</param>
    public void Hang(object target)
    {
        _answered.Reset();
        Volatile.Write(ref _hanging, target);
    }

    public void Answer()
    {
        Volatile.Write(ref _hanging, null);
        _answered.Set();
    }

    public void Dispose() => Answer();

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

        if (target.Equals(Volatile.Read(ref _hanging)))
        {
            Interlocked.Increment(ref _blocked);
            _answered.Wait(TimeSpan.FromMinutes(1));
            Interlocked.Decrement(ref _blocked);
        }

        var watch = new ActiveWatch(this, target, onSignal);
        lock (_gate)
        {
            Started++;
            _active.Add(watch);
            PeakActive = Math.Max(PeakActive, _active.Count);
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
