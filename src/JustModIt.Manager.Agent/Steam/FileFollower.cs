using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>
/// Watches files that may not be there yet: the folder of each, or while that folder is missing the way toward it, so each
/// step down is reported and the next <see cref="Follow"/> goes further. One caller at a time.
/// </summary>
/// <param name="fileSystem">Tells which folders exist.</param>
/// <param name="folders">Watches the folders.</param>
/// <param name="changed">Called, on any thread, with every change.</param>
internal sealed class FileFollower(IFileSystem fileSystem, IFolderWatcher folders, Action changed) : IDisposable
{
    private readonly Dictionary<WatchedFolder, IDisposable> _watches = [];

    /// <summary>Watches these files from now on, and no others.</summary>
    /// <param name="files">The files' full paths.</param>
    public void Follow(IEnumerable<string> files)
    {
        HashSet<WatchedFolder> targets = [.. files.GroupBy(file => Path.GetDirectoryName(file)!)
            .Select(folder => fileSystem.DirectoryExists(folder.Key)
                ? new WatchedFolder(folder.Key, folder.Select(file => Path.GetFileName(file)))
                : WatchedFolder.Toward(fileSystem, folder.Key))
            .OfType<WatchedFolder>()];
        foreach (var gone in _watches.Keys.Where(watched => !targets.Contains(watched)).ToList())
        {
            _watches.Remove(gone, out var watch);
            watch!.Dispose();
        }

        foreach (var target in targets.Where(target => !_watches.ContainsKey(target)))
        {
            try
            {
                _watches[target] = folders.Watch(target, _ => changed());
            }
            // The next follow tries again.
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public void Dispose()
    {
        foreach (var watch in _watches.Values)
        {
            watch.Dispose();
        }

        _watches.Clear();
    }
}
