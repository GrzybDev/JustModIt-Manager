using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Installation;

/// <summary>
/// A game's folder, canonicalised once per operation since libraries on linked drives could otherwise resolve differently
/// between two steps; its path resolution fails closed, so no path ever leaves it.
/// </summary>
internal sealed class GameFolder
{
    private readonly Dictionary<string, string?> _directories = new(PackagePath.Comparer);
    private readonly Dictionary<string, bool> _appFolders = new(PathComparison.Comparer);

    private GameFolder(string root, string hiddenFolderName)
    {
        Root = root;
        HiddenFolderName = hiddenFolderName;
    }

    public string Root { get; }

    public string HiddenFolderName { get; }

    /// <summary>The app's own folder at the game root: staging files, the lock and, by default, the originals.</summary>
    public string HiddenDirectory => Path.Join(Root, HiddenFolderName);

    /// <summary>The folder that holds an operation's new content until it is renamed into place.</summary>
    /// <param name="recordId">The operation's record id.</param>
    public string StagingDirectory(Guid recordId) => Path.Join(HiddenDirectory, "staging", recordId.ToString("N"));

    /// <summary>Opens a game folder for one operation; null when it does not exist.</summary>
    /// <param name="directory">The game's folder in any spelling.</param>
    /// <param name="hiddenFolderName">The name of the app's hidden folder at the game root.</param>
    public static GameFolder? TryOpen(string directory, string hiddenFolderName)
    {
        var root = PathCanonicalizer.Canonicalize(directory);
        return Directory.Exists(root) ? new GameFolder(root, hiddenFolderName) : null;
    }

    /// <summary>Resolves a package path to a full path that safely stays in the game folder.</summary>
    /// <param name="relativePath">A game-relative package path.</param>
    /// <returns>
    /// The canonical full path; null when the path is invalid, a link takes it out of the game, it leads into an app's or
    /// a store's folder (<see cref="StoreFolders"/>), or it would pass a folder off as an app's.
    /// </returns>
    public string? TryResolve(string relativePath)
    {
        if (!PackagePath.IsValid(relativePath)
            || AppFolders.IsReservedTopFolder(PackagePath.FirstSegment(relativePath), HiddenFolderName))
        {
            return null;
        }

        var separator = relativePath.LastIndexOf(PackagePath.Separator);
        var directory = separator < 0 ? Root : ResolveDirectory(relativePath[..separator]);
        var resolved = directory is null ? null : ResolveSegment(directory, relativePath[(separator + 1)..], isLast: true);
        // Judged by the resolved path, since a link or a short name spells the folders above differently.
        return resolved is null || AppFolders.WouldMark(RelativePathOf(resolved)) ? null : resolved;
    }

    /// <summary>
    /// The full path a game-relative path names, to compare paths by: resolved when it resolves safely, otherwise only joined
    /// to the game's folder.
    /// </summary>
    /// <param name="relativePath">A game-relative package path.</param>
    public string Locate(string relativePath) => TryResolve(relativePath) ?? PackagePath.ToFullPath(Root, relativePath);

    /// <summary>The game-relative package path of a full path in the game folder.</summary>
    /// <param name="fullPath">A full path inside the game folder.</param>
    public string RelativePathOf(string fullPath) => PackagePath.FromFullPath(Root, fullPath);

    /// <summary>
    /// Whether a folder is gone for good: its volume is attached and the folder is not on it, while a drive that is merely
    /// unplugged may come back with the files.
    /// </summary>
    /// <param name="directory">The folder.</param>
    /// <param name="volumeId">The volume the folder was on; null when it was not recorded.</param>
    /// <param name="volumes">Tells whether the volume is attached.</param>
    public static bool IsGoneForGood(string directory, string? volumeId, IVolumes volumes) =>
        !Directory.Exists(directory)
        && (volumeId is not null
            ? volumes.IsAttached(volumeId)
            : !PhysicalFileSystem.Instance.IsDriveMissing(directory));

    // Every segment, not just the last, can be a junction or symbolic link, and each one found is followed and checked.
    // Folders are cached for the operation, since many files share them.
    private string? ResolveDirectory(string relativeDirectory)
    {
        if (_directories.TryGetValue(relativeDirectory, out var cached))
        {
            return cached;
        }

        var separator = relativeDirectory.LastIndexOf(PackagePath.Separator);
        var parent = separator < 0 ? Root : ResolveDirectory(relativeDirectory[..separator]);
        var resolved = parent is null ? null : ResolveSegment(parent, relativeDirectory[(separator + 1)..], isLast: false);
        _directories[relativeDirectory] = resolved;
        return resolved;
    }

    private string? ResolveSegment(string parent, string name, bool isLast)
    {
        var path = Path.Join(parent, name);
        string? linkTarget;
        try
        {
            linkTarget = new FileInfo(path).LinkTarget;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (linkTarget is not null)
        {
            var target = PathCanonicalizer.Canonicalize(path);
            // A link that does not resolve (dangling or looping) canonicalises to itself.
            return PathComparison.AreSame(target, path) || !IsInside(target) ? null : target;
        }

        if (!isLast)
        {
            return path;
        }

        // The on-disk spelling (letter case, short names) of the file, or of the folders above a new one, makes two
        // spellings share one backup and keeps a short name from reaching into the app's folder.
        var resolved = PathCanonicalizer.Canonicalize(path);
        return IsInside(resolved) ? resolved : null;
    }

    private bool IsInside(string path) =>
        PathComparison.IsWithin(Root, path) && !PathComparison.AreSame(Root, path) && !PathComparison.IsWithin(HiddenDirectory, path)
        && !StoreFolders.Names.Any(store => PathComparison.IsWithin(Path.Join(Root, store), path))
        && !IsInAppFolder(path);

    private bool IsInAppFolder(string path)
    {
        for (var directory = path; directory is not null && !PathComparison.AreSame(directory, Root);
             directory = Path.GetDirectoryName(directory))
        {
            if (!_appFolders.TryGetValue(directory, out var isAppFolder))
            {
                isAppFolder = AppFolders.OwnerOf(PhysicalFileSystem.Instance, directory) is not null;
                _appFolders[directory] = isAppFolder;
            }

            if (isAppFolder)
            {
                return true;
            }
        }

        return false;
    }
}
