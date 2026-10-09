namespace JustModIt.Manager.Core.Library.Watching;

public interface IFolderWatcher
{
    /// <summary>Reports, on any thread, changes to the matching entries directly inside the folder.</summary>
    /// <param name="folder">The folder and the entries that matter.</param>
    /// <param name="onSignal">Receives each change, or <see cref="WatchSignal.Lost"/> when changes may have been missed.</param>
    /// <returns>Stops watching when disposed, without waiting for a report in progress.</returns>
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

public sealed class FileSystemFolderWatcher : IFolderWatcher
{
    public static FileSystemFolderWatcher Instance { get; } = new();

    public IDisposable Watch(WatchedFolder folder, Action<WatchSignal> onSignal)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(onSignal);
        var watcher = new FileSystemWatcher(folder.Directory)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        try
        {
            foreach (var filter in folder.Filters)
            {
                watcher.Filters.Add(filter);
            }

            watcher.Changed += (_, _) => onSignal(WatchSignal.Changed);
            watcher.Created += (_, _) => onSignal(WatchSignal.Changed);
            watcher.Deleted += (_, _) => onSignal(WatchSignal.Changed);
            watcher.Renamed += (_, _) => onSignal(WatchSignal.Changed);

            // A buffer overflow or a vanished folder means changes were missed.
            watcher.Error += (_, _) => onSignal(WatchSignal.Lost);
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
    }
}
