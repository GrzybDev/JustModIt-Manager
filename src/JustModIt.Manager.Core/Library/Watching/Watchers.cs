using System.Collections.Immutable;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Library.Watching;

public interface IFolderWatcher
{
    /// <summary>Reports, on any thread, changes to the matching entries directly inside the folder.</summary>
    /// <param name="folder">The folder and the entries that matter.</param>
    /// <param name="onSignal">Receives each change, or <see cref="WatchSignal.Lost"/> when changes may have been missed.</param>
    /// <returns>Stops watching when disposed, without waiting for a report in progress.</returns>
    /// <exception cref="WatchLimitException">The system's limit of watches is reached.</exception>
    IDisposable Watch(WatchedFolder folder, Action<WatchSignal> onSignal);
}

public interface IRegistryWatcher
{
    /// <summary>Reports, on any thread, changes to the key, waiting on its nearest existing ancestor while it is missing.</summary>
    /// <param name="key">The key to watch.</param>
    /// <param name="onSignal">Receives each change, or <see cref="WatchSignal.Lost"/> when changes may have been missed.</param>
    /// <returns>Stops watching when disposed, without waiting for a report in progress.</returns>
    IDisposable Watch(WatchedRegistryKey key, Action<WatchSignal> onSignal);
}

/// <summary>Moments after which store records may have changed unseen.</summary>
public interface ISystemEvents
{
    /// <summary>The machine woke from sleep or hibernation.</summary>
    event EventHandler? Resumed;

    /// <summary>A volume was mounted, such as a drive plugged back in.</summary>
    event EventHandler? VolumeArrived;
}

/// <summary>A folder could not be watched because the system's limit of watches, which other programs share, is reached.</summary>
/// <param name="message">The system's words, which name the limit.</param>
/// <param name="innerException">The failure as the system reported it.</param>
public sealed class WatchLimitException(string message, Exception innerException) : IOException(message, innerException);

/// <summary>
/// Watches folders through the system, with one system watcher per folder however many watches share it, since Linux
/// counts each against a limit per user; it matches the entries' names itself, ignoring letter case on every system.
/// </summary>
public sealed class FileSystemFolderWatcher : IFolderWatcher
{
    // .NET reports Linux's limits by their error numbers: ENFILE and EMFILE for watchers, ENOSPC for watches.
    private static readonly int[] LimitErrors = [23, 24, 28];

    private readonly Lock _gate = new();
    private readonly Dictionary<string, SharedWatch> _folders = new(PathComparison.Comparer);

    public static FileSystemFolderWatcher Instance { get; } = new();

    public IDisposable Watch(WatchedFolder folder, Action<WatchSignal> onSignal)
    {
        FolderWatch watch;
        lock (_gate)
        {
            if (!_folders.TryGetValue(folder.Directory, out var shared))
            {
                _folders[folder.Directory] = shared = new SharedWatch(this, folder.Directory);
            }

            watch = new FolderWatch(this, shared, folder, onSignal);
            shared.Watches = shared.Watches.Add(watch);
        }

        try
        {
            watch.Shared.Start();
            return watch;
        }
        catch
        {
            watch.Dispose();
            throw;
        }
    }

    private void Remove(FolderWatch watch)
    {
        var shared = watch.Shared;
        lock (_gate)
        {
            shared.Watches = shared.Watches.Remove(watch);
            if (!shared.Watches.IsEmpty)
            {
                return;
            }

            Forget(shared);
        }

        shared.Dispose();
    }

    private void Forget(SharedWatch shared)
    {
        if (_folders.GetValueOrDefault(shared.Directory) == shared)
        {
            _folders.Remove(shared.Directory);
        }
    }

    private sealed class FolderWatch(FileSystemFolderWatcher owner, SharedWatch shared, WatchedFolder folder, Action<WatchSignal> onSignal)
        : IDisposable
    {
        private int _disposed;

        public SharedWatch Shared => shared;

        public void Report(WatchSignal signal, string? name, string? oldName = null)
        {
            if (signal == WatchSignal.Lost || folder.Matches(name ?? "") || (oldName is not null && folder.Matches(oldName)))
            {
                onSignal(signal);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Remove(this);
            }
        }
    }

    /// <summary>The system watcher of one folder and the watches it reports to.</summary>
    /// <param name="owner">Forgets the watcher once it failed, so the next watch of the folder starts a new one.</param>
    /// <param name="directory">The folder.</param>
    private sealed class SharedWatch(FileSystemFolderWatcher owner, string directory) : IDisposable
    {
        private readonly Lock _start = new();
        private FileSystemWatcher? _watcher;

        public string Directory => directory;

        /// <summary>Copied on every change, so a report goes through the watches without a lock.</summary>
        public ImmutableArray<FolderWatch> Watches { get; set; } = [];

        /// <summary>Starts the system watcher unless it runs; a watch whose start failed tries again.</summary>
        public void Start()
        {
            // Starting may take until the system gives up on a drive that stopped answering, so it holds up only this folder.
            lock (_start)
            {
                if (_watcher is null)
                {
                    _watcher = Create();
                }
            }
        }

        // Only once its last watch is gone, and no watch starts it any more.
        public void Dispose() => _watcher?.Dispose();

        private FileSystemWatcher Create()
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            Exception? startFailure = null;
            var starting = true;
            try
            {
                watcher.Changed += (_, change) => Report(WatchSignal.Changed, change.Name);
                watcher.Created += (_, change) => Report(WatchSignal.Changed, change.Name);
                watcher.Deleted += (_, change) => Report(WatchSignal.Changed, change.Name);
                watcher.Renamed += (_, change) => Report(WatchSignal.Changed, change.Name, change.OldName);

                // Linux reports a watch it could not add while the watcher starts, which leaves the folder unwatched; later a
                // buffer overflow or a vanished folder means changes were missed.
                watcher.Error += (_, error) =>
                {
                    if (starting)
                    {
                        startFailure = error.GetException();
                        return;
                    }

                    lock (owner._gate)
                    {
                        owner.Forget(this);
                    }

                    Report(WatchSignal.Lost, null);
                };
                watcher.EnableRaisingEvents = true;
                starting = false;
                return startFailure is null ? watcher : throw startFailure;
            }
            catch (IOException exception) when (LimitErrors.Contains(exception.HResult) && OperatingSystem.IsLinux())
            {
                watcher.Dispose();
                throw new WatchLimitException(exception.Message, exception);
            }
            catch
            {
                watcher.Dispose();
                throw;
            }
        }

        private void Report(WatchSignal signal, string? name, string? oldName = null)
        {
            foreach (var watch in Watches)
            {
                watch.Report(signal, name, oldName);
            }
        }
    }
}
