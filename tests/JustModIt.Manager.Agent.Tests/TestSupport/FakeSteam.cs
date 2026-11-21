using System.Collections.Concurrent;
using JustModIt.Manager.Agent.Steam;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Agent.Tests.TestSupport;

/// <summary>A Steam client whose folder, records and process the test sets; none is installed until the test says so.</summary>
internal sealed class FakeSteamClient : ISteamClient
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _processes = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _started = new();
    private Action? _changed;
    private int _waits;

    public string? Root { get; set; }

    /// <summary>What Steam recorded; set directly, it changes without a report, as a game's start does on Linux.</summary>
    public SteamSession Session { get; set; } = new(null, null, "");

    public ConcurrentQueue<string> Opened { get; } = new();

    // Millennium's Windows layout on every system, so no test reaches the user's own folders.
    public SteamFolders? Find() => Root is { } root
        ? new SteamFolders(root, MillenniumFolders.For(root, new LibraryEnvironment { Platform = AppPlatform.Windows, HomeDirectory = root }))
        : null;

    public SteamSession Read() => Session;

    public bool IsRunning(int processId) => _processes.ContainsKey(processId);

    public DateTimeOffset? StartedAt(int processId) => _started.TryGetValue(processId, out var started) ? started : null;

    /// <summary>How many times a process's exit was awaited.</summary>
    public int Waits => Volatile.Read(ref _waits);

    public async Task<bool> WaitForExitAsync(int processId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _waits);
        if (_processes.TryGetValue(processId, out var process))
        {
            await process.Task.WaitAsync(cancellationToken);
        }

        return true;
    }

    public IDisposable Watch(Action changed)
    {
        _changed = changed;
        return new Unwatch(this);
    }

    public bool Open(string link)
    {
        Opened.Enqueue(link);
        return true;
    }

    /// <summary>The programs handed to Steam to add to its library.</summary>
    public ConcurrentQueue<string> Added { get; } = new();

    /// <summary>Steam would take a program to add now.</summary>
    public bool IsGateOpen { get; private set; }

    public bool AddToLibrary(string program)
    {
        IsGateOpen = true;
        Added.Enqueue(program);
        return true;
    }

    public void CloseLibraryGate() => IsGateOpen = false;

    public void Start(int processId, string language = "polish")
    {
        // A process starts before what it records, and file times are coarser than the clock.
        _started[processId] = DateTimeOffset.UtcNow.AddSeconds(-1);
        _processes[processId] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Change(Session with { ProcessId = processId, Language = language });
    }

    /// <summary>Steam exits cleanly, forgetting its process.</summary>
    public void Exit(int processId)
    {
        if (_processes.TryRemove(processId, out var process))
        {
            process.SetResult();
        }

        Change(Session with { ProcessId = null, RunningGame = null });
    }

    public void Change(SteamSession session)
    {
        Session = session;
        _changed?.Invoke();
    }

    private sealed class Unwatch(FakeSteamClient steam) : IDisposable
    {
        public void Dispose() => steam._changed = null;
    }
}

/// <summary>Settings held in memory.</summary>
internal sealed class FakeSettings(AppSettings settings) : ISettingsService
{
    private readonly Lock _gate = new();

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return settings;
            }
        }
    }

    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings previous, current;
        lock (_gate)
        {
            previous = settings;
            current = settings = change(settings);
        }

        if (current != previous)
        {
            Changed?.Invoke(this, new SettingsChangedEventArgs(previous, current));
        }

        return current;
    }
}

/// <summary>A folder watcher that reports when the test says something changed.</summary>
internal sealed class FakeFolderWatcher : IFolderWatcher
{
    private readonly ConcurrentDictionary<string, Action<WatchSignal>> _watches = new(StringComparer.OrdinalIgnoreCase);

    public IDisposable Watch(WatchedFolder folder, Action<WatchSignal> onSignal)
    {
        _watches[folder.Directory] = onSignal;
        return new Unwatch(() => _watches.TryRemove(folder.Directory, out _));
    }

    public bool IsWatching(string directory) => _watches.ContainsKey(Path.GetFullPath(directory));

    public void Report(string directory)
    {
        if (_watches.TryGetValue(Path.GetFullPath(directory), out var watch))
        {
            watch(WatchSignal.Changed);
        }
    }

    private sealed class Unwatch(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
